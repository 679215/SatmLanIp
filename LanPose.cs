using System;

namespace SatmLanIp;

internal static class LanPose
{
    public const int PayloadSize = 16;

    public static void Write(byte[] buf, int offset, float x, float y, float z, float yaw)
    {
        WriteF32(buf, offset, x);
        WriteF32(buf, offset + 4, y);
        WriteF32(buf, offset + 8, z);
        WriteF32(buf, offset + 12, yaw);
    }

    public static bool TryRead(byte[] buf, int len, int offset, out float x, out float y, out float z, out float yaw)
    {
        x = y = z = yaw = 0f;
        if (buf == null || len < offset + PayloadSize)
            return false;
        x = ReadF32(buf, offset);
        y = ReadF32(buf, offset + 4);
        z = ReadF32(buf, offset + 8);
        yaw = ReadF32(buf, offset + 12);
        return true;
    }

    public static void SelfCheck()
    {
        byte[] buf = new byte[PayloadSize];
        Write(buf, 0, 1.5f, -2.25f, 8f, 90f);
        if (!TryRead(buf, buf.Length, 0, out float x, out float y, out float z, out float yaw))
            throw new InvalidOperationException("SatmLanIp LanPose read");
        if (Math.Abs(x - 1.5f) > 0.0001f || Math.Abs(y + 2.25f) > 0.0001f ||
            Math.Abs(z - 8f) > 0.0001f || Math.Abs(yaw - 90f) > 0.0001f)
            throw new InvalidOperationException("SatmLanIp LanPose roundtrip");
    }

    private static void WriteF32(byte[] buf, int offset, float v)
    {
        byte[] b = BitConverter.GetBytes(v);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(b);
        buf[offset] = b[0];
        buf[offset + 1] = b[1];
        buf[offset + 2] = b[2];
        buf[offset + 3] = b[3];
    }

    private static float ReadF32(byte[] buf, int offset)
    {
        byte[] b = new byte[4];
        b[0] = buf[offset];
        b[1] = buf[offset + 1];
        b[2] = buf[offset + 2];
        b[3] = buf[offset + 3];
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(b);
        return BitConverter.ToSingle(b, 0);
    }
}
