using System;
using System.Net;
using System.Net.Sockets;
using System.IO;

using System.Net.NetworkInformation;
using System.Linq;

namespace Singulink.Net.Dhcp
{
    public abstract class DhcpServer
    {
        private const int ListeningPort = 67;
        private const int ReplyPort = 68;

        private UdpClient? _udpClient;
        private readonly IPEndPoint _localEndPoint;
        private readonly IPAddress _subnetMask;

        private readonly object _controlSync = new object();

        protected DhcpServer(IPAddress listeningAddress, IPAddress subnetMask)
        {
            if (listeningAddress.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("Only IPv4 addresses are supported.", nameof(listeningAddress));

            if (subnetMask.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("Only IPv4 subnet masks are supported.", nameof(subnetMask));

            _localEndPoint = new IPEndPoint(listeningAddress, ListeningPort);
            _subnetMask = subnetMask;
        }

        /// <summary>
        /// Gets a value indicating whether the DHCP server is currently running.
        /// </summary>
        public bool IsRunning => _udpClient != null;

        /// <summary>
        /// Gets a value indicating whether the server can be started, i.e. if the listening interface is currently up.
        /// </summary>
        public bool CanStart => NetworkInterface.GetAllNetworkInterfaces().Any(
            n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            n.Supports(NetworkInterfaceComponent.IPv4) &&
            n.OperationalStatus == OperationalStatus.Up &&
            n.GetIPProperties().UnicastAddresses.Any(a => _localEndPoint.Address.Equals(a.Address) && _subnetMask.Equals(a.IPv4Mask)));

        /// <summary>
        /// Starts the DHCP server.
        /// </summary>
        public virtual void Start()
        {
            lock (_controlSync) {
                if (IsRunning)
                    return;

                _udpClient = new UdpClient(_localEndPoint);
                RunReceiveLoopAsync(_udpClient);
            }
        }

        /// <summary>
        /// Stops the DHCP server.
        /// </summary>
        public virtual void Stop()
        {
            lock (_controlSync) {
                if (!IsRunning)
                    return;

                try {
                    _udpClient?.Close();
                }
                catch { }

                _udpClient = null;
            }
        }

        /// <summary>
        /// This method is called when a discover message is received.
        /// </summary>
        /// <param name="message">The discover message received.</param>
        /// <returns>An offer message to respond with, otherwise null for no response.</returns>
        protected abstract DhcpDiscoverResult? OnDiscoverReceived(DhcpMessage message);

        /// <summary>
        /// This method is called when a request message is recieved.
        /// </summary>
        /// <param name="message">The request message recived.</param>
        /// <returns>The acknowledge or no-acknowledge message to respond with, otherwise null for no response.</returns>
        protected abstract DhcpRequestResult? OnRequestReceived(DhcpMessage message);

        /// <summary>
        /// This method is called when a decline message is received.
        /// </summary>
        /// <param name="message">The decline message received.</param>
        protected abstract void OnDeclineReceived(DhcpMessage message);

        /// <summary>
        /// This method is called when a release message is received.
        /// </summary>
        /// <param name="message">The release message received.</param>
        protected abstract void OnReleaseReceived(DhcpMessage message);

        /// <summary>
        /// This method is called when an inform message is received.
        /// </summary>
        /// <param name="message">The inform message received.</param>
        protected abstract void OnInformReceived(DhcpMessage message);

        /// <summary>
        /// This method is called anytime a response has been successfully sent.
        /// </summary>
        /// <param name="message">The response sent.</param>
        protected abstract void OnResponseSent(DhcpMessage message);

        /// <summary>
        /// This method is called anytime a socket error occurs during communcation. Socket errors cause the DHCP server to be stopped.
        /// </summary>
        /// <param name="ex">The SocketException that caused the error to occur.</param>
        protected abstract void OnSocketError(SocketException ex);

        /// <summary>
        /// This method is called anytime a message is received that contains errors.
        /// </summary>
        /// <param name="ex">The exception that occured during received message parsing.</param>
        protected abstract void OnMessageError(Exception ex);

        private async void RunReceiveLoopAsync(UdpClient udpClient)
        {
            while (true) {
                UdpReceiveResult result;

                try {
                    result = await udpClient.ReceiveAsync().ConfigureAwait(false);
                }
                catch (SocketException ex) {
                    Stop();
                    OnSocketError(ex);

                    return;
                }
                catch (ObjectDisposedException) {
                    return;
                }

                DhcpMessage message;

                try {
                    message = new DhcpMessage(result.Buffer);
                }
                catch (InvalidDataException ex) {
                    OnMessageError(ex);
                    continue;
                }

                if (!message.RelayAgentIPAddress.Equals(IPAddress.Any)) {
                    OnMessageError(new DhcpMessageException("Relayed DHCP messages are not supported.", message));
                    continue;
                }

                if (message.OpCode != DhcpOpcode.BootRequest) {
                    OnMessageError(new DhcpMessageException("Invalid op code.", message));
                    continue;
                }

                switch (message.Options.MessageType) {
                    case DhcpMessageType.Discover:
                        var discoverResult = OnDiscoverReceived(message);

                        if (discoverResult != null)
                            SendResponse(udpClient, discoverResult.CreateMessage());

                        break;
                    case DhcpMessageType.Request:
                        if (message.Options.ServerIdentifier?.Equals(_localEndPoint.Address) == false)
                            continue;

                        var requestResult = OnRequestReceived(message);

                        if (requestResult != null)
                            SendResponse(udpClient, requestResult.CreateMessage());

                        break;
                    case DhcpMessageType.Release:
                        OnReleaseReceived(message);
                        break;
                    case DhcpMessageType.Decline:
                        OnDeclineReceived(message);
                        break;
                    default:
                        OnMessageError(new DhcpMessageException("Invalid DhcpMessageType received.", message));
                        break;
                }
            }
        }

        private async void SendResponse(UdpClient client, DhcpMessage message)
        {
            if (message.Options.MessageType == DhcpMessageType.Offer || message.Options.MessageType == DhcpMessageType.Acknowledge) {
                message.Options.SetValue(DhcpOption.SubnetMask, _subnetMask);
            }
            else if (message.Options.MessageType != DhcpMessageType.NoAcknowledge) {
                throw new DhcpMessageException($"Cannot send '{message.Options.MessageType}' messages from a server.", message);
            }

            message.Options.SetValue(DhcpOption.ServerIdentifier, _localEndPoint.Address);

            try {
                byte[] messageBytes = message.GetBytes();
                var remoteEndPoint = new IPEndPoint(message.ClientIPAddress.Equals(IPAddress.Any) ? IPAddress.Broadcast : message.ClientIPAddress, ReplyPort);

                await client.SendAsync(messageBytes, messageBytes.Length, remoteEndPoint).ConfigureAwait(false);

                OnResponseSent(message);
            }
            catch (SocketException ex) {
                OnSocketError(ex);
            }
            catch (ObjectDisposedException) { }
        }
    }
}