using System;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;

namespace SaklSerial.Transport
{
    /// <summary>
    /// High-level transport that auto-detects the radio's serial port,
    /// establishes a PPP session, and provides QueryRadio() for KMM communication.
    /// </summary>
    internal class RadioTransport : IDisposable
    {
        private const int DEFAULT_BAUD = 9600;
        private const ushort RADIO_UDP_PORT = 49165;
        private const ushort LOCAL_UDP_PORT = 50000;

        private SerialPort _serial;
        private PppClient _ppp;
        private readonly string _portName;
        private readonly int _baud;
        private readonly int _timeoutMs;
        private readonly bool _verbose;

        /// <summary>
        /// Create a RadioTransport.
        /// </summary>
        /// <param name="portName">Serial port name, or null to auto-detect.</param>
        /// <param name="baud">Baud rate (default 9600).</param>
        /// <param name="timeoutMs">Timeout for radio responses in ms.</param>
        /// <param name="verbose">Enable debug logging.</param>
        public RadioTransport(string portName = null, int baud = DEFAULT_BAUD,
                              int timeoutMs = 5000, bool verbose = false)
        {
            _portName = portName;
            _baud = baud;
            _timeoutMs = timeoutMs;
            _verbose = verbose;
        }

        /// <summary>
        /// Open serial port, perform modem handshake, negotiate PPP.
        /// </summary>
        public void Connect()
        {
            string port = _portName ?? AutoDetectPort();

            Log("Opening {0} at {1} baud...", port, _baud);

            _serial = new SerialPort(port, _baud, Parity.None, 8, StopBits.One);
            _serial.Handshake = Handshake.None;
            _serial.ReadTimeout = _timeoutMs;
            _serial.WriteTimeout = _timeoutMs;
            _serial.DtrEnable = true;
            _serial.RtsEnable = true;

            try
            {
                _serial.Open();
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Failed to open {0}: {1}", port, ex.Message), ex);
            }

            _ppp = new PppClient(_serial, _verbose);
            _ppp.Connect(_timeoutMs * 2); // give extra time for PPP negotiation
        }

        /// <summary>
        /// Send a KMM frame to the radio and receive the response.
        /// This wraps the KMM bytes in UDP/IP/PPP and sends over serial.
        /// </summary>
        public byte[] QueryRadio(byte[] toRadio)
        {
            if (_ppp == null)
                throw new InvalidOperationException("Not connected. Call Connect() first.");

            byte[] udpPacket = IpUdp.BuildUdpPacket(
                _ppp.LocalIp, _ppp.RemoteIp,
                LOCAL_UDP_PORT, RADIO_UDP_PORT,
                toRadio);

            LogHex("TX KMM", toRadio);

            _ppp.SendIp(udpPacket);

            // Receive response
            byte[] responseIp = _ppp.ReceiveIp(_timeoutMs);

            if (!IpUdp.TryParseUdp(responseIp, out _, out _, out _, out _, out byte[] payload))
                throw new Exception("Received non-UDP response from radio");

            LogHex("RX KMM", payload);

            return payload;
        }

        public void Dispose()
        {
            _ppp?.Dispose();
            if (_serial != null && _serial.IsOpen)
            {
                try { _serial.Close(); } catch { }
            }
        }

        // ── Port Auto-Detection ──────────────────────────────────────────

        private string AutoDetectPort()
        {
            Log("Auto-detecting radio serial port...");

            string[] portNames = SerialPort.GetPortNames();

            if (portNames.Length == 0)
                throw new Exception(
                    "No serial ports found. Ensure the radio is connected via the programming cable.");

            // Try to find a likely radio port by name pattern
            string[] candidates;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, modems typically register as COMx
                // Prefer higher COM numbers (USB adapters usually get higher numbers)
                candidates = portNames.OrderByDescending(p => p).ToArray();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // On macOS, look for USB modem/serial devices
                candidates = portNames
                    .Where(p => p.Contains("usbmodem") || p.Contains("usbserial"))
                    .OrderByDescending(p => p)
                    .ToArray();

                if (candidates.Length == 0)
                    candidates = portNames; // fallback to all
            }
            else
            {
                // Linux: look for ttyUSB or ttyACM
                candidates = portNames
                    .Where(p => p.Contains("ttyUSB") || p.Contains("ttyACM"))
                    .OrderByDescending(p => p)
                    .ToArray();

                if (candidates.Length == 0)
                    candidates = portNames;
            }

            if (candidates.Length == 1)
            {
                Log("Found port: {0}", candidates[0]);
                return candidates[0];
            }

            // Multiple candidates — list them and pick the first
            Log("Found {0} candidate port(s):", candidates.Length);
            foreach (string p in candidates)
                Log("  {0}", p);
            Log("Using: {0} (override with --port if wrong)", candidates[0]);

            return candidates[0];
        }

        private void Log(string fmt, params object[] args)
        {
            if (_verbose)
                Console.Error.WriteLine("[Transport] " + string.Format(fmt, args));
        }

        private void LogHex(string label, byte[] data)
        {
            if (_verbose)
                Console.Error.WriteLine("[Transport] {0} ({1} bytes): {2}",
                    label, data.Length, BitConverter.ToString(data));
        }
    }
}
