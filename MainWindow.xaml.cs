﻿global using System;
global using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Windows;
using System.Printing;
using Windows.Storage.Streams;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.Advertisement;
using System.Windows.Controls;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Networking;
using Windows.UI.Core;
using Windows.ApplicationModel.Background;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.VisualBasic;
using System.Reflection.PortableExecutable;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace BluetoothA2DPListener
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Label serverConsole;
        private Label recieverConsole;
        private DeviceWatcher deviceWatcher;
        private RfcommServiceProvider _rfcommProvider;
        private StreamSocketListener _socketListener;
        private DataWriter _writer;
        private StreamSocket _socket;
        private StreamSocket _receiveSocket;
        private RfcommDeviceService _serverService;
        private BufferedWaveProvider _bufferedWaveProvider;
        private VolumeWaveProvider16 _wavProvider;
        private MMDevice _mmDevice;
        
       
        public MainWindow()
        {
            InitializeComponent();

            InitializeVariable();
        }

        private void InitializeVariable()
        {
            serverConsole = this.FindName("ServerConsole") as Label;
            recieverConsole = this.FindName("RecieverConsole") as Label;
        }

        public async void OnClickAdvertise(object sender, RoutedEventArgs e)
        {

            // Guid SensorServiceUuid = new Guid("0000110e-0000-1000-8000-00805f9b34fb");

            // ペアリングした環境センサを検索対象にする
            // Guid SeosorServiceUuid = new Guid("0C4C3000-7700-46F4-AA96-D5E974E32A54");
            // string selector = "(" + GattDeviceService.GetDeviceSelectorFromUuid(SensorServiceUuid) + ")";

            // ウォッチャー(機器を監視、検索するやつ)を作成
            // private DeviceWatcher DeviceWatcher { get; set; }
            // deviceWatcher = DeviceInformation.CreateWatcher(selector);

            string[] requestedProperties = new string[] { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" };

            string selector = "(" + "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"" + ")";
            deviceWatcher = DeviceInformation.CreateWatcher(selector, requestedProperties, DeviceInformationKind.AssociationEndpoint);

            // デバイス情報更新時のハンドラを登録
            deviceWatcher.Added += Watcher_DeviceAdded;
            // deviceWatcher.EnumerationCompleted += Watcher_EnumerationCompleted;

            // ウォッチャーをスタート(検索開始)
            deviceWatcher.Start();


            await RecieverOutput("Scanning\n");
            await RecieverOutput("\n" + deviceWatcher.Status.ToString() + "\n");
        }

        private async void Watcher_DeviceAdded(DeviceWatcher sender, DeviceInformation deviceInfo)
        {
            await RecieverOutput("Info: " + deviceInfo.ToString() + deviceInfo.Name + "\n");
            var deviceName = deviceInfo.Name;

            if (deviceName != "DESKTOP-4MH8S0M")
                return;

            DeviceAccessStatus accessStatus = DeviceAccessInformation.CreateFromId(deviceInfo.Id).CurrentStatus;
            if (accessStatus == DeviceAccessStatus.DeniedByUser)
                return;

            // デバイス情報を保存
            await TryPairing(deviceInfo);
            var bd = await BluetoothDevice.FromIdAsync(deviceInfo.Id);
            await RecieverOutput($"Pairing Device:\n\tAddress:{bd.BluetoothAddress}\n\tPairing:{deviceInfo.Pairing.IsPaired} {deviceInfo.Pairing.CanPair}\n\tProtectionLevel:{deviceInfo.Pairing.ProtectionLevel}\n\tName:{bd.Name}\n\tHostName:{bd.HostName}\n\tService:{bd.ClassOfDevice.ServiceCapabilities}\n");
            var services = await bd.GetRfcommServicesForIdAsync(
                RfcommServiceId.FromShortId(Constants.RfcommServiceUUidAudioSink), BluetoothCacheMode.Uncached);
            var tmp2 = await bd.RequestAccessAsync();

            if (services.Services.Count > 0)
            {
                await RecieverOutput($"Access Result to {deviceName}: {services.Error} {services.Services.Count} {tmp2}\n");
                _serverService = services.Services[0];
            }
            else
            {
                return;
            }

            await RecieverOutput(await BluetoothAnalyzer.AnalyzeServiceResult(services));

            //foreach (var service in services.Services)
            //{
            //    var attributeList = await service.GetSdpRawAttributesAsync();
            //    foreach (var attribute in attributeList)
            //    {
            //        if (attribute.Key == 0x0009)
            //        {
            //            var buf = attribute.Value;
            //            var reader = DataReader.FromBuffer(buf);
            //            await ServerOutput(SdpAnalyzer.AnalyzeSdp(reader));
            //        }
            //    }
            //    if (_serverService != null)
            //        break;
            //}
            if (_serverService == null)
                return;

            StopWatcher();

            lock (this)
            {
                _receiveSocket = new StreamSocket();
            }
            try
            {
                await _receiveSocket.ConnectAsync(_serverService.ConnectionHostName, _serverService.ConnectionServiceName);

                _writer = new DataWriter(_receiveSocket.OutputStream);
                DataReader chatReader = new DataReader(_receiveSocket.InputStream);


                _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
                _wavProvider = new VolumeWaveProvider16(_bufferedWaveProvider);
                _mmDevice = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                using (IWavePlayer wavPlayer = new WasapiOut(_mmDevice, AudioClientShareMode.Shared, false, 200))
                {
                    wavPlayer.Init(_wavProvider);
                    wavPlayer.Play();

                    while (true)
                    {
                        ReceiveStringLoop(_bufferedWaveProvider, chatReader);
                    }

                    wavPlayer.Stop();
                }

            }
            catch (Exception ex) when ((uint)ex.HResult == 0x80070490) // ERROR_ELEMENT_NOT_FOUND
            {
                await RecieverOutput("Please verify that you are running the BluetoothRfcommChat server.");
            }
            catch (Exception ex) when ((uint)ex.HResult == 0x80072740) // WSAEADDRINUSE
            {
                await RecieverOutput("Please verify that there is no other RFCOMM connection to the same device.");
            }
        }

        private async void ReceiveStringLoop(BufferedWaveProvider provider, DataReader chatReader)
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
                    if (_receiveSocket == null)
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

        private async void InitializeRfcommServer()
        {
            try
            {
                _rfcommProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromShortId(Constants.RfcommServiceUUidAudioSink));
            }
            // Catch exception HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_AVAILABLE).
            catch (Exception ex)
            {
                // The Bluetooth radio may be off.
                await ServerOutput("failed Initialize\n");
                return;
            }

            if (_rfcommProvider == null)
            {
                await ServerOutput("uwaaaaaaaaaaaa");
                return;
            }


            // Create a listener for this service and start listening
            _socketListener = new StreamSocketListener();
            _socketListener.ConnectionReceived += OnConnectionReceived;
            var rfcomm = _rfcommProvider.ServiceId.AsString();

            await _socketListener.BindServiceNameAsync(_rfcommProvider.ServiceId.AsString(),
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

            // Set the SDP attributes and start Bluetooth advertising
            InitializeServiceSdpAttributes(_rfcommProvider);

            foreach (var s in _rfcommProvider.SdpRawAttributes)
            {
                await ServerOutput(BluetoothAnalyzer.AnalyzeAttribute(s));
            }

            try
            {
                _rfcommProvider.StartAdvertising(_socketListener, true);
            }
            catch (Exception e)
            {
                // If you aren't able to get a reference to an RfcommServiceProvider, tell the user why.  Usually throws an exception if user changed their privacy settings to prevent Sync w/ Devices.  
                await ServerOutput(e.Message + "\n");
                await ServerOutput($"HRESULT:{e.HResult:X8}\n");
                await ServerOutput(e.Source + "\n");
                await ServerOutput(e.StackTrace + "\n");
                await ServerOutput(e.TargetSite.ToString());
                return;
            }
            await ServerOutput($"\nListening for incoming connections {_rfcommProvider.ServiceId.AsString()}\n");
        }

        private void InitializeServiceSdpAttributes(RfcommServiceProvider rfcommProvider)
        {
            //// とりあえずX1をパクる
            var sdp = new SdpData(4, "Hello Bluetooth");
            SdpInitializer.InitializeSdp(rfcommProvider, sdp, 0x0100);


            // Bluetooth Profile Descriptor List
            var ls = new List<SdpData>();
            var ls2 = new List<SdpData>();
            ls.Add(new SdpData(3, new byte[] { 0x11, 0x0D }));
            ls.Add(new SdpData(1, new byte[] { 0x01, 0x03 }));
            ls2.Add(new SdpData(6, ls.ToArray()));
            sdp = new SdpData(6, ls2.ToArray());
            SdpInitializer.InitializeSdp(rfcommProvider, sdp, 0x0009);


            // Supported Features
            sdp = new SdpData(1, new byte[] { 0x00, 0x3F });
            SdpInitializer.InitializeSdp(rfcommProvider, sdp, 0x0311);

            //// Service Class ID List
            //ls = new List<SdpData>();
            //ls.Add(new SdpData(3, new byte[] { 0x11, 0x0B }));
            //sdp = new SdpData(6, ls.ToArray());
            //SdpInitializer.InitializeSdp(rfcommProvider, sdp, 0x0001);


            //var sdpWriter = new DataWriter();

            //// Write the Service Name Attribute.
            //sdpWriter.WriteByte(Constants.SdpServiceNameAttributeType);

            //// The length of the UTF-8 encoded Service Name SDP Attribute.
            //sdpWriter.WriteByte((byte)Constants.SdpServiceName.Length);

            //// The UTF-8 encoded Service Name value.
            //sdpWriter.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            //sdpWriter.WriteString(Constants.SdpServiceName);

            //// Set the SDP Attribute on the RFCOMM Service Provider.
            //rfcommProvider.SdpRawAttributes.Add(Constants.SdpServiceNameAttributeId, sdpWriter.DetachBuffer());
        }

        private async void Disconnect(string disconnection)
        {
            if (_rfcommProvider != null)
            {
                _rfcommProvider.StopAdvertising();
                _rfcommProvider = null;
            }

            if (_socketListener != null)
            {
                _socketListener.Dispose();
                _socketListener = null;
            }

            if (_writer != null)
            {
                _writer.DetachStream();
                _writer = null;
            }

            if (_socket != null)
            {
                _socket.Dispose();
                _socket = null;
            }

            await RecieverOutput(disconnection);
        }

        private void StopWatcher()
        {
            if (null != deviceWatcher)
            {
                if ((DeviceWatcherStatus.Started == deviceWatcher.Status ||
                     DeviceWatcherStatus.EnumerationCompleted == deviceWatcher.Status))
                {
                    deviceWatcher.Stop();
                }
                deviceWatcher = null;
            }
        }

        private async void OnConnectionReceived(
            StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            await ServerOutput("kityaaaaaaaaaaaa");
            // Don't need the listener anymore
            _socketListener.Dispose();
            _socketListener = null;

            try
            {
                _socket = args.Socket;
            }
            catch (Exception e)
            {
                await ServerOutput("failed on connection\n");
                Disconnect("failed");
                return;
            }

            // Note - this is the supported way to get a Bluetooth device from a given socket
            var remoteDevice = await BluetoothDevice.FromHostNameAsync(_socket.Information.RemoteHostName);

            _writer = new DataWriter(_socket.OutputStream);
            var reader = new DataReader(_socket.InputStream);
            bool remoteDisconnection = false;

            await ServerOutput("Connected to Client: " + remoteDevice.Name);





            //外部入力のダミーとして適当な音声データを用意して使う
            string wavFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "sample.wav"
                );
            //mp3を使うならこう。
            string mp3FilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "sample.mp3"
                );

            if (!(File.Exists(wavFilePath) || File.Exists(mp3FilePath)))
            {
                Console.WriteLine("Target sound files were not found. Wav file or MP3 file is needed for this program.");
                Console.WriteLine($"expected wav file: {wavFilePath}");
                Console.WriteLine($"expected mp3 file: {wavFilePath}");
                Console.WriteLine("(note: ONE file is enough, two files is not needed)");
                return;
            }

            //mp3しかない場合、先にwavへ変換を行う
            if (!File.Exists(wavFilePath))
            {
                using (var mp3reader = new Mp3FileReader(mp3FilePath))
                using (var pcmStream = WaveFormatConversionStream.CreatePcmStream(mp3reader))
                {
                    WaveFileWriter.CreateWaveFile(wavFilePath, pcmStream);
                }
            }

            byte[] data = File.ReadAllBytes(wavFilePath);

            //若干効率が悪いがヘッダのバイト数を確実に割り出して削る
            using (var r = new WaveFileReader(wavFilePath))
            {
                int headerLength = (int)(data.Length - r.Length);
                data = data.Skip(headerLength).ToArray();
            }

            int bufsize = 44100;

            for (int i = 0; i + bufsize < data.Length; i += bufsize) {
                try
                {
                    int length = Math.Min(bufsize, data.Length - i);
                    _writer.WriteUInt32((uint)length);
                    _writer.WriteBytes(data[i..(i + length)]);

                    await _writer.StoreAsync();
                }
                catch (Exception ex) when ((uint)ex.HResult == 0x80072745)
                {
                    // The remote device has disconnected the connection
                    await ServerOutput("Remote Connection Disconnected");
                    return;
                }
            }






            // Infinite read buffer loop
            while (true)
            {
                try
                {
                    // Based on the protocol we've defined, the first uint is the size of the message
                    uint readLength = await reader.LoadAsync(sizeof(uint));

                    // Check if the size of the data is expected (otherwise the remote has already terminated the connection)
                    if (readLength < sizeof(uint))
                    {
                        remoteDisconnection = true;
                        break;
                    }
                    uint currentLength = reader.ReadUInt32();

                    // Load the rest of the message since you already know the length of the data expected.  
                    readLength = await reader.LoadAsync(currentLength);

                    // Check if the size of the data is expected (otherwise the remote has already terminated the connection)
                    if (readLength < currentLength)
                    {
                        remoteDisconnection = true;
                        break;
                    }
                    string message = reader.ReadString(currentLength);
                    await ServerOutput(message);

                    // await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    // {
                    //     ConversationListBox.Items.Add("Received: " + message);
                    // });
                }
                // Catch exception HRESULT_FROM_WIN32(ERROR_OPERATION_ABORTED).
                catch (Exception ex) when ((uint)ex.HResult == 0x800703E3)
                {
                    // await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    // {
                    //     rootPage.NotifyUser("Client Disconnected Successfully", NotifyType.StatusMessage);
                    // });
                    await ServerOutput("failed on connection\n");
                    break;
                }
            }

            reader.DetachStream();
            if (remoteDisconnection)
            {
                Disconnect("failed");
                // await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                // {
                //     rootPage.NotifyUser("Client disconnected",NotifyType.StatusMessage);
                // });
                await ServerOutput("Client disconnected");
            }
        }

        private void OnClickServer(object sender, RoutedEventArgs e)
        {
            InitializeRfcommServer();
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
                await RecieverOutput($"Pairing Completed {deviceInfo.Name}\n");
            }
            else
            {
                // fail
                await RecieverOutput($"Pairing Failed {deviceInfo.Name}\n");
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

        private async Task ServerOutput(string s)
        {
            await this.Dispatcher.InvokeAsync(() => {
                serverConsole.Content += s;
            });
        }

        private async Task RecieverOutput(string s)
        {
            await this.Dispatcher.InvokeAsync(() => {
                recieverConsole.Content += s;
            });
        }
    }
}
