using System;

namespace SatmLanIp;

/// <summary>
/// Pure helpers for the LAN Fusion path: skip Photon cloud, Host bind, Client Connect(LAN IP).
/// </summary>
internal static class LanFusionStart
{
    public static bool ShouldSkipPhoton(bool allowFusionStart, bool pluginActive)
    {
        return allowFusionStart && pluginActive;
    }

    public static bool ShouldBindHostAddress(bool isHost)
    {
        return isHost;
    }

    /// <summary>
    /// Fusion binds LAN listenPort+1 so side-channel UDP can keep listenPort.
    /// Sharing one port made Host Fusion eat Client Ready/Heartbeat and never Accept Connect.
    /// </summary>
    public static ushort HostBindPort(int listenPort)
    {
        int lan = listenPort;
        if (lan < 1 || lan > 65535)
            lan = 37241;
        int fusion = lan + 1;
        if (fusion > 65535)
            fusion = 37242;
        return (ushort)fusion;
    }

    /// <summary>
    /// Same-PC dual launch: NanoSockets hairpin to own LAN IP often drops; System.Net LAN
    /// side-channel still works. Route Fusion Connect via loopback when JoinAddress is ours.
    /// </summary>
    public static string ResolveClientConnectIp(string joinIp)
    {
        if (string.IsNullOrEmpty(joinIp))
            return "";
        if (LanLocalIp.IsOwnLanIp(joinIp))
            return "127.0.0.1";
        return joinIp;
    }

    public static bool TryClientTarget(string joinAddress, int joinPort, out string ip, out ushort port)
    {
        ip = "";
        port = 0;
        if (!LanHostParse.TryParseHostPort(joinAddress, joinPort, out string host, out int p, out _))
            return false;
        if (host.Length == 0 || p < 1 || p > 65535)
            return false;
        ip = ResolveClientConnectIp(host);
        port = HostBindPort(p);
        return true;
    }

    internal static void SelfCheck()
    {
        if (ShouldSkipPhoton(false, true) || ShouldSkipPhoton(true, false) || !ShouldSkipPhoton(true, true))
            throw new InvalidOperationException("SatmLanIp skip-photon self-check failed");
        if (!ShouldBindHostAddress(true) || ShouldBindHostAddress(false))
            throw new InvalidOperationException("SatmLanIp host-bind self-check failed");
        if (HostBindPort(37241) != 37242 || HostBindPort(0) != 37242)
            throw new InvalidOperationException("SatmLanIp bind-port self-check failed");
        if (HostBindPort(65535) != 37242)
            throw new InvalidOperationException("SatmLanIp bind-port overflow self-check failed");
        if (ResolveClientConnectIp("127.0.0.1") != "127.0.0.1")
            throw new InvalidOperationException("SatmLanIp loopback resolve self-check failed");
        string ip;
        ushort port;
        if (!TryClientTarget("10.0.0.2:27015", 37241, out ip, out port)
            || ip != "10.0.0.2" || port != 27016)
            throw new InvalidOperationException("SatmLanIp client-target port self-check failed");
        if (TryClientTarget("", 37241, out _, out _))
            throw new InvalidOperationException("SatmLanIp client-target empty self-check failed");
    }
}
