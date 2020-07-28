using System;
using System.Text;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;

namespace Singulink.Net.Dhcp
{
    // DHCP MESSAGE FORMAT (adapted from https://www.ietf.org/rfc/rfc2131.txt):

    // FIELD     OFFSET   OCTETS            DESCRIPTION
    // -----     ------   ------            -----------

    // op          0         1       Message op code / message type.
    //                               1 = BOOTREQUEST, 2 = BOOTREPLY
    // htype       1         1       Hardware address type, see ARP section in "Assigned
    //                               Numbers" RFC; e.g., '1' = 10mb ethernet.
    // hlen        2         1       Hardware address length (e.g.  '6' for 10mb
    //                               ethernet).
    // hops        3         1       Client sets to zero, optionally used by relay agents
    //                               when booting via a relay agent.
    // xid         4         4       Transaction ID, a random number chosen by the
    //                               client, used by the client and server to associate
    //                               messages and responses between a client and a
    //                               server.
    // secs        8         2       Filled in by client, seconds elapsed since client
    //                               began address acquisition or renewal process.
    // flags      10         2       Flags (see figure 2).
    // ciaddr     12         4       Client IP address; only filled in if client is in
    //                               BOUND, RENEW or REBINDING state and can respond
    //                               to ARP requests.
    // yiaddr     16         4       'your' (client) IP address.
    // siaddr     20         4       IP address of next server to use in bootstrap;
    //                               returned in DHCPOFFER, DHCPACK by server.
    // giaddr     24         4       Relay agent IP address, used in booting via a
    //                               relay agent.
    // chaddr     28        16       Client hardware address.
    // sname      44        64       Optional server host name, null terminated string.
    // file      108       128       Boot file name, null terminated string; "generic"
    //                               name or null in DHCPDISCOVER, fully qualified
    //                               directory-path name in DHCPOFFER.
    // mcookie   236         4       Magic cookie value 99, 130, 83 and 99 to identify
    //                               this as a DHCP/BOOTP options format
    // options   240       var       Optional parameters field.  See the options
    //                               documents for a list of defined options.

    /// <summary>
    /// Represents a DHCP message.
    /// </summary>
    public class DhcpMessage
    {
        private static readonly IPAddress BootPMagicCookieValue = IPAddress.Parse("99.130.83.99");
        internal const int OptionFieldOffset = 240;

        public DhcpOpcode OpCode { get; }

        public DhcpHardwareType HardwareAddressType  { get; internal set; }

        public byte HardwareAddressLength { get; internal set; }

        public byte HardwareOptions { get; internal set; }

        public uint TransactionID { get; internal set; }

        public ushort SecondsElapsed { get; internal set; }

        public ushort Flags { get; internal set; }

        public IPAddress ClientIPAddress { get; internal set; } = IPAddress.Any;

        public IPAddress YourClientIPAddress { get; internal set; } = IPAddress.Any;

        public IPAddress NextServerIPAddress  { get; internal set; } = IPAddress.Any;

        public IPAddress RelayAgentIPAddress  { get; internal set; } = IPAddress.Any;

        public PhysicalAddress ClientMacAddress { get; internal set; } = new PhysicalAddress(new byte[6]);

        public string ServerHostName { get; internal set; } = string.Empty;

        public string BootFileName { get; internal set; } = string.Empty;

        public DhcpOptionCollection Options { get; }

        internal DhcpMessage(DhcpOpcode opCode, DhcpMessageType messageType)
        {
            OpCode = opCode;
            Options = new DhcpOptionCollection(messageType);
        }

        internal DhcpMessage(byte[] messageData)
        {
            if (messageData.Length < 244)
                throw new InvalidDataException("Message is not long enough to be a valid DHCP message.");

            OpCode                = (DhcpOpcode)messageData[0];
            HardwareAddressType   = (DhcpHardwareType)messageData[1];
            HardwareAddressLength = messageData[2];
            HardwareOptions       = messageData[3];
            TransactionID         = ReadUInt32(messageData, 4);
            SecondsElapsed        = ReadUInt16(messageData, 8);
            Flags                 = ReadUInt16(messageData, 10);
            ClientIPAddress       = ReadIPAddress(messageData, 12);
            YourClientIPAddress   = ReadIPAddress(messageData, 16);
            NextServerIPAddress   = ReadIPAddress(messageData, 20);
            RelayAgentIPAddress   = ReadIPAddress(messageData, 24);
            ClientMacAddress      = ReadPhysicalAddress(messageData, 28);
            ServerHostName        = ReadNullTerminatedString(messageData, 44, 64);
            BootFileName          = ReadNullTerminatedString(messageData, 108, 128);

            var magicCookie = ReadIPAddress(messageData, 236);

            if (!BootPMagicCookieValue.Equals(magicCookie))
                throw new InvalidDataException($"Wrong magic cookie value. Expected: {BootPMagicCookieValue}, Received: {magicCookie}");

            Options = new DhcpOptionCollection(messageData);
        }

        internal byte[] GetBytes()
        {
            if (!Options.ContainsKey(DhcpOption.DhcpMessageType))
                throw new InvalidDataException($"Required option '{nameof(DhcpOption.DhcpMessageType)}' is missing.");

            using var stream = new MemoryStream(512);

            stream.WriteByte((byte)OpCode);
            stream.WriteByte((byte)HardwareAddressType);
            stream.WriteByte(HardwareAddressLength);
            stream.WriteByte(HardwareOptions);
            Write(TransactionID, stream);
            Write(SecondsElapsed, stream);
            Write(Flags, stream);
            Write(ClientIPAddress, stream);
            Write(YourClientIPAddress, stream);
            Write(NextServerIPAddress, stream);
            Write(RelayAgentIPAddress, stream);
            Write(ClientMacAddress, stream);
            WriteNullTerminatedString(ServerHostName, 64, stream);
            WriteNullTerminatedString(BootFileName, 128, stream);

            Write(BootPMagicCookieValue, stream);
            Options.Write(stream);

            return stream.ToArray();
        }

        internal static string ReadNullTerminatedString(byte[] data, int offset, int count)
        {
            int nullIndex = Array.IndexOf(data, (byte)0, offset, count);
            count = nullIndex >= 0 ? nullIndex - offset: count;

            return Encoding.ASCII.GetString(data, offset, count);
        }

        internal static PhysicalAddress ReadPhysicalAddress(byte[] data, int offset)
        {
            byte[] bytes = new byte[6];
            Buffer.BlockCopy(data, offset, bytes, 0, 6);
            return new PhysicalAddress(bytes);
        }

        internal static IPAddress ReadIPAddress(byte[] data, int offset)
        {
            byte[] bytes = new byte[4];
            Buffer.BlockCopy(data, offset, bytes, 0, 4);
            return new IPAddress(bytes);
        }

        internal static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] << 8 | data[offset]);
        }

        internal static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        internal static void Write(ushort value, Stream stream)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        internal static void Write(uint value, Stream stream)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        internal static void Write(IPAddress value, Stream stream)
        {
            if (value.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new ArgumentException("Only IPv4 addresses are supported.");

            byte[] bytes = value.GetAddressBytes();
            stream.Write(bytes, 0, 4);
        }

        internal static void Write(PhysicalAddress value, Stream stream)
        {
            byte[] bytes = value.GetAddressBytes();
            byte[] padding = new byte[10];

            if (bytes.Length != 6)
                throw new ArgumentException("Only MAC-48 physical addresses are supported.", nameof(value));

            stream.Write(bytes, 0, bytes.Length);
            stream.Write(padding, 0, padding.Length);
        }

        internal static void WriteNullTerminatedString(string value, int fieldLength, Stream stream)
        {
            if (value.Length >= fieldLength)
                throw new ArgumentException("The value was too long for the field length.");

            byte[] bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);

            for (int i = bytes.Length; i < fieldLength; i++)
                stream.WriteByte(0);
        }

        public override string ToString()
        {
            string value = $"DHCP Message: [{Options.MessageType}] {ClientMacAddress} / {ClientIPAddress}";

            var address = Options.RequestedIPAddress ?? YourClientIPAddress;

            if (address?.Equals(IPAddress.Any) == false)
                value += $" => {address}";

            if (Options.ContainsKey(DhcpOption.Message))
                value += $" - {Options.GetString(DhcpOption.Message)}";

            return value;
        }
    }
}
