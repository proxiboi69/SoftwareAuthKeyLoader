using System;

namespace SaklSerial.Kmm
{
    public class InventoryCommandListActiveSuId : KmmBody
    {
        public override MessageId MessageId => MessageId.InventoryCommand;
        public override ResponseKind ResponseKind => ResponseKind.Immediate;

        public override byte[] ToBytes()
        {
            return new byte[] { (byte)InventoryType.ListActiveSuId };
        }
    }

    public class InventoryCommandListSuIdItems : KmmBody
    {
        public int InventoryMarker { get; private set; }
        public int MaxSuIdRequested { get; private set; }

        public override MessageId MessageId => MessageId.InventoryCommand;
        public override ResponseKind ResponseKind => ResponseKind.Immediate;

        public InventoryCommandListSuIdItems(int inventoryMarker, int maxSuIdRequested)
        {
            if (inventoryMarker < 0 || inventoryMarker > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(inventoryMarker));
            if (maxSuIdRequested < 0 || maxSuIdRequested > 0xFFFF)
                throw new ArgumentOutOfRangeException(nameof(maxSuIdRequested));

            InventoryMarker = inventoryMarker;
            MaxSuIdRequested = maxSuIdRequested;
        }

        public override byte[] ToBytes()
        {
            byte[] contents = new byte[6];
            contents[0] = (byte)InventoryType.ListSuIdItems;
            contents[1] = (byte)((InventoryMarker >> 16) & 0xFF);
            contents[2] = (byte)((InventoryMarker >> 8) & 0xFF);
            contents[3] = (byte)(InventoryMarker & 0xFF);
            contents[4] = (byte)((MaxSuIdRequested >> 8) & 0xFF);
            contents[5] = (byte)(MaxSuIdRequested & 0xFF);
            return contents;
        }
    }
}
