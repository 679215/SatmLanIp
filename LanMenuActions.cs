using System;

namespace SatmLanIp;

internal static class LanMenuActions
{
    public static void Host()
    {
        Host(LanCloneUi.ReadMaxPlayers());
    }

    public static void Host(int maxPlayers)
    {
        LanSession cur = Plugin.Transport != null ? Plugin.Transport.Session : null;
        if (cur != null && !cur.IsHost && cur.InRoom)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] Host refused: already joining");
            LanCloneUi.SetCreateNotice("先离开再创建房间");
            return;
        }
        if (cur != null && cur.IsHost && cur.InRoom)
            return;

        ConflictGuard.Refresh();
        if (!ConflictGuard.CanActivate())
        {
            string why = ConflictGuard.ConflictsPresent
                ? ("conflict: " + ConflictGuard.ConflictSummary)
                : "Enabled=false";
            Plugin.LogSrc.LogError("[SatmLanIp] Host refused: " + why);
            return;
        }

        Plugin.Transport.StartHost(Plugin.ListenPort, maxPlayers);
        LanSession s = Plugin.Transport.Session;
        if (s.State == LanState.Fail)
        {
            LanCloneUi.SetCreateNotice(s.FailReason);
            return;
        }
        if (s.State == LanState.Listen || s.State == LanState.Connected)
            Plugin.LogSrc.LogInfo("[SatmLanIp] Host advertise: " + LanLocalIp.FormatAdvertise(Plugin.ListenPort));
        LanCloneUi.ClearCreateNotice();
        LanCloneUi.ShowLobby();
        LanMenuFlow.EnterLobby();
    }

    public static void Join(string ipOrEmpty)
    {
        string ip = (ipOrEmpty ?? "").Trim();
        if (ip.Length == 0)
            ip = (Plugin.JoinAddress ?? "").Trim();
        if (ip.Length == 0)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] Join refused: JoinAddress empty");
            LanCloneUi.SetCreateNotice("加入前填写加入地址（房主 IP）");
            return;
        }

        LanSession cur = Plugin.Transport != null ? Plugin.Transport.Session : null;
        if (cur != null && cur.IsHost && cur.InRoom)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] Join refused: already hosting");
            LanCloneUi.SetCreateNotice("先离开房间再加入");
            return;
        }

        ConflictGuard.Refresh();
        if (!ConflictGuard.CanActivate())
        {
            string why = ConflictGuard.ConflictsPresent
                ? ("conflict: " + ConflictGuard.ConflictSummary)
                : "Enabled=false";
            Plugin.LogSrc.LogError("[SatmLanIp] Join refused: " + why);
            return;
        }

        Plugin.SetJoinAddress(ip);
        Plugin.Transport.StartClient(ip, Plugin.JoinPort, Plugin.ConnectTimeoutSec);
        LanSession s = Plugin.Transport.Session;
        if (s.State == LanState.Fail)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] Join failed: " + s.FailReason);
            LanCloneUi.SetCreateNotice(s.FailReason);
            return;
        }
        LanCloneUi.ClearCreateNotice();
        LanCloneUi.ShowLobby();
        LanMenuFlow.EnterLobby();
        Plugin.LogSrc.LogInfo("[SatmLanIp] Join " + ip);
    }

    public static void Disconnect()
    {
        Plugin.Transport?.Disconnect();
        Plugin.IsActive = false;
        LanHudBehaviour.NotifyJoinPromptClosed();
        LanHudBehaviour.NotifyHostAdvertise("");
        LanHudBehaviour.NotifyMatchReset();
        LanMatch.Reset();
        Plugin.LogSrc.LogInfo("[SatmLanIp] LAN disconnected");
    }

    public static void ToggleReady()
    {
        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        if (s == null || !s.InRoom)
            return;
        Plugin.Transport.ToggleLocalReady();
    }

    public static void StartMatch()
    {
        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        if (s == null)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] StartMatch refused: no session");
            return;
        }
        if (!s.IsHost)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] StartMatch refused: not host");
            return;
        }
        if (!s.AllReady)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] StartMatch refused: not all ready"
                + " pc=" + s.PlayerCount + "/" + s.MaxPlayers
                + " ready=" + s.ReadyMask.ToString("X2")
                + " occ=" + s.OccupiedMask.ToString("X2"));
            return;
        }
        Plugin.Transport.SendStartMatch();
        LanMatch.TryBegin("host-ui");
        Plugin.LogSrc.LogInfo("[SatmLanIp] MATCH host start");
    }

    public static string StatusSummary()
    {
        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        if (s == null)
            return "LAN IDLE";
        string role = s.State == LanState.Idle ? "-" : (s.IsHost ? "HOST" : "CLIENT");
        if (s.State == LanState.Connected || (s.IsHost && s.State == LanState.Listen))
            return LanRoom.FormatRoomLine(role, s.PlayerCount, s.MaxPlayers, s.ReadyMask);

        string peer = s.PeerEndPoint != null && s.PeerEndPoint.Length > 0 ? s.PeerEndPoint : "-";
        if (s.State == LanState.Fail && s.FailReason != null && s.FailReason.Length > 0)
            peer = s.FailReason;
        return LanHudBehaviour.FormatLine(role, StateName(s.State), peer, s.LastRttMs);
    }

    private static string StateName(LanState st)
    {
        switch (st)
        {
            case LanState.Listen: return "LISTEN";
            case LanState.Connecting: return "CONNECTING";
            case LanState.Connected: return "CONNECTED";
            case LanState.Fail: return "FAIL";
            case LanState.Drop: return "DROP";
            default: return "IDLE";
        }
    }

    internal static void SelfCheck()
    {
        string line = LanRoom.FormatRoomLine("HOST", 2, 3, 1);
        if (line != "LAN  HOST  ROOM  2/3  ready=01")
            throw new InvalidOperationException("SatmLanIp LanMenuActions FormatRoomLine");
    }
}
