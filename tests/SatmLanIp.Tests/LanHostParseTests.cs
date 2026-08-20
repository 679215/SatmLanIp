using Xunit;

namespace SatmLanIp.Tests;

public sealed class LanHostParseTests
{
    [Fact]
    public void LanHostParse_SelfCheck_passes() => LanHostParse.SelfCheck();

    [Fact]
    public void LanHostParse_invalid_port_returns_error()
    {
        Assert.False(LanHostParse.TryParseHostPort("10.0.0.1:99999", 37241, out _, out _, out string err));
        Assert.Equal("invalid JoinPort", err);
    }

    [Fact]
    public void LanHostParse_rejects_port_reserved_for_fusion()
    {
        Assert.False(LanHostParse.TryParseHostPort("10.0.0.1:65535", 37241, out _, out _, out string err));
        Assert.Equal("invalid JoinPort", err);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void LanHostParse_rejects_empty_or_whitespace_address(string raw)
    {
        Assert.False(LanHostParse.TryParseHostPort(raw, 37241, out _, out _, out string err));
        Assert.Equal("JoinAddress empty", err);
    }

    [Fact]
    public void LanHostParse_null_input_is_empty()
    {
        Assert.False(LanHostParse.TryParseHostPort(null, 37241, out _, out _, out string err));
        Assert.Equal("JoinAddress empty", err);
    }

    [Fact]
    public void LanHostParse_trims_host_and_explicit_port()
    {
        Assert.True(LanHostParse.TryParseHostPort(" 10.0.0.1 : 27015 ", 37241, out string host, out int port, out _));
        Assert.Equal("10.0.0.1", host);
        Assert.Equal(27015, port);
    }

    [Fact]
    public void LanHostParse_host_only_uses_default_port()
    {
        Assert.True(LanHostParse.TryParseHostPort("10.0.0.1", 37241, out string host, out int port, out _));
        Assert.Equal("10.0.0.1", host);
        Assert.Equal(37241, port);
    }

    [Fact]
    public void LanHostParse_rejects_empty_host()
    {
        Assert.False(LanHostParse.TryParseHostPort(":37241", 37241, out _, out _, out string err));
        Assert.Equal("JoinAddress empty host", err);
    }

    [Theory]
    [InlineData("10.0.0.1:0")]
    [InlineData("10.0.0.1:not-a-port")]
    public void LanHostParse_rejects_invalid_explicit_ports(string raw)
    {
        Assert.False(LanHostParse.TryParseHostPort(raw, 37241, out _, out _, out string err));
        Assert.Equal("invalid JoinPort", err);
    }

    [Fact]
    public void LanHostParse_strips_repeated_default_port_suffix()
    {
        Assert.True(LanHostParse.TryParseHostPort("192.168.1.10:37241:37241", 37241, out string host, out int port, out _));
        Assert.Equal("192.168.1.10", host);
        Assert.Equal(37241, port);
    }

    [Fact]
    public void LanHostParse_hostname_without_colon_uses_whole_string()
    {
        Assert.True(LanHostParse.TryParseHostPort("game-pc.local", 37241, out string host, out int port, out _));
        Assert.Equal("game-pc.local", host);
        Assert.Equal(37241, port);
    }

    [Fact]
    public void LanHostParse_accepts_max_valid_join_port()
    {
        Assert.True(LanHostParse.TryParseHostPort("10.0.0.1:65534", 37241, out string host, out int port, out _));
        Assert.Equal("10.0.0.1", host);
        Assert.Equal(65534, port);
    }

    [Fact]
    public void LanHostParse_rejects_whitespace_only_host_before_port()
    {
        Assert.False(LanHostParse.TryParseHostPort(" :37241", 37241, out _, out _, out string err));
        Assert.Equal("JoinAddress empty host", err);
    }
}
