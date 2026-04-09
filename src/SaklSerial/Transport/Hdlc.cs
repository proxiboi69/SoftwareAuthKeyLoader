using System;
using System.Collections.Generic;

namespace SaklSerial.Transport
{
    /// <summary>
    /// HDLC-like framing for PPP per RFC 1662.
    /// Handles byte stuffing, FCS-16 calculation, frame construction and parsing.
    /// </summary>
    internal static class Hdlc
    {
        public const byte FLAG = 0x7E;
        public const byte ESCAPE = 0x7D;
        public const byte ADDRESS = 0xFF;
        public const byte CONTROL = 0x03;

        private static readonly ushort[] FcsTable = BuildFcsTable();

        private static ushort[] BuildFcsTable()
        {
            ushort[] table = new ushort[256];
            for (int i = 0; i < 256; i++)
            {
                ushort fcs = (ushort)i;
                for (int j = 0; j < 8; j++)
                {
                    if ((fcs & 1) != 0)
                        fcs = (ushort)((fcs >> 1) ^ 0x8408);
                    else
                        fcs >>= 1;
                }
                table[i] = fcs;
            }
            return table;
        }

        /// <summary>
        /// Calculate FCS-16 (CRC-CCITT) over data.
        /// </summary>
        public static ushort CalculateFcs(byte[] data, int offset, int length)
        {
            ushort fcs = 0xFFFF;
            for (int i = offset; i < offset + length; i++)
            {
                fcs = (ushort)((fcs >> 8) ^ FcsTable[(fcs ^ data[i]) & 0xFF]);
            }
            return (ushort)(fcs ^ 0xFFFF);
        }

        /// <summary>
        /// Build a complete HDLC frame: Flag | stuffed(Address + Control + Protocol + Data + FCS) | Flag
        /// </summary>
        public static byte[] Frame(ushort protocol, byte[] data, uint accm)
        {
            // Build unstuffed content: Address + Control + Protocol + Data
            int contentLen = 2 + 2 + data.Length;
            byte[] content = new byte[contentLen];
            content[0] = ADDRESS;
            content[1] = CONTROL;
            content[2] = (byte)((protocol >> 8) & 0xFF);
            content[3] = (byte)(protocol & 0xFF);
            Array.Copy(data, 0, content, 4, data.Length);

            // Calculate FCS over content
            ushort fcs = CalculateFcs(content, 0, content.Length);

            // Append FCS (little-endian)
            byte[] withFcs = new byte[contentLen + 2];
            Array.Copy(content, 0, withFcs, 0, contentLen);
            withFcs[contentLen] = (byte)(fcs & 0xFF);
            withFcs[contentLen + 1] = (byte)((fcs >> 8) & 0xFF);

            // Byte-stuff
            List<byte> stuffed = new List<byte>(withFcs.Length + 16);
            stuffed.Add(FLAG);
            for (int i = 0; i < withFcs.Length; i++)
            {
                byte b = withFcs[i];
                if (b == FLAG || b == ESCAPE || (b < 0x20 && ((accm >> b) & 1) != 0))
                {
                    stuffed.Add(ESCAPE);
                    stuffed.Add((byte)(b ^ 0x20));
                }
                else
                {
                    stuffed.Add(b);
                }
            }
            stuffed.Add(FLAG);
            return stuffed.ToArray();
        }

        /// <summary>
        /// Parse an HDLC frame. Input should be the raw bytes between (and including) flags.
        /// Returns the protocol and payload, or throws on FCS mismatch.
        /// </summary>
        public static bool TryParse(byte[] raw, out ushort protocol, out byte[] payload)
        {
            protocol = 0;
            payload = null;

            // Strip leading/trailing flags
            int start = 0;
            int end = raw.Length - 1;
            while (start < raw.Length && raw[start] == FLAG) start++;
            while (end > start && raw[end] == FLAG) end--;

            if (end - start < 5) // Minimum: addr + ctrl + protocol(2) + fcs(2) - 1
                return false;

            // Un-stuff bytes
            List<byte> unstuffed = new List<byte>(end - start + 1);
            for (int i = start; i <= end; i++)
            {
                if (raw[i] == ESCAPE && i + 1 <= end)
                {
                    unstuffed.Add((byte)(raw[++i] ^ 0x20));
                }
                else if (raw[i] != FLAG)
                {
                    unstuffed.Add(raw[i]);
                }
            }

            byte[] data = unstuffed.ToArray();
            if (data.Length < 6) // addr(1) + ctrl(1) + proto(2) + fcs(2)
                return false;

            // Verify FCS: calculate over everything except the 2-byte FCS at the end
            ushort calcFcs = CalculateFcs(data, 0, data.Length - 2);
            ushort recvFcs = (ushort)(data[data.Length - 2] | (data[data.Length - 1] << 8));
            if (calcFcs != recvFcs)
                return false;

            // Skip address and control bytes
            // Address should be 0xFF, Control should be 0x03
            protocol = (ushort)((data[2] << 8) | data[3]);
            int payloadLen = data.Length - 6; // minus addr + ctrl + proto + fcs
            payload = new byte[payloadLen];
            Array.Copy(data, 4, payload, 0, payloadLen);
            return true;
        }
    }
}
