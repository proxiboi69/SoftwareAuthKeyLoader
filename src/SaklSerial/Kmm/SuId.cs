using System;

namespace SaklSerial.Kmm
{
    public class SuId
    {
        public int WacnId { get; private set; }
        public int SystemId { get; private set; }
        public int UnitId { get; private set; }

        public SuId(int wacnId, int systemId, int unitId)
        {
            if (wacnId < 0 || wacnId > 0xFFFFF)
                throw new ArgumentOutOfRangeException(nameof(wacnId));
            if (systemId < 0 || systemId > 0xFFF)
                throw new ArgumentOutOfRangeException(nameof(systemId));
            if (unitId < 0 || unitId > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(unitId));

            WacnId = wacnId;
            SystemId = systemId;
            UnitId = unitId;
        }

        public SuId(byte[] contents)
        {
            if (contents.Length != 7)
                throw new ArgumentOutOfRangeException(nameof(contents),
                    string.Format("length mismatch - expected 7, got {0}", contents.Length));

            WacnId = ((contents[0] & 0xFF) << 12)
                   | ((contents[1] & 0xFF) << 4)
                   | ((contents[2] >> 4) & 0x0F);

            SystemId = ((contents[2] & 0x0F) << 8)
                     | (contents[3] & 0xFF);

            UnitId = ((contents[4] & 0xFF) << 16)
                   | ((contents[5] & 0xFF) << 8)
                   | (contents[6] & 0xFF);
        }

        public byte[] ToBytes()
        {
            byte[] contents = new byte[7];
            contents[0] = (byte)((WacnId >> 12) & 0xFF);
            contents[1] = (byte)((WacnId >> 4) & 0xFF);
            contents[2] = (byte)(((WacnId << 4) & 0xF0) | ((SystemId >> 8) & 0x0F));
            contents[3] = (byte)(SystemId & 0xFF);
            contents[4] = (byte)((UnitId >> 16) & 0xFF);
            contents[5] = (byte)((UnitId >> 8) & 0xFF);
            contents[6] = (byte)(UnitId & 0xFF);
            return contents;
        }

        public override string ToString()
        {
            return string.Format("WACN=0x{0:X5} System=0x{1:X3} Unit=0x{2:X6}", WacnId, SystemId, UnitId);
        }
    }
}
