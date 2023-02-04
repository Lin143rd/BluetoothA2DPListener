using System.Text;
using Windows.Storage.Streams;

namespace BluetoothA2DPListener
{
    class SdpAnalyzer
    {
        public static string AnalyzeSdp(DataReader reader)
        {
            var result = new StringBuilder();
            while (reader.UnconsumedBufferLength > 0)
            {
                var analyzer = new SdpDataElementAnalyzer(reader, 0);
                result.Append(analyzer.AnalyzeDataElement());
            }
            return result.ToString();
        }
        class SdpDataElementAnalyzer
        {
            private const char NestIdentiier = '\t';
            private DataReader _reader;
            private readonly int _rank;
            private int _byteCounter;
            public int ByteCounter { get { return _byteCounter; } }
            public SdpDataElementAnalyzer(DataReader reader, int rank)
            {
                _reader = reader;
                _rank = rank;
                _byteCounter = 0;
            }

            // 上位5bitがType,下位3bitがSize
            // Size
            // 0: 1 byte if not null else 0 byte
            // 1: 2byte
            // 2: 4byte
            // 3: 8byte
            // 4: 16byte
            // 5: next datasize 1byte
            // 6: next datasize 2byte
            // 7: next datasize 4byte
            public string AnalyzeDataElement()
            {
                int header = (int)_reader.ReadByte();
                _byteCounter++;
                int type = (header & (31 << 3)) >> 3;
                int size = AnalyzeDataSize(header & 7);
                return AnalyzeDataContent(type, size);
            }

            private string AnalyzeDataType(int type)
            {
                switch (type)
                {
                    case 0:
                        return "NULL";
                    case 1:
                        return "Unsigned Integer";
                    case 2:
                        return "Signed twos-component integer";
                    case 3:
                        return "UUID";
                    case 4:
                        return "String";
                    case 5:
                        return "Boolean";
                    case 6:
                        return "Array";
                    case 7:
                        return "Alternative";
                    case 8:
                        return "URL";
                    default:
                        return "Reserved";
                }
            }

            private int AnalyzeDataSize(int size)
            {
                switch (size)
                {
                    case 0:
                        return 1;
                    case 1:
                        return 2;
                    case 2:
                        return 4;
                    case 3:
                        return 8;
                    case 4:
                        return 16;
                    case 5:
                        _byteCounter++;
                        return (int)_reader.ReadByte();
                    case 6:
                        _byteCounter += 2;
                        return (int)_reader.ReadInt16();
                    case 7:
                        _byteCounter += 4;
                        return (int)_reader.ReadInt32();
                    default:
                        return -1;
                }
            }

            private string AnalyzeDataContent(int type, int size)
            {
                var result = new StringBuilder();
                result.Append(NestIdentiier, _rank);
                result.Append($"{AnalyzeDataType(type)}: ");
                switch (type)
                {
                    case 0:
                        result.Append("\n");
                        break;
                    case 1:
                    case 2:
                    case 3:
                        result.Append("0x");
                        for (int i = 0; i < size; ++i)
                        {
                            result.Append($"{_reader.ReadByte():X2}");
                            _byteCounter++;
                        }
                        result.Append("\n");
                        break;
                    case 4:
                    case 8:
                        var buf = new byte[size];
                        _reader.ReadBytes(buf);
                        _byteCounter += size;
                        result.Append(new string(Encoding.UTF8.GetChars(buf)));
                        result.Append("\n");
                        break;
                    case 5:
                        result.Append($"{(_reader.ReadByte() == 0 ? "False" : "True")}\n");
                        _byteCounter++;
                        break;
                    case 6:
                    case 7:
                        result.Append("\n");
                        int readBytes = 0;
                        while (readBytes < size)
                        {
                            var analyzer = new SdpDataElementAnalyzer(_reader, _rank + 1);
                            result.Append(analyzer.AnalyzeDataElement());
                            readBytes += analyzer.ByteCounter;
                        }
                        _byteCounter += readBytes;
                        break;
                    default:
                        result.Append("\n");
                        break;
                }
                return result.ToString();
            }
        }
    }
}