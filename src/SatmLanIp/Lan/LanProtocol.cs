using System;

namespace SatmLanIp;

internal enum LanPacketType : byte
{
    Hello = 1,
    HelloAck = 2,
    Heartbeat = 3,
    Goodbye = 4,
    Ready = 5,
    RoomSnap = 6,
    StartMatch = 7,
    Pose = 8,
    RoomFull = 9,
    /// <summary>Host already started match; join too late.</summary>
    MatchBusy = 10,
    BuildMismatch = 11,
}

/// <summary>
/// 16-byte datagram: magic BE "SLIP" | ver | type | seq LE | unixMs LE
/// RoomSnap v4 appends LanRoom.SnapPayloadSize bytes.
/// Hello / HelloAck / BuildMismatch append 4-byte Steam buildid LE.
/// </summary>
internal static class LanProtocol
{
    public const uint Magic = 0x534C4950; // S L I P
    public const byte Version = 4;
    public const int PacketSize = 16;
    public const int BuildPayloadSize = 4;
    public const int ExtendedPacketSize = PacketSize + BuildPayloadSize;
    public const LanPacketType MaxPacketType = LanPacketType.BuildMismatch;

    public static byte[] Encode(LanPacketType type, ushort seq, long unixMs)
    {
        byte[] buf = new byte[PacketSize];
        WriteHeader(buf, 0, type, seq, unixMs);
        return buf;
    }

    public static void WriteHeader(byte[] buf, int offset, LanPacketType type, ushort seq, long unixMs)
    {
        buf[offset] = (byte)'S';
        buf[offset + 1] = (byte)'L';
        buf[offset + 2] = (byte)'I';
        buf[offset + 3] = (byte)'P';
        buf[offset + 4] = Version;
        buf[offset + 5] = (byte)type;
        buf[offset + 6] = (byte)(seq & 0xFF);
        buf[offset + 7] = (byte)((seq >> 8) & 0xFF);
        unchecked
        {
            ulong u = (ulong)unixMs;
            buf[offset + 8] = (byte)(u & 0xFF);
            buf[offset + 9] = (byte)((u >> 8) & 0xFF);
            buf[offset + 10] = (byte)((u >> 16) & 0xFF);
            buf[offset + 11] = (byte)((u >> 24) & 0xFF);
            buf[offset + 12] = (byte)((u >> 32) & 0xFF);
            buf[offset + 13] = (byte)((u >> 40) & 0xFF);
            buf[offset + 14] = (byte)((u >> 48) & 0xFF);
            buf[offset + 15] = (byte)((u >> 56) & 0xFF);
        }
    }

    public static void WriteBuildPayload(byte[] buf, int offset, uint buildId)
    {
        buf[offset] = (byte)(buildId & 0xFF);
        buf[offset + 1] = (byte)((buildId >> 8) & 0xFF);
        buf[offset + 2] = (byte)((buildId >> 16) & 0xFF);
        buf[offset + 3] = (byte)((buildId >> 24) & 0xFF);
    }

    public static bool TryReadBuildPayload(byte[] buf, int len, out uint buildId)
    {
        buildId = 0;
        if (buf == null || len < ExtendedPacketSize)
            return false;
        buildId = (uint)(buf[PacketSize]
            | (buf[PacketSize + 1] << 8)
            | (buf[PacketSize + 2] << 16)
            | (buf[PacketSize + 3] << 24));
        return true;
    }

    public static int WriteHelloPacket(byte[] buf, ushort seq, long unixMs, uint buildId)
    {
        WriteHeader(buf, 0, LanPacketType.Hello, seq, unixMs);
        WriteBuildPayload(buf, PacketSize, buildId);
        return ExtendedPacketSize;
    }

    public static int WriteHelloAckPacket(byte[] buf, ushort slot, long unixMs, uint buildId)
    {
        WriteHeader(buf, 0, LanPacketType.HelloAck, slot, unixMs);
        WriteBuildPayload(buf, PacketSize, buildId);
        return ExtendedPacketSize;
    }

    public static int WriteBuildMismatchPacket(byte[] buf, uint hostBuildId)
    {
        WriteHeader(buf, 0, LanPacketType.BuildMismatch, 0, 0);
        WriteBuildPayload(buf, PacketSize, hostBuildId);
        return ExtendedPacketSize;
    }

    public static int WritePosePacket(byte[] buf, ushort seq, float x, float y, float z, float yaw)
    {
        WriteHeader(buf, 0, LanPacketType.Pose, seq, 0);
        LanPose.Write(buf, PacketSize, x, y, z, yaw);
        return PacketSize + LanPose.PayloadSize;
    }

    public static int WriteRoomSnapPacket(byte[] buf, int maxPlayers, int playerCount, int readyMask, int occupiedMask)
    {
        WriteHeader(buf, 0, LanPacketType.RoomSnap, 0, 0);
        LanRoom.WriteSnap(buf, PacketSize, maxPlayers, playerCount, readyMask, occupiedMask);
        return PacketSize + LanRoom.SnapPayloadSize;
    }

    public static byte[] EncodePose(ushort seq, float x, float y, float z, float yaw)
    {
        byte[] buf = new byte[PacketSize + LanPose.PayloadSize];
        WritePosePacket(buf, seq, x, y, z, yaw);
        return buf;
    }

    public static byte[] EncodeRoomSnap(int maxPlayers, int playerCount, int readyMask, int occupiedMask)
    {
        byte[] buf = new byte[PacketSize + LanRoom.SnapPayloadSize];
        WriteRoomSnapPacket(buf, maxPlayers, playerCount, readyMask, occupiedMask);
        return buf;
    }

    /// <summary>Lower = handle sooner when inbox is congested (Pose last).</summary>
    public static int DrainPriority(LanPacketType type)
    {
        switch (type)
        {
            case LanPacketType.Hello:
            case LanPacketType.HelloAck:
            case LanPacketType.Goodbye:
            case LanPacketType.Ready:
            case LanPacketType.StartMatch:
            case LanPacketType.RoomFull:
            case LanPacketType.MatchBusy:
            case LanPacketType.BuildMismatch:
                return 0;
            case LanPacketType.Heartbeat:
            case LanPacketType.RoomSnap:
                return 1;
            default:
                return 2;
        }
    }

    public static bool TryParse(byte[] buf, int len, out LanPacketType type, out ushort seq, out long unixMs)
    {
        type = 0;
        seq = 0;
        unixMs = 0;
        if (buf == null || len < PacketSize)
            return false;
        if (buf[0] != (byte)'S' || buf[1] != (byte)'L' || buf[2] != (byte)'I' || buf[3] != (byte)'P')
            return false;
        if (buf[4] != Version)
            return false;
        byte t = buf[5];
        if (t < (byte)LanPacketType.Hello || t > (byte)MaxPacketType)
            return false;
        type = (LanPacketType)t;
        seq = (ushort)(buf[6] | (buf[7] << 8));
        ulong u = buf[8]
            | ((ulong)buf[9] << 8)
            | ((ulong)buf[10] << 16)
            | ((ulong)buf[11] << 24)
            | ((ulong)buf[12] << 32)
            | ((ulong)buf[13] << 40)
            | ((ulong)buf[14] << 48)
            | ((ulong)buf[15] << 56);
        unixMs = unchecked((long)u);
        return true;
    }

    public static void SelfCheck()
    {
        byte[] hello = Encode(LanPacketType.Hello, 1, 1_700_000_000_000L);
        if (hello.Length != 16)
            throw new InvalidOperationException("SatmLanIp protocol len");
        if (hello[0] != (byte)'S' || hello[1] != (byte)'L' || hello[2] != (byte)'I' || hello[3] != (byte)'P')
            throw new InvalidOperationException("SatmLanIp protocol magic");
        if (hello[4] != 4 || hello[5] != (byte)LanPacketType.Hello)
            throw new InvalidOperationException("SatmLanIp protocol ver/type");
        if (!TryParse(hello, hello.Length, out LanPacketType t, out ushort seq, out long ms) ||
            t != LanPacketType.Hello || seq != 1 || ms != 1_700_000_000_000L)
            throw new InvalidOperationException("SatmLanIp protocol roundtrip");
        byte[] junk = new byte[16];
        if (TryParse(junk, 16, out _, out _, out _))
            throw new InvalidOperationException("SatmLanIp protocol junk accepted");
        if (Version != 4)
            throw new InvalidOperationException("SatmLanIp protocol ver");
        byte[] snap = EncodeRoomSnap(3, 2, 1, 3);
        if (!TryParse(snap, snap.Length, out LanPacketType t2, out _, out _) ||
            t2 != LanPacketType.RoomSnap)
            throw new InvalidOperationException("SatmLanIp RoomSnap parse");
        if (!LanRoom.TryReadSnap(snap, snap.Length, PacketSize, out int max, out int pn, out int mask, out int occ) ||
            max != 3 || pn != 2 || mask != 1 || occ != 3)
            throw new InvalidOperationException("SatmLanIp RoomSnap payload");
        byte[] pose = EncodePose(9, 1.5f, 2f, 3f, 45f);
        if (pose.Length != 32 || !TryParse(pose, pose.Length, out LanPacketType tp, out ushort sp, out _) ||
            tp != LanPacketType.Pose || sp != 9)
            throw new InvalidOperationException("SatmLanIp Pose header");
        if (!LanPose.TryRead(pose, pose.Length, PacketSize, out float px, out _, out _, out float pyaw) ||
            Math.Abs(px - 1.5f) > 0.0001f || Math.Abs(pyaw - 45f) > 0.0001f)
            throw new InvalidOperationException("SatmLanIp Pose payload");
        byte[] old = Encode(LanPacketType.Hello, 1, 1);
        old[4] = 3;
        if (TryParse(old, old.Length, out _, out _, out _))
            throw new InvalidOperationException("SatmLanIp v3 datagram accepted");
        byte[] full = Encode(LanPacketType.RoomFull, 0, 0);
        if (!TryParse(full, full.Length, out LanPacketType tf, out _, out _) || tf != LanPacketType.RoomFull)
            throw new InvalidOperationException("SatmLanIp RoomFull");
        byte[] busy = Encode(LanPacketType.MatchBusy, 0, 0);
        if (!TryParse(busy, busy.Length, out LanPacketType tb, out _, out _) || tb != LanPacketType.MatchBusy)
            throw new InvalidOperationException("SatmLanIp MatchBusy");
        byte[] ext = new byte[ExtendedPacketSize];
        WriteHelloPacket(ext, 7, 42, 24837841u);
        if (!TryParse(ext, ExtendedPacketSize, out LanPacketType th, out ushort sh, out long tm) ||
            th != LanPacketType.Hello || sh != 7 || tm != 42)
            throw new InvalidOperationException("SatmLanIp extended Hello header");
        if (!TryReadBuildPayload(ext, ExtendedPacketSize, out uint hb) || hb != 24837841u)
            throw new InvalidOperationException("SatmLanIp extended Hello build");
        if (TryReadBuildPayload(ext, PacketSize, out uint shortBuild) && shortBuild != 0)
            throw new InvalidOperationException("SatmLanIp short Hello build must be 0");
        WriteHelloAckPacket(ext, 2, 0, 24450017u);
        if (!TryReadBuildPayload(ext, ExtendedPacketSize, out uint ackBuild) || ackBuild != 24450017u)
            throw new InvalidOperationException("SatmLanIp HelloAck build");
        WriteBuildMismatchPacket(ext, 24837841u);
        if (!TryParse(ext, ExtendedPacketSize, out LanPacketType tmis, out _, out _) ||
            tmis != LanPacketType.BuildMismatch)
            throw new InvalidOperationException("SatmLanIp BuildMismatch parse");
        if (DrainPriority(LanPacketType.Pose) <= DrainPriority(LanPacketType.Hello)
            || DrainPriority(LanPacketType.Ready) != 0
            || DrainPriority(LanPacketType.BuildMismatch) != 0)
            throw new InvalidOperationException("SatmLanIp DrainPriority");
    }
}
