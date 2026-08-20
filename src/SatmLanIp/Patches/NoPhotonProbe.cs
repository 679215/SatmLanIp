using System;

namespace SatmLanIp;

/// <summary>
/// Short window after LAN connect to spot stock Fusion StartAsHost/Client leaks.
/// Quiet unless <see cref="Plugin.VerboseNetworkLog"/> is on.
/// </summary>
internal static class NoPhotonProbe
{
    private static bool _windowOpen;
    private static float _windowEnd;
    private static int _leakStarts;
    private static bool _logged;

    public static void OnConnected()
    {
        _leakStarts = 0;
        _logged = false;
        _windowOpen = true;
        _windowEnd = UnityEngine.Time.unscaledTime + 5f;
        if (Plugin.Transport != null)
            Plugin.Transport.Session.FusionStartsBlocked = 0;
        if (Plugin.VerboseNetworkLog)
            Plugin.LogSrc.LogInfo("[SatmLanIp] NoPhotonProbe window 5s started");
    }

    public static void NoteFusionStartAttempt()
    {
        if (!_windowOpen)
            return;
        _leakStarts++;
    }

    public static void Poll()
    {
        if (!_windowOpen || _logged)
            return;
        if (UnityEngine.Time.unscaledTime < _windowEnd)
            return;

        _windowOpen = false;
        _logged = true;
        if (!Plugin.VerboseNetworkLog && _leakStarts == 0)
            return;
        if (_leakStarts == 0)
            Plugin.LogSrc.LogInfo("[SatmLanIp] photon_session=none");
        else
            Plugin.LogSrc.LogWarning($"[SatmLanIp] photon_session=leak FusionStartsSeen={_leakStarts}");
    }
}
