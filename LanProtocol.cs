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
}

/// <summary>
/// 16-byte datagram: magic BE "SLIP" | ver | type | seq LE | unixMs LE
/// RoomSnap v4 appends LanRoom.SnapPayloadSize bytes.
/// </summary>
internal static class LanProtocol
{
    public const uint Magic = 0x534C4950; // S L I P
    public const byte Version = 4;
    public const int PacketSize = 16;

    public static byte[] Encode(LanPacketType type, ushort seq, long unixMs)
    {
        byte[] buf = new byte[PacketSize];
        buf[0] = (byte)'S';
        buf[1] = (byte)'L';
        buf[2] = (byte)'I';
        buf[3] = (byte)'P';
        buf[4] = Version;
        buf[5] = (byte)type;
        buf[6] = (byte)(seq & 0xFF);
        buf[7] = (byte)((seq >> 8) & 0xFF);
        unchecked
        {
            ulong u = (ulong)unixMs;
            buf[8] = (byte)(u & 0xFF);
            buf[9] = (byte)((u >> 8) & 0xFF);
            buf[10] = (byte)((u >> 16) & 0xFF);
            buf[11] = (byte)((u >> 24) & 0xFF);
            buf[12] = (byte)((u >> 32) & 0xFF);
            buf[13] = (byte)((u >> 40) & 0xFF);
            buf[14] = (byte)((u >> 48) & 0xFF);
            buf[15] = (byte)((u >> 56) & 0xFF);
        }
        return buf;
    }

    public static byte[] EncodePose(ushort seq, float x, float y, float z, float yaw)
    {
        byte[] head = Encode(LanPacketType.Pose, seq, 0);
        byte[] buf = new byte[PacketSize + LanPose.PayloadSize];
        for (int i = 0; i < PacketSize; i++)
            buf[i] = head[i];
        LanPose.Write(buf, PacketSize, x, y, z, yaw);
        return buf;
    }

    public static byte[] EncodeRoomSnap(int maxPlayers, int playerCount, int readyMask, int occupiedMask)
    {
        byte[] head = Encode(LanPacketType.RoomSnap, 0, 0);
        byte[] buf = new byte[PacketSize + LanRoom.SnapPayloadSize];
        for (int i = 0; i < PacketSize; i++)
            buf[i] = head[i];
        LanRoom.WriteSnap(buf, PacketSize, maxPlayers, playerCount, readyMask, occupiedMask);
        return buf;
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
        if (t < (byte)LanPacketType.Hello || t > (byte)LanPacketType.RoomFull)
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
    }
}
