using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace SatmLanIp;

/// <summary>
/// Disable LAN when known-incompatible local plugins are present (file-name scan).
/// </summary>
internal static class ConflictGuard
{
    private static readonly string[] ConflictFileNames =
    {
        "SatmForceDirect.dll",
        "SatmPhotonSwap.dll",
        "SatmRegionForce.dll",
    };

    public static bool ConflictsPresent { get; private set; }
    public static string ConflictSummary { get; private set; } = "";

    public static void SelfCheck()
    {
        if (ConflictFileNames.Length != 3
            || ConflictFileNames[0] != "SatmForceDirect.dll")
            throw new InvalidOperationException("SatmLanIp ConflictGuard file list");
    }

    public static void Refresh()
    {
        var hits = new List<string>();

        try
        {
            string pluginPath = Paths.PluginPath;
            if (!string.IsNullOrEmpty(pluginPath) && Directory.Exists(pluginPath))
            {
                foreach (string name in ConflictFileNames)
                {
                    string full = Path.Combine(pluginPath, name);
                    if (File.Exists(full))
                        hits.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSrc?.LogWarning($"[SatmLanIp] ConflictGuard file scan: {ex.GetType().Name}: {ex.Message}");
        }

        // File scan is authoritative (Chainloader load-order is unreliable at Load time).
        ConflictsPresent = hits.Count > 0;
        ConflictSummary = hits.Count == 0 ? "" : string.Join("; ", hits);
    }

    public static bool CanActivate()
    {
        return Plugin.Enabled && !ConflictsPresent;
    }
}
