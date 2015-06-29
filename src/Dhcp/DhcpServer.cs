﻿using Dhcp.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Dhcp
{
    /// <summary>
    /// Represents a DHCP Server
    /// </summary>
    public class DhcpServer
    {
        internal DHCP_IP_ADDRESS ipAddress;
        private string name;
        private Lazy<Tuple<int, int>> version;
        private Lazy<DhcpServerConfiguration> config;
        private Lazy<Tuple<string, string>> specificStrings;
        private Lazy<Dictionary<int, DhcpServerOption>> options;
        private Lazy<DhcpServerAuditLog> auditLog;

        internal DhcpServer(DHCP_IP_ADDRESS ipAddress, string name)
        {
            this.ipAddress = ipAddress;
            this.name = name;

            this.version = new Lazy<Tuple<int, int>>(GetVersion);
            this.config = new Lazy<DhcpServerConfiguration>(() => DhcpServerConfiguration.GetConfiguration(this));
            this.specificStrings = new Lazy<Tuple<string, string>>(GetSpecificStrings);
            this.options = new Lazy<Dictionary<int, DhcpServerOption>>(() => DhcpServerOption.GetOptions(this).ToDictionary(o => o.OptionId));
            this.auditLog = new Lazy<DhcpServerAuditLog>(() => DhcpServerAuditLog.GetParams(this));
        }

        public IPAddress IpAddress { get { return this.ipAddress.ToIPAddress(); } }
        public int IpAddressNative { get { return (int)this.ipAddress; } }
        public string Name { get { return this.name; } }
        public int VersionMajor { get { return this.version.Value.Item1; } }
        public int VersionMinor { get { return this.version.Value.Item2; } }
        public DhcpServerVersions Version { get { return (DhcpServerVersions)(((ulong)this.version.Value.Item1 << 16) | (uint)this.version.Value.Item2); } }
        public DhcpServerConfiguration Configuration { get { return this.config.Value; } }

        public string DefaultVendorClassName { get { return this.specificStrings.Value.Item1; } }
        public string DefaultUserClassName { get { return this.specificStrings.Value.Item2; } }

        public bool IsCompatible(DhcpServerVersions Version)
        {
            return (long)Version <= (long)this.Version;
        }

        /// <summary>
        /// Enumerates a list of Classes associated with the DHCP Server
        /// </summary>
        public IEnumerable<DhcpServerClass> Classes
        {
            get
            {
                return DhcpServerClass.GetClasses(this);
            }
        }

        /// <summary>
        /// Enumerates a list of Global Option Values associated with the DHCP Server
        /// </summary>
        public IEnumerable<DhcpServerOptionValue> GlobalOptionValues
        {
            get
            {
                return DhcpServerOptionValue.EnumGlobalOptionValues(this);
            }
        }

        /// <summary>
        /// Enumerates a list of Options associated with the DHCP Server
        /// </summary>
        public IEnumerable<DhcpServerOption> Options
        {
            get
            {
                return this.options.Value.Values;
            }
        }

        /// <summary>
        /// Enumerates a list of Scopes associated with the DHCP Server
        /// </summary>
        public IEnumerable<DhcpServerScope> Scopes
        {
            get
            {
                return DhcpServerScope.GetScopes(this);
            }
        }

        /// <summary>
        /// Enumerates a list of all Clients (in all Scopes) associated with the DHCP Server
        /// </summary>
        public IEnumerable<DhcpServerClient> Clients
        {
            get
            {
                return DhcpServerClient.GetClients(this);
            }
        }

        /// <summary>
        /// Enumerates a list of Binding Elements associated with the DHCP Server
        /// </summary>
        public IEnumerable<DhcpServerBindingElement> BindingElements
        {
            get
            {
                return DhcpServerBindingElement.GetBindingInfo(this);
            }
        }

        /// <summary>
        /// Queries the DHCP Server for the specified OptionId
        /// </summary>
        /// <param name="OptionId">The identifier for the option to retrieve</param>
        /// <returns>A <see cref="DhcpServerOption"/> or <see cref="null"/> if the option wasn't present on the DHCP Server.</returns>
        public DhcpServerOption GetOption(int OptionId)
        {
            if (this.options.IsValueCreated)
            {
                // Use Cache
                DhcpServerOption option;
                if (this.options.Value.TryGetValue(OptionId, out option))
                    return option;
                else
                    return null;
            }
            else
            {
                return DhcpServerOption.GetOption(this, OptionId);
            }
        }

        /// <summary>
        /// The audit log configuration settings from the DHCP server.
        /// </summary>
        public DhcpServerAuditLog AuditLog
        {
            get
            {
                return auditLog.Value;
            }
        }

        /// <summary>
        /// Enumerates a list of DHCP servers found in the directory service. 
        /// </summary>
        public static IEnumerable<DhcpServer> Servers
        {
            get
            {
                IntPtr serversPtr;

                var result = Api.DhcpEnumServers(0, IntPtr.Zero, out serversPtr, IntPtr.Zero, IntPtr.Zero);

                if (result != DhcpErrors.SUCCESS)
                    throw new DhcpServerException("DhcpEnumServers", result);

                var servers = (DHCPDS_SERVERS)Marshal.PtrToStructure(serversPtr, typeof(DHCPDS_SERVERS));

                try
                {
                    foreach (var server in servers.Servers)
                    {
                        yield return DhcpServer.FromNative(server);
                    }
                }
                finally
                {
                    Api.DhcpRpcFreeMemory(serversPtr);
                }
            }
        }

        /// <summary>
        /// Connects to a DHCP server
        /// </summary>
        /// <param name="HostNameOrAddress"></param>
        public static DhcpServer Connect(string HostNameOrAddress)
        {
            if (string.IsNullOrWhiteSpace(HostNameOrAddress))
                throw new ArgumentNullException("HostNameOrAddress");

            var dnsEntry = Dns.GetHostEntry(HostNameOrAddress);

            var dnsAddress = dnsEntry.AddressList.Where(a => a.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault();

            if (dnsAddress == null)
                throw new NotSupportedException("Unable to resolve an IPv4 address for the DHCP Server");

            var address = DHCP_IP_ADDRESS.FromIPAddress(dnsAddress);

            return new DhcpServer(address, dnsEntry.HostName);
        }

        private Tuple<int, int> GetVersion()
        {
            int major, minor;

            var result = Api.DhcpGetVersion(this.ipAddress.ToString(), out major, out minor);

            if (result != DhcpErrors.SUCCESS)
                throw new DhcpServerException("DhcpGetVersion", result);

            return Tuple.Create(major, minor);
        }

        private Tuple<string, string> GetSpecificStrings()
        {
            IntPtr stringsPtr;

            var result = Api.DhcpGetServerSpecificStrings(this.ipAddress.ToString(), out stringsPtr);

            if (result != DhcpErrors.SUCCESS)
                throw new DhcpServerException("DhcpGetServerSpecificStrings", result);

            var strings = (DHCP_SERVER_SPECIFIC_STRINGS)Marshal.PtrToStructure(stringsPtr, typeof(DHCP_SERVER_SPECIFIC_STRINGS));

            try
            {
                return Tuple.Create(strings.DefaultVendorClassName, strings.DefaultUserClassName);
            }
            finally
            {
                Api.DhcpRpcFreeMemory(stringsPtr);
            }
        }

        private static DhcpServer FromNative(DHCPDS_SERVER Native)
        {
            return new DhcpServer(Native.ServerAddress, Native.ServerName);
        }

        public override string ToString()
        {
            return string.Format("DHCP Server: {0} ({1})", this.Name, this.IpAddress);
        }
    }
}
