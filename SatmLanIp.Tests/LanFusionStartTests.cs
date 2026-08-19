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
    [Trait("Category", "KnownFailure")]
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
    public void LanFusionStart_TryClientTarget_rejects_empty_address()
    {
        Assert.False(LanFusionStart.TryClientTarget("", 37241, out _, out _));
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
}
