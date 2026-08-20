using Xunit;

namespace SatmLanIp.Tests;

public sealed class LanFusionStartTests
{
    [Fact]
    public void LanFusionStart_SelfCheck_passes() => LanFusionStart.SelfCheck();

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void LanFusionStart_ShouldSkipPhoton(bool allowFusion, bool pluginActive, bool expected)
    {
        Assert.Equal(expected, LanFusionStart.ShouldSkipPhoton(allowFusion, pluginActive));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void LanFusionStart_ShouldBindHostAddress(bool isHost, bool expected)
    {
        Assert.Equal(expected, LanFusionStart.ShouldBindHostAddress(isHost));
    }

    [Fact]
    public void LanFusionStart_HostBindPort_increments_listen_port()
    {
        Assert.Equal(37242, LanFusionStart.HostBindPort(37241));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(65534, 65535)]
    [InlineData(0, 37242)]
    [InlineData(65535, 37242)]
    public void LanFusionStart_HostBindPort_handles_edges(int listenPort, int expected)
    {
        Assert.Equal((ushort)expected, LanFusionStart.HostBindPort(listenPort));
    }

    [Fact]
    public void LanFusionStart_TryClientTarget_rejects_reserved_port()
    {
        Assert.False(LanFusionStart.TryClientTarget("10.0.0.1:65535", 37241, out _, out _));
    }

    [Fact]
    public void LanFusionStart_TryClientTarget_parses_remote_host()
    {
        Assert.True(LanFusionStart.TryClientTarget("10.0.0.2:27015", 37241, out string ip, out ushort port));
        Assert.Equal("10.0.0.2", ip);
        Assert.Equal((ushort)27016, port);
    }

    [Fact]
    public void LanFusionStart_TryClientTarget_host_only_uses_listen_plus_one()
    {
        Assert.True(LanFusionStart.TryClientTarget("10.0.0.2", 37241, out string ip, out ushort port));
        Assert.Equal("10.0.0.2", ip);
        Assert.Equal((ushort)37242, port);
    }

    [Fact]
    public void LanFusionStart_TryClientTarget_rejects_empty_address()
    {
        Assert.False(LanFusionStart.TryClientTarget("", 37241, out _, out _));
    }

    [Theory]
    [InlineData(27015, 27015)]
    [InlineData(37241, 37241)]
    [InlineData(65534, 65534)]
    public void LanFusionStart_SessionPortAfterJoinParse_keeps_explicit_port(int parsed, int expected)
    {
        Assert.Equal(expected, LanFusionStart.SessionPortAfterJoinParse(parsed));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65535)]
    [InlineData(-1)]
    public void LanFusionStart_SessionPortAfterJoinParse_falls_back_on_invalid(int parsed)
    {
        Assert.Equal(LanConfig.DefaultPort, LanFusionStart.SessionPortAfterJoinParse(parsed));
    }

    [Fact]
    public void LanFusionStart_ResolveClientConnectIp_keeps_empty_input_empty()
    {
        Assert.Equal("", LanFusionStart.ResolveClientConnectIp(""));
    }

    [Fact]
    public void LanFusionStart_ResolveClientConnectIp_passes_through_remote_ip()
    {
        Assert.Equal("10.0.0.9", LanFusionStart.ResolveClientConnectIp("10.0.0.9"));
    }

    [Fact]
    public void LanFusionStart_ResolveClientConnectIp_keeps_loopback()
    {
        Assert.Equal("127.0.0.1", LanFusionStart.ResolveClientConnectIp("127.0.0.1"));
        Assert.Equal("127.0.0.1", LanFusionStart.ResolveClientConnectIp("127.9.9.9"));
    }

    [Fact]
    public void LanFusionStart_ResolveClientConnectIp_rewrites_own_advertise_ip_to_loopback()
    {
        var mine = LanLocalIp.ListIPv4();
        if (mine.Count == 0)
            return; // no NIC IPv4 on this host; skip environment-dependent case

        Assert.Equal("127.0.0.1", LanFusionStart.ResolveClientConnectIp(mine[0]));
        Assert.True(LanFusionStart.TryClientTarget(mine[0], 37241, out string ip, out ushort port));
        Assert.Equal("127.0.0.1", ip);
        Assert.Equal((ushort)37242, port);
    }

    [Theory]
    [InlineData(true, 0, 1)]
    [InlineData(false, 1, 2)]
    [InlineData(false, 2, 3)]
    [InlineData(false, 5, 6)]
    [InlineData(false, 0, 2)]
    [InlineData(false, -1, 2)]
    [InlineData(false, 6, 6)]
    public void LanFusionStart_ResolveLanActorId(bool isHost, int localSlot, int expected)
    {
        Assert.Equal(expected, LanFusionStart.ResolveLanActorId(isHost, localSlot));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 3)]
    [InlineData(6, 6)]
    [InlineData(99, 6)]
    public void LanFusionStart_HostPremapPeerActorHi(int maxPlayers, int expected)
    {
        Assert.Equal(expected, LanFusionStart.HostPremapPeerActorHi(maxPlayers));
    }
}
