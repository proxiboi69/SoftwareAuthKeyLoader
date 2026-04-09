using System;

namespace SaklSerial.Kmm
{
    public class NegativeAcknowledgement : KmmBody
    {
        public MessageId AcknowledgedMessageId { get; private set; }
        public Status Status { get; private set; }

        public override MessageId MessageId => MessageId.NegativeAcknowledgement;
        public override ResponseKind ResponseKind => ResponseKind.None;

        public NegativeAcknowledgement(byte[] contents)
        {
            if (contents.Length != 2)
                throw new ArgumentOutOfRangeException(nameof(contents),
                    string.Format("length mismatch - expected 2, got {0}", contents.Length));

            AcknowledgedMessageId = (MessageId)contents[0];
            Status = (Status)contents[1];
        }

        public override byte[] ToBytes() => throw new NotImplementedException();
    }
}
