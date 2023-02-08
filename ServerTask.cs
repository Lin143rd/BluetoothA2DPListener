using System.IO;
using System.Linq;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using NAudio.Wave;
using System.Threading.Tasks;

namespace BluetoothA2DPListener
{
    internal class ServerTask
    {
        private MainWindow _mainWindow;

        private RfcommServiceProvider _rfcommProvider;
        private StreamSocketListener _socketListener;
        private StreamSocket _socket;
        private DataWriter _writer;

        internal async void InitializeRfcommServer(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            try
            {
                _rfcommProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromShortId(Constants.RfcommServiceUUidAudioSink));
            }
            // Catch exception HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_AVAILABLE).
            catch (Exception ex)
            {
                // The Bluetooth radio may be off.
                await _mainWindow.ServerOutput("failed Initialize\n");
                return;
            }

            if (_rfcommProvider == null)
            {
                await _mainWindow.ServerOutput("failed to create provider\n");
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
                await _mainWindow.ServerOutput(BluetoothAnalyzer.AnalyzeAttribute(s));
            }

            try
            {
                _rfcommProvider.StartAdvertising(_socketListener, true);
            }
            catch (Exception e)
            {
                // If you aren't able to get a reference to an RfcommServiceProvider, tell the user why.  Usually throws an exception if user changed their privacy settings to prevent Sync w/ Devices.  
                await _mainWindow.ServerOutput(e.Message + "\n");
                await _mainWindow.ServerOutput($"HRESULT:{e.HResult:X8}\n");
                await _mainWindow.ServerOutput(e.Source + "\n");
                await _mainWindow.ServerOutput(e.StackTrace + "\n");
                await _mainWindow.ServerOutput(e.TargetSite.ToString());
                return;
            }
            await _mainWindow.ServerOutput($"\nListening for incoming connections {_rfcommProvider.ServiceId.AsString()}\n");
        }

        private void InitializeServiceSdpAttributes(RfcommServiceProvider rfcommProvider)
        {
            //// とりあえずX1をパクる
            var sdp = new SdpData(4, "Hello Bluetooth");
            SdpInitializer.InitializeSdp(rfcommProvider, sdp, 0x0100);


            //// Bluetooth Profile Descriptor List
            //var ls = new List<SdpData>();
            //var ls2 = new List<SdpData>();
            //ls.Add(new SdpData(3, new byte[] { 0x11, 0x0D }));
            //ls.Add(new SdpData(1, new byte[] { 0x01, 0x03 }));
            //ls2.Add(new SdpData(6, ls.ToArray()));
            //sdp = new SdpData(6, ls2.ToArray());
            //SdpInitializer.InitializeSdp(rfcommProvider, sdp, 0x0009);


            // Supported Features
            sdp = new SdpData(1, new byte[] { 0x00, 0x3F });
            SdpInitializer.InitializeSdp(rfcommProvider, sdp, 0x0311);
        }

        private async void OnConnectionReceived(
            StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            await _mainWindow.ServerOutput("connection received\n");
            // Don't need the listener anymore
            _socketListener.Dispose();
            _socketListener = null;

            try
            {
                _socket = args.Socket;
            }
            catch (Exception e)
            {
                await _mainWindow.ServerOutput("failed on connection\n");
                Disconnect("failed");
                return;
            }

            // Note - this is the supported way to get a Bluetooth device from a given socket
            var remoteDevice = await BluetoothDevice.FromHostNameAsync(_socket.Information.RemoteHostName);

            var audioManager = new AudioManager(new DataReader(_socket.InputStream), new DataWriter(_socket.OutputStream));

            await _mainWindow.ServerOutput("Connected to Client: " + remoteDevice.Name);

            Task t1 = audioManager.Play();
            Task t2 = audioManager.Stream();

            await Task.WhenAll(t1, t2);

            audioManager.Dispose();
            Disconnect("Disconnected\n");
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

            await _mainWindow.ServerOutput(disconnection);
        }
    }
}
