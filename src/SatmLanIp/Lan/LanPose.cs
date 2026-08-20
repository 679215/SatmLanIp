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
        int bits = BitConverter.SingleToInt32Bits(v);
        if (!BitConverter.IsLittleEndian)
        {
            buf[offset] = (byte)((bits >> 24) & 0xFF);
            buf[offset + 1] = (byte)((bits >> 16) & 0xFF);
            buf[offset + 2] = (byte)((bits >> 8) & 0xFF);
            buf[offset + 3] = (byte)(bits & 0xFF);
            return;
        }
        buf[offset] = (byte)(bits & 0xFF);
        buf[offset + 1] = (byte)((bits >> 8) & 0xFF);
        buf[offset + 2] = (byte)((bits >> 16) & 0xFF);
        buf[offset + 3] = (byte)((bits >> 24) & 0xFF);
    }

    private static float ReadF32(byte[] buf, int offset)
    {
        int bits;
        if (!BitConverter.IsLittleEndian)
        {
            bits = (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];
        }
        else
        {
            bits = buf[offset]
                | (buf[offset + 1] << 8)
                | (buf[offset + 2] << 16)
                | (buf[offset + 3] << 24);
        }
        return BitConverter.Int32BitsToSingle(bits);
    }
}
