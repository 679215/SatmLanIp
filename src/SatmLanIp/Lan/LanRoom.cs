using System;
using System.Text;

namespace SatmLanIp;

/// <summary>
/// SLIP v4 room snap payload (after 16-byte header):
/// maxPlayers, playerCount, readyMask (bit i = slot i ready; slot 0 = host).
/// </summary>
internal static class LanRoom
{
    public const int SlotCap = 6;
    public const int SnapPayloadSize = 4;

    /// <summary>
    /// Host evicts a client with no Hello/Heartbeat/Ready/Pose for this long (unscaled seconds).
    /// Headroom covers alt-tab / brief frame stalls on any machine — not a loopback special case.
    /// </summary>
    public const float ClientIdleTimeoutSec = 45f;

    /// <summary>True when a seated peer has gone silent longer than timeoutSec.</summary>
    public static bool ShouldEvictIdleClient(float lastRxUnscaled, float nowUnscaled, float timeoutSec)
    {
        if (lastRxUnscaled <= 0f || timeoutSec <= 0f)
            return false;
        return (nowUnscaled - lastRxUnscaled) >= timeoutSec;
    }

    /// <summary>Lobby-only: do not drop UDP slots mid-match (Fusion owns peer lifetime).</summary>
    public static bool AllowIdleEviction(bool matchActive)
    {
        return !matchActive;
    }

    /// <summary>
    /// Joiner lobby keepalive may run off the Unity main thread so alt-tab / focus loss
    /// does not starve host idle detection. Host does not need this path.
    /// </summary>
    public static bool ShouldClientLobbyKeepalive(bool isHost, LanState state, int localSlot)
    {
        if (isHost || state != LanState.Connected)
            return false;
        return localSlot >= 1 && localSlot < SlotCap;
    }

    public static int ClampMax(int n)
    {
        if (n >= 6)
            return 6;
        if (n >= 3)
            return 3;
        return 2;
    }

    public static bool SlotReady(int mask, int slot)
    {
        if (slot < 0 || slot >= SlotCap)
            return false;
        return (mask & (1 << slot)) != 0;
    }

    public static int SetSlotReady(int mask, int slot, bool ready)
    {
        if (slot < 0 || slot >= SlotCap)
            return mask;
        int bit = 1 << slot;
        if (ready)
            return mask | bit;
        return mask & ~bit;
    }

    public static bool AllOccupiedReady(int readyMask, int occupiedMask)
    {
        int occ = occupiedMask & 0x3F;
        if (occ <= 1)
            return false;
        for (int i = 0; i < SlotCap; i++)
        {
            int bit = 1 << i;
            if ((occ & bit) == 0)
                continue;
            if ((readyMask & bit) == 0)
                return false;
        }
        return true;
    }

    /// <summary>Ready slots among currently occupied seats (not vs MaxPlayers).</summary>
    public static int CountOccupiedReady(int readyMask, int occupiedMask)
    {
        int n = 0;
        int occ = occupiedMask & 0x3F;
        for (int i = 0; i < SlotCap; i++)
        {
            int bit = 1 << i;
            if ((occ & bit) == 0)
                continue;
            if ((readyMask & bit) != 0)
                n++;
        }
        return n;
    }

    public static int PopCount6(int mask)
    {
        int n = 0;
        int m = mask & 0x3F;
        while (m != 0)
        {
            n += m & 1;
            m >>= 1;
        }
        return n;
    }

    /// <summary>
    /// Seat bits for lobby ready math: drop SlotCap leftover bits above MaxPlayers,
    /// and don't treat empty max-slots as occupied when PlayerCount is smaller.
    /// </summary>
    public static int SeatedMask(int occupiedMask, int playerCount, int maxPlayers)
    {
        int max = ClampMax(maxPlayers < 2 ? 2 : maxPlayers);
        int cap = (1 << max) - 1;
        int occ = occupiedMask & cap;
        int pc = playerCount;
        if (pc < 1)
            pc = 1;
        if (pc > max)
            pc = max;
        int n = PopCount6(occ);
        if (n == pc)
            return occ;
        return cap & ((1 << pc) - 1);
    }

    public static int CountSeatedReady(
        int readyMask, int occupiedMask, int playerCount, int maxPlayers,
        bool localReady, int localSlot, out int seated)
    {
        int occ = SeatedMask(occupiedMask, playerCount, maxPlayers);
        seated = PopCount6(occ);
        if (seated < 1)
            seated = 1;
        int n = CountOccupiedReady(readyMask, occ);
        if (localReady && localSlot >= 0 && localSlot < SlotCap)
        {
            int bit = 1 << localSlot;
            if ((occ & bit) != 0 && (readyMask & bit) == 0)
                n++;
        }
        if (n > seated)
            n = seated;
        return n;
    }

    public static void WriteSnap(byte[] buf, int offset, int maxPlayers, int playerCount, int readyMask, int occupiedMask)
    {
        buf[offset] = (byte)ClampMax(maxPlayers);
        int pc = playerCount;
        if (pc < 1)
            pc = 1;
        if (pc > SlotCap)
            pc = SlotCap;
        buf[offset + 1] = (byte)pc;
        buf[offset + 2] = (byte)(readyMask & 0x3F);
        buf[offset + 3] = (byte)(occupiedMask & 0x3F);
    }

    public static bool TryReadSnap(byte[] buf, int len, int offset, out int maxPlayers, out int playerCount, out int readyMask, out int occupiedMask)
    {
        maxPlayers = 2;
        playerCount = 1;
        readyMask = 0;
        occupiedMask = 1;
        if (buf == null || offset < 0 || len < offset + SnapPayloadSize)
            return false;
        maxPlayers = ClampMax(buf[offset]);
        playerCount = buf[offset + 1];
        if (playerCount < 1)
            playerCount = 1;
        if (playerCount > SlotCap)
            playerCount = SlotCap;
        readyMask = buf[offset + 2] & 0x3F;
        occupiedMask = buf[offset + 3] & 0x3F;
        if (occupiedMask == 0)
            occupiedMask = 1;
        return true;
    }

    public static ushort PackReady(bool ready)
    {
        return PackReady(ready, 0);
    }

    /// <summary>
    /// Wire Ready seq: high byte = LocalSlot (0..5), low byte = ready flag.
    /// Slot lets the host rebind 127.0.0.1 multi-instance peers when UDP ports churn.
    /// </summary>
    public static ushort PackReady(bool ready, int slot)
    {
        int s = slot;
        if (s < 0)
            s = 0;
        if (s >= SlotCap)
            s = SlotCap - 1;
        return (ushort)((s << 8) | (ready ? 1 : 0));
    }

    public static bool UnpackReady(ushort seq)
    {
        return (seq & 0xFF) != 0;
    }

    public static int UnpackReadySlot(ushort seq)
    {
        return (seq >> 8) & 0xFF;
    }

    public static string FormatRoomLine(string role, int playerCount, int maxPlayers, int readyMask)
    {
        int max = ClampMax(maxPlayers);
        int pc = playerCount < 1 ? 1 : playerCount;
        return "LAN  " + role + "  ROOM  " + pc.ToString() + "/" + max.ToString()
            + "  ready=" + readyMask.ToString("X2");
    }

    public static void SelfCheck()
    {
        if (ClampMax(1) != 2 || ClampMax(2) != 2 || ClampMax(3) != 3 || ClampMax(4) != 3 || ClampMax(6) != 6)
            throw new InvalidOperationException("SatmLanIp ClampMax");
        if (ShouldEvictIdleClient(0f, 20f, 15f)
            || ShouldEvictIdleClient(10f, 20f, 15f)
            || !ShouldEvictIdleClient(5f, 20f, 15f))
            throw new InvalidOperationException("SatmLanIp ShouldEvictIdleClient");
        if (ClientIdleTimeoutSec < 45f)
            throw new InvalidOperationException("SatmLanIp ClientIdleTimeoutSec lobby headroom");
        if (ShouldClientLobbyKeepalive(true, LanState.Connected, 0)
            || !ShouldClientLobbyKeepalive(false, LanState.Connected, 2)
            || ShouldClientLobbyKeepalive(false, LanState.Connecting, 1))
            throw new InvalidOperationException("SatmLanIp ShouldClientLobbyKeepalive");
        if (!AllowIdleEviction(false) || AllowIdleEviction(true))
            throw new InvalidOperationException("SatmLanIp AllowIdleEviction");
        byte[] buf = new byte[SnapPayloadSize];
        int ready = SetSlotReady(1, 1, true);
        WriteSnap(buf, 0, 3, 2, ready, 3);
        if (!TryReadSnap(buf, buf.Length, 0, out int max, out int pc, out int mask, out int occ)
            || max != 3 || pc != 2 || occ != 3)
            throw new InvalidOperationException("SatmLanIp WriteSnap roundtrip");
        if (!SlotReady(mask, 0) || !SlotReady(mask, 1) || SlotReady(mask, 2))
            throw new InvalidOperationException("SatmLanIp ready bits");
        if (!AllOccupiedReady(mask, 3) || AllOccupiedReady(mask, 7))
            throw new InvalidOperationException("SatmLanIp AllOccupiedReady");
        if (AllOccupiedReady(ready, 5))
            throw new InvalidOperationException("SatmLanIp AllOccupiedReady hole");
        if (CountOccupiedReady(mask, 3) != 2 || CountOccupiedReady(ready, 5) != 1)
            throw new InvalidOperationException("SatmLanIp CountOccupiedReady");
        if (SeatedMask(0x3F, 3, 3) != 7)
            throw new InvalidOperationException("SatmLanIp SeatedMask wide occ");
        if (CountSeatedReady(1, 0x3F, 3, 3, true, 0, out int seatedWide) != 1 || seatedWide != 3)
            throw new InvalidOperationException("SatmLanIp CountSeatedReady host-only");
        if (CountSeatedReady(7, 0x3F, 3, 3, true, 0, out int seatedAll) != 3 || seatedAll != 3)
            throw new InvalidOperationException("SatmLanIp CountSeatedReady all-ready");
        if (AllOccupiedReady(7, SeatedMask(0x3F, 3, 3)) == false)
            throw new InvalidOperationException("SatmLanIp AllOccupiedReady seated");
        if (PackReady(true) != 1 || PackReady(false) != 0 || !UnpackReady(1) || UnpackReady(0))
            throw new InvalidOperationException("SatmLanIp PackReady");
        if (PackReady(true, 3) != 0x0301 || UnpackReadySlot(0x0301) != 3 || !UnpackReady(0x0301))
            throw new InvalidOperationException("SatmLanIp PackReady slot");
        string line = FormatRoomLine("HOST", 2, 3, 1);
        if (line != "LAN  HOST  ROOM  2/3  ready=01")
            throw new InvalidOperationException("SatmLanIp FormatRoomLine: " + line);
        string slots = FormatSlotLines(3, 1, 1);
        if (slots.IndexOf("空", StringComparison.Ordinal) < 0
            || slots.IndexOf("房主", StringComparison.Ordinal) < 0
            || slots.IndexOf("已准备", StringComparison.Ordinal) < 0)
            throw new InvalidOperationException("SatmLanIp FormatSlotLines: " + slots);
        if (FormatStartHint(false, false, 1, 3) != ""
            || FormatStartHint(true, true, 2, 3) != ""
            || FormatStartHint(true, false, 1, 3).IndexOf("2", StringComparison.Ordinal) < 0)
            throw new InvalidOperationException("SatmLanIp FormatStartHint");
    }

    public static string FormatSlotLines(int maxPlayers, int occupiedMask, int readyMask)
    {
        int max = ClampMax(maxPlayers);
        var sb = new StringBuilder();
        for (int i = 0; i < max; i++)
        {
            if (sb.Length > 0)
                sb.Append('\n');
            int bit = 1 << i;
            string who = i == 0 ? "房主" : ("玩家" + (i + 1).ToString());
            sb.Append("槽").Append(i + 1).Append("  ").Append(who).Append("  ");
            if ((occupiedMask & bit) == 0)
                sb.Append("空");
            else
                sb.Append(SlotReady(readyMask, i) ? "已准备" : "未准备");
        }
        return sb.ToString();
    }

    public static string FormatStartHint(bool isHost, bool allReady, int playerCount, int maxPlayers)
    {
        if (!isHost || allReady)
            return "";
        if (playerCount < 2)
            return "开始：至少 2 人且全员准备";
        return "开始：等待全员准备";
    }
}
