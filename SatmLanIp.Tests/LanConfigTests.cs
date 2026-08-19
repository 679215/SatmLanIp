using Xunit;

namespace SatmLanIp.Tests;

public sealed class LanConfigTests
{
    [Fact]
    public void LanConfig_SelfCheck_passes() => LanConfig.SelfCheck();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65535)]
    public void LanConfig_invalid_port_falls_back_to_default(int rawPort)
    {
        Assert.Equal(LanConfig.DefaultPort, LanConfig.NormalizePort(rawPort));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37241)]
    [InlineData(65534)]
    public void LanConfig_valid_port_is_preserved(int port)
    {
        Assert.Equal(port, LanConfig.NormalizePort(port));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(LanConfig.MaxPort, true)]
    [InlineData(37241, true)]
    [InlineData(0, false)]
    [InlineData(LanConfig.MaxPort + 1, false)]
    public void LanConfig_IsValidPort_matches_range(int port, bool expected)
    {
        Assert.Equal(expected, LanConfig.IsValidPort(port));
    }

    [Fact]
    public void LanConfig_defaults_are_stable()
    {
        Assert.True(LanConfig.DefaultEnabled);
        Assert.Equal(37241, LanConfig.DefaultPort);
        Assert.Equal(65534, LanConfig.MaxPort);
        Assert.Equal(30, LanConfig.DefaultTimeoutSec);
        Assert.Equal("", LanConfig.DefaultJoinAddress);
    }
}
