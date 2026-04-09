using System;
using System.Collections.Generic;

namespace SaklSerial.Kmm
{
    public class InventoryResponseListActiveSuId : KmmBody
    {
        public InventoryType InventoryType { get; private set; }
        public bool ActiveSuId { get; private set; }
        public bool KeyAssigned { get; private set; }
        public SuId SuId { get; private set; }
        public Status Status { get; private set; }

        public override MessageId MessageId => MessageId.InventoryResponse;
        public override ResponseKind ResponseKind => ResponseKind.None;

        public InventoryResponseListActiveSuId(byte[] contents)
        {
            if (contents.Length != 10)
                throw new ArgumentOutOfRangeException(nameof(contents),
                    string.Format("length mismatch - expected 10, got {0}", contents.Length));

            InventoryType = (InventoryType)contents[0];
            ActiveSuId = (contents[1] & 0x01) != 0;
            KeyAssigned = (contents[1] & 0x02) != 0;

            byte[] suId = new byte[7];
            Array.Copy(contents, 2, suId, 0, 7);
            SuId = new SuId(suId);

            Status = (Status)contents[9];
        }

        public override byte[] ToBytes() => throw new NotImplementedException();
    }

    public class InventoryResponseListSuIdItems : KmmBody
    {
        public InventoryType InventoryType { get; private set; }
        public int InventoryMarker { get; private set; }
        public int NumberOfItems { get; private set; }
        public List<SuIdStatus> SuIdStatuses { get; private set; }

        public override MessageId MessageId => MessageId.InventoryResponse;
        public override ResponseKind ResponseKind => ResponseKind.None;

        public InventoryResponseListSuIdItems(byte[] contents)
        {
            if (contents.Length < 5)
                throw new ArgumentOutOfRangeException(nameof(contents),
                    string.Format("length mismatch - expected at least 5, got {0}", contents.Length));

            InventoryType = (InventoryType)contents[0];

            // Fixed: original code wrote TO contents instead of reading FROM it
            InventoryMarker = ((contents[1] & 0xFF) << 16)
                            | ((contents[2] & 0xFF) << 8)
                            | (contents[3] & 0xFF);

            NumberOfItems = contents[4] & 0xFF;

            SuIdStatuses = new List<SuIdStatus>();

            if (NumberOfItems == 0)
                return;

            int expectedDataLen = NumberOfItems * 8;
            int actualDataLen = contents.Length - 5;

            if (actualDataLen < expectedDataLen)
                throw new Exception(string.Format(
                    "item count ({0}) does not match data length ({1} bytes, expected {2})",
                    NumberOfItems, actualDataLen, expectedDataLen));

            for (int i = 0; i < NumberOfItems; i++)
            {
                byte[] suIdStatusBytes = new byte[8];
                Array.Copy(contents, 5 + (i * 8), suIdStatusBytes, 0, 8);
                SuIdStatuses.Add(new SuIdStatus(suIdStatusBytes));
            }
        }

        public override byte[] ToBytes() => throw new NotImplementedException();
    }
}
