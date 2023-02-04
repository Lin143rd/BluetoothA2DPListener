namespace BluetoothA2DPListener
{
    class Constants
    {
        public const UInt32 SdpServiceNameAttributeId = 0x0100;
        // type4: string
        // size5: サイズ指定子が8ビット 0~255
        public const byte SdpServiceNameAttributeType = (4 << 3) | 5;
        public const string SdpServiceName = "Hello Bluetooth";
        public const string SwitchConnectionHostName = "EC:C4:0D:F2:47:55";
        public const string SwitchConnectionServiceName = "Bluetooth#Bluetoothe8:48:b8:c8:20:00-ec:c4:0d:f2:47:55#RFCOMM:00000000:{0000110e-0000-1000-8000-00805f9b34fb}";
        public static readonly Guid RfcommServiceUuid = Guid.Parse("84B1CF4D-1069-4AD6-89B6-E161D79BE4D8");
        public static readonly UInt32 RfcommServiceUUidAudioSink = 0x1111110A;
        public static readonly UInt32 hoge = 0x0000111A;
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