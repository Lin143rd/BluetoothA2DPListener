using NAudio.Wave;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using System.Net.Sockets;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Devices.Sms;

namespace BluetoothA2DPListener
{
    internal class AudioManager: IDisposable
    {

        private DataReader _reader;
        private DataWriter _writer;

        private BufferedWaveProvider _bufferedWaveProvider;
        private VolumeWaveProvider16 _wavProvider;
        private MMDevice _mmDevice;

        public AudioManager(DataReader reader, DataWriter writer)
        {
            _reader= reader;
            _writer= writer;
        }
        public async Task Stream()
        {
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
                catch (Exception ex)
                {
                    // The remote device has disconnected the connection
                    return;
                }
            }
        }

        public async Task Play()
        {
            _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
            _wavProvider = new VolumeWaveProvider16(_bufferedWaveProvider);
            _mmDevice = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            using (IWavePlayer wavPlayer = new WasapiOut(_mmDevice, AudioClientShareMode.Shared, false, 200))
            {
                wavPlayer.Init(_wavProvider);
                wavPlayer.Play();

                while (true)
                {
                    try
                    {
                        await ReceiveAudioBuffer(_bufferedWaveProvider, _reader);
                    }
                    catch (Exception ex)
                    {
                        break;
                    }
                }

                wavPlayer.Stop();
            }
        }
        private async Task ReceiveAudioBuffer(BufferedWaveProvider provider, DataReader reader)
        {
            try
            {
                uint size = await reader.LoadAsync(sizeof(uint));
                if (size < sizeof(uint))
                {
                    return;
                }

                uint bufferLength = reader.ReadUInt32();
                uint actualBufferLength = await reader.LoadAsync(bufferLength);
                if (actualBufferLength != bufferLength)
                {
                    // The underlying socket was closed before we were able to read the whole data
                    return;
                }

                var data = new byte[bufferLength];

                reader.ReadBytes(data);

                int bufsize = 4410;

                for (int i = 0; i + bufsize < data.Length; i += bufsize)
                {
                    while (provider.BufferedBytes + bufsize >= provider.BufferLength)
                        await Task.Delay(10);
                    provider.AddSamples(data, i, bufsize);
                    await Task.Delay(10);
                }
            }
            catch (Exception ex)
            {
                return;
            }
        }

        public void Dispose()
        {
            _reader.DetachStream();
            _writer.DetachStream();
        }
    }
}
