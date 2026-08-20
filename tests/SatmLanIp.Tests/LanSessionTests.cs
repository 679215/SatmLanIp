using Xunit;

namespace SatmLanIp.Tests;

public sealed class LanSessionTests
{
    [Fact]
    public void LanSession_AllReady_false_until_every_seated_slot_is_ready()
    {
        var s = new LanSession
        {
            MaxPlayers = 3,
            PlayerCount = 2,
            OccupiedMask = 0b011,
            ReadyMask = 0b001,
        };
        Assert.False(s.AllReady);
        s.ReadyMask = 0b011;
        Assert.True(s.AllReady);
    }

    [Fact]
    public void LanSession_AllReady_false_for_solo_host()
    {
        var s = new LanSession
        {
            MaxPlayers = 6,
            PlayerCount = 1,
            OccupiedMask = 0b001,
            ReadyMask = 0b001,
        };
        Assert.False(s.AllReady);
    }

    [Theory]
    [InlineData((int)LanState.Listen, true)]
    [InlineData((int)LanState.Connecting, true)]
    [InlineData((int)LanState.Connected, true)]
    [InlineData((int)LanState.Idle, false)]
    [InlineData((int)LanState.Fail, false)]
    [InlineData((int)LanState.Drop, false)]
    public void LanSession_InRoom(int state, bool expected)
    {
        Assert.Equal(expected, new LanSession { State = (LanState)state }.InRoom);
    }

    [Fact]
    public void LanSession_HostReady_and_ClientReady_read_slots()
    {
        var s = new LanSession
        {
            MaxPlayers = 3,
            PlayerCount = 3,
            OccupiedMask = 0b111,
            ReadyMask = 0b100, // only slot 2 ready
        };
        Assert.False(s.HostReady);
        Assert.True(s.ClientReady); // any non-host seated ready
        s.ReadyMask = 0b001;
        Assert.False(s.ClientReady);
    }

    [Theory]
    [InlineData(2, 0, 1, 2)]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(-1, 3, -1, 3)]
    [InlineData(-1, -5, -1, 0)]
    public void LanSession_ResolveSaveSlot(int picked, int current, int firstExisting, int expected)
    {
        Assert.Equal(expected, LanSession.ResolveSaveSlot(picked, current, firstExisting));
    }

    [Fact]
    public void LanSession_SetPeerPose_keeps_per_slot()
    {
        var s = new LanSession();
        s.SetPeerPose(1, 1f, 2f, 3f, 4f);
        s.SetPeerPose(2, 10f, 20f, 30f, 40f);
        Assert.True(s.TryGetPeerPose(1, out float x1, out _, out _, out _));
        Assert.Equal(1f, x1);
        Assert.True(s.TryGetPeerPose(2, out float x2, out _, out _, out _));
        Assert.Equal(10f, x2);
        Assert.Equal(10f, s.RemoteX); // last writer
    }

    [Fact]
    public void LanSession_MarkHostDrop_clears_MatchActive()
    {
        var s = new LanSession
        {
            MatchActive = true,
            State = LanState.Connected,
        };
        s.MarkHostDrop();
        Assert.False(s.MatchActive);
        Assert.Equal(LanState.Drop, s.State);
    }

    [Fact]
    public void LanSession_ClearMatchActive_only_clears_flag()
    {
        var s = new LanSession { MatchActive = true, State = LanState.Connected };
        s.ClearMatchActive();
        Assert.False(s.MatchActive);
        Assert.Equal(LanState.Connected, s.State);
    }
}
