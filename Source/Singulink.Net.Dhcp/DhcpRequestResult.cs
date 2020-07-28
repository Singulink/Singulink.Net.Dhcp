using System;
using System.Net;

namespace Singulink.Net.Dhcp
{
    public sealed class DhcpRequestResult
    {
        public DhcpMessage SourceMessage { get; }

        public DhcpMessageType MessageType { get; }

        public IPAddress? AssignedIPAddress { get; }

        public uint LeaseSeconds { get; }

        public string? ErrorMessage { get; }

        private DhcpRequestResult(DhcpMessage sourceMessage, IPAddress assignedIPAddress, uint leaseSeconds)
        {
            if (sourceMessage.Options.MessageType != DhcpMessageType.Request)
                throw new ArgumentException("Source message must be a Request type.", nameof(sourceMessage));

            SourceMessage = sourceMessage;
            MessageType = DhcpMessageType.Acknowledge;

            AssignedIPAddress = assignedIPAddress;
            LeaseSeconds = leaseSeconds;
        }

        private DhcpRequestResult(DhcpMessage sourceMessage, string errorMessage)
        {
            if (sourceMessage.Options.MessageType != DhcpMessageType.Request)
                throw new ArgumentException("Source message must be a Request type.", nameof(sourceMessage));

            SourceMessage = sourceMessage;
            MessageType = DhcpMessageType.NoAcknowledge;

            ErrorMessage = errorMessage;
        }

        public static DhcpRequestResult CreateAcknowledgement(DhcpMessage sourceMessage, IPAddress assignedIPAddress, uint leaseSeconds)
        {
            return new DhcpRequestResult(sourceMessage, assignedIPAddress, leaseSeconds);
        }

        public static DhcpRequestResult CreateNoAcknowledgement(DhcpMessage sourceMessage, string errorMessage)
        {
            return new DhcpRequestResult(sourceMessage, errorMessage);
        }

        internal DhcpMessage CreateMessage()
        {
            //  [X] = handled here
            //  [S] = handled by DhcpServer

            //      Field      DHCPOFFER            DHCPACK              DHCPNAK
            //      -----      ---------            -------              -------

            //  [X] 'op'       BOOTREPLY            BOOTREPLY            BOOTREPLY
            //  [X] 'htype'    Copy                 Copy                 Copy
            //  [X] 'hlen'     Copy                 Copy                 Copy
            //  [ ] 'hops'     -                    -                    -
            //  [X] 'xid'      Copy                 Copy                 Copy
            //  [ ] 'secs'     -                    -                    -
            //  [X] 'ciaddr'   -                    Copy                 -
            //  [X] 'yiaddr'   IP address offered   IP address assigned  -
            //  [ ] 'siaddr'   -                    -                    -
            //  [X] 'flags'    Copy                 Copy                 Copy
            //  [X] 'giaddr'   Copy                 Copy                 Copy
            //  [X] 'chaddr'   Copy                 Copy                 Copy
            //  [ ] 'sname'    Server name/options  Server name/options  -
            //  [ ] 'file'     Boot file/options    Boot file/options     -
            //  [ ] 'options'  options              options

            //      Option                    DHCPOFFER    DHCPACK            DHCPNAK
            //      ------                    ---------    -------            -------
            //  [ ] Requested IP address      MUST NOT     MUST NOT           MUST NOT
            //  [X] IP address lease time     MUST         MUST (DHCPREQUEST) MUST NOT
            //                                             MUST NOT (DHCPINFORM)
            //  [ ] Use 'file'/'sname' fields MAY          MAY                MUST NOT
            //  [ ] DHCP message type         DHCPOFFER    DHCPACK            DHCPNAK
            //  [ ] Parameter request list    MUST NOT     MUST NOT           MUST NOT
            //  [ ] Message                   SHOULD       SHOULD             SHOULD
            //  [ ] Client identifier         MUST NOT     MUST NOT           MAY
            //  [ ] Vendor class identifier   MAY          MAY                MAY
            //  [S] Server identifier         MUST         MUST               MUST
            //  [ ] Maximum message size      MUST NOT     MUST NOT           MUST NOT
            //  [S] Subnet mask               MAY          MAY                MUST NOT
            //  [ ] All others                MAY          MAY                MUST NOT

            var response = new DhcpMessage(DhcpOpcode.BootReply, MessageType) {
                HardwareAddressType = SourceMessage.HardwareAddressType,
                HardwareAddressLength = SourceMessage.HardwareAddressLength,
                TransactionID = SourceMessage.TransactionID,
                Flags = SourceMessage.Flags,
                RelayAgentIPAddress = SourceMessage.RelayAgentIPAddress,
                ClientMacAddress = SourceMessage.ClientMacAddress
            };

            if (MessageType == DhcpMessageType.Acknowledge)
            {
                response.ClientIPAddress = SourceMessage.ClientIPAddress;
                response.YourClientIPAddress = AssignedIPAddress!;
                response.Options.SetValue(DhcpOption.IPAddressLeaseTime, LeaseSeconds);
            }
            else if (!string.IsNullOrEmpty(ErrorMessage))
            {
                response.Options.SetValue(DhcpOption.Message, ErrorMessage);
            }

            return response;
        }
    }
}