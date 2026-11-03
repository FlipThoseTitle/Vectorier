using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Vectorier.Services.Collab;

/// <summary>An address the host can put in the Session ID, with the name of the network it belongs to.</summary>
public sealed record CollabAddress(string Address, string Label, bool IsVpn)
{
    public override string ToString() => $"{Address}  ({Label})";
}

/// <summary>
/// Everyone in a session is in the same virtual network (Radmin VPN, or Tailscale / ZeroTier): the host listens on
/// its VPN address and the others connect to it directly. No server, nothing to set up on routers.
/// </summary>
public static class CollabNetwork
{
    public const int DefaultPort = 27850;

    /// <summary>This computer's addresses, VPN ones first (Radmin VPN, then Tailscale, then ZeroTier).</summary>
    public static List<CollabAddress> LocalAddresses()
    {
        var list = new List<(CollabAddress Address, int Rank)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    var ip = unicast.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                        continue;
                    var (label, rank) = Describe(ip, nic);
                    list.Add((new CollabAddress(ip.ToString(), label, rank < 3), rank));
                }
            }
        }
        catch
        {
            // Some systems refuse to list interfaces: a typed address still works.
        }
        return list.OrderBy(a => a.Rank).Select(a => a.Address).DistinctBy(a => a.Address).ToList();
    }

    private static (string Label, int Rank) Describe(IPAddress ip, NetworkInterface nic)
    {
        var b = ip.GetAddressBytes();
        string names = nic.Name + " " + nic.Description;
        if (b[0] == 26 || names.Contains("Radmin", StringComparison.OrdinalIgnoreCase))
            return ("Radmin VPN", 0);
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127 || names.Contains("Tailscale", StringComparison.OrdinalIgnoreCase))
            return ("Tailscale", 1);
        if (names.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase))
            return ("ZeroTier", 2);
        return (nic.Name, 3);
    }
}
