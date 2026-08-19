using System;

namespace SatmLanIp;

internal static class LanConfig
{
    public const bool DefaultEnabled = true;
    public const int DefaultPort = 37241;
    public const int MaxPort = 65534;
    public const int DefaultTimeoutSec = 30;
    public const string DefaultJoinAddress = "";

    public static bool IsValidPort(int port)
    {
        return port >= 1 && port <= MaxPort;
    }

    public static int NormalizePort(int port)
    {
        return IsValidPort(port) ? port : DefaultPort;
    }

    internal static void SelfCheck()
    {
        if (!DefaultEnabled || DefaultPort != 37241 || MaxPort != 65534
            || DefaultTimeoutSec != 30 || DefaultJoinAddress != "")
            throw new InvalidOperationException("SatmLanIp LanConfig defaults");
        if (!IsValidPort(1) || !IsValidPort(MaxPort)
            || IsValidPort(0) || IsValidPort(MaxPort + 1)
            || NormalizePort(37241) != 37241
            || NormalizePort(0) != DefaultPort
            || NormalizePort(MaxPort + 1) != DefaultPort)
            throw new InvalidOperationException("SatmLanIp LanConfig ports");
    }
}
