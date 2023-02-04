using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Storage.Streams;
using System.Threading.Tasks;
using System.Windows;

namespace BluetoothA2DPListener
{
    public class SdpData
    {
        private int _type;
        public int Type { get { return _type; } }
        private dynamic _data;
        public dynamic Data { get { return _data; } }
        public SdpData(int type, dynamic data)
        {
            _type = type;
            _data = data;
        }

        public bool CheckIfInteger()
        {
            return (_type == 1 || _type == 2 || _type == 3) && (_data.GetType() == typeof(byte[]));
        }

        public bool CheckIfString()
        {
            return (_type == 4 || _type == 8) && (_data.GetType() == typeof(string));
        }

        public bool CheckIfBoolean()
        {
            return (_type == 5) && (_data.GetType() == typeof(bool));
        }

        public bool CheckIfArray()
        {
            return (_type == 6 || _type == 7) && (_data.GetType().IsArray) && (_data[0].GetType() == typeof(SdpData));
        }

        // バイト数を返す（ヘッダー含む）
        public int GetDataSize()
        {
            if (CheckIfArray())
            {
                int childSize = 0;
                foreach (var datum in _data)
                {
                    childSize += datum.GetDataSize();
                }
                childSize += GetOptionalDataSize();
                return childSize + 1;
            }
            if (CheckIfString())
            {
                int stringSize = _data.Length;
                stringSize += GetOptionalDataSize();
                return stringSize + 1;
            }
            if (CheckIfInteger())
            {
                int numberSize = _data.Length;
                return numberSize + 1;
            }
            if (CheckIfBoolean())
            {
                return 2;
            }
            return 1;
        }

        // 配列の子のデータサイズを返す
        public int GetChildDataSize()
        {
            if (CheckIfArray())
            {
                int childSize = 0;
                foreach (var datum in _data)
                {
                    childSize += datum.GetDataSize();
                }
                return childSize;
            }
            return -1;
        }

        // データサイズを別で送信する時のバイト数を返す
        public int GetOptionalDataSize()
        {
            if (CheckIfArray())
            {
                // 子供だけ足す
                int childSize = GetChildDataSize();
                return GetHeaderDataSize(childSize);
            }
            if (CheckIfString())
            {
                int stringSize = _data.Length;
                return GetHeaderDataSize(stringSize);
            }
            return -1;
        }

        // データサイズを別で送信する時のヘッダーのsizeindexを返す
        public static int GetOptionalSizeIndex(int length)
        {
            if (length <= byte.MaxValue)
                return 5;
            if (length <= ushort.MaxValue)
                return 6;
            return 7;
        }

        // データサイズを別で送信する時の必要バイト数を返す
        private int GetHeaderDataSize(int length)
        {
            if (length <= byte.MaxValue)
                return 1;
            if (length <= ushort.MaxValue)
                return 2;
            return 4;
        }
    }
    public class SdpInitializer
    {
        public static void InitializeSdp(RfcommServiceProvider rfcommProvider, SdpData sdp, UInt32 attributeId)
        {
            var sdpWriter = new DataWriter();
            SetSdpData(sdpWriter, sdp);
            rfcommProvider.SdpRawAttributes.Add(attributeId, sdpWriter.DetachBuffer());
        }

        private static void SetSdpData(DataWriter sdpWriter, SdpData sdp)
        {
            if (sdp.CheckIfArray())
            {
                SetArray(sdpWriter, sdp);
            }
            else if (sdp.CheckIfString())
            {
                SetString(sdpWriter, sdp);
            }
            else if (sdp.CheckIfInteger())
            {
                SetInteger(sdpWriter, sdp);
            }
            else if (sdp.CheckIfBoolean())
            {
                SetBoolean(sdpWriter, sdp);
            }
            else
            {
                SetNull(sdpWriter, sdp);
            }
        }

        private static void SetNull(DataWriter sdpWriter, SdpData sdp)
        {
            // header

            int headerType = 0;
            int headerSize = 0;
            int header = headerType + headerSize;
            sdpWriter.WriteByte(Convert.ToByte(header));

            // data

        }

        private static void SetInteger(DataWriter sdpWriter, SdpData sdp)
        {
            // header

            int headerType = sdp.Type << 3;
            int headerSize = 0;
            int length = sdp.Data.Length;
            int tmpLength = length;
            while (tmpLength > 1)
            {
                tmpLength /= 2;
                headerSize++;
            }
            int header = headerType + headerSize;
            sdpWriter.WriteByte(Convert.ToByte(header));

            // data
            sdpWriter.WriteBytes(sdp.Data);
        }

        private static void SetString(DataWriter sdpWriter, SdpData sdp)
        {
            // header

            int headerType = sdp.Type << 3;
            int length = sdp.Data.Length;
            int headerSize = SdpData.GetOptionalSizeIndex(length);
            int header = headerType + headerSize;
            sdpWriter.WriteByte(Convert.ToByte(header));
            switch (headerSize)
            {
                case 5:
                    sdpWriter.WriteByte(Convert.ToByte(length));
                    break;
                case 6:
                    sdpWriter.WriteInt16(Convert.ToInt16(length));
                    break;
                case 7:
                    sdpWriter.WriteInt32(length);
                    break;
            }

            // data
            sdpWriter.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            sdpWriter.WriteString(sdp.Data);
        }

        private static void SetBoolean(DataWriter sdpWriter, SdpData sdp)
        {
            // header

            int headerType = sdp.Type << 3;
            int headerSize = 0;
            int header = headerType + headerSize;
            sdpWriter.WriteByte(Convert.ToByte(header));

            // data
            sdpWriter.WriteBoolean(sdp.Data);
        }

        private static void SetArray(DataWriter sdpWriter, SdpData sdp)
        {
            // header

            int headerType = sdp.Type << 3;
            int length = sdp.GetChildDataSize();
            int headerSize = SdpData.GetOptionalSizeIndex(length);
            int header = headerType + headerSize;
            sdpWriter.WriteByte(Convert.ToByte(header));
            switch (headerSize)
            {
                case 5:
                    sdpWriter.WriteByte(Convert.ToByte(length));
                    break;
                case 6:
                    sdpWriter.WriteInt16(Convert.ToInt16(length));
                    break;
                case 7:
                    sdpWriter.WriteInt32(length);
                    break;
            }

            // data
            foreach (var datum in sdp.Data)
            {
                SetSdpData(sdpWriter, datum);
            }
        }
    }
}