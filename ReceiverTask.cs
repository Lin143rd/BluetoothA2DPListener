using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace BluetoothA2DPListener
{
    internal class ReceiverTask
    {
        MainWindow _mainWindow;

        private DeviceWatcher _deviceWatcher;
        private RfcommDeviceService _serverService;
        private StreamSocket _socket;
        private DataWriter _writer;

        private BufferedWaveProvider _bufferedWaveProvider;
        private VolumeWaveProvider16 _wavProvider;
        private MMDevice _mmDevice;
        public async void ReceiveAdvertise(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            string[] requestedProperties = new string[] { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" };

            string selector = "(" + "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"" + ")";
            _deviceWatcher = DeviceInformation.CreateWatcher(selector, requestedProperties, DeviceInformationKind.AssociationEndpoint);

            // デバイス情報更新時のハンドラを登録
            _deviceWatcher.Added += WatcherDeviceAdded;

            // ウォッチャーをスタート(検索開始)
            _deviceWatcher.Start();


            await _mainWindow.ReceiverOutput("Scanning\n");
            await _mainWindow.ReceiverOutput("\n" + _deviceWatcher.Status.ToString() + "\n");
        }

        private async void WatcherDeviceAdded(DeviceWatcher sender, DeviceInformation deviceInfo)
        {
            await _mainWindow.ReceiverOutput("Info: " + deviceInfo.ToString() + deviceInfo.Name + "\n");
            var deviceName = deviceInfo.Name;

            if (deviceName != "DESKTOP-4MH8S0M")
                return;

            DeviceAccessStatus accessStatus = DeviceAccessInformation.CreateFromId(deviceInfo.Id).CurrentStatus;
            if (accessStatus == DeviceAccessStatus.DeniedByUser)
                return;

            // デバイス情報を保存
            await TryPairing(deviceInfo);
            var bd = await BluetoothDevice.FromIdAsync(deviceInfo.Id);
            await _mainWindow.ReceiverOutput($"Pairing Device:\n\tAddress:{bd.BluetoothAddress}\n\tPairing:{deviceInfo.Pairing.IsPaired} {deviceInfo.Pairing.CanPair}\n\tProtectionLevel:{deviceInfo.Pairing.ProtectionLevel}\n\tName:{bd.Name}\n\tHostName:{bd.HostName}\n\tService:{bd.ClassOfDevice.ServiceCapabilities}\n");
            var services = await bd.GetRfcommServicesForIdAsync(
                RfcommServiceId.FromShortId(Constants.RfcommServiceUUidAudioSink), BluetoothCacheMode.Uncached);
            var tmp2 = await bd.RequestAccessAsync();

            if (services.Services.Count > 0)
            {
                await _mainWindow.ReceiverOutput($"Access Result to {deviceName}: {services.Error} {services.Services.Count} {tmp2}\n");
                _serverService = services.Services[0];
            }
            else
            {
                return;
            }

            await _mainWindow.ReceiverOutput(await BluetoothAnalyzer.AnalyzeServiceResult(services));

            if (_serverService == null)
                return;

            StopWatcher();

            lock (this)
            {
                _socket = new StreamSocket();
            }
            try
            {
                await _socket.ConnectAsync(_serverService.ConnectionHostName, _serverService.ConnectionServiceName);

                _writer = new DataWriter(_socket.OutputStream);
                DataReader chatReader = new DataReader(_socket.InputStream);


                _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
                _wavProvider = new VolumeWaveProvider16(_bufferedWaveProvider);
                _mmDevice = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                using (IWavePlayer wavPlayer = new WasapiOut(_mmDevice, AudioClientShareMode.Shared, false, 200))
                {
                    wavPlayer.Init(_wavProvider);
                    wavPlayer.Play();

                    while (true)
                    {
                        await ReceiveStringLoop(_bufferedWaveProvider, chatReader);
                    }

                    wavPlayer.Stop();
                }

            }
            catch (Exception ex) when ((uint)ex.HResult == 0x80070490) // ERROR_ELEMENT_NOT_FOUND
            {
                await _mainWindow.ReceiverOutput("Please verify that you are running the BluetoothRfcommChat server.");
            }
            catch (Exception ex) when ((uint)ex.HResult == 0x80072740) // WSAEADDRINUSE
            {
                await _mainWindow.ReceiverOutput("Please verify that there is no other RFCOMM connection to the same device.");
            }
        }

        private async Task ReceiveStringLoop(BufferedWaveProvider provider, DataReader chatReader)
        {
            try
            {
                uint size = await chatReader.LoadAsync(sizeof(uint));
                if (size < sizeof(uint))
                {
                    Disconnect("Remote device terminated connection - make sure only one instance of server is running on remote device");
                    return;
                }

                uint stringLength = chatReader.ReadUInt32();
                uint actualStringLength = await chatReader.LoadAsync(stringLength);
                if (actualStringLength != stringLength)
                {
                    // The underlying socket was closed before we were able to read the whole data
                    return;
                }

                var data = new byte[stringLength];

                chatReader.ReadBytes(data);

                int bufsize = 44100;

                for (int i = 0; i + bufsize < data.Length; i += bufsize)
                {
                    while (provider.BufferedBytes + bufsize >= provider.BufferLength)
                        await Task.Delay(250);
                    provider.AddSamples(data, i, bufsize);
                    await Task.Delay(250);
                }
            }
            catch (Exception ex)
            {
                lock (this)
                {
                    if (_socket == null)
                    {
                        // Do not print anything here -  the user closed the socket.
                        if ((uint)ex.HResult == 0x80072745)
                            Console.WriteLine("Disconnect triggered by remote device");
                        else if ((uint)ex.HResult == 0x800703E3)
                            Console.WriteLine("The I/O operation has been aborted because of either a thread exit or an application request.");
                    }
                    else
                    {
                        Disconnect("Read stream failed with error: " + ex.Message);
                    }
                }
            }
        }

        private void StopWatcher()
        {
            if (null != _deviceWatcher)
            {
                if ((DeviceWatcherStatus.Started == _deviceWatcher.Status ||
                     DeviceWatcherStatus.EnumerationCompleted == _deviceWatcher.Status))
                {
                    _deviceWatcher.Stop();
                }
                _deviceWatcher = null;
            }
        }

        private async Task TryPairing(DeviceInformation deviceInfo)
        {
            var customPairing = deviceInfo.Pairing.Custom;
            customPairing.PairingRequested += PairingRequestedHandler;
            DevicePairingResult result = await customPairing.PairAsync(DevicePairingKinds.ConfirmOnly, DevicePairingProtectionLevel.Default);
            customPairing.PairingRequested -= PairingRequestedHandler;
            if (result.Status == DevicePairingResultStatus.Paired || result.Status == DevicePairingResultStatus.AlreadyPaired)
            {
                // success
                await _mainWindow.ReceiverOutput($"Pairing Completed {deviceInfo.Name}\n");
            }
            else
            {
                // fail
                await _mainWindow.ReceiverOutput($"Pairing Failed {deviceInfo.Name}\n");
            }
        }

        private static void PairingRequestedHandler(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
        {
            switch (args.PairingKind)
            {
                case DevicePairingKinds.ConfirmOnly:
                    args.Accept();
                    break;
            }
        }

        private void Disconnect(string disconnectReason)
        {
            if (_writer != null)
            {
                _writer.DetachStream();
                _writer = null;
            }


            if (_serverService != null)
            {
                _serverService.Dispose();
                _serverService = null;
            }
            lock (this)
            {
                if (_socket != null)
                {
                    _socket.Dispose();
                    _socket = null;
                }
            }
        }
    }
}
