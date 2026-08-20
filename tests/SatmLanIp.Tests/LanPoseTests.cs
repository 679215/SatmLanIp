using Xunit;

namespace SatmLanIp.Tests;

public sealed class LanPoseTests
{
    [Fact]
    public void LanPose_SelfCheck_passes() => LanPose.SelfCheck();

    [Fact]
    public void LanPose_roundtrips_negative_and_fractional_values()
    {
        byte[] buf = new byte[LanPose.PayloadSize];
        LanPose.Write(buf, 0, -1.25f, 0.5f, 999.75f, -180.5f);

        Assert.True(LanPose.TryRead(buf, buf.Length, 0, out float x, out float y, out float z, out float yaw));
        Assert.Equal(-1.25f, x);
        Assert.Equal(0.5f, y);
        Assert.Equal(999.75f, z);
        Assert.Equal(-180.5f, yaw);
    }

    [Fact]
    public void LanPose_rejects_null_and_short_buffers()
    {
        Assert.False(LanPose.TryRead(null, LanPose.PayloadSize, 0, out _, out _, out _, out _));
        Assert.False(LanPose.TryRead(new byte[LanPose.PayloadSize - 1], LanPose.PayloadSize - 1, 0, out _, out _, out _, out _));
    }

    [Fact]
    public void LanPose_rejects_offset_past_buffer()
    {
        byte[] buf = new byte[LanPose.PayloadSize];
        Assert.False(LanPose.TryRead(buf, buf.Length, 1, out _, out _, out _, out _));
    }

    [Fact]
    public void LanPose_write_at_nonzero_offset()
    {
        byte[] buf = new byte[LanPose.PayloadSize + 4];
        LanPose.Write(buf, 4, 10f, 20f, 30f, 40f);
        Assert.True(LanPose.TryRead(buf, buf.Length, 4, out float x, out float y, out float z, out float yaw));
        Assert.Equal(10f, x);
        Assert.Equal(20f, y);
        Assert.Equal(30f, z);
        Assert.Equal(40f, yaw);
    }
}
