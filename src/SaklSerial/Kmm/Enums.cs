namespace SaklSerial.Kmm
{
    public enum MessageId : byte
    {
        InventoryCommand = 0x0D,
        InventoryResponse = 0x0E,
        NegativeAcknowledgement = 0x16,
        LoadAuthenticationKeyCommand = 0x28,
        LoadAuthenticationKeyResponse = 0x29,
        DeleteAuthenticationKeyCommand = 0x2A,
        DeleteAuthenticationKeyResponse = 0x2B
    }

    public enum AlgorithmId : byte
    {
        Clear = 0x80,
        AES128 = 0x85
    }

    public enum ResponseKind : byte
    {
        None = 0x00,
        Delayed = 0x01,
        Immediate = 0x02
    }

    public enum InventoryType : byte
    {
        ListActiveSuId = 0xF7,
        ListSuIdItems = 0xF8
    }

    public enum Status : byte
    {
        CommandWasPerformed = 0x00,
        CommandCouldNotBePerformed = 0x01,
        ItemDoesNotExist = 0x02,
        InvalidMessageId = 0x03,
        InvalidChecksumOrMac = 0x04,
        OutOfMemory = 0x05,
        CouldNotDecryptMessage = 0x06,
        InvalidMessageNumber = 0x07,
        InvalidKeyId = 0x08,
        InvalidAlgorithmId = 0x09,
        InvalidMfId = 0x0A,
        ModuleFailure = 0x0B,
        MiAllZeros = 0x0C,
        Keyfail = 0x0D,
        InvalidWacnIdOrSystemId = 0x0E,
        InvalidSubscriberId = 0x0F,
        Unknown = 0xFF
    }
}
