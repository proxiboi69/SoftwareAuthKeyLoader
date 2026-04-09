using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace SaklSerial.Transport
{
    /// <summary>
    /// Minimal PPP client that handles LCP and IPCP negotiation over a serial port.
    /// Provides the ability to send/receive IP packets through the PPP tunnel.
    /// </summary>
    internal class PppClient : IDisposable
    {
        // PPP protocol numbers
        private const ushort PROTO_LCP = 0xC021;
        private const ushort PROTO_IPCP = 0x8021;
        private const ushort PROTO_IP = 0x0021;

        // LCP/IPCP codes
        private const byte CODE_CONF_REQ = 1;
        private const byte CODE_CONF_ACK = 2;
        private const byte CODE_CONF_NAK = 3;
        private const byte CODE_CONF_REJ = 4;
        private const byte CODE_TERM_REQ = 5;
        private const byte CODE_TERM_ACK = 6;
        private const byte CODE_PROT_REJ = 8;

        // LCP options
        private const byte LCP_OPT_MRU = 1;
        private const byte LCP_OPT_ACCM = 2;
        private const byte LCP_OPT_AUTH = 3;
        private const byte LCP_OPT_MAGIC = 5;
        private const byte LCP_OPT_PFC = 7;   // Protocol Field Compression
        private const byte LCP_OPT_ACFC = 8;  // Addr/Control Field Compression

        // IPCP options
        private const byte IPCP_OPT_ADDR = 3;

        private readonly SerialPort _serial;
        private byte _lcpIdent = 1;
        private byte _ipcpIdent = 1;
        private bool _lcpUp;
        private bool _ipcpUp;

        // Negotiated ACCM — start with all control chars escaped
        private uint _sendAccm = 0xFFFFFFFF;

        // IP addresses
        public byte[] LocalIp { get; private set; }
        public byte[] RemoteIp { get; private set; }

        private readonly bool _verbose;

        public PppClient(SerialPort serial, bool verbose = false)
        {
            _serial = serial;
            _verbose = verbose;
            LocalIp = new byte[] { 0, 0, 0, 0 };
            RemoteIp = new byte[] { 192, 168, 128, 1 }; // default radio IP
        }

        /// <summary>
        /// Perform the CLIENT/CLIENTSERVER modem handshake, then negotiate PPP.
        /// </summary>
        public void Connect(int timeoutMs = 10000)
        {
            Log("Sending modem handshake...");
            ModemHandshake(timeoutMs);

            Log("Negotiating LCP...");
            NegotiateLcp(timeoutMs);

            Log("Negotiating IPCP...");
            NegotiateIpcp(timeoutMs);

            Log("PPP link up. Local IP: {0}, Remote IP: {1}",
                FormatIp(LocalIp), FormatIp(RemoteIp));
        }

        /// <summary>
        /// Send an IP packet through the PPP tunnel.
        /// </summary>
        public void SendIp(byte[] ipPacket)
        {
            byte[] frame = Hdlc.Frame(PROTO_IP, ipPacket, _sendAccm);
            _serial.Write(frame, 0, frame.Length);
        }

        /// <summary>
        /// Receive an IP packet from the PPP tunnel.
        /// Handles and responds to any LCP/IPCP control traffic transparently.
        /// </summary>
        public byte[] ReceiveIp(int timeoutMs = 5000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) break;

                byte[] raw = ReadFrame(remaining);
                if (raw == null) continue;

                if (!Hdlc.TryParse(raw, out ushort proto, out byte[] payload))
                    continue;

                switch (proto)
                {
                    case PROTO_IP:
                        return payload;

                    case PROTO_LCP:
                        HandleLcpPacket(payload);
                        break;

                    case PROTO_IPCP:
                        HandleIpcpPacket(payload);
                        break;

                    default:
                        // Send protocol reject for unknown protocols
                        SendProtocolReject(proto, payload);
                        break;
                }
            }

            throw new TimeoutException("Timed out waiting for IP packet from radio");
        }

        public void Disconnect()
        {
            try
            {
                // Send LCP Terminate-Request
                byte[] termReq = MakeControlPacket(CODE_TERM_REQ, _lcpIdent++, new byte[0]);
                SendControl(PROTO_LCP, termReq);
            }
            catch { /* best effort */ }
        }

        public void Dispose()
        {
            Disconnect();
        }

        // ── Modem Handshake ──────────────────────────────────────────────

        private void ModemHandshake(int timeoutMs)
        {
            // Flush any stale data
            _serial.DiscardInBuffer();
            _serial.DiscardOutBuffer();

            // Send CLIENT
            byte[] clientCmd = System.Text.Encoding.ASCII.GetBytes("CLIENT");
            _serial.Write(clientCmd, 0, clientCmd.Length);

            // Wait for CLIENTSERVER response
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            string buffer = "";
            string expected = "CLIENTSERVER";

            while (DateTime.UtcNow < deadline)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) break;

                _serial.ReadTimeout = Math.Min(remaining, 1000);
                try
                {
                    int b = _serial.ReadByte();
                    if (b >= 0)
                    {
                        char c = (char)b;
                        buffer += c;
                        if (buffer.Contains(expected))
                        {
                            Log("Modem handshake complete");
                            Thread.Sleep(100); // let radio settle
                            return;
                        }
                        // Keep buffer from growing unbounded
                        if (buffer.Length > 256)
                            buffer = buffer.Substring(buffer.Length - expected.Length);
                    }
                }
                catch (TimeoutException) { }
            }

            throw new TimeoutException(
                "Radio did not respond to CLIENT handshake (no CLIENTSERVER received). " +
                "Check cable connection and ensure radio is powered on.");
        }

        // ── LCP Negotiation ──────────────────────────────────────────────

        private void NegotiateLcp(int timeoutMs)
        {
            _lcpUp = false;
            bool ourAcked = false;
            bool peerAcked = false;

            // Send our LCP Configure-Request: just request default ACCM
            byte[] ourOptions = MakeAccmOption(0x00000000);
            byte[] ourConfReq = MakeControlPacket(CODE_CONF_REQ, _lcpIdent++, ourOptions);
            SendControl(PROTO_LCP, ourConfReq);

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline && !_lcpUp)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) break;

                byte[] raw = ReadFrame(Math.Min(remaining, 2000));
                if (raw == null)
                {
                    // Retransmit our config request
                    SendControl(PROTO_LCP, ourConfReq);
                    continue;
                }

                if (!Hdlc.TryParse(raw, out ushort proto, out byte[] payload))
                    continue;

                if (proto != PROTO_LCP) continue;
                if (payload.Length < 4) continue;

                byte code = payload[0];
                byte ident = payload[1];

                switch (code)
                {
                    case CODE_CONF_REQ:
                        // Peer wants to configure — try to ACK everything
                        // But reject auth if requested (we don't want authentication)
                        byte[] peerOptions = ExtractOptions(payload);
                        byte[] rejected = FindRejectable(peerOptions, LCP_OPT_AUTH);
                        if (rejected.Length > 0)
                        {
                            byte[] confRej = MakeControlPacket(CODE_CONF_REJ, ident, rejected);
                            SendControl(PROTO_LCP, confRej);
                        }
                        else
                        {
                            byte[] confAck = MakeControlPacket(CODE_CONF_ACK, ident, peerOptions);
                            SendControl(PROTO_LCP, confAck);
                            peerAcked = true;
                        }
                        break;

                    case CODE_CONF_ACK:
                        ourAcked = true;
                        break;

                    case CODE_CONF_NAK:
                        // Peer wants different values — accept what they suggest
                        byte[] nakOptions = ExtractOptions(payload);
                        byte[] newReq = MakeControlPacket(CODE_CONF_REQ, _lcpIdent++, nakOptions);
                        SendControl(PROTO_LCP, newReq);
                        ourConfReq = newReq;
                        break;

                    case CODE_CONF_REJ:
                        // Peer rejects some options — resend without them
                        byte[] rejOptions = ExtractOptions(payload);
                        byte[] trimmed = RemoveOptions(ourOptions, rejOptions);
                        ourOptions = trimmed;
                        ourConfReq = MakeControlPacket(CODE_CONF_REQ, _lcpIdent++, trimmed);
                        SendControl(PROTO_LCP, ourConfReq);
                        break;
                }

                if (ourAcked && peerAcked)
                {
                    _lcpUp = true;
                    _sendAccm = 0x00000000; // now safe to send without escaping control chars
                    Log("LCP up");
                }
            }

            if (!_lcpUp)
                throw new TimeoutException("LCP negotiation timed out");
        }

        // ── IPCP Negotiation ─────────────────────────────────────────────

        private void NegotiateIpcp(int timeoutMs)
        {
            _ipcpUp = false;
            bool ourAcked = false;
            bool peerAcked = false;

            // Request 0.0.0.0 — asking peer to assign us an IP
            byte[] ourOptions = MakeIpOption(LocalIp);
            byte[] ourConfReq = MakeControlPacket(CODE_CONF_REQ, _ipcpIdent++, ourOptions);
            SendControl(PROTO_IPCP, ourConfReq);

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline && !_ipcpUp)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) break;

                byte[] raw = ReadFrame(Math.Min(remaining, 2000));
                if (raw == null)
                {
                    SendControl(PROTO_IPCP, ourConfReq);
                    continue;
                }

                if (!Hdlc.TryParse(raw, out ushort proto, out byte[] payload))
                    continue;

                if (proto == PROTO_LCP)
                {
                    HandleLcpPacket(payload);
                    continue;
                }

                if (proto != PROTO_IPCP) continue;
                if (payload.Length < 4) continue;

                byte code = payload[0];
                byte ident = payload[1];

                switch (code)
                {
                    case CODE_CONF_REQ:
                        // Peer's IPCP config — extract their IP and ACK
                        byte[] peerOpts = ExtractOptions(payload);
                        byte[] peerIp = FindIpInOptions(peerOpts);
                        if (peerIp != null)
                            RemoteIp = peerIp;
                        byte[] confAck = MakeControlPacket(CODE_CONF_ACK, ident, peerOpts);
                        SendControl(PROTO_IPCP, confAck);
                        peerAcked = true;
                        break;

                    case CODE_CONF_ACK:
                        ourAcked = true;
                        break;

                    case CODE_CONF_NAK:
                        // Peer suggests an IP for us
                        byte[] nakOpts = ExtractOptions(payload);
                        byte[] suggestedIp = FindIpInOptions(nakOpts);
                        if (suggestedIp != null)
                            LocalIp = suggestedIp;
                        ourOptions = MakeIpOption(LocalIp);
                        ourConfReq = MakeControlPacket(CODE_CONF_REQ, _ipcpIdent++, ourOptions);
                        SendControl(PROTO_IPCP, ourConfReq);
                        break;

                    case CODE_CONF_REJ:
                        // If IP option is rejected, try with just empty options
                        ourOptions = new byte[0];
                        ourConfReq = MakeControlPacket(CODE_CONF_REQ, _ipcpIdent++, ourOptions);
                        SendControl(PROTO_IPCP, ourConfReq);
                        break;
                }

                if (ourAcked && peerAcked)
                {
                    _ipcpUp = true;
                    Log("IPCP up");
                }
            }

            if (!_ipcpUp)
                throw new TimeoutException("IPCP negotiation timed out");
        }

        // ── Packet Handling ──────────────────────────────────────────────

        private void HandleLcpPacket(byte[] payload)
        {
            if (payload.Length < 4) return;
            byte code = payload[0];
            byte ident = payload[1];

            switch (code)
            {
                case CODE_CONF_REQ:
                    byte[] opts = ExtractOptions(payload);
                    byte[] rejected = FindRejectable(opts, LCP_OPT_AUTH);
                    if (rejected.Length > 0)
                    {
                        SendControl(PROTO_LCP, MakeControlPacket(CODE_CONF_REJ, ident, rejected));
                    }
                    else
                    {
                        SendControl(PROTO_LCP, MakeControlPacket(CODE_CONF_ACK, ident, opts));
                    }
                    break;

                case CODE_TERM_REQ:
                    SendControl(PROTO_LCP, MakeControlPacket(CODE_TERM_ACK, ident, new byte[0]));
                    break;

                case CODE_PROT_REJ:
                    // Peer doesn't understand something we sent — ignore
                    break;
            }
        }

        private void HandleIpcpPacket(byte[] payload)
        {
            if (payload.Length < 4) return;
            byte code = payload[0];
            byte ident = payload[1];

            if (code == CODE_CONF_REQ)
            {
                byte[] opts = ExtractOptions(payload);
                byte[] peerIp = FindIpInOptions(opts);
                if (peerIp != null)
                    RemoteIp = peerIp;
                SendControl(PROTO_IPCP, MakeControlPacket(CODE_CONF_ACK, ident, opts));
            }
        }

        private void SendProtocolReject(ushort rejectedProto, byte[] data)
        {
            int len = Math.Min(data.Length, 128);
            byte[] rejData = new byte[2 + len];
            rejData[0] = (byte)((rejectedProto >> 8) & 0xFF);
            rejData[1] = (byte)(rejectedProto & 0xFF);
            Array.Copy(data, 0, rejData, 2, len);
            byte[] pkt = MakeControlPacket(CODE_PROT_REJ, _lcpIdent++, rejData);
            SendControl(PROTO_LCP, pkt);
        }

        // ── Frame I/O ────────────────────────────────────────────────────

        private void SendControl(ushort protocol, byte[] data)
        {
            byte[] frame = Hdlc.Frame(protocol, data, _sendAccm);
            _serial.Write(frame, 0, frame.Length);
        }

        /// <summary>
        /// Read a complete HDLC frame from serial (flag-delimited).
        /// </summary>
        private byte[] ReadFrame(int timeoutMs)
        {
            List<byte> buffer = new List<byte>(512);
            bool inFrame = false;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            _serial.ReadTimeout = Math.Min(timeoutMs, 500);

            while (DateTime.UtcNow < deadline)
            {
                int b;
                try
                {
                    b = _serial.ReadByte();
                }
                catch (TimeoutException)
                {
                    if (inFrame && buffer.Count > 0)
                    {
                        int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                        if (remaining > 0)
                        {
                            _serial.ReadTimeout = Math.Min(remaining, 500);
                            continue;
                        }
                    }
                    continue;
                }

                if (b < 0) continue;

                if ((byte)b == Hdlc.FLAG)
                {
                    if (inFrame && buffer.Count > 4)
                    {
                        // End of frame
                        buffer.Add((byte)b);
                        return buffer.ToArray();
                    }
                    else
                    {
                        // Start of new frame
                        buffer.Clear();
                        buffer.Add((byte)b);
                        inFrame = true;
                    }
                }
                else if (inFrame)
                {
                    buffer.Add((byte)b);
                }
            }

            return null;
        }

        // ── Option Helpers ───────────────────────────────────────────────

        private static byte[] MakeControlPacket(byte code, byte ident, byte[] options)
        {
            int length = 4 + options.Length;
            byte[] pkt = new byte[length];
            pkt[0] = code;
            pkt[1] = ident;
            pkt[2] = (byte)((length >> 8) & 0xFF);
            pkt[3] = (byte)(length & 0xFF);
            Array.Copy(options, 0, pkt, 4, options.Length);
            return pkt;
        }

        private static byte[] MakeAccmOption(uint accm)
        {
            byte[] opt = new byte[6];
            opt[0] = LCP_OPT_ACCM;
            opt[1] = 6;
            opt[2] = (byte)((accm >> 24) & 0xFF);
            opt[3] = (byte)((accm >> 16) & 0xFF);
            opt[4] = (byte)((accm >> 8) & 0xFF);
            opt[5] = (byte)(accm & 0xFF);
            return opt;
        }

        private static byte[] MakeIpOption(byte[] ip)
        {
            byte[] opt = new byte[6];
            opt[0] = IPCP_OPT_ADDR;
            opt[1] = 6;
            Array.Copy(ip, 0, opt, 2, 4);
            return opt;
        }

        /// <summary>
        /// Extract the options portion from a control packet (skip code, ident, length).
        /// </summary>
        private static byte[] ExtractOptions(byte[] packet)
        {
            if (packet.Length <= 4) return new byte[0];
            int optLen = packet.Length - 4;
            byte[] opts = new byte[optLen];
            Array.Copy(packet, 4, opts, 0, optLen);
            return opts;
        }

        /// <summary>
        /// Find options that should be rejected (e.g., auth).
        /// Returns the TLV bytes of rejectable options, or empty if none.
        /// </summary>
        private static byte[] FindRejectable(byte[] options, byte rejectType)
        {
            List<byte> rejected = new List<byte>();
            int i = 0;
            while (i < options.Length - 1)
            {
                byte type = options[i];
                byte len = options[i + 1];
                if (len < 2 || i + len > options.Length) break;

                if (type == rejectType)
                {
                    for (int j = 0; j < len; j++)
                        rejected.Add(options[i + j]);
                }
                i += len;
            }
            return rejected.ToArray();
        }

        /// <summary>
        /// Remove specific option types from an option block.
        /// </summary>
        private static byte[] RemoveOptions(byte[] options, byte[] rejected)
        {
            // Build set of rejected option types
            HashSet<byte> rejTypes = new HashSet<byte>();
            int j = 0;
            while (j < rejected.Length - 1)
            {
                rejTypes.Add(rejected[j]);
                int len = rejected[j + 1];
                if (len < 2) break;
                j += len;
            }

            List<byte> result = new List<byte>();
            int i = 0;
            while (i < options.Length - 1)
            {
                byte type = options[i];
                byte len = options[i + 1];
                if (len < 2 || i + len > options.Length) break;

                if (!rejTypes.Contains(type))
                {
                    for (int k = 0; k < len; k++)
                        result.Add(options[i + k]);
                }
                i += len;
            }
            return result.ToArray();
        }

        /// <summary>
        /// Find IP address (option type 3) in IPCP options.
        /// </summary>
        private static byte[] FindIpInOptions(byte[] options)
        {
            int i = 0;
            while (i < options.Length - 1)
            {
                byte type = options[i];
                byte len = options[i + 1];
                if (len < 2 || i + len > options.Length) break;

                if (type == IPCP_OPT_ADDR && len == 6)
                {
                    byte[] ip = new byte[4];
                    Array.Copy(options, i + 2, ip, 0, 4);
                    return ip;
                }
                i += len;
            }
            return null;
        }

        private static string FormatIp(byte[] ip)
        {
            return string.Format("{0}.{1}.{2}.{3}", ip[0], ip[1], ip[2], ip[3]);
        }

        private void Log(string fmt, params object[] args)
        {
            if (_verbose)
                Console.Error.WriteLine("[PPP] " + string.Format(fmt, args));
        }
    }
}
