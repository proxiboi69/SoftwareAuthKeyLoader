using System;
using SaklSerial.Kmm;
using SaklSerial.Transport;

namespace SaklSerial
{
    internal static class Actions
    {
        public static int LoadAuthenticationKey(RadioTransport transport,
            bool targetSpecificSuId, int wacnId, int systemId, int unitId, byte[] key)
        {
            SuId suId = new SuId(wacnId, systemId, unitId);
            LoadAuthenticationKeyCommand cmd = new LoadAuthenticationKeyCommand(targetSpecificSuId, suId, key);
            KmmFrame cmdFrame = new KmmFrame(cmd);

            byte[] fromRadio = transport.QueryRadio(cmdFrame.ToBytes());
            KmmFrame rspFrame = new KmmFrame(fromRadio);

            if (rspFrame.KmmBody is LoadAuthenticationKeyResponse rsp)
            {
                if (rsp.AssignmentSuccess && rsp.Status == Status.CommandWasPerformed)
                    return 0;

                Console.Error.WriteLine("Abnormal response - success: {0}, status: {1} (0x{2:X2})",
                    rsp.AssignmentSuccess, rsp.Status, (byte)rsp.Status);
                return -1;
            }

            if (rspFrame.KmmBody is NegativeAcknowledgement nak)
            {
                Console.Error.WriteLine("Negative acknowledgement - msg: {0}, status: {1} (0x{2:X2})",
                    nak.AcknowledgedMessageId, nak.Status, (byte)nak.Status);
                return -1;
            }

            Console.Error.WriteLine("Unexpected response from radio");
            return -1;
        }

        public static int DeleteAuthenticationKey(RadioTransport transport,
            bool targetSpecificSuId, bool deleteAllKeys, int wacnId, int systemId, int unitId)
        {
            SuId suId = new SuId(wacnId, systemId, unitId);
            DeleteAuthenticationKeyCommand cmd = new DeleteAuthenticationKeyCommand(targetSpecificSuId, deleteAllKeys, suId);
            KmmFrame cmdFrame = new KmmFrame(cmd);

            byte[] fromRadio = transport.QueryRadio(cmdFrame.ToBytes());
            KmmFrame rspFrame = new KmmFrame(fromRadio);

            if (rspFrame.KmmBody is DeleteAuthenticationKeyResponse rsp)
            {
                if (rsp.Status == Status.CommandWasPerformed)
                {
                    Console.WriteLine("Keys deleted: {0}", rsp.NumKeysDeleted);
                    return 0;
                }

                Console.Error.WriteLine("Abnormal response - status: {0} (0x{1:X2})",
                    rsp.Status, (byte)rsp.Status);
                return -1;
            }

            if (rspFrame.KmmBody is NegativeAcknowledgement nak)
            {
                Console.Error.WriteLine("Negative acknowledgement - msg: {0}, status: {1} (0x{2:X2})",
                    nak.AcknowledgedMessageId, nak.Status, (byte)nak.Status);
                return -1;
            }

            Console.Error.WriteLine("Unexpected response from radio");
            return -1;
        }

        public static int ListActiveSuId(RadioTransport transport)
        {
            InventoryCommandListActiveSuId cmd = new InventoryCommandListActiveSuId();
            KmmFrame cmdFrame = new KmmFrame(cmd);

            byte[] fromRadio = transport.QueryRadio(cmdFrame.ToBytes());
            KmmFrame rspFrame = new KmmFrame(fromRadio);

            if (rspFrame.KmmBody is InventoryResponseListActiveSuId rsp)
            {
                if (rsp.Status == Status.CommandWasPerformed)
                {
                    Console.WriteLine("WACN: 0x{0:X}, System: 0x{1:X}, Unit: 0x{2:X}, Key Assigned: {3}, Active: {4}",
                        rsp.SuId.WacnId, rsp.SuId.SystemId, rsp.SuId.UnitId,
                        rsp.KeyAssigned, rsp.ActiveSuId);
                    return 0;
                }

                Console.Error.WriteLine("Abnormal response - status: {0} (0x{1:X2})",
                    rsp.Status, (byte)rsp.Status);
                return -1;
            }

            if (rspFrame.KmmBody is NegativeAcknowledgement nak)
            {
                Console.Error.WriteLine("Negative acknowledgement - msg: {0}, status: {1} (0x{2:X2})",
                    nak.AcknowledgedMessageId, nak.Status, (byte)nak.Status);
                return -1;
            }

            Console.Error.WriteLine("Unexpected response from radio");
            return -1;
        }

        public static int ListSuIdItems(RadioTransport transport)
        {
            int inventoryMarker = 0;

            do
            {
                InventoryCommandListSuIdItems cmd = new InventoryCommandListSuIdItems(inventoryMarker, 59);
                KmmFrame cmdFrame = new KmmFrame(cmd);

                byte[] fromRadio = transport.QueryRadio(cmdFrame.ToBytes());
                KmmFrame rspFrame = new KmmFrame(fromRadio);

                if (rspFrame.KmmBody is InventoryResponseListSuIdItems rsp)
                {
                    inventoryMarker = rsp.InventoryMarker;

                    foreach (SuIdStatus s in rsp.SuIdStatuses)
                    {
                        Console.WriteLine("WACN: 0x{0:X}, System: 0x{1:X}, Unit: 0x{2:X}, Key Assigned: {3}, Active: {4}",
                            s.SuId.WacnId, s.SuId.SystemId, s.SuId.UnitId,
                            s.KeyAssigned, s.ActiveSuId);
                    }
                }
                else if (rspFrame.KmmBody is NegativeAcknowledgement nak)
                {
                    Console.Error.WriteLine("Negative acknowledgement - msg: {0}, status: {1} (0x{2:X2})",
                        nak.AcknowledgedMessageId, nak.Status, (byte)nak.Status);
                    return -1;
                }
                else
                {
                    Console.Error.WriteLine("Unexpected response from radio");
                    return -1;
                }
            }
            while (inventoryMarker > 0);

            return 0;
        }
    }
}
