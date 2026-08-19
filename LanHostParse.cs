using System;

namespace SatmLanIp;

/// <summary>Pure host:port parsing for JoinAddress (no Unity / socket deps).</summary>
internal static class LanHostParse
{
    internal static bool TryParseHostPort(string raw, int defaultPort, out string host, out int port, out string error)
    {
        host = "";
        port = defaultPort;
        error = "";
        string s = (raw ?? "").Trim();
        if (s.Length == 0)
        {
            error = "JoinAddress empty";
            return false;
        }

        string portStr = defaultPort.ToString();
        while (s.EndsWith(":" + portStr + ":" + portStr))
            s = s.Substring(0, s.Length - (portStr.Length + 1));

        int colon = s.LastIndexOf(':');
        if (colon > 0 && s.IndexOf(':') == colon)
        {
            string portPart = s.Substring(colon + 1).Trim();
            string hostPart = s.Substring(0, colon).Trim();
            if (hostPart.Length == 0)
            {
                error = "JoinAddress empty host";
                return false;
            }
            if (!int.TryParse(portPart, out int p) || p < 1 || p > 65535)
            {
                error = "invalid JoinPort";
                return false;
            }
            host = hostPart;
            port = p;
            return true;
        }

        host = s;
        port = defaultPort;
        return true;
    }

    internal static void SelfCheck()
    {
        if (!TryParseHostPort("192.168.1.10", 37241, out string h1, out int p1, out _) ||
            h1 != "192.168.1.10" || p1 != 37241)
            throw new InvalidOperationException("SatmLanIp parse ip-only failed");
        if (!TryParseHostPort("192.168.1.10:37241", 37241, out string h2, out int p2, out _) ||
            h2 != "192.168.1.10" || p2 != 37241)
            throw new InvalidOperationException("SatmLanIp parse ip:port failed");
        if (!TryParseHostPort("192.168.1.10:37241:37241", 37241, out string h3, out int p3, out _) ||
            h3 != "192.168.1.10" || p3 != 37241)
            throw new InvalidOperationException("SatmLanIp parse double-port failed");
        if (TryParseHostPort("", 37241, out _, out _, out string err) || err != "JoinAddress empty")
            throw new InvalidOperationException("SatmLanIp parse empty failed");
    }
}
