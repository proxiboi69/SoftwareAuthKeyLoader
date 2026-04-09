using System;
using System.Collections;

namespace SaklSerial.Kmm
{
    public class DeleteAuthenticationKeyCommand : KmmBody
    {
        public bool TargetSpecificSuId { get; private set; }
        public bool DeleteAllKeys { get; private set; }
        public SuId SuId { get; private set; }

        public override MessageId MessageId => MessageId.DeleteAuthenticationKeyCommand;
        public override ResponseKind ResponseKind => ResponseKind.Immediate;

        public DeleteAuthenticationKeyCommand(bool targetSpecificSuId, bool deleteAllKeys, SuId suId)
        {
            SuId = suId ?? throw new ArgumentNullException(nameof(suId));
            TargetSpecificSuId = targetSpecificSuId;
            DeleteAllKeys = deleteAllKeys;
        }

        public override byte[] ToBytes()
        {
            byte[] contents = new byte[8];

            BitArray authInstruction = new BitArray(8, false);
            authInstruction.Set(0, TargetSpecificSuId);
            authInstruction.Set(1, DeleteAllKeys);
            authInstruction.CopyTo(contents, 0);

            byte[] suId = SuId.ToBytes();
            Array.Copy(suId, 0, contents, 1, suId.Length);

            return contents;
        }
    }
}
