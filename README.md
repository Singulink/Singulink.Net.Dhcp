# Singulink.Net.Dhcp

[![Join the chat at https://gitter.im/Singulink/Singulink.Net.Dhcp](https://badges.gitter.im/Singulink/Singulink.Net.Dhcp.svg)](https://gitter.im/Singulink/Singulink.Net.Dhcp?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)
[![View nuget packages](https://img.shields.io/nuget/v/Singulink.Net.Dhcp.svg)](https://www.nuget.org/packages/Singulink.Net.Dhcp/)
[![Build](https://github.com/Singulink/Singulink.Net.Dhcp/workflows/build/badge.svg)](https://github.com/Singulink/Singulink.Net.Dhcp/actions?query=workflow%3A%22build%22)

This cross-platform .NET Standard 2.0 library allows you to easily integrate a DHCP server into your .NET Core or .NET Framework application. It has been in active use in mission-critical applications since 2010 serving IP addresses to embedded devices on private networks.

## Installation

Simply install the `Singulink.Net.Dhcp` NuGet package and implement the `DhcpServer` class as required by your application.

## Example Implementation

The following is a minimal example of how you might implement a custom DHCP server with logging using this library:

```c#
using System;
using System.Net;
using System.Net.Sockets;
using Singulink.Net.Dhcp;
using System.Net.NetworkInformation;

namespace SampleApplication
{
    public class AppDhcpServer : DhcpServer
    {
        private static readonly IPAddress SubnetMask = IPAddress.Parse("255.255.0.0");
        
        // Grab a logger using your logging library of choice:
        private static readonly ILog _log = LogProvider.GetCurrentClassLogger();

        // Your implementation of mappings between IP addresses and MAC addresses - could be
        // an in-memory mapping, database lookup based, or some other custom mechanism of 
        // mapping values:
        private IPAddressMap _clientMap = new IPAddressMap();

        private readonly object _syncRoot = new object();

        public event Action<PhysicalAddress> DiscoverReceived = delegate { };
        public event Action Disconnected = delegate { };

        public AppDhcpServer(IPAddress listeningAddress) : base(listeningAddress, SubnetMask) { }

        public override void Start()
        {
            _log.Debug("Starting server...");
            base.Start();
            _log.Info("Started.");
        }

        public override void Stop()
        {
            _log.Debug("Stopping server...");
            base.Stop();
            _log.Info("Stopped.");
        }

        public PhysicalAddress? GetMacAddress(IPAddress ip)
        {
            lock (_syncRoot)
            {
                if (_clientMap.TryGetValue(ip, out PhysicalAddress mac))
                    return mac;
            }

            return null;
        }

        protected override DhcpDiscoverResult? OnDiscoverReceived(DhcpMessage message)
        {
            _log.Debug(message);

            IPAddress ip;
            PhysicalAddress mac = message.ClientMacAddress;

            lock (_syncRoot)
            {
                if (!_clientMap.TryGetValue(mac, out ip))
                {
                    try
                    {
                        ip = _clientMap.GetNextAvailableIPAddress();
                    }
                    catch
                    {
                        _log.Error("No more IP addresses available.");
                        return null;
                    }

                    _clientMap[mac] = ip;
                }
            }

            DiscoverReceived.Invoke(mac);
            return DhcpDiscoverResult.CreateOffer(message, ip, uint.MaxValue);
        }

        protected override DhcpRequestResult? OnRequestReceived(DhcpMessage message)
        {
            _log.Debug(message);

            var ip = message.Options.RequestedIPAddress;
            var mac = message.ClientMacAddress;

            if (ip == null)
                return DhcpRequestResult.CreateNoAcknowledgement(message, "No requested IP address provided");

            lock (_syncRoot)
            {
                if (!_clientMap.Contains(mac, ip))
                    return DhcpRequestResult.CreateNoAcknowledgement(message, "No matching offer found.");
            }

            return DhcpRequestResult.CreateAcknowledgement(message, ip, uint.MaxValue);
        }

        protected override void OnReleaseReceived(DhcpMessage message)
        {
            _log.Debug(message);
        }

        protected override void OnDeclineReceived(DhcpMessage message)
        {
            _log.Debug(message);

            var ip = message.Options.RequestedIPAddress;
            var mac = message.ClientMacAddress;

            if (ip != null)
            {
                _log.DebugFormat("Purge requested for client record: {0} / {1}.", mac, ip);

                lock (_syncRoot)
                {
                    if (_clientMap.Remove(mac, ip))
                    {
                        _log.DebugFormat("Purged client record: {0} / {1}.", mac, ip);
                    }
                }
            }
        }

        protected override void OnInformReceived(DhcpMessage message)
        {
            _log.Debug(message);
        }

        protected override void OnResponseSent(DhcpMessage message)
        {
            _log.Debug(message);
        }

        protected override void OnMessageError(Exception ex)
        {
            _log.Error("Bad message recieved.", ex);
        }

        protected override void OnSocketError(SocketException ex)
        {
            _log.Error("Socket error.", ex);
            Disconnected.Invoke();
        }
    }
}

```
