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
        if (!LanConfig.IsValidPort(lan))
            lan = LanConfig.DefaultPort;
        int fusion = lan + 1;
        if (fusion > 65535)
            fusion = LanConfig.DefaultPort + 1;
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

    /// <summary>
    /// JoinAddress may embed a non-default port. Lobby UDP already uses the parsed value;
    /// callers must also apply this to Plugin Listen/Join so Fusion Connect + UniqueId stay aligned.
    /// </summary>
    public static int SessionPortAfterJoinParse(int parsedPort)
    {
        return LanConfig.IsValidPort(parsedPort) ? parsedPort : LanConfig.DefaultPort;
    }

    public static bool TryClientTarget(string joinAddress, int joinPort, out string ip, out ushort port)
    {
        ip = "";
        port = 0;
        if (!LanHostParse.TryParseHostPort(joinAddress, joinPort, out string host, out int p, out _))
            return false;
        if (host.Length == 0 || !LanConfig.IsValidPort(p))
            return false;
        ip = ResolveClientConnectIp(host);
        port = HostBindPort(p);
        return true;
    }

    /// <summary>
    /// Photon-less Direct identity: Host=1; clients use UDP LocalSlot+1 (slot1→2 … slot5→6).
    /// </summary>
    public static int ResolveLanActorId(bool isHost, int localSlot)
    {
        if (isHost)
            return 1;
        int actor = localSlot + 1;
        if (actor < 2)
            return 2;
        if (actor > LanRoom.SlotCap)
            return LanRoom.SlotCap;
        return actor;
    }

    /// <summary>Highest peer actor Host should UniqueId-premap (inclusive), from room MaxPlayers.</summary>
    public static int HostPremapPeerActorHi(int maxPlayers)
    {
        int hi = LanRoom.ClampMax(maxPlayers);
        if (hi < 2)
            hi = 2;
        return hi;
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
        if (SessionPortAfterJoinParse(27015) != 27015
            || SessionPortAfterJoinParse(0) != LanConfig.DefaultPort
            || SessionPortAfterJoinParse(65535) != LanConfig.DefaultPort)
            throw new InvalidOperationException("SatmLanIp session-port-after-join self-check failed");
        if (ResolveClientConnectIp("127.0.0.1") != "127.0.0.1")
            throw new InvalidOperationException("SatmLanIp loopback resolve self-check failed");
        string ip;
        ushort port;
        if (!TryClientTarget("10.0.0.2:27015", 37241, out ip, out port)
            || ip != "10.0.0.2" || port != 27016)
            throw new InvalidOperationException("SatmLanIp client-target port self-check failed");
        if (TryClientTarget("", 37241, out _, out _))
            throw new InvalidOperationException("SatmLanIp client-target empty self-check failed");
        if (ResolveLanActorId(true, 0) != 1
            || ResolveLanActorId(false, 1) != 2
            || ResolveLanActorId(false, 2) != 3
            || ResolveLanActorId(false, 5) != 6
            || ResolveLanActorId(false, 0) != 2)
            throw new InvalidOperationException("SatmLanIp ResolveLanActorId self-check failed");
        if (HostPremapPeerActorHi(6) != 6 || HostPremapPeerActorHi(3) != 3 || HostPremapPeerActorHi(1) != 2)
            throw new InvalidOperationException("SatmLanIp HostPremapPeerActorHi clamp");
        if (HostPremapPeerActorHi(2) != 2)
            throw new InvalidOperationException("SatmLanIp premap 2p");
        if (HostPremapPeerActorHi(3) != 3)
            throw new InvalidOperationException("SatmLanIp premap 3p");
        if (HostPremapPeerActorHi(6) != 6)
            throw new InvalidOperationException("SatmLanIp premap 6p");
    }
}
