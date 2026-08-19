using System.Collections.Generic;
using System.Net.NetworkInformation;
using Xunit;

namespace SatmLanIp.Tests;

public sealed class LanLocalIpTests
{
    [Fact]
    public void LanLocalIp_SelfCheck_passes() => LanLocalIp.SelfCheck();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("127.0.0.1")]
    [InlineData("127.42.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.254")]
    public void LanLocalIp_filters_non_joinable_advertise_addresses(string ip)
    {
        Assert.True(LanLocalIp.ShouldSkipAdvertiseIp(ip));
    }

    [Theory]
    [InlineData("10.0.0.2")]
    [InlineData("192.168.1.10")]
    [InlineData("198.20.0.1")]
    public void LanLocalIp_keeps_joinable_advertise_addresses(string ip)
    {
        Assert.False(LanLocalIp.ShouldSkipAdvertiseIp(ip));
    }

    [Fact]
    public void LanLocalIp_classifies_physical_adapters()
    {
        Assert.True(LanLocalIp.IsPhysicalAdvertiseAdapter(NetworkInterfaceType.Ethernet));
        Assert.True(LanLocalIp.IsPhysicalAdvertiseAdapter(NetworkInterfaceType.Wireless80211));
        Assert.False(LanLocalIp.IsPhysicalAdvertiseAdapter(NetworkInterfaceType.Loopback));
    }

    [Fact]
    public void LanLocalIp_keeps_overlay_adapters_but_skips_host_only_adapters()
    {
        Assert.True(LanLocalIp.IsOverlayAdvertiseAdapter("Wintun", "Wintun Userspace Tunnel"));
        Assert.True(LanLocalIp.IsOverlayAdvertiseAdapter("VPN", "WireGuard Adapter"));
        Assert.False(LanLocalIp.IsJunkAdvertiseAdapter("WLAN", "Intel Wi-Fi"));
        Assert.True(LanLocalIp.IsJunkAdvertiseAdapter("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter"));
    }

    [Fact]
    public void LanLocalIp_formats_empty_advertise_rows()
    {
        Assert.Equal("(no LAN IPv4 found) port=37241", LanLocalIp.FormatAdvertiseRows(new List<LanLocalIp.AdvertiseAddr>(), 37241));
        Assert.Equal("(no LAN IPv4 found) port=37241", LanLocalIp.FormatAdvertiseRows(null, 37241));
    }

    [Fact]
    public void LanLocalIp_formats_multiple_advertise_rows()
    {
        var rows = new List<LanLocalIp.AdvertiseAddr>
        {
            new() { Kind = LanLocalIp.KindLan, Ip = "192.168.0.5" },
            new() { Kind = LanLocalIp.KindOverlay, Ip = "10.8.0.2" },
        };
        string formatted = LanLocalIp.FormatAdvertiseRows(rows, 27015);
        Assert.Contains("192.168.0.5:27015", formatted);
        Assert.Contains("10.8.0.2:27015", formatted);
        Assert.Contains(LanLocalIp.KindLan, formatted);
        Assert.Contains(LanLocalIp.KindOverlay, formatted);
    }

    [Fact]
    public void LanLocalIp_IsOwnLanIp_treats_loopback_as_own()
    {
        Assert.True(LanLocalIp.IsOwnLanIp("127.0.0.1"));
        Assert.True(LanLocalIp.IsOwnLanIp("127.1.2.3"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LanLocalIp_IsOwnLanIp_rejects_empty(string ip)
    {
        Assert.False(LanLocalIp.IsOwnLanIp(ip));
    }
}
