using System;

namespace SaklSerial.Kmm
{
    public class LoadAuthenticationKeyResponse : KmmBody
    {
        public bool AssignmentSuccess { get; private set; }
        public SuId SuId { get; private set; }
        public Status Status { get; private set; }

        public override MessageId MessageId => MessageId.LoadAuthenticationKeyResponse;
        public override ResponseKind ResponseKind => ResponseKind.None;

        public LoadAuthenticationKeyResponse(byte[] contents)
        {
            if (contents.Length != 9)
                throw new ArgumentOutOfRangeException(nameof(contents),
                    string.Format("length mismatch - expected 9, got {0}", contents.Length));

            AssignmentSuccess = (contents[0] & 0x01) != 0;

            byte[] suId = new byte[7];
            Array.Copy(contents, 1, suId, 0, 7);
            SuId = new SuId(suId);

            Status = (Status)contents[8];
        }

        public override byte[] ToBytes() => throw new NotImplementedException();
    }
}
