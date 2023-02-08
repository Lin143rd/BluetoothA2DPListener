namespace BluetoothA2DPListener
{
    class Constants
    {
        // type4: string
        // size5: サイズ指定子が8ビット 0~255
        public const string SdpServiceName = "Hello Bluetooth";
        public static readonly Guid RfcommServiceUuid = Guid.Parse("84B1CF4D-1069-4AD6-89B6-E161D79BE4D8");
        public static readonly UInt32 RfcommServiceUUidAudioSink = 0x0000110A;
        public static readonly Dictionary<UInt32, string> attributeList = new Dictionary<UInt32, string>()
        {
            {0x0000, "ServiceRecordHandle"},
            {0x0001, "ServiceClassIDList"},
            {0x0002, "ServiceRecordState"},
            {0x0003, "ServiceID"},
            {0x0004, "ProtocolDescriptorList"},
            {0x0005, "BrowseGroupList"},
            {0x0006, "LanguageBaseAttributeIDList"},
            {0x0007, "ServiceInfoTimeToLive"},
            {0x0008, "ServiceAvailability"},
            {0x0009, "BluetoothProfileDescriptorList"},
            {0x000A, "DocumentationURL"},
            {0x000B, "ClientExecutableURL"},
            {0x000C, "IconURL"},
            {0x000D, "AdditionalProtocolDescriptorLists"},
            {0x0100, "ServiceName"},
            {0x0101, "ServiceDescription"},
            {0x0102, "ProviderName"},
            {0x0200, "GoepL2capPsm (BIP v1.1 and later"},
            {0x0311, "SupportedFeature"}
        };
    }
}