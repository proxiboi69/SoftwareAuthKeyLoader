using System;

namespace SaklSerial.Kmm
{
    public class DeleteAuthenticationKeyResponse : KmmBody
    {
        public SuId SuId { get; private set; }
        public int NumKeysDeleted { get; private set; }
        public Status Status { get; private set; }

        public override MessageId MessageId => MessageId.DeleteAuthenticationKeyResponse;
        public override ResponseKind ResponseKind => ResponseKind.None;

        public DeleteAuthenticationKeyResponse(byte[] contents)
        {
            if (contents.Length != 10)
                throw new ArgumentOutOfRangeException(nameof(contents),
                    string.Format("length mismatch - expected 10, got {0}", contents.Length));

            byte[] suId = new byte[7];
            Array.Copy(contents, 0, suId, 0, 7);
            SuId = new SuId(suId);

            NumKeysDeleted = ((contents[7] & 0xFF) << 8) | (contents[8] & 0xFF);
            Status = (Status)contents[9];
        }

        public override byte[] ToBytes() => throw new NotImplementedException();
    }
}
