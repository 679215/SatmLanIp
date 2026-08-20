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

    public readonly bool[] PeerPoseHas = new bool[LanRoom.SlotCap];
    public readonly float[] PeerPoseX = new float[LanRoom.SlotCap];
    public readonly float[] PeerPoseY = new float[LanRoom.SlotCap];
    public readonly float[] PeerPoseZ = new float[LanRoom.SlotCap];
    public readonly float[] PeerPoseYaw = new float[LanRoom.SlotCap];

    public bool HostReady => LanRoom.SlotReady(ReadyMask, 0);

    /// <summary>True if any seated non-host slot is ready (not only slot 1).</summary>
    public bool ClientReady
    {
        get
        {
            int seated = LanRoom.SeatedMask(OccupiedMask, PlayerCount, MaxPlayers);
            for (int i = 1; i < LanRoom.SlotCap; i++)
            {
                if ((seated & (1 << i)) == 0)
                    continue;
                if (LanRoom.SlotReady(ReadyMask, i))
                    return true;
            }
            return false;
        }
    }

    public bool AllReady =>
        LanRoom.AllOccupiedReady(ReadyMask, LanRoom.SeatedMask(OccupiedMask, PlayerCount, MaxPlayers));

    public bool InRoom =>
        State == LanState.Listen || State == LanState.Connecting || State == LanState.Connected;

    /// <summary>Host left / Goodbye: drop lobby state and unblock Escape / ReturnClientToCreate.</summary>
    public void MarkHostDrop()
    {
        MatchActive = false;
        State = LanState.Drop;
    }

    public void ClearMatchActive()
    {
        MatchActive = false;
    }

    public void SetPeerPose(int slot, float x, float y, float z, float yaw)
    {
        if (slot < 0 || slot >= LanRoom.SlotCap)
            slot = 0;
        PeerPoseHas[slot] = true;
        PeerPoseX[slot] = x;
        PeerPoseY[slot] = y;
        PeerPoseZ[slot] = z;
        PeerPoseYaw[slot] = yaw;
        HasRemotePose = true;
        RemoteX = x;
        RemoteY = y;
        RemoteZ = z;
        RemoteYaw = yaw;
    }

    public bool TryGetPeerPose(int slot, out float x, out float y, out float z, out float yaw)
    {
        x = y = z = yaw = 0f;
        if (slot < 0 || slot >= LanRoom.SlotCap || !PeerPoseHas[slot])
            return false;
        x = PeerPoseX[slot];
        y = PeerPoseY[slot];
        z = PeerPoseZ[slot];
        yaw = PeerPoseYaw[slot];
        return true;
    }

    public void ClearPeerPoses()
    {
        for (int i = 0; i < LanRoom.SlotCap; i++)
            PeerPoseHas[i] = false;
        HasRemotePose = false;
        RemoteX = 0f;
        RemoteY = 0f;
        RemoteZ = 0f;
        RemoteYaw = 0f;
    }

    /// <summary>Prefer lobby-picked save; else first existing; else current (floored to 0).</summary>
    public static int ResolveSaveSlot(int hostPickedSlot, int currentSlot, int firstExistingOrNeg1)
    {
        if (hostPickedSlot >= 0)
            return hostPickedSlot;
        if (firstExistingOrNeg1 >= 0)
            return firstExistingOrNeg1;
        return currentSlot < 0 ? 0 : currentSlot;
    }
}
