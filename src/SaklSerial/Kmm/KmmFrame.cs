using System;
using System.Collections;

namespace SaklSerial.Kmm
{
    public class KmmFrame
    {
        public KmmBody KmmBody { get; private set; }

        public KmmFrame(KmmBody kmmBody)
        {
            KmmBody = kmmBody ?? throw new ArgumentNullException(nameof(kmmBody));
        }

        public KmmFrame(byte[] contents)
        {
            Parse(contents);
        }

        public byte[] ToBytes()
        {
            byte[] body = KmmBody.ToBytes();
            int length = 24 + body.Length;
            byte[] contents = new byte[length];

            // version
            contents[0] = 0x00;
            // mfid
            contents[1] = 0x00;
            // algorithm id
            contents[2] = (byte)AlgorithmId.Clear;
            // key id
            contents[3] = 0x00;
            contents[4] = 0x00;
            // message indicator (9 bytes of zeros)
            // already zero from array init

            // KMM header
            // message id
            contents[14] = (byte)KmmBody.MessageId;

            // message length (7 + body length)
            int messageLength = 7 + body.Length;
            contents[15] = (byte)((messageLength >> 8) & 0xFF);
            contents[16] = (byte)(messageLength & 0xFF);

            // message format (response kind bits)
            BitArray messageFormat = new BitArray(8, false);
            messageFormat.Set(7, (((byte)KmmBody.ResponseKind & 0x02) >> 1) != 0);
            messageFormat.Set(6, ((byte)KmmBody.ResponseKind & 0x01) != 0);
            messageFormat.CopyTo(contents, 17);

            // destination RSI
            contents[18] = 0xFF;
            contents[19] = 0xFF;
            contents[20] = 0xFF;

            // source RSI
            contents[21] = 0xFF;
            contents[22] = 0xFF;
            contents[23] = 0xFF;

            // message body
            Array.Copy(body, 0, contents, 24, body.Length);

            return contents;
        }

        private void Parse(byte[] contents)
        {
            if (contents.Length <= 17)
                throw new ArgumentOutOfRangeException(nameof(contents),
                    string.Format("length mismatch - expected at least 18, got {0}", contents.Length));

            byte messageId = contents[14];

            int messageLength = ((contents[15] & 0xFF) << 8) | (contents[16] & 0xFF);
            int messageBodyLength = messageLength - 7;
            byte[] messageBody = new byte[messageBodyLength];
            Array.Copy(contents, 24, messageBody, 0, messageBodyLength);

            switch ((MessageId)messageId)
            {
                case MessageId.InventoryResponse:
                    if (messageBody.Length == 0)
                        throw new Exception("inventory response length zero");
                    InventoryType inventoryType = (InventoryType)messageBody[0];
                    switch (inventoryType)
                    {
                        case InventoryType.ListActiveSuId:
                            KmmBody = new InventoryResponseListActiveSuId(messageBody);
                            break;
                        case InventoryType.ListSuIdItems:
                            KmmBody = new InventoryResponseListSuIdItems(messageBody);
                            break;
                        default:
                            throw new Exception(string.Format("unknown inventory response type: 0x{0:X2}", (byte)inventoryType));
                    }
                    break;

                case MessageId.NegativeAcknowledgement:
                    KmmBody = new NegativeAcknowledgement(messageBody);
                    break;

                case MessageId.LoadAuthenticationKeyResponse:
                    KmmBody = new LoadAuthenticationKeyResponse(messageBody);
                    break;

                case MessageId.DeleteAuthenticationKeyResponse:
                    KmmBody = new DeleteAuthenticationKeyResponse(messageBody);
                    break;

                default:
                    throw new Exception(string.Format("unknown kmm - message id: 0x{0:X2}", messageId));
            }
        }
    }
}
