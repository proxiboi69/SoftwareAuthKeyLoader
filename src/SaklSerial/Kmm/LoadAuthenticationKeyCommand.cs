using System;
using System.Collections;

namespace SaklSerial.Kmm
{
    public class LoadAuthenticationKeyCommand : KmmBody
    {
        public bool TargetSpecificSuId { get; private set; }
        public SuId SuId { get; private set; }
        public AlgorithmId InnerAlgorithmId { get; private set; }
        public byte[] Key { get; private set; }

        public override MessageId MessageId => MessageId.LoadAuthenticationKeyCommand;
        public override ResponseKind ResponseKind => ResponseKind.Immediate;

        public LoadAuthenticationKeyCommand(bool targetSpecificSuId, SuId suId, byte[] key)
        {
            if (suId == null) throw new ArgumentNullException(nameof(suId));
            if (key.Length != 16)
                throw new ArgumentOutOfRangeException(nameof(key),
                    string.Format("length mismatch - expected 16, got {0}", key.Length));

            TargetSpecificSuId = targetSpecificSuId;
            SuId = suId;
            InnerAlgorithmId = AlgorithmId.AES128;
            Key = key;
        }

        public override byte[] ToBytes()
        {
            byte[] contents = new byte[14 + Key.Length];

            // decryption instruction format
            contents[0] = 0x00;
            // outer algorithm id
            contents[1] = (byte)AlgorithmId.Clear;
            // key id
            contents[2] = 0x00;
            contents[3] = 0x00;

            // authentication instruction
            BitArray authInstruction = new BitArray(8, false);
            authInstruction.Set(0, TargetSpecificSuId);
            authInstruction.CopyTo(contents, 4);

            // suid
            byte[] suId = SuId.ToBytes();
            Array.Copy(suId, 0, contents, 5, suId.Length);

            // inner algorithm id
            contents[12] = (byte)InnerAlgorithmId;

            // key length
            contents[13] = (byte)Key.Length;

            // key data
            Array.Copy(Key, 0, contents, 14, Key.Length);

            return contents;
        }
    }
}
