using System;

namespace SaklSerial.Transport
{
    /// <summary>
    /// Constructs and parses raw IPv4 + UDP packets for encapsulation in PPP.
    /// </summary>
    internal static class IpUdp
    {
        /// <summary>
        /// Build a complete IP+UDP packet.
        /// </summary>
        public static byte[] BuildUdpPacket(byte[] srcIp, byte[] dstIp,
                                             ushort srcPort, ushort dstPort,
                                             byte[] payload)
        {
            // UDP header
            int udpLen = 8 + payload.Length;
            byte[] udp = new byte[udpLen];
            udp[0] = (byte)((srcPort >> 8) & 0xFF);
            udp[1] = (byte)(srcPort & 0xFF);
            udp[2] = (byte)((dstPort >> 8) & 0xFF);
            udp[3] = (byte)(dstPort & 0xFF);
            udp[4] = (byte)((udpLen >> 8) & 0xFF);
            udp[5] = (byte)(udpLen & 0xFF);
            udp[6] = 0; // checksum = 0 (optional for IPv4)
            udp[7] = 0;
            Array.Copy(payload, 0, udp, 8, payload.Length);

            // IP header (20 bytes, no options)
            int totalLen = 20 + udpLen;
            byte[] ip = new byte[totalLen];
            ip[0] = 0x45; // version 4, IHL 5
            ip[1] = 0x00; // DSCP/ECN
            ip[2] = (byte)((totalLen >> 8) & 0xFF);
            ip[3] = (byte)(totalLen & 0xFF);
            ip[4] = 0x00; // identification
            ip[5] = 0x00;
            ip[6] = 0x40; // flags: Don't Fragment
            ip[7] = 0x00; // fragment offset
            ip[8] = 0x40; // TTL = 64
            ip[9] = 0x11; // protocol = UDP (17)
            ip[10] = 0;   // header checksum (placeholder)
            ip[11] = 0;
            // source IP
            Array.Copy(srcIp, 0, ip, 12, 4);
            // destination IP
            Array.Copy(dstIp, 0, ip, 16, 4);

            // Calculate IP header checksum
            ushort cksum = Checksum(ip, 0, 20);
            ip[10] = (byte)((cksum >> 8) & 0xFF);
            ip[11] = (byte)(cksum & 0xFF);

            // Copy UDP after IP header
            Array.Copy(udp, 0, ip, 20, udpLen);

            return ip;
        }

        /// <summary>
        /// Parse an IP+UDP packet, extracting the UDP payload.
        /// Returns false if the packet isn't UDP or is malformed.
        /// </summary>
        public static bool TryParseUdp(byte[] packet, out byte[] srcIp, out byte[] dstIp,
                                        out ushort srcPort, out ushort dstPort,
                                        out byte[] payload)
        {
            srcIp = null;
            dstIp = null;
            srcPort = 0;
            dstPort = 0;
            payload = null;

            if (packet.Length < 28) // minimum IP(20) + UDP(8)
                return false;

            // Verify IPv4
            if ((packet[0] >> 4) != 4)
                return false;

            int ihl = (packet[0] & 0x0F) * 4;
            if (ihl < 20 || packet.Length < ihl + 8)
                return false;

            // Verify UDP protocol
            if (packet[9] != 0x11)
                return false;

            srcIp = new byte[4];
            dstIp = new byte[4];
            Array.Copy(packet, 12, srcIp, 0, 4);
            Array.Copy(packet, 16, dstIp, 0, 4);

            int udpOffset = ihl;
            srcPort = (ushort)((packet[udpOffset] << 8) | packet[udpOffset + 1]);
            dstPort = (ushort)((packet[udpOffset + 2] << 8) | packet[udpOffset + 3]);
            int udpLen = (packet[udpOffset + 4] << 8) | packet[udpOffset + 5];
            int payloadLen = udpLen - 8;

            if (payloadLen < 0 || udpOffset + 8 + payloadLen > packet.Length)
                return false;

            payload = new byte[payloadLen];
            Array.Copy(packet, udpOffset + 8, payload, 0, payloadLen);
            return true;
        }

        /// <summary>
        /// Standard internet checksum (RFC 1071).
        /// </summary>
        private static ushort Checksum(byte[] data, int offset, int length)
        {
            uint sum = 0;
            int i = offset;
            int end = offset + length;

            while (i + 1 < end)
            {
                sum += (uint)((data[i] << 8) | data[i + 1]);
                i += 2;
            }
            if (i < end)
                sum += (uint)(data[i] << 8);

            while ((sum >> 16) != 0)
                sum = (sum >> 16) + (sum & 0xFFFF);

            return (ushort)(~sum & 0xFFFF);
        }
    }
}
