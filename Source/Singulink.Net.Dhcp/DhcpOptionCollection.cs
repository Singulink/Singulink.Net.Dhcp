using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Text;

namespace Singulink.Net.Dhcp
{
    public class DhcpOptionCollection : ReadOnlyDictionary<DhcpOption, byte[]>
    {
        internal DhcpOptionCollection(byte[] messageData) : base(CreateDictionary(messageData))
        {
        }

        internal DhcpOptionCollection(DhcpMessageType messageType) : base(new Dictionary<DhcpOption, byte[]>())
        {
            SetValue(DhcpOption.DhcpMessageType, (byte)messageType);
        }

        public DhcpMessageType MessageType => (DhcpMessageType)GetByte(DhcpOption.DhcpMessageType);

        public IPAddress? ServerIdentifier => ContainsKey(DhcpOption.ServerIdentifier) ? GetIPAddress(DhcpOption.ServerIdentifier) : null;

        public IPAddress? RequestedIPAddress => ContainsKey(DhcpOption.RequestedIPAddress) ? GetIPAddress(DhcpOption.RequestedIPAddress) : null;

        public IPAddress? SubnetMask => ContainsKey(DhcpOption.SubnetMask) ? GetIPAddress(DhcpOption.SubnetMask) : null;

        public uint? IPAddressLeaseTime => ContainsKey(DhcpOption.IPAddressLeaseTime) ? (uint?)GetUInt32(DhcpOption.IPAddressLeaseTime) : null;

        public string? Message => ContainsKey(DhcpOption.Message) ? GetString(DhcpOption.Message) : null;

        internal static Dictionary<DhcpOption, byte[]> CreateDictionary(byte[] messageData)
        {
            var dictionary = new Dictionary<DhcpOption, byte[]>();

            int offset = DhcpMessage.OptionFieldOffset;

            while (offset < messageData.Length) {
                if (messageData[offset] == (byte)DhcpOption.Pad) {
                    offset++;
                    continue;
                }

                if (messageData[offset] == (byte)DhcpOption.End)
                    break;

                if (offset + 2 > messageData.Length)
                    throw new InvalidDataException("Option field data is malformed.");

                var option = (DhcpOption)messageData[offset++];
                byte length = messageData[offset++];

                if (offset + length > messageData.Length)
                    throw new InvalidDataException("Option field data is malformed.");

                byte[] value = new byte[length];
                Buffer.BlockCopy(messageData, offset, value, 0, length);

                if (dictionary.ContainsKey(option))
                    throw new InvalidDataException($"Option '{option}' was specified multiple times.");

                dictionary.Add(option, value);

                offset += length;
            }

            if (!dictionary.ContainsKey(DhcpOption.DhcpMessageType))
                throw new InvalidDataException($"Required option '{nameof(DhcpOption.DhcpMessageType)}' is missing.");

            return dictionary;
        }

        internal void SetValue(DhcpOption option, byte value)
        {
            Dictionary[option] = new byte[] { value };
        }

        internal void SetValue(DhcpOption option, ushort value)
        {
            Dictionary[option] = new byte[] { (byte)(value >> 8), (byte)value };
        }

        internal void SetValue(DhcpOption option, uint value)
        {
            Dictionary[option] = new byte[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }

        internal void SetValue(DhcpOption option, IPAddress value)
        {
            Dictionary[option] = value.GetAddressBytes();
        }

        internal void SetValue(DhcpOption option, string value)
        {
            Dictionary[option] = Encoding.ASCII.GetBytes(value);
        }

        public byte GetByte(DhcpOption option)
        {
            byte[] data = this[option];

            CheckSize(data, 1);
            return data[0];
        }

        public ushort GetUInt16(DhcpOption option)
        {
            byte[] data = this[option];

            CheckSize(data, 2);
            return DhcpMessage.ReadUInt16(data, 0);
        }

        public uint GetUInt32(DhcpOption option)
        {
            byte[] data = this[option];

            CheckSize(data, 4);
            return DhcpMessage.ReadUInt32(data, 0);
        }

        public IPAddress GetIPAddress(DhcpOption option)
        {
            byte[] data = this[option];

            CheckSize(data, 4);
            return DhcpMessage.ReadIPAddress(data, 0);
        }

        public string GetString(DhcpOption option)
        {
            byte[] bytes = this[option];
            int nullIndex = Array.IndexOf(bytes, (byte)0);
            int count = nullIndex >= 0 ? nullIndex : bytes.Length;

            return Encoding.ASCII.GetString(this[option], 0, count);
        }

        private void CheckSize(byte[] data, int expectedSize)
        {
            if (data.Length != expectedSize)
                throw new InvalidDataException("The size of the option did not match the requested value type.");
        }

        internal void Write(MemoryStream stream)
        {
            foreach (var pair in this) {
                if (pair.Key == DhcpOption.Pad || pair.Key == DhcpOption.End)
                    throw new InvalidOperationException("The DHCP option collection cannot have Pad and End options included.");

                if (pair.Value.Length > byte.MaxValue)
                    throw new InvalidOperationException("The option value for '" + pair.Key + "' has a length greater than " + byte.MaxValue + " (" + pair.Value.Length + ")");

                stream.WriteByte((byte)pair.Key);
                stream.WriteByte((byte)pair.Value.Length);
                stream.Write(pair.Value, 0, pair.Value.Length);
            }
        }
    }
}
