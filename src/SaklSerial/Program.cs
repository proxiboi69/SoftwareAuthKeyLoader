using System;
using System.Globalization;
using System.Text.RegularExpressions;
using SaklSerial.Transport;

namespace SaklSerial
{
    class Program
    {
        static int Main(string[] args)
        {
            // Defaults
            string port = null;    // auto-detect
            string baud = "9600";
            string timeout = "5000";
            bool verbose = false;
            bool quiet = false;

            string action = null;  // load, zeroize, read
            string scope = null;   // device, active, named

            string wacn = "";
            string system = "";
            string unit = "";
            string key = "";
            bool help = false;

            // Simple argument parser (compatible with original /flag and --flag styles)
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].TrimStart('-', '/').ToLowerInvariant();

                switch (arg)
                {
                    case "h": case "?": case "help":
                        help = true; break;
                    case "v": case "verbose":
                        verbose = true; break;
                    case "q": case "quiet":
                        quiet = true; break;
                    case "l": case "load":
                        action = "load"; break;
                    case "z": case "zeroize":
                        action = "zeroize"; break;
                    case "r": case "read":
                        action = "read"; break;
                    case "d": case "device":
                        scope = "device"; break;
                    case "a": case "active":
                        scope = "active"; break;
                    case "n": case "named":
                        scope = "named"; break;

                    case "port":
                        if (++i < args.Length) port = args[i]; break;
                    case "baud":
                        if (++i < args.Length) baud = args[i]; break;
                    case "t": case "timeout":
                        if (++i < args.Length) timeout = args[i]; break;
                    case "w": case "wacn":
                        if (++i < args.Length) wacn = args[i]; break;
                    case "s": case "system":
                        if (++i < args.Length) system = args[i]; break;
                    case "u": case "unit":
                        if (++i < args.Length) unit = args[i]; break;
                    case "k": case "key":
                        if (++i < args.Length) key = args[i]; break;

                    default:
                        // Handle --port=VALUE style
                        if (arg.Contains("="))
                        {
                            string[] parts = arg.Split(new[] { '=' }, 2);
                            switch (parts[0])
                            {
                                case "port": port = parts[1]; break;
                                case "baud": baud = parts[1]; break;
                                case "timeout": case "t": timeout = parts[1]; break;
                                case "wacn": case "w": wacn = parts[1]; break;
                                case "system": case "s": system = parts[1]; break;
                                case "unit": case "u": unit = parts[1]; break;
                                case "key": case "k": key = parts[1]; break;
                            }
                        }
                        break;
                }
            }

            if (!quiet)
            {
                Console.WriteLine("SAKL - P25 Link Layer Authentication Key Loader (Serial Direct)");
                Console.WriteLine("Supports Manual Rekeying per TIA-102.AACD-A");
                Console.WriteLine();
            }

            if (help || args.Length == 0)
            {
                ShowHelp();
                return 0;
            }

            // ── Validate inputs ──────────────────────────────────────────

            int baudRate;
            if (!int.TryParse(baud, out baudRate) || baudRate <= 0)
            {
                Console.Error.WriteLine("Error: invalid baud rate: {0}", baud);
                return -1;
            }

            int timeoutMs;
            if (!int.TryParse(timeout, out timeoutMs) || timeoutMs <= 0)
            {
                Console.Error.WriteLine("Error: invalid timeout: {0}", timeout);
                return -1;
            }

            if (action == null)
            {
                Console.Error.WriteLine("Error: no action specified (use --load, --zeroize, or --read)");
                return -1;
            }

            if (scope == null)
            {
                Console.Error.WriteLine("Error: no scope specified (use --device, --active, or --named)");
                return -1;
            }

            byte[] keyData = null;
            if (action == "load")
            {
                if (scope == "device")
                {
                    Console.Error.WriteLine("Error: device scope not supported for load action");
                    return -1;
                }

                if (string.IsNullOrEmpty(key))
                {
                    Console.Error.WriteLine("Error: --key required for load action");
                    return -1;
                }

                if (!IsHex(key) || key.Length != 32)
                {
                    Console.Error.WriteLine("Error: key must be exactly 32 hex characters (16-byte AES-128 key)");
                    return -1;
                }

                keyData = HexToBytes(key);
            }

            int wacnId = 0, systemId = 0, unitId = 0;
            if (scope == "named")
            {
                if (!ValidateHexField("wacn", wacn, 5, out wacnId)) return -1;
                if (!ValidateHexField("system", system, 3, out systemId)) return -1;
                if (!ValidateHexField("unit", unit, 6, out unitId)) return -1;
            }

            if (action == "read" && scope == "named")
            {
                Console.Error.WriteLine("Error: named scope not supported for read action");
                return -1;
            }

            // ── Execute ──────────────────────────────────────────────────

            using (RadioTransport transport = new RadioTransport(port, baudRate, timeoutMs, verbose))
            {
                try
                {
                    transport.Connect();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error connecting to radio: {0}", ex.Message);
                    if (verbose)
                        Console.Error.WriteLine(ex.StackTrace);
                    return -1;
                }

                int result = -1;

                try
                {
                    switch (action)
                    {
                        case "load":
                            bool targetSpecific = (scope == "named");
                            result = Actions.LoadAuthenticationKey(transport,
                                targetSpecific, wacnId, systemId, unitId, keyData);
                            if (result == 0 && !quiet)
                                Console.WriteLine("Loaded authentication key successfully");
                            break;

                        case "zeroize":
                            bool deleteAll = (scope == "device");
                            bool targetSu = (scope == "named");
                            result = Actions.DeleteAuthenticationKey(transport,
                                targetSu, deleteAll, wacnId, systemId, unitId);
                            if (result == 0 && !quiet)
                                Console.WriteLine("Zeroized authentication key(s) successfully");
                            break;

                        case "read":
                            if (scope == "active")
                                result = Actions.ListActiveSuId(transport);
                            else if (scope == "device")
                                result = Actions.ListSuIdItems(transport);
                            if (result == 0 && !quiet)
                                Console.WriteLine("Read complete");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error: {0}", ex.Message);
                    if (verbose)
                        Console.Error.WriteLine(ex.StackTrace);
                    return -1;
                }

                return result;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static bool ValidateHexField(string name, string value, int maxLen, out int parsed)
        {
            parsed = 0;
            if (string.IsNullOrEmpty(value))
            {
                Console.Error.WriteLine("Error: --{0} required for named scope", name);
                return false;
            }
            if (!IsHex(value) || value.Length > maxLen)
            {
                Console.Error.WriteLine("Error: {0} must be 1-{1} hex characters, got: {2}", name, maxLen, value);
                return false;
            }
            parsed = int.Parse(value, NumberStyles.HexNumber);
            return true;
        }

        private static bool IsHex(string s)
        {
            return Regex.IsMatch(s, @"^[0-9a-fA-F]+$");
        }

        private static byte[] HexToBytes(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Usage: sakl [OPTIONS]");
            Console.WriteLine();
            Console.WriteLine("Connection (plug in programming cable and go):");
            Console.WriteLine("  --port VALUE     Serial port (auto-detected if omitted)");
            Console.WriteLine("  --baud VALUE     Baud rate [default: 9600]");
            Console.WriteLine("  --timeout VALUE  Response timeout in ms [default: 5000]");
            Console.WriteLine("  --verbose        Show debug output");
            Console.WriteLine("  --quiet          Suppress informational output");
            Console.WriteLine();
            Console.WriteLine("Actions:");
            Console.WriteLine("  --load           Load an authentication key");
            Console.WriteLine("  --zeroize        Zeroize (delete) key(s)");
            Console.WriteLine("  --read           Read/query key(s)");
            Console.WriteLine();
            Console.WriteLine("Scope:");
            Console.WriteLine("  --device         All keys on device");
            Console.WriteLine("  --active         Active SuId only");
            Console.WriteLine("  --named          Specific SuId (requires --wacn, --system, --unit)");
            Console.WriteLine();
            Console.WriteLine("Parameters:");
            Console.WriteLine("  --wacn VALUE     WACN ID in hex (max 5 chars)");
            Console.WriteLine("  --system VALUE   System ID in hex (max 3 chars)");
            Console.WriteLine("  --unit VALUE     Unit ID in hex (max 6 chars)");
            Console.WriteLine("  --key VALUE      AES-128 key in hex (32 chars)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  sakl --load --active --key 000102030405060708090a0b0c0d0e0f");
            Console.WriteLine("  sakl --load --named --wacn a4398 --system f10 --unit 99b584 --key 000102030405060708090a0b0c0d0e0f");
            Console.WriteLine("  sakl --zeroize --device");
            Console.WriteLine("  sakl --zeroize --active");
            Console.WriteLine("  sakl --read --device");
            Console.WriteLine("  sakl --read --active");
            Console.WriteLine("  sakl --read --active --port COM3");
            Console.WriteLine("  sakl --read --active --port COM3 --verbose");
        }
    }
}
