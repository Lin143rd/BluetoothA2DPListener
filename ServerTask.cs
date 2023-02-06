using NAudio.Wave;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

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
                await _mainWindow.ServerOutput("uwaaaaaaaaaaaa");
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
        }

        private async void OnConnectionReceived(
            StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            await _mainWindow.ServerOutput("kityaaaaaaaaaaaa");
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

            _writer = new DataWriter(_socket.OutputStream);
            var reader = new DataReader(_socket.InputStream);
            bool remoteDisconnection = false;

            await _mainWindow.ServerOutput("Connected to Client: " + remoteDevice.Name);





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

            for (int i = 0; i + bufsize < data.Length; i += bufsize)
            {
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
                    await _mainWindow.ServerOutput("Remote Connection Disconnected");
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
                    await _mainWindow.ServerOutput(message);
                }
                // Catch exception HRESULT_FROM_WIN32(ERROR_OPERATION_ABORTED).
                catch (Exception ex) when ((uint)ex.HResult == 0x800703E3)
                {
                    await _mainWindow.ServerOutput("failed on connection\n");
                    break;
                }
            }

            reader.DetachStream();
            if (remoteDisconnection)
            {
                Disconnect("failed");
                await _mainWindow.ServerOutput("Client disconnected");
            }
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
