using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using System;

namespace BluetoothA2DPListener
{
    class BluetoothAnalyzer
    {
        public static async ValueTask<string> AnalyzeServiceResult(RfcommDeviceServicesResult result)
        {
            if (result.Error != BluetoothError.Success)
                return result.Error.ToString();
            return await AnalyzeServiceList(result.Services);
        }
        public static async ValueTask<string> AnalyzeServiceList(IReadOnlyList<RfcommDeviceService> services)
        {
            var serviceRecord = new StringBuilder();
            for (int i = 0; i < services.Count; ++i)
            {
                serviceRecord.Append($"-------------Service{i + 1}-------------\n");
                serviceRecord.Append(await AnalyzeService(services[i]));
                serviceRecord.Append("\n");
            }
            return serviceRecord.ToString();
        }
        public static async ValueTask<string> AnalyzeService(RfcommDeviceService service)
        {
            var attributeList = await service.GetSdpRawAttributesAsync();
            var attributeRecord = new StringBuilder();
            foreach (var attribute in attributeList)
            {
                attributeRecord.Append(AnalyzeAttribute(attribute));
                attributeRecord.Append("\n");
            }
            return attributeRecord.ToString();
        }
        public static string AnalyzeAttribute(KeyValuePair<UInt32, IBuffer> attribute)
        {
            var record = new StringBuilder();

            var attributeId = attribute.Key;

            record.Append($"AttributeId: 0x{attributeId:X4} -- ");

            if (Constants.attributeList.ContainsKey(attributeId))
            {
                var attributeName = Constants.attributeList[attributeId];
                record.Append(attributeName);
                record.Append("\n");
                var reader = DataReader.FromBuffer(attribute.Value);
                record.Append(SdpAnalyzer.AnalyzeSdp(reader));
            }
            else
            {
                record.Append("Unknown Attribute\n");
            }

            return record.ToString();
        }
    }
}