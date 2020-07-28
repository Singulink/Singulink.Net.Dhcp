using System;

namespace Singulink.Net.Dhcp
{
    public sealed class DhcpMessageException : Exception
    {
        public DhcpMessage? DhcpMessage { get; }

        public DhcpMessageException()
        {
        }

        public DhcpMessageException(string? message) : base(message)
        {
        }

        public DhcpMessageException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        public DhcpMessageException(string? message, DhcpMessage? dhcpMessage) : base(message)
        {
            DhcpMessage = dhcpMessage;
        }
    }
}
