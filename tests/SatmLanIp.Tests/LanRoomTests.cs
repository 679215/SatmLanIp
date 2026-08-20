using System;
using Xunit;

namespace SatmLanIp.Tests;

public sealed class LanRoomTests
{
    [Fact]
    public void LanRoom_SelfCheck_passes() => LanRoom.SelfCheck();

    [Fact]
    public void LanRoom_AllOccupiedReady_requires_every_occupied_seat()
    {
        int occ = 0b101; // slots 0 and 2
        Assert.False(LanRoom.AllOccupiedReady(0b001, occ));
        Assert.True(LanRoom.AllOccupiedReady(0b101, occ));
    }

    [Fact]
    public void LanRoom_AllOccupiedReady_false_when_single_occupant()
    {
        Assert.False(LanRoom.AllOccupiedReady(0b001, 0b001));
    }

    [Fact]
    public void LanRoom_invalid_slots_leave_ready_mask_unchanged()
    {
        const int mask = 0b001011;
        Assert.Equal(mask, LanRoom.SetSlotReady(mask, -1, true));
        Assert.Equal(mask, LanRoom.SetSlotReady(mask, LanRoom.SlotCap, false));
        Assert.False(LanRoom.SlotReady(mask, -1));
        Assert.False(LanRoom.SlotReady(mask, LanRoom.SlotCap));
    }

    [Fact]
    public void LanRoom_SetSlotReady_clears_bit_when_not_ready()
    {
        const int mask = 0b000111;
        Assert.Equal(0b000011, LanRoom.SetSlotReady(mask, 2, false));
    }

    [Theory]
    [InlineData(0b000000, 0)]
    [InlineData(0b000101, 2)]
    [InlineData(0b111111, 6)]
    public void LanRoom_PopCount6_counts_set_bits(int mask, int expected)
    {
        Assert.Equal(expected, LanRoom.PopCount6(mask));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 3)]
    [InlineData(6, 6)]
    [InlineData(99, 6)]
    public void LanRoom_ClampMax_uses_supported_room_sizes(int raw, int expected)
    {
        Assert.Equal(expected, LanRoom.ClampMax(raw));
    }

    [Fact]
    public void LanRoom_CountOccupiedReady_ignores_empty_and_high_slots()
    {
        Assert.Equal(2, LanRoom.CountOccupiedReady(0xFF, 0b010001));
    }

    [Theory]
    [InlineData(0x3F, 3, 3, 0x07)]
    [InlineData(0x21, 2, 3, 0x03)]
    [InlineData(0x07, 3, 3, 0x07)]
    [InlineData(0x00, 0, 6, 0x01)]
    public void LanRoom_SeatedMask_matches_player_count(int occupied, int playerCount, int maxPlayers, int expected)
    {
        Assert.Equal(expected, LanRoom.SeatedMask(occupied, playerCount, maxPlayers));
    }

    [Fact]
    public void LanRoom_CountSeatedReady_includes_local_ready_state_once()
    {
        int ready = LanRoom.CountSeatedReady(0b000001, 0b000011, 2, 3, true, 1, out int seated);
        Assert.Equal(2, ready);
        Assert.Equal(2, seated);
    }

    [Fact]
    public void LanRoom_CountSeatedReady_ignores_local_ready_outside_seated_mask()
    {
        int ready = LanRoom.CountSeatedReady(0, 0b000001, 1, 3, true, 1, out int seated);
        Assert.Equal(0, ready);
        Assert.Equal(1, seated);
    }

    [Fact]
    public void LanRoom_WriteSnap_clamps_payload_fields()
    {
        byte[] buf = new byte[LanRoom.SnapPayloadSize];
        LanRoom.WriteSnap(buf, 0, 99, 99, 0xFF, 0xFF);
        Assert.Equal(new byte[] { 6, 6, 0x3F, 0x3F }, buf);
    }

    [Fact]
    public void LanRoom_WriteSnap_floors_player_count_to_one()
    {
        byte[] buf = new byte[LanRoom.SnapPayloadSize];
        LanRoom.WriteSnap(buf, 0, 3, 0, 0, 0);
        Assert.Equal(3, buf[0]);
        Assert.Equal(1, buf[1]);
    }

    [Fact]
    public void LanRoom_TryReadSnap_clamps_player_count()
    {
        Assert.True(LanRoom.TryReadSnap(new byte[] { 3, 0, 0, 1 }, 4, 0, out _, out int low, out _, out _));
        Assert.Equal(1, low);
        Assert.True(LanRoom.TryReadSnap(new byte[] { 3, 99, 0, 1 }, 4, 0, out _, out int high, out _, out _));
        Assert.Equal(6, high);
    }

    [Fact]
    public void LanRoom_CountSeatedReady_does_not_double_count_local_ready()
    {
        int ready = LanRoom.CountSeatedReady(0b011, 0b011, 2, 3, true, 1, out int seated);
        Assert.Equal(2, ready);
        Assert.Equal(2, seated);
    }

    [Fact]
    public void LanRoom_TryReadSnap_roundtrips_payload()
    {
        byte[] buf = new byte[LanRoom.SnapPayloadSize];
        LanRoom.WriteSnap(buf, 0, 3, 2, 0b011, 0b101);
        Assert.True(LanRoom.TryReadSnap(buf, buf.Length, 0, out int max, out int pc, out int ready, out int occ));
        Assert.Equal(3, max);
        Assert.Equal(2, pc);
        Assert.Equal(0b011, ready);
        Assert.Equal(0b101, occ);
    }

    [Fact]
    public void LanRoom_TryReadSnap_defaults_occupied_when_zero()
    {
        byte[] buf = new byte[] { 2, 1, 0, 0 };
        Assert.True(LanRoom.TryReadSnap(buf, buf.Length, 0, out _, out _, out _, out int occ));
        Assert.Equal(1, occ);
    }

    [Fact]
    public void LanRoom_TryReadSnap_rejects_null_short_and_negative_offsets()
    {
        Assert.False(LanRoom.TryReadSnap(null, 4, 0, out _, out _, out _, out _));
        Assert.False(LanRoom.TryReadSnap(new byte[LanRoom.SnapPayloadSize], 3, 0, out _, out _, out _, out _));
        Assert.False(LanRoom.TryReadSnap(new byte[LanRoom.SnapPayloadSize], LanRoom.SnapPayloadSize, -1, out _, out _, out _, out _));
    }

    [Theory]
    [InlineData(true, 0, (ushort)1)]
    [InlineData(false, 0, (ushort)0)]
    [InlineData(true, 2, (ushort)0x0201)]
    [InlineData(false, 3, (ushort)0x0300)]
    public void LanRoom_PackReady_and_UnpackReady(bool ready, int slot, ushort packed)
    {
        Assert.Equal(packed, LanRoom.PackReady(ready, slot));
        Assert.Equal(ready, LanRoom.UnpackReady(packed));
        Assert.Equal(slot, LanRoom.UnpackReadySlot(packed));
    }

    [Fact]
    public void LanRoom_FormatRoomLine_matches_self_check_shape()
    {
        Assert.Equal("LAN  HOST  ROOM  2/3  ready=01", LanRoom.FormatRoomLine("HOST", 2, 3, 1));
    }

    [Fact]
    public void LanRoom_FormatSlotLines_lists_host_and_empty_slots()
    {
        string lines = LanRoom.FormatSlotLines(3, 0b001, 0b001);
        Assert.Contains("房主", lines);
        Assert.Contains("空", lines);
        Assert.Contains("已准备", lines);
    }

    [Theory]
    [InlineData(false, false, 1, 3, "")]
    [InlineData(true, true, 2, 3, "")]
    [InlineData(true, false, 1, 3, "至少 2 人")]
    [InlineData(true, false, 2, 3, "等待全员准备")]
    public void LanRoom_FormatStartHint(bool isHost, bool allReady, int playerCount, int maxPlayers, string fragment)
    {
        string hint = LanRoom.FormatStartHint(isHost, allReady, playerCount, maxPlayers);
        if (fragment.Length == 0)
            Assert.Equal("", hint);
        else
            Assert.Contains(fragment, hint);
    }

    [Fact]
    public void LanRoom_partial_lobby_of_six_can_be_ready_with_two_seated()
    {
        const int occ = 0b011;
        const int ready = 0b011;
        int seated = LanRoom.SeatedMask(occ, 2, 6);
        Assert.Equal(0b011, seated);
        Assert.True(LanRoom.AllOccupiedReady(ready, seated));
    }

    [Fact]
    public void LanRoom_solo_occupant_cannot_start_even_if_ready()
    {
        Assert.False(LanRoom.AllOccupiedReady(0b001, LanRoom.SeatedMask(0b001, 1, 6)));
    }

    [Theory]
    [InlineData(0f, 20f, 15f, false)]   // never seen
    [InlineData(10f, 20f, 15f, false)]  // 10s < 15s
    [InlineData(5f, 20f, 15f, true)]    // 15s idle
    [InlineData(4f, 20f, 15f, true)]
    [InlineData(10f, 20f, 0f, false)]   // timeout disabled
    public void LanRoom_ShouldEvictIdleClient(float lastRx, float now, float timeout, bool expected)
    {
        Assert.Equal(expected, LanRoom.ShouldEvictIdleClient(lastRx, now, timeout));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void LanRoom_AllowIdleEviction_skips_during_match(bool matchActive, bool expected)
    {
        Assert.Equal(expected, LanRoom.AllowIdleEviction(matchActive));
    }

    /// <summary>
    /// Lobby idle must tolerate alt-tab / brief Unity stalls on any machine.
    /// Do not special-case loopback — multi-instance on one PC is only a test harness.
    /// </summary>
    [Fact]
    public void LanRoom_ClientIdleTimeoutSec_gives_lobby_headroom()
    {
        Assert.True(LanRoom.ClientIdleTimeoutSec >= 45f);
    }

    [Theory]
    [InlineData(false, (int)LanState.Connected, 1, true)]
    [InlineData(false, (int)LanState.Connected, 3, true)]
    [InlineData(true, (int)LanState.Connected, 0, false)]  // host
    [InlineData(false, (int)LanState.Connecting, 1, false)]
    [InlineData(false, (int)LanState.Connected, 0, false)] // slot not assigned yet
    [InlineData(false, (int)LanState.Listen, 1, false)]
    public void LanRoom_ShouldClientLobbyKeepalive(
        bool isHost, int state, int localSlot, bool expected)
    {
        Assert.Equal(expected, LanRoom.ShouldClientLobbyKeepalive(isHost, (LanState)state, localSlot));
    }
}
