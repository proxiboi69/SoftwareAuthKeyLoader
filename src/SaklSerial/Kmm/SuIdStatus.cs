using System;

namespace SaklSerial.Kmm
{
    public class SuIdStatus
    {
        public SuId SuId { get; private set; }
        public bool KeyAssigned { get; private set; }
        public bool ActiveSuId { get; private set; }

        public SuIdStatus(byte[] contents)
        {
            if (contents.Length != 8)
                throw new ArgumentOutOfRangeException(nameof(contents),
                    string.Format("length mismatch - expected 8, got {0}", contents.Length));

            byte[] suId = new byte[7];
            Array.Copy(contents, 0, suId, 0, 7);
            SuId = new SuId(suId);

            KeyAssigned = (contents[7] & 0x01) != 0;
            ActiveSuId = (contents[7] & 0x02) != 0;
        }

        public override string ToString()
        {
            return string.Format("[{0}, KeyAssigned={1}, Active={2}]", SuId, KeyAssigned, ActiveSuId);
        }
    }
}
