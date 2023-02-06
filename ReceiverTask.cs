using NAudio.CoreAudioApi;
using NAudio.Wave;
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

            await StartAudioPlayer();
        }

        private async Task StartAudioPlayer()
        {
            lock (this)
            {
                _socket = new StreamSocket();
            }
            try
            {
                await _socket.ConnectAsync(_serverService.ConnectionHostName, _serverService.ConnectionServiceName);

                _writer = new DataWriter(_socket.OutputStream);
                DataReader audioReader = new DataReader(_socket.InputStream);

                var audioManager = new AudioManager(new DataReader(_socket.InputStream), new DataWriter(_socket.OutputStream));

                await _mainWindow.ServerOutput("Connected to Client: " + _serverService.Device.Name);

                Task t1 = audioManager.Play();
                Task t2 = audioManager.Stream();

                await Task.WhenAll(t1, t2);

                audioManager.Dispose();
                Disconnect("Disconnected\n");
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
