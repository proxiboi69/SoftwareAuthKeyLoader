namespace SaklSerial.Kmm
{
    public abstract class KmmBody
    {
        public abstract MessageId MessageId { get; }
        public abstract ResponseKind ResponseKind { get; }
        public abstract byte[] ToBytes();
    }
}
