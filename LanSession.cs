namespace SatmLanIp;

internal enum LanState
{
    Idle,
    Listen,
    Connecting,
    Connected,
    Fail,
    Drop,
}

internal sealed class LanSession
{
    public LanState State = LanState.Idle;
    public bool IsHost;
    public string PeerEndPoint = "";
    public int LastRttMs = -1;
    public string FailReason = "";
    public int FusionStartsBlocked;
    public int MaxPlayers = 2;
    public int PlayerCount = 1;
    public int ReadyMask;
    public int OccupiedMask = 1;
    public int LocalSlot;
    public bool LocalReady;
    public bool MatchActive;
    public bool HasRemotePose;
    public float RemoteX;
    public float RemoteY;
    public float RemoteZ;
    public float RemoteYaw;

    public bool HostReady => LanRoom.SlotReady(ReadyMask, 0);

    public bool ClientReady => LanRoom.SlotReady(ReadyMask, 1);

    public bool AllReady =>
        LanRoom.AllOccupiedReady(ReadyMask, LanRoom.SeatedMask(OccupiedMask, PlayerCount, MaxPlayers));

    public bool InRoom =>
        State == LanState.Listen || State == LanState.Connecting || State == LanState.Connected;
}
