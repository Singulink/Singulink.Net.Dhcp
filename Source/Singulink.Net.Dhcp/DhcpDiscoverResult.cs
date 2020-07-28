using System;
using System.Net;

namespace Singulink.Net.Dhcp
{
    public class DhcpDiscoverResult
    {
        /// <summary>
        /// Gets the source DHCP DISCOVER message that initiated the request.
        /// </summary>
        public DhcpMessage SourceMessage { get; }

        /// <summary>
        /// Gets the IP address being offered to the client.
        /// </summary>
        public IPAddress OfferedIPAddress { get; }

        /// <summary>
        /// Gets the lease time (in seconds) that the offer is valid.
        /// </summary>
        public uint LeaseSeconds { get; }

        private DhcpDiscoverResult(DhcpMessage sourceMessage, IPAddress offeredIPAddress, uint leaseSeconds)
        {
            if (sourceMessage.Options.MessageType != DhcpMessageType.Discover)
                throw new ArgumentException("Source message must be a Discover type.", nameof(sourceMessage));

            SourceMessage = sourceMessage;
            OfferedIPAddress = offeredIPAddress;
            LeaseSeconds = leaseSeconds;
        }

        /// <summary>
        /// Creates an offer based on a source request message.
        /// </summary>
        public static DhcpDiscoverResult CreateOffer(DhcpMessage sourceMessage, IPAddress offeredIPAddress, uint leaseSeconds)
        {
            return new DhcpDiscoverResult(sourceMessage, offeredIPAddress, leaseSeconds);
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
            //  [ ] 'ciaddr'   -                    Copy                 -
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
            //  [X] DHCP message type         DHCPOFFER    DHCPACK            DHCPNAK
            //  [ ] Parameter request list    MUST NOT     MUST NOT           MUST NOT
            //  [ ] Message                   SHOULD       SHOULD             SHOULD
            //  [ ] Client identifier         MUST NOT     MUST NOT           MAY
            //  [ ] Vendor class identifier   MAY          MAY                MAY
            //  [X] Server identifier         MUST         MUST               MUST
            //  [ ] Maximum message size      MUST NOT     MUST NOT           MUST NOT
            //  [S] Subnet mask               MAY          MAY                MUST NOT
            //  [ ] All others                MAY          MAY                MUST NOT

            var response = new DhcpMessage(DhcpOpcode.BootReply, DhcpMessageType.Offer) {
                HardwareAddressType = SourceMessage.HardwareAddressType,
                HardwareAddressLength = SourceMessage.HardwareAddressLength,
                TransactionID = SourceMessage.TransactionID,
                YourClientIPAddress = OfferedIPAddress,
                Flags = SourceMessage.Flags,
                RelayAgentIPAddress = SourceMessage.RelayAgentIPAddress,
                ClientMacAddress = SourceMessage.ClientMacAddress,
            };

            response.Options.SetValue(DhcpOption.IPAddressLeaseTime, LeaseSeconds);

            return response;
        }
    }
}