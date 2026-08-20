using System;
using Xunit;

namespace SatmLanIp.Tests;

public sealed class LanProtocolTests
{
    [Fact]
    public void LanProtocol_SelfCheck_passes() => LanProtocol.SelfCheck();

    [Fact]
    public void LanProtocol_rejects_wrong_magic()
    {
        byte[] buf = LanProtocol.Encode(LanPacketType.Hello, 1, 0);
        buf[0] = (byte)'X';
        Assert.False(LanProtocol.TryParse(buf, buf.Length, out _, out _, out _));
    }

    [Fact]
    public void LanProtocol_roundtrips_sequence_and_signed_timestamp()
    {
        const ushort sequence = 0xBEEF;
        const long timestamp = -1234567890123L;
        byte[] buf = LanProtocol.Encode(LanPacketType.Heartbeat, sequence, timestamp);

        Assert.True(LanProtocol.TryParse(buf, buf.Length, out LanPacketType type, out ushort parsedSequence, out long parsedTimestamp));
        Assert.Equal(LanPacketType.Heartbeat, type);
        Assert.Equal(sequence, parsedSequence);
        Assert.Equal(timestamp, parsedTimestamp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void LanProtocol_rejects_null_or_short_packets(int length)
    {
        byte[] buf = length == 0 ? null : new byte[length];
        Assert.False(LanProtocol.TryParse(buf, length, out _, out _, out _));
    }

    [Fact]
    public void LanProtocol_rejects_unknown_type_and_version()
    {
        byte[] unknownType = LanProtocol.Encode(LanPacketType.Hello, 1, 1);
        unknownType[5] = 0;
        Assert.False(LanProtocol.TryParse(unknownType, unknownType.Length, out _, out _, out _));

        byte[] unknownVersion = LanProtocol.Encode(LanPacketType.Hello, 1, 1);
        unknownVersion[4] = 0;
        Assert.False(LanProtocol.TryParse(unknownVersion, unknownVersion.Length, out _, out _, out _));
    }

    [Fact]
    public void LanProtocol_encode_room_snap_and_pose_have_expected_lengths()
    {
        Assert.Equal(LanProtocol.PacketSize + LanRoom.SnapPayloadSize, LanProtocol.EncodeRoomSnap(3, 2, 1, 3).Length);
        Assert.Equal(LanProtocol.PacketSize + LanPose.PayloadSize, LanProtocol.EncodePose(9, 1.5f, 2f, 3f, 45f).Length);
    }

    [Fact]
    public void LanProtocol_encode_sets_magic_and_version()
    {
        byte[] buf = LanProtocol.Encode(LanPacketType.Hello, 0, 0);
        Assert.Equal((byte)'S', buf[0]);
        Assert.Equal((byte)'L', buf[1]);
        Assert.Equal((byte)'I', buf[2]);
        Assert.Equal((byte)'P', buf[3]);
        Assert.Equal(LanProtocol.Version, buf[4]);
        Assert.Equal(LanProtocol.PacketSize, buf.Length);
    }

    [Theory]
    [InlineData(1)]  // Hello
    [InlineData(2)]  // HelloAck
    [InlineData(3)]  // Heartbeat
    [InlineData(4)]  // Goodbye
    [InlineData(5)]  // Ready
    [InlineData(6)]  // RoomSnap
    [InlineData(7)]  // StartMatch
    [InlineData(8)]  // Pose
    [InlineData(9)]  // RoomFull
    public void LanProtocol_roundtrips_every_packet_type(int packetTypeId)
    {
        var packetType = (LanPacketType)packetTypeId;
        byte[] buf = LanProtocol.Encode(packetType, 42, 1_700_000_000_000L);
        Assert.True(LanProtocol.TryParse(buf, buf.Length, out LanPacketType parsed, out ushort seq, out long ms));
        Assert.Equal(packetType, parsed);
        Assert.Equal((ushort)42, seq);
        Assert.Equal(1_700_000_000_000L, ms);
    }

    [Fact]
    public void LanProtocol_encodeRoomSnap_payload_roundtrips()
    {
        byte[] snap = LanProtocol.EncodeRoomSnap(3, 2, 0b011, 0b101);
        Assert.True(LanProtocol.TryParse(snap, snap.Length, out LanPacketType type, out _, out _));
        Assert.Equal(LanPacketType.RoomSnap, type);
        Assert.True(LanRoom.TryReadSnap(snap, snap.Length, LanProtocol.PacketSize, out int max, out int pc, out int ready, out int occ));
        Assert.Equal(3, max);
        Assert.Equal(2, pc);
        Assert.Equal(0b011, ready);
        Assert.Equal(0b101, occ);
    }

    [Fact]
    public void LanProtocol_encodePose_payload_roundtrips()
    {
        byte[] pose = LanProtocol.EncodePose(7, 1f, 2f, 3f, 90f);
        Assert.True(LanProtocol.TryParse(pose, pose.Length, out LanPacketType type, out ushort seq, out _));
        Assert.Equal(LanPacketType.Pose, type);
        Assert.Equal((ushort)7, seq);
        Assert.True(LanPose.TryRead(pose, pose.Length, LanProtocol.PacketSize, out float x, out float y, out float z, out float yaw));
        Assert.Equal(1f, x);
        Assert.Equal(2f, y);
        Assert.Equal(3f, z);
        Assert.Equal(90f, yaw);
    }

    [Fact]
    public void LanProtocol_TryParse_ignores_trailing_bytes()
    {
        byte[] hello = LanProtocol.Encode(LanPacketType.Hello, 1, 1);
        byte[] padded = new byte[hello.Length + 4];
        hello.CopyTo(padded, 0);
        Assert.True(LanProtocol.TryParse(padded, padded.Length, out LanPacketType type, out ushort seq, out _));
        Assert.Equal(LanPacketType.Hello, type);
        Assert.Equal((ushort)1, seq);
    }
}
