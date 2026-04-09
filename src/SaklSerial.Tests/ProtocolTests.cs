using System;
using Xunit;
using SaklSerial.Kmm;
using SaklSerial.Transport;

namespace SaklSerial.Tests
{
    public class FcsTests
    {
        [Fact]
        public void Fcs16_KnownVector_CorrectChecksum()
        {
            // RFC 1662 test vector: FCS of "123456789" = 0x906E
            byte[] data = System.Text.Encoding.ASCII.GetBytes("123456789");
            ushort fcs = Hdlc.CalculateFcs(data, 0, data.Length);
            Assert.Equal(0x906E, fcs);
        }

        [Fact]
        public void Fcs16_EmptyData_CorrectChecksum()
        {
            byte[] data = new byte[0];
            ushort fcs = Hdlc.CalculateFcs(data, 0, 0);
            // FCS of empty data with init 0xFFFF ^ 0xFFFF = 0x0000... actually:
            // init=0xFFFF, no iterations, result = 0xFFFF ^ 0xFFFF = 0x0000
            Assert.Equal((ushort)0x0000, fcs);
        }

        [Fact]
        public void Fcs16_SingleByte_Consistent()
        {
            byte[] data = new byte[] { 0x41 }; // 'A'
            ushort fcs1 = Hdlc.CalculateFcs(data, 0, 1);
            ushort fcs2 = Hdlc.CalculateFcs(data, 0, 1);
            Assert.Equal(fcs1, fcs2);
            Assert.NotEqual((ushort)0, fcs1);
        }
    }

    public class HdlcTests
    {
        [Fact]
        public void Frame_And_Parse_Roundtrip()
        {
            byte[] payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            ushort protocol = 0xC021; // LCP

            byte[] frame = Hdlc.Frame(protocol, payload, 0xFFFFFFFF);

            // Frame should start and end with 0x7E
            Assert.Equal(Hdlc.FLAG, frame[0]);
            Assert.Equal(Hdlc.FLAG, frame[frame.Length - 1]);

            // Parse it back
            bool ok = Hdlc.TryParse(frame, out ushort parsedProto, out byte[] parsedPayload);
            Assert.True(ok, "Frame should parse successfully");
            Assert.Equal(protocol, parsedProto);
            Assert.Equal(payload, parsedPayload);
        }

        [Fact]
        public void Frame_EscapesSpecialBytes()
        {
            // Payload containing 0x7E (flag) and 0x7D (escape) bytes
            byte[] payload = new byte[] { 0x7E, 0x7D, 0x00 };
            byte[] frame = Hdlc.Frame(0x0021, payload, 0xFFFFFFFF);

            // The 0x7E and 0x7D in payload should be escaped
            // 0x00 should also be escaped since ACCM=0xFFFFFFFF has bit 0 set
            // Verify we can roundtrip
            bool ok = Hdlc.TryParse(frame, out _, out byte[] parsedPayload);
            Assert.True(ok);
            Assert.Equal(payload, parsedPayload);
        }

        [Fact]
        public void Frame_ControlCharsEscaped_WithFullAccm()
        {
            // With ACCM=0xFFFFFFFF, all control chars 0x00-0x1F should be escaped
            byte[] payload = new byte[] { 0x03, 0x10, 0x1F };
            byte[] frame = Hdlc.Frame(0x0021, payload, 0xFFFFFFFF);

            bool ok = Hdlc.TryParse(frame, out _, out byte[] parsedPayload);
            Assert.True(ok);
            Assert.Equal(payload, parsedPayload);
        }

        [Fact]
        public void Frame_ControlCharsNotEscaped_WithZeroAccm()
        {
            byte[] payload = new byte[] { 0x03, 0x10, 0x1F };
            byte[] frame = Hdlc.Frame(0x0021, payload, 0x00000000);

            // Should still roundtrip fine
            bool ok = Hdlc.TryParse(frame, out _, out byte[] parsedPayload);
            Assert.True(ok);
            Assert.Equal(payload, parsedPayload);
        }

        [Fact]
        public void TryParse_GarbageData_ReturnsFalse()
        {
            byte[] garbage = new byte[] { 0x7E, 0x01, 0x02, 0x7E };
            bool ok = Hdlc.TryParse(garbage, out _, out _);
            Assert.False(ok); // too short or bad FCS
        }
    }

    public class SuIdTests
    {
        [Fact]
        public void SuId_EncodeAndDecode_Roundtrip()
        {
            int wacn = 0xA4398;
            int system = 0xF10;
            int unit = 0x99B584;

            SuId suId = new SuId(wacn, system, unit);
            byte[] bytes = suId.ToBytes();

            Assert.Equal(7, bytes.Length);

            SuId parsed = new SuId(bytes);
            Assert.Equal(wacn, parsed.WacnId);
            Assert.Equal(system, parsed.SystemId);
            Assert.Equal(unit, parsed.UnitId);
        }

        [Fact]
        public void SuId_ZeroValues()
        {
            SuId suId = new SuId(0, 0, 0);
            byte[] bytes = suId.ToBytes();
            Assert.All(bytes, b => Assert.Equal(0, b));

            SuId parsed = new SuId(bytes);
            Assert.Equal(0, parsed.WacnId);
            Assert.Equal(0, parsed.SystemId);
            Assert.Equal(0, parsed.UnitId);
        }

        [Fact]
        public void SuId_MaxValues()
        {
            SuId suId = new SuId(0xFFFFF, 0xFFF, 0xFFFFFF);
            byte[] bytes = suId.ToBytes();
            Assert.All(bytes, b => Assert.Equal(0xFF, b));

            SuId parsed = new SuId(bytes);
            Assert.Equal(0xFFFFF, parsed.WacnId);
            Assert.Equal(0xFFF, parsed.SystemId);
            Assert.Equal(0xFFFFFF, parsed.UnitId);
        }

        [Fact]
        public void SuId_InvalidLength_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SuId(new byte[6]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SuId(new byte[8]));
        }
    }

    public class KmmFrameTests
    {
        [Fact]
        public void LoadCommand_Serialization()
        {
            SuId suId = new SuId(0xA4398, 0xF10, 0x99B584);
            byte[] key = new byte[16];
            for (int i = 0; i < 16; i++) key[i] = (byte)i;

            LoadAuthenticationKeyCommand cmd = new LoadAuthenticationKeyCommand(true, suId, key);
            Assert.Equal(MessageId.LoadAuthenticationKeyCommand, cmd.MessageId);
            Assert.Equal(ResponseKind.Immediate, cmd.ResponseKind);

            byte[] body = cmd.ToBytes();
            Assert.Equal(30, body.Length); // 14 + 16

            // Verify authentication instruction bit 0 is set (targetSpecificSuId=true)
            Assert.Equal(0x01, body[4] & 0x01);

            // Verify algorithm id
            Assert.Equal((byte)AlgorithmId.AES128, body[12]);

            // Verify key length
            Assert.Equal(16, body[13]);
        }

        [Fact]
        public void KmmFrame_Roundtrip_LoadCommand()
        {
            SuId suId = new SuId(0x12345, 0xABC, 0xDEF012);
            byte[] key = new byte[16];
            for (int i = 0; i < 16; i++) key[i] = (byte)(0xF0 + i);

            LoadAuthenticationKeyCommand cmd = new LoadAuthenticationKeyCommand(false, suId, key);
            KmmFrame frame = new KmmFrame(cmd);
            byte[] serialized = frame.ToBytes();

            // Verify header fields
            Assert.Equal(0x00, serialized[0]);  // version
            Assert.Equal(0x00, serialized[1]);  // mfid
            Assert.Equal((byte)AlgorithmId.Clear, serialized[2]);  // algorithm
            Assert.Equal((byte)MessageId.LoadAuthenticationKeyCommand, serialized[14]); // message id
            Assert.Equal(0xFF, serialized[18]); // dest RSI
            Assert.Equal(0xFF, serialized[21]); // src RSI

            // Total should be 24 (header) + 30 (body) = 54
            Assert.Equal(54, serialized.Length);
        }

        [Fact]
        public void DeleteCommand_DeviceScope()
        {
            SuId suId = new SuId(0, 0, 0);
            DeleteAuthenticationKeyCommand cmd = new DeleteAuthenticationKeyCommand(false, true, suId);
            byte[] body = cmd.ToBytes();

            Assert.Equal(8, body.Length);
            // Bit 0 = targetSpecificSuId = false, Bit 1 = deleteAllKeys = true
            Assert.Equal(0x02, body[0] & 0x03);
        }

        [Fact]
        public void DeleteCommand_NamedScope()
        {
            SuId suId = new SuId(0xA4398, 0xF10, 0x99B584);
            DeleteAuthenticationKeyCommand cmd = new DeleteAuthenticationKeyCommand(true, false, suId);
            byte[] body = cmd.ToBytes();

            Assert.Equal(8, body.Length);
            // Bit 0 = targetSpecificSuId = true, Bit 1 = deleteAllKeys = false
            Assert.Equal(0x01, body[0] & 0x03);
        }

        [Fact]
        public void InventoryCommandListActiveSuId_Serialization()
        {
            InventoryCommandListActiveSuId cmd = new InventoryCommandListActiveSuId();
            byte[] body = cmd.ToBytes();
            Assert.Single(body);
            Assert.Equal((byte)InventoryType.ListActiveSuId, body[0]);
        }

        [Fact]
        public void InventoryCommandListSuIdItems_Serialization()
        {
            InventoryCommandListSuIdItems cmd = new InventoryCommandListSuIdItems(0x123, 59);
            byte[] body = cmd.ToBytes();
            Assert.Equal(6, body.Length);
            Assert.Equal((byte)InventoryType.ListSuIdItems, body[0]);
            // Verify marker encoding
            Assert.Equal(0x00, body[1]);
            Assert.Equal(0x01, body[2]);
            Assert.Equal(0x23, body[3]);
            // Verify max requested
            Assert.Equal(0x00, body[4]);
            Assert.Equal(59, body[5]);
        }

        [Fact]
        public void LoadAuthenticationKeyResponse_Parse()
        {
            // Simulate a successful response
            byte[] body = new byte[9];
            body[0] = 0x01; // assignmentSuccess = true
            // SuId bytes (7 bytes of 0xFF = max values)
            for (int i = 1; i <= 7; i++) body[i] = 0xFF;
            body[8] = 0x00; // Status = CommandWasPerformed

            LoadAuthenticationKeyResponse rsp = new LoadAuthenticationKeyResponse(body);
            Assert.True(rsp.AssignmentSuccess);
            Assert.Equal(Status.CommandWasPerformed, rsp.Status);
            Assert.Equal(0xFFFFF, rsp.SuId.WacnId);
            Assert.Equal(0xFFF, rsp.SuId.SystemId);
            Assert.Equal(0xFFFFFF, rsp.SuId.UnitId);
        }

        [Fact]
        public void DeleteAuthenticationKeyResponse_Parse()
        {
            byte[] body = new byte[10];
            // SuId = zeros
            // NumKeysDeleted = 3
            body[7] = 0x00;
            body[8] = 0x03;
            body[9] = 0x00; // CommandWasPerformed

            DeleteAuthenticationKeyResponse rsp = new DeleteAuthenticationKeyResponse(body);
            Assert.Equal(3, rsp.NumKeysDeleted);
            Assert.Equal(Status.CommandWasPerformed, rsp.Status);
        }

        [Fact]
        public void NegativeAcknowledgement_Parse()
        {
            byte[] body = new byte[] { (byte)MessageId.LoadAuthenticationKeyCommand, 0x01 };
            NegativeAcknowledgement nak = new NegativeAcknowledgement(body);
            Assert.Equal(MessageId.LoadAuthenticationKeyCommand, nak.AcknowledgedMessageId);
            Assert.Equal(Status.CommandCouldNotBePerformed, nak.Status);
        }

        [Fact]
        public void KmmFrame_ParseResponse_LoadSuccess()
        {
            // Build a complete KMM frame for a LoadAuthenticationKeyResponse
            byte[] responseBody = new byte[9];
            responseBody[0] = 0x01; // success
            for (int i = 1; i <= 7; i++) responseBody[i] = 0x00; // zero SuId
            responseBody[8] = 0x00; // CommandWasPerformed

            int msgLen = 7 + responseBody.Length;
            byte[] frame = new byte[24 + responseBody.Length];
            frame[14] = (byte)MessageId.LoadAuthenticationKeyResponse;
            frame[15] = (byte)((msgLen >> 8) & 0xFF);
            frame[16] = (byte)(msgLen & 0xFF);
            Array.Copy(responseBody, 0, frame, 24, responseBody.Length);

            KmmFrame parsed = new KmmFrame(frame);
            Assert.IsType<LoadAuthenticationKeyResponse>(parsed.KmmBody);

            var rsp = (LoadAuthenticationKeyResponse)parsed.KmmBody;
            Assert.True(rsp.AssignmentSuccess);
            Assert.Equal(Status.CommandWasPerformed, rsp.Status);
        }

        [Fact]
        public void KmmFrame_ParseResponse_InventoryListSuIdItems()
        {
            // Build a response with 1 SuId item
            byte[] item = new byte[8]; // 7 bytes SuId + 1 byte status
            item[7] = 0x03; // KeyAssigned=true, ActiveSuId=true

            byte[] responseBody = new byte[5 + 8];
            responseBody[0] = (byte)InventoryType.ListSuIdItems;
            responseBody[1] = 0; // marker high
            responseBody[2] = 0; // marker mid
            responseBody[3] = 0; // marker low (0 = last batch)
            responseBody[4] = 1; // 1 item
            Array.Copy(item, 0, responseBody, 5, 8);

            int msgLen = 7 + responseBody.Length;
            byte[] frame = new byte[24 + responseBody.Length];
            frame[14] = (byte)MessageId.InventoryResponse;
            frame[15] = (byte)((msgLen >> 8) & 0xFF);
            frame[16] = (byte)(msgLen & 0xFF);
            Array.Copy(responseBody, 0, frame, 24, responseBody.Length);

            KmmFrame parsed = new KmmFrame(frame);
            Assert.IsType<InventoryResponseListSuIdItems>(parsed.KmmBody);

            var rsp = (InventoryResponseListSuIdItems)parsed.KmmBody;
            Assert.Equal(0, rsp.InventoryMarker);
            Assert.Equal(1, rsp.NumberOfItems);
            Assert.Single(rsp.SuIdStatuses);
            Assert.True(rsp.SuIdStatuses[0].KeyAssigned);
            Assert.True(rsp.SuIdStatuses[0].ActiveSuId);
        }
    }

    public class IpUdpTests
    {
        [Fact]
        public void BuildAndParse_Roundtrip()
        {
            byte[] srcIp = new byte[] { 192, 168, 128, 2 };
            byte[] dstIp = new byte[] { 192, 168, 128, 1 };
            ushort srcPort = 50000;
            ushort dstPort = 49165;
            byte[] payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

            byte[] packet = IpUdp.BuildUdpPacket(srcIp, dstIp, srcPort, dstPort, payload);

            // Verify IP header basics
            Assert.Equal(0x45, packet[0]); // IPv4, IHL=5
            Assert.Equal(0x11, packet[9]); // UDP protocol

            bool ok = IpUdp.TryParseUdp(packet, out byte[] pSrcIp, out byte[] pDstIp,
                                          out ushort pSrcPort, out ushort pDstPort,
                                          out byte[] pPayload);

            Assert.True(ok);
            Assert.Equal(srcIp, pSrcIp);
            Assert.Equal(dstIp, pDstIp);
            Assert.Equal(srcPort, pSrcPort);
            Assert.Equal(dstPort, pDstPort);
            Assert.Equal(payload, pPayload);
        }

        [Fact]
        public void BuildUdpPacket_CorrectLength()
        {
            byte[] payload = new byte[100];
            byte[] packet = IpUdp.BuildUdpPacket(
                new byte[] { 1, 2, 3, 4 },
                new byte[] { 5, 6, 7, 8 },
                1234, 5678, payload);

            // IP header (20) + UDP header (8) + payload (100) = 128
            Assert.Equal(128, packet.Length);

            // Verify total length field in IP header
            int totalLen = (packet[2] << 8) | packet[3];
            Assert.Equal(128, totalLen);
        }

        [Fact]
        public void TryParseUdp_NonUdp_ReturnsFalse()
        {
            // Build a packet but change protocol to TCP (6)
            byte[] packet = IpUdp.BuildUdpPacket(
                new byte[] { 1, 2, 3, 4 },
                new byte[] { 5, 6, 7, 8 },
                1234, 5678, new byte[] { 0x00 });
            packet[9] = 0x06; // TCP instead of UDP

            bool ok = IpUdp.TryParseUdp(packet, out _, out _, out _, out _, out _);
            Assert.False(ok);
        }

        [Fact]
        public void TryParseUdp_TooShort_ReturnsFalse()
        {
            bool ok = IpUdp.TryParseUdp(new byte[10], out _, out _, out _, out _, out _);
            Assert.False(ok);
        }
    }

    public class SuIdStatusTests
    {
        [Fact]
        public void Parse_KeyAssignedAndActive()
        {
            byte[] data = new byte[8];
            data[7] = 0x03; // bit 0 = KeyAssigned, bit 1 = ActiveSuId
            SuIdStatus status = new SuIdStatus(data);
            Assert.True(status.KeyAssigned);
            Assert.True(status.ActiveSuId);
        }

        [Fact]
        public void Parse_NeitherAssignedNorActive()
        {
            byte[] data = new byte[8];
            data[7] = 0x00;
            SuIdStatus status = new SuIdStatus(data);
            Assert.False(status.KeyAssigned);
            Assert.False(status.ActiveSuId);
        }

        [Fact]
        public void Parse_InvalidLength_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SuIdStatus(new byte[7]));
        }
    }
}
