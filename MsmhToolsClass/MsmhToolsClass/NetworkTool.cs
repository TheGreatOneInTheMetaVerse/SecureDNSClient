﻿using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MsmhToolsClass;

public static class NetworkTool
{
    /// <summary>
    /// IP to Host using Nslookup (Windows Only)
    /// </summary>
    /// <param name="ip"></param>
    /// <returns></returns>
    public static string IpToHost(string ip, out string baseHost)
    {
        string result = string.Empty;
        baseHost = string.Empty;
        if (!OperatingSystem.IsWindows()) return result;
        if (!IsInternetAlive()) return result; // nslookup takes time when there is no internet access

        string content = ProcessManager.Execute(out _, "nslookup", null, ip, true, true);
        if (string.IsNullOrEmpty(content)) return result;
        content = content.ToLower();
        string[] split = content.Split(Environment.NewLine);
        for (int n = 0; n < split.Length; n++)
        {
            string line = split[n];
            if (line.Contains("name:"))
            {
                result = line.Replace("name:", string.Empty).Trim();
                if (result.Contains('.'))
                {
                    GetHostDetails(result, 0, out _, out _, out baseHost, out _, out _, out _);
                }
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Restart NAT Driver - Windows Only
    /// </summary>
    /// <returns></returns>
    public static async Task RestartNATDriver()
    {
        if (!OperatingSystem.IsWindows()) return;
        // Solve: "bind: An attempt was made to access a socket in a way forbidden by its access permissions"
        // Windows 10 above
        try
        {
            await ProcessManager.ExecuteAsync("net", null, "stop winnat", true, true);
            await ProcessManager.ExecuteAsync("net", null, "start winnat", true, true);
        }
        catch (Exception) { }
    }

    public static int GetNextPort(int currentPort)
    {
        currentPort = currentPort < 65535 ? currentPort + 1 : currentPort - 1;
        return currentPort;
    }

    public static Uri? UrlToUri(string url)
    {
        try
        {
            string[] split1 = url.Split("//");
            string prefix = "https://";
            for (int n1 = 0; n1 < split1.Length; n1++)
            {
                if (n1 > 0)
                {
                    prefix += split1[n1];
                    if (n1 < split1.Length - 1)
                        prefix += "//";
                }
            }

            Uri uri = new(prefix);
            return uri;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
        return null;
    }

    public static void GetUrlDetails(string url, int defaultPort, out string scheme, out string host, out string subHost, out string baseHost, out int port, out string path, out bool isIPv6)
    {
        url = url.Trim();
        scheme = string.Empty;

        // Strip xxxx://
        if (url.Contains("//"))
        {
            string[] split = url.Split("//");

            if (split.Length > 0)
                if (!string.IsNullOrEmpty(split[0]))
                    scheme = $"{split[0]}//";

            if (split.Length > 1)
                if (!string.IsNullOrEmpty(split[1]))
                    url = split[1];
        }

        GetHostDetails(url, defaultPort, out host, out subHost, out baseHost, out port, out path, out isIPv6);
    }

    public static void GetHostDetails(string hostIpPort, int defaultPort, out string host, out string subHost, out string baseHost, out int port, out string path, out bool isIPv6)
    {
        hostIpPort = hostIpPort.Trim();
        host = hostIpPort;
        subHost = string.Empty;
        baseHost = host;
        port = defaultPort;
        path = string.Empty;
        isIPv6 = false;

        try
        {
            // Strip /xxxx (Path)
            if (!hostIpPort.Contains("//") && hostIpPort.Contains('/'))
            {
                string[] split = hostIpPort.Split('/');
                if (!string.IsNullOrEmpty(split[0]))
                    hostIpPort = split[0];

                // Get Path
                string slash = "/";
                string outPath = slash;
                for (int n = 0; n < split.Length; n++)
                {
                    if (n != 0) outPath += split[n] + "/";
                }
                if (outPath.Length > 1 && outPath.EndsWith("/")) outPath = outPath.TrimEnd(slash.ToCharArray());
                if (!outPath.Equals("/")) path = outPath;
            }

            // Split Host and Port
            string host0 = hostIpPort;
            if (hostIpPort.Contains('[') && hostIpPort.Contains("]:")) // IPv6 + Port
            {
                string[] split = hostIpPort.Split("]:");
                if (split.Length == 2)
                {
                    isIPv6 = true;
                    host0 = $"{split[0]}]";
                    bool isInt = int.TryParse(split[1], out int result);
                    if (isInt) port = result;
                }
            }
            else if (hostIpPort.Contains('[') && hostIpPort.Contains(']')) // IPv6
            {
                string[] split = hostIpPort.Split(']');
                if (split.Length == 2)
                {
                    isIPv6 = true;
                    host0 = $"{split[0]}]";
                }
            }
            else if (!hostIpPort.Contains('[') && !hostIpPort.Contains(']') && hostIpPort.Contains(':')) // Host + Port OR IPv4 + Port
            {
                string[] split = hostIpPort.Split(':');
                if (split.Length == 2)
                {
                    host0 = split[0];
                    bool isInt = int.TryParse(split[1], out int result);
                    if (isInt) port = result;
                }
            }

            host = host0;

            // Get Base Host
            if (!IsIp(host, out _) && host.Contains('.'))
            {
                baseHost = host;
                string[] dotSplit = host.Split('.');
                int realLength = dotSplit.Length;
                if (realLength >= 3)
                {
                    // e.g. *.co.uk, *.org.us
                    if (dotSplit[^2].Length <= 3 && dotSplit[^1].Length <= 2) realLength--;

                    if (realLength >= 3)
                    {
                        if (realLength == 3 && dotSplit[0].Equals("www"))
                            baseHost = baseHost.TrimStart("www.");
                        else
                        {
                            int domainLength = realLength < dotSplit.Length ? 3 : 2;

                            baseHost = string.Empty;
                            for (int i = 0; i < dotSplit.Length; i++)
                            {
                                if (i >= dotSplit.Length - domainLength)
                                    baseHost += $"{dotSplit[i]}.";
                            }
                            if (baseHost.EndsWith('.')) baseHost = baseHost[..^1];
                        }
                    }
                }
            }

            // Get Sub Host (Subdomain)
            if (!baseHost.Equals(host))
            {
                string baseHostWithDot = $".{baseHost}";
                if (host.Contains(baseHostWithDot))
                    subHost = host.Replace(baseHostWithDot, string.Empty);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("GetHostDetails: " + ex.Message);
        }
    }

    public static bool IsLocalIP(string ipv4)
    {
        string ip = ipv4.Trim();
        return ip.ToLower().Equals("localhost") || ip.Equals("0.0.0.0") || ip.StartsWith("10.") || ip.StartsWith("127.") || ip.StartsWith("192.168.") ||
               ip.StartsWith("172.16.") || ip.StartsWith("172.17.") || ip.StartsWith("172.18.") || ip.StartsWith("172.19.") ||
               ip.StartsWith("172.20.") || ip.StartsWith("172.21.") || ip.StartsWith("172.22.") || ip.StartsWith("172.23.") ||
               ip.StartsWith("172.24.") || ip.StartsWith("172.25.") || ip.StartsWith("172.26.") || ip.StartsWith("172.27.") ||
               ip.StartsWith("172.28.") || ip.StartsWith("172.29.") || ip.StartsWith("172.30.") || ip.StartsWith("172.31.");
    }

    /// <summary>
    /// Uses ipinfo.io to get result
    /// </summary>
    /// <param name="iPAddress">IP to check</param>
    /// <param name="proxyScheme">Use proxy to connect</param>
    /// <returns>Company name</returns>
    public static async Task<string?> IpToCompanyAsync(string iPStr, string? proxyScheme = null)
    {
        string? company = null;
        try
        {
            using SocketsHttpHandler socketsHttpHandler = new();
            if (proxyScheme != null)
                socketsHttpHandler.Proxy = new WebProxy(proxyScheme, true);
            using HttpClient httpClient2 = new(socketsHttpHandler);
            company = await httpClient2.GetStringAsync("https://ipinfo.io/" + iPStr + "/org");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
        return company;
    }

    public static IPAddress? GetLocalIPv4(string remoteHostToCheck = "8.8.8.8")
    {
        try
        {
            IPAddress? localIP;
            using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect(remoteHostToCheck, 80);
            IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
            localIP = endPoint?.Address;
            return localIP;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            return null;
        }
    }

    public static IPAddress? GetLocalIPv6(string remoteHostToCheck = "8.8.8.8")
    {
        try
        {
            IPAddress? localIP;
            using Socket socket = new(AddressFamily.InterNetworkV6, SocketType.Dgram, 0);
            socket.Connect(remoteHostToCheck, 80);
            IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
            localIP = endPoint?.Address;
            return localIP;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            return null;
        }
    }

    public static IPAddress? GetDefaultGateway(bool ipv6 = false)
    {
        IPAddress? gateway = null;
        try
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            for (int n = 0; n < nics.Length; n++)
            {
                NetworkInterface nic = nics[n];
                if (nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    IPInterfaceProperties ipProperties = nic.GetIPProperties();
                    GatewayIPAddressInformationCollection gatewayAddresses = ipProperties.GatewayAddresses;
                    foreach (GatewayIPAddressInformation gatewayAddress in gatewayAddresses)
                    {
                        IPAddress address = gatewayAddress.Address;
                        if (!ipv6)
                        {
                            if (address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                gateway = address;
                                Debug.WriteLine("GetDefaultGateway: " + gateway);
                                return gateway;
                            }
                        }
                        else
                        {
                            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                gateway = address;
                                Debug.WriteLine("GetDefaultGateway: " + gateway);
                                return gateway;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception) { }
        return gateway;
    }

    [DllImport("iphlpapi.dll", CharSet = CharSet.Auto)]
    private static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);
    public static IPAddress? GetGatewayForDestination(IPAddress destinationAddress)
    {
        try
        {
            uint destaddr = BitConverter.ToUInt32(destinationAddress.GetAddressBytes(), 0);

            int result = GetBestInterface(destaddr, out uint interfaceIndex);
            if (result != 0)
            {
                Debug.WriteLine(new Win32Exception(result));
                return null;
            }

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                var niprops = ni.GetIPProperties();
                if (niprops == null) continue;

                var gateway = niprops.GatewayAddresses?.FirstOrDefault()?.Address;
                if (gateway == null) continue;

                if (ni.Supports(NetworkInterfaceComponent.IPv4))
                {
                    var v4props = niprops.GetIPv4Properties();
                    if (v4props == null) continue;

                    if (v4props.Index == interfaceIndex) return gateway;
                }

                if (ni.Supports(NetworkInterfaceComponent.IPv6))
                {
                    var v6props = niprops.GetIPv6Properties();
                    if (v6props == null) continue;

                    if (v6props.Index == interfaceIndex) return gateway;
                }
            }
        }
        catch (Exception) { }

        return null;
    }

    public static bool IsIp(string ipStr, out IPAddress? ip)
    {
        ip = null;
        if (!string.IsNullOrEmpty(ipStr))
            return IPAddress.TryParse(ipStr, out ip);
        return false;
    }

    public static bool IsIPv4(IPAddress iPAddress)
    {
        return iPAddress.AddressFamily == AddressFamily.InterNetwork;
    }

    public static bool IsIPv4Valid(string ipString, out IPAddress? iPAddress)
    {
        iPAddress = null;
        if (string.IsNullOrWhiteSpace(ipString)) return false;
        if (!ipString.Contains('.')) return false;
        if (ipString.Count(c => c == '.') != 3) return false;
        if (ipString.StartsWith('.')) return false;
        if (ipString.EndsWith('.')) return false;
        string[] splitValues = ipString.Split('.');
        if (splitValues.Length != 4) return false;

        foreach (string splitValue in splitValues)
        {
            // 0x and 0xx are not valid
            if (splitValue.Length > 1)
            {
                bool isInt1 = int.TryParse(splitValue.AsSpan(0, 1), out int first);
                if (isInt1 && first == 0) return false;
            }

            bool isInt2 = int.TryParse(splitValue, out int testInt);
            if (!isInt2) return false;
            if (testInt < 0 || testInt > 255) return false;
        }

        bool isIP = IPAddress.TryParse(ipString, out IPAddress? outIP);
        if (!isIP) return false;
        iPAddress = outIP;
        return true;
    }

    public static bool IsIPv6(IPAddress iPAddress)
    {
        return iPAddress.AddressFamily == AddressFamily.InterNetworkV6;
    }

    /// <summary>
    /// Windows Only
    /// </summary>
    /// <param name="ipStr">Ipv4 Or Ipv6</param>
    /// <returns></returns>
    public static bool IsIpProtocolReachable(string ipStr)
    {
        if (!OperatingSystem.IsWindows()) return true;
        string args = $"-n 2 {ipStr}";
        string content = ProcessManager.Execute(out _, "ping", null, args, true, false);
        return !content.Contains("transmit failed") && !content.Contains("General failure");
    }

    public static bool IsPortOpen(string host, int port, double timeoutSeconds)
    {
        try
        {
            using TcpClient client = new();
            var result = client.BeginConnect(host, port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeoutSeconds));
            client.EndConnect(result);
            return success;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public class NICResult
    {
        public string NIC_Name { get; set; } = string.Empty;
        private NetworkInterface? pNIC;
        public NetworkInterface? NIC
        {
            get
            {
                if (pNIC != null) return pNIC;
                return GetNICByName(NIC_Name);
            }
            set
            {
                if (pNIC != value) pNIC = value;
            }
        }
        public bool IsUpAndRunning { get; set; } = false;
        public string DnsAddressPrimary { get; set; } = string.Empty;
        public bool IsDnsSetToLoopback { get; set; } = false;
        public bool IsDnsSetToAny { get; set; } = false;
    }

    /// <summary>
    /// Get All Network Interfaces
    /// </summary>
    /// <returns>A List of NIC Names (NetConnectionID)</returns>
    public static List<NICResult> GetAllNetworkInterfaces()
    {
        List<NICResult> nicsList = new();
        List<NICResult> nicsList1 = new();
        if (!OperatingSystem.IsWindows()) return nicsList;

        // API 1
        try
        {
            ObjectQuery? query = new("SELECT * FROM Win32_NetworkAdapter");

            using ManagementObjectSearcher searcher = new(query);
            ManagementObjectCollection queryCollection = searcher.Get();

            foreach (ManagementBaseObject m in queryCollection)
            {
                object netIdObj0 = m["NetConnectionID"];
                if (netIdObj0 == null) continue;
                string netId0 = netIdObj0.ToString() ?? string.Empty;
                netId0 = netId0.Trim();
                if (string.IsNullOrEmpty(netId0)) continue;

                // Get NIC
                NetworkInterface? nic = GetNICByName(netId0);

                // Get Up And Running
                ushort up = 0;
                try { up = Convert.ToUInt16(m["NetConnectionStatus"]); } catch (Exception) { }
                bool isUpAndRunning = up == 2; // Connected

                // Get Prinary DNS Address
                string dnsAddressPrimary = string.Empty;
                if (nic != null)
                {
                    try
                    {
                        IPAddressCollection dnss = nic.GetIPProperties().DnsAddresses;
                        if (dnss.Any()) dnsAddressPrimary = dnss[0].ToString();
                    }
                    catch (Exception) { }
                }

                // Get IsDnsSetToLoopback
                bool isDnsSetToLoopback = dnsAddressPrimary.Equals(IPAddress.Loopback.ToString()) ||
                                          dnsAddressPrimary.Equals(IPAddress.IPv6Loopback.ToString());

                // Get IsDnsSetToAny
                bool isDnsSetToAny = dnsAddressPrimary.Equals(IPAddress.Any.ToString()) ||
                                     dnsAddressPrimary.Equals(IPAddress.IPv6Any.ToString());

                NICResult nicr = new()
                {
                    NIC_Name = netId0,
                    NIC = nic,
                    IsUpAndRunning = isUpAndRunning,
                    DnsAddressPrimary = dnsAddressPrimary,
                    IsDnsSetToLoopback = isDnsSetToLoopback,
                    IsDnsSetToAny = isDnsSetToAny
                };
                nicsList1.Add(nicr);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetAllNetworkInterfaces: {ex.Message}");
        }

        // API 2
        List<NICResult> nicsList2 = GetNetworkInterfaces();

        // Merge API1 & API2
        try
        {
            nicsList = nicsList1.Concat(nicsList2).ToList();
            nicsList = nicsList.DistinctBy(x => x.NIC_Name).ToList();
        }
        catch (Exception) { }

        return nicsList;
    }

    /// <summary>
    /// Does not contain disabled NICs
    /// </summary>
    /// <returns></returns>
    public static List<NICResult> GetNetworkInterfaces()
    {
        List<NICResult> nicsList = new();

        try
        {
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            for (int n1 = 0; n1 < networkInterfaces.Length; n1++)
            {
                NetworkInterface nic = networkInterfaces[n1];
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                {
                    IPInterfaceStatistics statistics = nic.GetIPStatistics();
                    if (statistics.BytesReceived > 0 && statistics.BytesSent > 0)
                    {
                        bool isUpAndRunning = nic.OperationalStatus == OperationalStatus.Up;

                        // Get Prinary DNS Address
                        string dnsAddressPrimary = string.Empty;
                        try
                        {
                            IPAddressCollection dnss = nic.GetIPProperties().DnsAddresses;
                            if (dnss.Any()) dnsAddressPrimary = dnss[0].ToString();
                        }
                        catch (Exception) { }

                        // Get IsDnsSetToLoopback
                        bool isDnsSetToLoopback = dnsAddressPrimary.Equals(IPAddress.Loopback.ToString());

                        // Get IsDnsSetToAny
                        bool isDnsSetToAny = dnsAddressPrimary.Equals(IPAddress.Any.ToString());

                        NICResult nicr = new()
                        {
                            NIC_Name = nic.Name,
                            NIC = nic,
                            IsUpAndRunning = isUpAndRunning,
                            DnsAddressPrimary = dnsAddressPrimary,
                            IsDnsSetToLoopback = isDnsSetToLoopback,
                            IsDnsSetToAny = isDnsSetToAny
                        };
                        nicsList.Add(nicr);
                    }
                }
            }
        }
        catch (Exception) { }

        return nicsList;
    }

    public static async Task EnableNICAsync(string nicName)
    {
        string args = $"interface set interface \"{nicName}\" enable";
        await ProcessManager.ExecuteAsync("netsh", null, args, true, true);
    }

    public static void EnableNIC(string nicName)
    {
        string args = $"interface set interface \"{nicName}\" enable";
        ProcessManager.ExecuteOnly("netsh", args, true, true);
    }

    public static async Task DisableNICAsync(string nicName)
    {
        string args = $"interface set interface \"{nicName}\" disable";
        await ProcessManager.ExecuteAsync("netsh", null, args, true, true);
    }

    public static void DisableNIC(string nicName)
    {
        string args = $"interface set interface \"{nicName}\" disable";
        ProcessManager.ExecuteOnly("netsh", args, true, true);
    }

    public static NetworkInterface? GetNICByName(string name)
    {
        try
        {
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            for (int n = 0; n < networkInterfaces.Length; n++)
            {
                NetworkInterface nic = networkInterfaces[n];
                if (nic.Name.Equals(name)) return nic;
            }
        }
        catch (Exception) { }
        return null;
    }

    public static NetworkInterface? GetNICByDescription(string description)
    {
        try
        {
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            for (int n = 0; n < networkInterfaces.Length; n++)
            {
                NetworkInterface nic = networkInterfaces[n];
                if (nic.Description.Equals(description)) return nic;
            }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>
    /// Set's the IPv4 DNS Server of the local machine (Windows Only)
    /// </summary>
    /// <param name="nic">NIC address</param>
    /// <param name="dnsServers">Comma seperated list of DNS server addresses</param>
    /// <remarks>Requires a reference to the System.Management namespace</remarks>
    public static async Task SetDnsIPv4(NetworkInterface nic, string dnsServers)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Requires Elevation
        if (nic == null) return;

        await SetDnsIPv4(nic.Name, dnsServers);
    }

    /// <summary>
    /// Set's the IPv4 DNS Server of the local machine (Windows Only)
    /// </summary>
    /// <param name="nicName">NIC Name</param>
    /// <param name="dnsServers">Comma seperated list of DNS server addresses</param>
    /// <remarks>Requires a reference to the System.Management namespace</remarks>
    public static async Task SetDnsIPv4(string nicName, string dnsServers)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Requires Elevation
        // Only netsh can set DNS on Windows 7
        if (string.IsNullOrEmpty(nicName)) return;

        try
        {
            string dnsServer1 = dnsServers;
            string dnsServer2 = string.Empty;
            if (dnsServers.Contains(','))
            {
                string[] split = dnsServers.Split(',');
                dnsServer1 = split[0].Trim();
                dnsServer2 = split[1].Trim();
            }

            string processName = "netsh";
            string processArgs1 = $"interface ipv4 delete dnsservers \"{nicName}\" all";
            string processArgs2 = $"interface ipv4 set dnsservers \"{nicName}\" static {dnsServer1} primary";
            string processArgs3 = $"interface ipv4 add dnsservers \"{nicName}\" {dnsServer2} index=2";
            await ProcessManager.ExecuteAsync(processName, null, processArgs1, true, true);
            await ProcessManager.ExecuteAsync(processName, null, processArgs2, true, true);
            if (!string.IsNullOrEmpty(dnsServer2))
                await ProcessManager.ExecuteAsync(processName, null, processArgs3, true, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("SetDnsIPv4: " + ex.Message);
        }
    }

    /// <summary>
    /// Unset IPv4 DNS to DHCP (Windows Only)
    /// </summary>
    /// <param name="nic">Network Interface</param>
    public static async Task UnsetDnsIPv4(NetworkInterface nic)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Requires Elevation - Can't Unset DNS when there is no Internet connectivity but netsh can :)
        if (nic == null) return;

        await UnsetDnsIPv4(nic.Name);
    }

    /// <summary>
    /// Unset IPv4 DNS to DHCP (Windows Only)
    /// </summary>
    /// <param name="nicName">Network Interface Name</param>
    public static async Task UnsetDnsIPv4(string nicName)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Requires Elevation - Can't Unset DNS when there is no Internet connectivity but netsh can :)
        // NetSh Command: netsh interface ip set dns "nicName" source=dhcp
        if (string.IsNullOrEmpty(nicName)) return;

        try
        {
            string processName = "netsh";
            string processArgs1 = $"interface ipv4 delete dnsservers \"{nicName}\" all";
            string processArgs2 = $"interface ipv4 set dnsservers \"{nicName}\" source=dhcp";
            await ProcessManager.ExecuteAsync(processName, null, processArgs1, true, true);
            await ProcessManager.ExecuteAsync(processName, null, processArgs2, true, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    /// <summary>
    /// Unset IPv4 DNS by seting DNS to Static
    /// </summary>
    /// <param name="nic">Network Interface</param>
    /// <param name="dns1">Primary</param>
    /// <param name="dns2">Secondary</param>
    public static async Task UnsetDnsIPv4(NetworkInterface nic, string dns1, string? dns2)
    {
        string dnsServers = dns1;
        if (!string.IsNullOrEmpty(dns2)) dnsServers += $",{dns2}";
        await SetDnsIPv4(nic, dnsServers);
    }

    /// <summary>
    /// Unset IPv4 DNS by seting DNS to Static
    /// </summary>
    /// <param name="nicName">Network Interface Name</param>
    /// <param name="dns1">Primary</param>
    /// <param name="dns2">Secondary</param>
    public static async Task UnsetDnsIPv4(string nicName, string dns1, string? dns2)
    {
        string dnsServers = dns1;
        if (!string.IsNullOrEmpty(dns2)) dnsServers += $",{dns2}";
        await SetDnsIPv4(nicName, dnsServers);
    }

    /// <summary>
    /// Unset IPv4 DNS to DHCP (Windows Only)
    /// </summary>
    /// <param name="nic">Network Interface</param>
    public static async Task UnsetDnsIPv6(NetworkInterface nic)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Requires Elevation - Can't Unset DNS when there is no Internet connectivity but netsh can :)
        if (nic == null) return;

        await UnsetDnsIPv6(nic.Name);
    }

    /// <summary>
    /// Unset IPv4 DNS to DHCP (Windows Only)
    /// </summary>
    /// <param name="nicName">Network Interface Name</param>
    public static async Task UnsetDnsIPv6(string nicName)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Requires Elevation - Can't Unset DNS when there is no Internet connectivity but netsh can :)
        // NetSh Command: netsh interface ip set dns "nicName" source=dhcp
        if (string.IsNullOrEmpty(nicName)) return;

        try
        {
            string processName = "netsh";
            string processArgs1 = $"interface ipv6 delete dnsservers \"{nicName}\" all";
            string processArgs2 = $"interface ipv6 set dnsservers \"{nicName}\" source=dhcp";
            await ProcessManager.ExecuteAsync(processName, null, processArgs1, true, true);
            await ProcessManager.ExecuteAsync(processName, null, processArgs2, true, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("UnsetDnsIPv6: " + ex.Message);
        }
    }

    /// <summary>
    /// Is DNS Set to 127.0.0.1 - Using Nslookup (Windows Only)
    /// </summary>
    public static bool IsDnsSetToLocal(out string host, out string ip)
    {
        bool result = false;
        host = ip = string.Empty;
        if (!OperatingSystem.IsWindows()) return result;
        if (!IsInternetAlive()) return result; // nslookup takes time when there is no internet access

        string content = ProcessManager.Execute(out _, "nslookup", null, "0.0.0.0", true, true);
        if (string.IsNullOrEmpty(content)) return result;
        content = content.ToLower();
        string[] split = content.Split(Environment.NewLine);
        for (int n = 0; n < split.Length; n++)
        {
            string line = split[n];
            if (line.Contains("server:"))
            {
                line = line.Replace("server:", string.Empty).Trim();
                host = line;
                if (host.Equals("localhost")) result = true;
            }
            else if (line.Contains("address:"))
            {
                line = line.Replace("address:", string.Empty).Trim();
                ip = line;
                if (ip.Equals("127.0.0.1")) result = true;
                if (ip.Equals(IPAddress.Loopback.ToString())) result = true;
            }
        }
        return result;
    }

    /// <summary>
    /// Check if DNS is set to Static or DHCP using netsh (Windows Only)
    /// </summary>
    /// <param name="nic">Network Interface</param>
    /// <param name="dnsServer1">Primary DNS Server</param>
    /// <param name="dnsServer2">Secondary DNS Server</param>
    /// <returns>True = Static, False = DHCP</returns>
    public static bool IsDnsSet(NetworkInterface nic, out string dnsServer1, out string dnsServer2)
    {
        dnsServer1 = dnsServer2 = string.Empty;
        if (!OperatingSystem.IsWindows()) return false;
        if (nic == null) return false;

        string processName = "netsh";
        string processArgs = $"interface ipv4 show dnsservers {nic.Name}";
        string stdout = ProcessManager.Execute(out _, processName, null, processArgs, true, true);

        List<string> lines = stdout.SplitToLines();
        for (int n = 0; n < lines.Count; n++)
        {
            string line = lines[n];
            // Get Primary
            if (line.Contains(": ") && line.Contains('.') && line.Count(c => c == '.') == 3)
            {
                string[] split = line.Split(": ");
                if (split.Length > 1)
                {
                    dnsServer1 = split[1].Trim();
                    Debug.WriteLine($"DNS 1: {dnsServer1}");
                }
            }

            // Get Secondary
            if (!line.Contains(": ") && line.Contains('.') && line.Count(c => c == '.') == 3)
            {
                dnsServer2 = line.Trim();
                Debug.WriteLine($"DNS 2: {dnsServer2}");
            }
        }
        //Debug.WriteLine(stdout);
        return !stdout.Contains("DHCP");
    }

    /// <summary>
    /// Check Internet Access Based On NIC Send And Receive
    /// </summary>
    public static bool IsInternetAlive()
    {
        try
        {
            // Only recognizes changes related to Internet adapters
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            for (int n = 0; n < nics.Length; n++)
            {
                NetworkInterface nic = nics[n];
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                {
                    if (nic.OperationalStatus == OperationalStatus.Up)
                    {
                        IPInterfaceStatistics statistics = nic.GetIPStatistics();
                        if (statistics.BytesReceived > 0 && statistics.BytesSent > 0) return true;
                    }
                }
            }
            return false;
        }
        catch (Exception)
        {
            // NetworkInformationException The system cannot find the file specified
            return false;
        }
    }

    /// <summary>
    /// Check Internet Access Based On Pinging A DNS IP
    /// </summary>
    public static async Task<bool> IsInternetAliveAsync(IPAddress? ip, int timeoutMS = 3000)
    {
        try
        {
            ip ??= CultureInfo.InstalledUICulture switch
            {
                { Name: string n } when n.ToLower().StartsWith("fa") => IPAddress.Parse("8.8.8.8"), // Iran
                { Name: string n } when n.ToLower().StartsWith("ru") => IPAddress.Parse("77.88.8.7"), // Russia
                { Name: string n } when n.ToLower().StartsWith("zh") => IPAddress.Parse("223.6.6.6"), // China
                _ => IPAddress.Parse("1.1.1.1") // Others
            };

            Ping ping = new();
            PingReply reply = await ping.SendPingAsync(ip, timeoutMS);
            ping.Dispose();
            return reply.Status == IPStatus.Success;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Check Internet Access Based On Pinging A DNS IP
    /// </summary>
    public static async Task<bool> IsInternetAliveAsync(string? ipStr = null, int timeoutMS = 1000)
    {
        try
        {
            ipStr ??= CultureInfo.InstalledUICulture switch
            {
                { Name: string n } when n.ToLower().StartsWith("fa") => "8.8.8.8", // Iran
                { Name: string n } when n.ToLower().StartsWith("zh") => "77.88.8.7", // Russia
                { Name: string n } when n.ToLower().StartsWith("zh") => "223.6.6.6", // China
                _ => "1.1.1.1" // Others
            };

            Ping ping = new();
            PingReply? reply;
            bool isIp = IsIp(ipStr, out IPAddress? ip);

            if (isIp && ip != null)
                reply = await ping.SendPingAsync(ip, timeoutMS);
            else
                reply = await ping.SendPingAsync(ipStr, timeoutMS);

            if (reply == null) return false;

            ping.Dispose();
            return reply.Status == IPStatus.Success;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<string> GetHeaders(string urlOrDomain, string? ip, int timeoutMs, bool useSystemProxy, string? proxyScheme = null, string? proxyUser = null, string? proxyPass = null)
    {
        HttpResponseMessage? response = null;

        urlOrDomain = urlOrDomain.ToLower().Trim();
        GetUrlDetails(urlOrDomain, 443, out string scheme, out string host, out _, out _, out int port, out string path, out _);
        if (string.IsNullOrEmpty(scheme))
        {
            scheme = "https://";
            urlOrDomain = $"{scheme}{host}:{port}{path}";
        }
        string url = urlOrDomain;
        Debug.WriteLine("GetHeaders: " + url);
        if (!string.IsNullOrEmpty(ip))
        {
            ip = ip.Trim();
            url = $"{scheme}{ip}:{port}{path}";
            Debug.WriteLine("GetHeaders: " + url);
        }

        try
        {
            Uri uri = new(url, UriKind.Absolute);

            using HttpClientHandler handler = new();
            handler.AllowAutoRedirect = true;
            if (useSystemProxy)
            {
                // WebRequest.GetSystemWebProxy() Can't always detect System Proxy
                proxyScheme = GetSystemProxy(); // Reading from Registry
                if (!string.IsNullOrEmpty(proxyScheme))
                {
                    Debug.WriteLine("GetHeaders: " + proxyScheme);
                    NetworkCredential credential = CredentialCache.DefaultNetworkCredentials;
                    handler.Proxy = new WebProxy(proxyScheme, true, null, credential);
                    handler.Credentials = credential;
                    handler.UseProxy = true;
                }
                else
                {
                    Debug.WriteLine("GetHeaders: System Proxy Is Null.");
                    handler.UseProxy = false;
                }
            }
            else if (!string.IsNullOrEmpty(proxyScheme))
            {
                Debug.WriteLine("GetHeaders: " + proxyScheme);
                NetworkCredential credential = new(proxyUser, proxyPass);
                handler.Proxy = new WebProxy(proxyScheme, true, null, credential);
                handler.Credentials = credential;
                handler.UseProxy = true;
            }
            else
                handler.UseProxy = false;

            // Ignore Cert Check To Make It Faster
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true;

            // Get Only Header
            using HttpRequestMessage message = new(HttpMethod.Head, uri);
            message.Headers.TryAddWithoutValidation("User-Agent", "Other");
            message.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml");
            message.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
            message.Headers.TryAddWithoutValidation("Accept-Charset", "ISO-8859-1");

            if (!string.IsNullOrEmpty(ip))
            {
                message.Headers.TryAddWithoutValidation("host", host);
            }

            using HttpClient httpClient = new(handler);
            httpClient.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
            response = await httpClient.SendAsync(message, CancellationToken.None);
        }
        catch (Exception) { }

        string result = string.Empty;
        if (response != null)
        {
            result += response.StatusCode.ToString();
            Debug.WriteLine("GetHeaders: " + result);
            result += Environment.NewLine + response.Headers.ToString();
            try { response.Dispose(); } catch (Exception) { }
        }
        result = result.Trim();

        if (string.IsNullOrEmpty(result) && !urlOrDomain.Contains("://www."))
        {
            urlOrDomain = $"{scheme}www.{host}:{port}{path}";
            result = await GetHeaders(urlOrDomain, ip, timeoutMs, useSystemProxy, proxyScheme, proxyUser, proxyPass);
        }

        return result;
    }

    /// <summary>
    /// IsWebsiteOnlineAsync
    /// </summary>
    /// <param name="url">URL or Domain to check</param>
    /// <param name="timeoutMs">Timeout (Ms)</param>
    /// <param name="useSystemProxy">Use System Proxy (will override proxyScheme, proxyUser and proxyPass)</param>
    /// <param name="proxyScheme">Only the 'http', 'socks4', 'socks4a' and 'socks5' schemes are allowed for proxies.</param>
    /// <returns></returns>
    public static async Task<bool> IsWebsiteOnlineAsync(string urlOrDomain, string? ip, int timeoutMs, bool useSystemProxy, string? proxyScheme = null, string? proxyUser = null, string? proxyPass = null)
    {
        string headers = await GetHeaders(urlOrDomain, ip, timeoutMs, useSystemProxy, proxyScheme, proxyUser, proxyPass);
        return !string.IsNullOrEmpty(headers);
    }

    /// <summary>
    /// Check if Proxy is Set (Windows Only)
    /// </summary>
    /// <param name="httpProxy"></param>
    /// <param name="httpsProxy"></param>
    /// <param name="ftpProxy"></param>
    /// <param name="socksProxy"></param>
    /// <returns></returns>
    public static bool IsProxySet(out string httpProxy, out string httpsProxy, out string ftpProxy, out string socksProxy)
    {
        bool isProxyEnable = false;
        httpProxy = httpsProxy = ftpProxy = socksProxy = string.Empty;
        if (!OperatingSystem.IsWindows()) return false;
        RegistryKey? registry = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", false);
        if (registry != null)
        {
            // ProxyServer
            object? proxyServerObj = registry.GetValue("ProxyServer");
            if (proxyServerObj != null)
            {
                string? proxyServers = proxyServerObj.ToString();
                if (proxyServers != null)
                {
                    if (proxyServers.Contains(';'))
                    {
                        string[] split = proxyServers.Split(';');
                        for (int n = 0; n < split.Length; n++)
                        {
                            string server = split[n];
                            if (server.StartsWith("http=")) httpProxy = server[5..];
                            else if (server.StartsWith("https=")) httpsProxy = server[6..];
                            else if (server.StartsWith("ftp=")) ftpProxy = server[4..];
                            else if (server.StartsWith("socks=")) socksProxy = server[6..];
                        }
                    }
                    else if (proxyServers.Contains('='))
                    {
                        string[] split = proxyServers.Split('=');
                        if (split[0] == "http") httpProxy = split[1];
                        else if (split[0] == "https") httpsProxy = split[1];
                        else if (split[0] == "ftp") ftpProxy = split[1];
                        else if (split[0] == "socks") socksProxy = split[1];
                    }
                    else if (proxyServers.Contains("://"))
                    {
                        string[] split = proxyServers.Split("://");
                        if (split[0] == "http") httpProxy = split[1];
                        else if (split[0] == "https") httpsProxy = split[1];
                        else if (split[0] == "ftp") ftpProxy = split[1];
                        else if (split[0] == "socks") socksProxy = split[1];
                    }
                    else if (!string.IsNullOrEmpty(proxyServers)) httpProxy = proxyServers;
                }
            }

            // ProxyEnable
            object? proxyEnableObj = registry.GetValue("ProxyEnable");
            if (proxyEnableObj != null)
            {
                string? proxyEnable = proxyEnableObj.ToString();
                if (proxyEnable != null)
                {
                    bool isInt = int.TryParse(proxyEnable, out int value);
                    if (isInt)
                        isProxyEnable = value == 1;
                }
            }

            try { registry.Dispose(); } catch (Exception) { }
        }
        return isProxyEnable;
    }

    public static string GetSystemProxy()
    {
        string result = string.Empty;
        bool isProxySet = IsProxySet(out string httpProxy, out string httpsProxy, out _, out string socksProxy);
        if (isProxySet)
        {
            if (!string.IsNullOrEmpty(httpProxy)) result = $"http://{httpProxy}";
            else if (!string.IsNullOrEmpty(httpsProxy)) result = $"https://{httpsProxy}";
            else if (!string.IsNullOrEmpty(socksProxy)) result = $"socks5://{socksProxy}";
        }
        return result;
    }

    /// <summary>
    /// Set Proxy to System (Windows Only)
    /// </summary>
    public static void SetProxy(string? httpIpPort, string? httpsIpPort, string? ftpIpPort, string? socksIpPort, bool useHttpForAll)
    {
        if (!OperatingSystem.IsWindows()) return;
        RegistryKey? registry = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", true);
        if (registry != null)
        {
            string proxyServer = string.Empty;
            if (useHttpForAll)
            {
                if (!string.IsNullOrEmpty(httpIpPort)) proxyServer += $"http://{httpIpPort}";
            }
            else
            {
                if (!string.IsNullOrEmpty(httpIpPort)) proxyServer += $"http={httpIpPort};";
                if (!string.IsNullOrEmpty(httpsIpPort)) proxyServer += $"https={httpsIpPort};";
                if (!string.IsNullOrEmpty(ftpIpPort)) proxyServer += $"ftp={ftpIpPort};";
                if (!string.IsNullOrEmpty(socksIpPort)) proxyServer += $"socks={socksIpPort};";
                if (proxyServer.EndsWith(';')) proxyServer = proxyServer.TrimEnd(';');
            }

            try
            {
                if (!string.IsNullOrEmpty(proxyServer))
                {
                    registry.SetValue("AutoDetect", 0, RegistryValueKind.DWord);
                    registry.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    registry.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Set Http Proxy: {ex.Message}");
            }

            RegistryTool.ApplyRegistryChanges();
            try { registry.Dispose(); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Unset Internet Options Proxy (Windows Only)
    /// </summary>
    /// <param name="clearIpPort">Clear IP and Port</param>
    /// <param name="applyRegistryChanges">Don't apply registry changes on app exit</param>
    public static void UnsetProxy(bool clearIpPort, bool applyRegistryChanges)
    {
        if (!OperatingSystem.IsWindows()) return;
        RegistryKey? registry = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", true);
        if (registry != null)
        {
            try
            {
                registry.SetValue("AutoDetect", 1, RegistryValueKind.DWord);
                registry.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                if (clearIpPort)
                    registry.SetValue("ProxyServer", "", RegistryValueKind.String);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unset Proxy: {ex.Message}");
            }

            if (applyRegistryChanges) RegistryTool.ApplyRegistryChanges();
            try { registry.Dispose(); } catch (Exception) { }
        }
    }

    public static async Task<bool> IsHostBlocked(string host, int port, int timeoutMS)
    {
        string url;
        if (port == 80) url = $"http://{host}:{port}";
        else url = $"https://{host}:{port}";
        return !await IsWebsiteOnlineAsync(url, null, timeoutMS, false);
    }

    public static async Task<bool> CanPing(string host, int timeoutMS)
    {
        host = host.Trim();
        if (string.IsNullOrEmpty(host)) return false;
        if (host.Equals("0.0.0.0")) return false;
        if (host.Equals("::0")) return false;
        Task<bool> task = Task.Run(() =>
        {
            try
            {
                Ping ping = new();
                PingReply? reply;
                bool isIp = IsIp(host, out IPAddress? ip);
                if (isIp && ip != null)
                    reply = ping.Send(ip, timeoutMS);
                else
                    reply = ping.Send(host, timeoutMS);

                if (reply == null) return false;

                ping.Dispose();
                return reply.Status == IPStatus.Success;
            }
            catch (Exception)
            {
                return false;
            }
        });

        try { return await task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMS + 100)); } catch (Exception) { return false; }
    }

    public static async Task<bool> CanTcpConnect(string host, int port, int timeoutMS)
    {
        var task = Task.Run(() =>
        {
            try
            {
                using TcpClient client = new(host, port);
                client.SendTimeout = timeoutMS;
                client.ReceiveTimeout = timeoutMS;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        });
        
        try { return await task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMS + 100)); } catch (Exception) { return false; }
    }

    public static async Task<bool> CanUdpConnect(string host, int port, int timeoutMS)
    {
        var task = Task.Run(() =>
        {
            try
            {
                using UdpClient client = new(host, port);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        });

        try { return await task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMS + 100)); } catch (Exception) { return false; }
    }

    public static async Task<bool> CanConnect(string host, int port, int timeoutMS)
    {
        var task = Task.Run(async () =>
        {
            try
            {
                string url = $"https://{host}:{port}";
                Uri uri = new(url, UriKind.Absolute);

                using HttpClient httpClient = new();
                httpClient.Timeout = TimeSpan.FromMilliseconds(timeoutMS);

                await httpClient.GetAsync(uri);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        });

        try { return await task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMS + 100)); } catch (Exception) { return false; }
    }

}