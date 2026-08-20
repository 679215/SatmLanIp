using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace SatmLanIp;

/// <summary>
/// Best-effort LAN advertise candidates. Multi-homed Windows can still list more than one
/// usable address; callers must treat the result as hints, not a guaranteed join target.
/// </summary>
internal static class LanLocalIp
{
    internal const string KindLan = "局域网";
    internal const string KindOverlay = "组网";
    internal const string KindOther = "其他";

    internal struct AdvertiseAddr
    {
        public string Kind;
        public string Ip;
    }

    /// <summary>
    /// IPv4 candidates for a friend to type. Order: physical LAN, then overlay (TUN/TAP),
    /// then everything else still up. Only omit adapters we can identify as host-only
    /// Hyper-V/WSL; unknown NICs stay listed so a real join target is not dropped.
    /// </summary>
    public static List<string> ListIPv4()
    {
        List<AdvertiseAddr> rows = ListAdvertise();
        var ips = new List<string>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
            ips.Add(rows[i].Ip);
        return ips;
    }

    internal static List<AdvertiseAddr> ListAdvertise()
    {
        var physical = new List<AdvertiseAddr>();
        var overlay = new List<AdvertiseAddr>();
        var other = new List<AdvertiseAddr>();
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                string name = ni.Name ?? "";
                string desc = ni.Description ?? "";
                if (IsJunkAdvertiseAdapter(name, desc))
                    continue;

                bool overlayNic = IsOverlayAdvertiseAdapter(name, desc);
                bool physicalNic = IsPhysicalAdvertiseAdapter(ni.NetworkInterfaceType) && !overlayNic;
                string kind = overlayNic ? KindOverlay : (physicalNic ? KindLan : KindOther);

                IPInterfaceProperties props = ni.GetIPProperties();
                foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    string ip = ua.Address.ToString();
                    if (ShouldSkipAdvertiseIp(ip))
                        continue;

                    var row = new AdvertiseAddr { Kind = kind, Ip = ip };
                    if (overlayNic)
                        overlay.Add(row);
                    else if (physicalNic)
                        physical.Add(row);
                    else
                        other.Add(row);
                }
            }
        }
        catch (Exception)
        {
            // NIC enumeration can fail on restricted hosts; return whatever we collected.
        }

        return DedupeRows(physical, overlay, other);
    }

    public static string FormatAdvertise(int port)
    {
        return FormatAdvertiseRows(ListAdvertise(), port);
    }

    internal static string FormatAdvertiseRows(List<AdvertiseAddr> rows, int port)
    {
        if (rows == null || rows.Count == 0)
            return "(no LAN IPv4 found) port=" + port.ToString();

        var sb = new StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0)
                sb.Append('\n');
            sb.Append(PadKind(rows[i].Kind));
            sb.Append(rows[i].Ip);
            sb.Append(':');
            sb.Append(port.ToString());
        }
        return sb.ToString();
    }

    /// <summary>
    /// Skip loopback, link-local, and Clash/Surge Fake-IP (198.18.0.0/15).
    /// </summary>
    internal static bool ShouldSkipAdvertiseIp(string ip)
    {
        if (string.IsNullOrEmpty(ip))
            return true;
        if (ip.StartsWith("127.") || ip.StartsWith("169.254."))
            return true;
        // Clash/Surge Fake-IP pool — looks like LAN but peers cannot route to it.
        if (ip.StartsWith("198.18.") || ip.StartsWith("198.19."))
            return true;
        return false;
    }

    /// <summary>
    /// Omit only Windows Hyper-V host switches (vEthernet / WSL). These are local to this
    /// machine; a friend cannot route to them. Do not match VM/VPN/TUN by guesswork —
    /// a false drop hides the address the peer must type.
    /// </summary>
    internal static bool IsJunkAdvertiseAdapter(string name, string desc)
    {
        string s = (name ?? "") + " " + (desc ?? "");
        if (s.Length == 0)
            return false;
        return ContainsAny(s, "vEthernet", "Hyper-V", "WSL");
    }

    /// <summary>
    /// Remote-LAN / VPN overlay NICs (SteamVPN, ZeroTier, Tailscale, etc.). Keep these —
    /// they are often the correct JoinAddress when peers are not on the same Wi-Fi.
    /// </summary>
    internal static bool IsOverlayAdvertiseAdapter(string name, string desc)
    {
        string s = (name ?? "") + " " + (desc ?? "");
        if (s.Length == 0)
            return false;
        return ContainsAny(s,
            "Wintun",
            "WireGuard",
            "ZeroTier",
            "Tailscale",
            "Hamachi",
            "SteamVPN",
            "TAP-Windows",
            "TAP-Win32",
            "OpenVPN",
            "Radmin VPN",
            "SoftEther");
    }

    internal static bool IsPhysicalAdvertiseAdapter(NetworkInterfaceType t)
    {
        return t == NetworkInterfaceType.Ethernet
            || t == NetworkInterfaceType.Wireless80211
            || t == NetworkInterfaceType.GigabitEthernet
            || t == NetworkInterfaceType.FastEthernetT
            || t == NetworkInterfaceType.FastEthernetFx;
    }

    /// <summary>True if ip is one of this machine's advertised NIC addresses (same-PC dual instance).</summary>
    public static bool IsOwnLanIp(string ip)
    {
        if (string.IsNullOrEmpty(ip))
            return false;
        if (ip == "127.0.0.1" || ip.StartsWith("127."))
            return true;
        List<string> mine = ListIPv4();
        for (int i = 0; i < mine.Count; i++)
        {
            if (string.Equals(mine[i], ip, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal static void SelfCheck()
    {
        if (!ShouldSkipAdvertiseIp("198.18.0.1") ||
            !ShouldSkipAdvertiseIp("198.19.1.2") ||
            ShouldSkipAdvertiseIp("192.168.1.10"))
            throw new InvalidOperationException("SatmLanIp Fake-IP filter self-check failed");
        if (!IsJunkAdvertiseAdapter("vEthernet (WSL (Hyper-V firewall))", "Hyper-V Virtual Ethernet Adapter"))
            throw new InvalidOperationException("SatmLanIp junk adapter WSL self-check failed");
        if (!IsJunkAdvertiseAdapter("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter"))
            throw new InvalidOperationException("SatmLanIp junk adapter Default Switch self-check failed");
        if (IsJunkAdvertiseAdapter("WLAN", "Intel(R) Wi-Fi 6") ||
            IsJunkAdvertiseAdapter("本地连接 2", "TAP-Windows Adapter V9") ||
            IsJunkAdvertiseAdapter("Wintun", "Wintun Userspace Tunnel") ||
            IsJunkAdvertiseAdapter("ZeroTier One", "ZeroTier Virtual Port") ||
            IsJunkAdvertiseAdapter("以太网", "Intel(R) Ethernet Connection"))
            throw new InvalidOperationException("SatmLanIp usable adapter marked junk");
        if (!IsOverlayAdvertiseAdapter("本地连接 2", "TAP-Windows Adapter V9") ||
            !IsOverlayAdvertiseAdapter("Wintun", "Wintun Userspace Tunnel"))
            throw new InvalidOperationException("SatmLanIp overlay adapter self-check failed");

        var rows = new List<AdvertiseAddr>
        {
            new AdvertiseAddr { Kind = KindLan, Ip = "192.168.1.10" },
            new AdvertiseAddr { Kind = KindOverlay, Ip = "10.10.0.2" },
            new AdvertiseAddr { Kind = KindOther, Ip = "100.64.1.2" },
        };
        string formatted = FormatAdvertiseRows(rows, 37241);
        string expect = PadKind(KindLan) + "192.168.1.10:37241\n"
            + PadKind(KindOverlay) + "10.10.0.2:37241\n"
            + PadKind(KindOther) + "100.64.1.2:37241";
        if (formatted != expect)
            throw new InvalidOperationException("SatmLanIp FormatAdvertiseRows: " + formatted);
    }

    internal static string PadKind(string kind)
    {
        if (kind == null)
            kind = KindOther;
        if (kind.Length >= 3)
            return kind + "  ";
        return kind + "　　";
    }

    private static bool ContainsAny(string hay, params string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
        {
            if (hay.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static List<AdvertiseAddr> DedupeRows(params List<AdvertiseAddr>[] buckets)
    {
        var seen = new HashSet<string>();
        var result = new List<AdvertiseAddr>();
        for (int b = 0; b < buckets.Length; b++)
        {
            List<AdvertiseAddr> list = buckets[b];
            for (int i = 0; i < list.Count; i++)
            {
                if (seen.Add(list[i].Ip))
                    result.Add(list[i]);
            }
        }
        return result;
    }
}
