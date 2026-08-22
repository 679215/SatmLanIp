using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SatmLanIp;

/// <summary>Steam buildid from appmanifest; carried in Hello / HelloAck / BuildMismatch payloads.</summary>
internal static class LanBuild
{
    public const uint SteamAppId = 3722330;
    private const int MaxParentWalk = 8;

    private static readonly Regex AppIdLine =
        new("\"appid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BuildIdLine =
        new("\"buildid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LibraryPathLine =
        new("\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Tests may override game root (non-Steam layout).</summary>
    internal static string GameRootOverride;

    /// <summary>Plugin sets: () => Application.dataPath</summary>
    internal static Func<string> DataPathProvider;

    public static uint Current { get; private set; }

    public static void Resolve()
    {
        try
        {
            Current = TryResolveFromManifest();
        }
        catch
        {
            Current = 0;
        }
    }

    /// <summary>Plugin Load may run before Unity dataPath is ready; retry once in lobby.</summary>
    public static void EnsureResolved()
    {
        if (Current != 0)
            return;
        Resolve();
    }

    public static bool ShouldReject(uint local, uint remote)
    {
        return local != 0 && remote != 0 && local != remote;
    }

    public static string FormatMismatch(uint local, uint remote)
    {
        if (remote == 0)
            return "游戏版本不一致 (本机 build " + local.ToString() + "，主机 build 未知)";
        return "游戏版本不一致 (build " + local.ToString() + " vs " + remote.ToString() + ")";
    }

    internal static uint TryParseAcfFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return 0;
        try
        {
            string text = File.ReadAllText(path);
            Match app = AppIdLine.Match(text);
            if (!app.Success || !uint.TryParse(app.Groups[1].Value, out uint appId) || appId != SteamAppId)
                return 0;
            Match build = BuildIdLine.Match(text);
            if (!build.Success || !uint.TryParse(build.Groups[1].Value, out uint buildId))
                return 0;
            return buildId;
        }
        catch
        {
            return 0;
        }
    }

    internal static uint TryResolveFromManifest()
    {
        if (GameRootOverride != null)
        {
            uint fromOverride = TryResolveUnder(GameRootOverride);
            if (fromOverride != 0)
                return fromOverride;
        }

        string dataPath = null;
        try
        {
            if (DataPathProvider != null)
                dataPath = DataPathProvider();
        }
        catch
        {
            dataPath = null;
        }
        if (string.IsNullOrEmpty(dataPath))
            return 0;

        try
        {
            string dir = Path.GetFullPath(dataPath);
            for (int i = 0; i < MaxParentWalk && dir != null; i++)
            {
                uint id = TryResolveUnder(dir);
                if (id != 0)
                    return id;
                string parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || parent == dir)
                    break;
                dir = parent;
            }
        }
        catch
        {
        }

        return TryResolveFromSteamLibraries();
    }

    private static uint TryResolveFromSteamLibraries()
    {
        string steamPath = TryReadSteamInstallPath();
        if (string.IsNullOrEmpty(steamPath))
            return 0;

        var roots = new List<string>(4) { steamPath };
        CollectLibraryRoots(steamPath, roots);
        for (int i = 0; i < roots.Count; i++)
        {
            uint id = TryResolveUnder(roots[i]);
            if (id != 0)
                return id;
        }
        return 0;
    }

    private static string TryReadSteamInstallPath()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string steamPath = key?.GetValue("SteamPath") as string;
            if (string.IsNullOrWhiteSpace(steamPath))
                return null;
            steamPath = steamPath.Replace('/', '\\').Trim();
            return Directory.Exists(steamPath) ? steamPath : null;
        }
        catch
        {
            return null;
        }
    }

    internal static void CollectLibraryRoots(string steamPath, List<string> roots)
    {
        if (string.IsNullOrEmpty(steamPath))
            return;
        string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            return;
        try
        {
            string text = File.ReadAllText(vdf);
            MatchCollection matches = LibraryPathLine.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                string path = matches[i].Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal);
                if (path.Length == 0 || !Directory.Exists(path))
                    continue;
                bool seen = false;
                for (int j = 0; j < roots.Count; j++)
                {
                    if (string.Equals(roots[j], path, StringComparison.OrdinalIgnoreCase))
                    {
                        seen = true;
                        break;
                    }
                }
                if (!seen)
                    roots.Add(path);
            }
        }
        catch
        {
        }
    }

    private static string ResolveSteamAppsDir(string root)
    {
        if (string.IsNullOrEmpty(root))
            return null;
        string name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(name, "steamapps", StringComparison.OrdinalIgnoreCase))
            return root;
        string steamApps = Path.Combine(root, "steamapps");
        return Directory.Exists(steamApps) ? steamApps : null;
    }

    private static uint TryResolveUnder(string root)
    {
        string steamApps = ResolveSteamAppsDir(root);
        if (steamApps == null)
            return 0;
        return ScanManifestsInSteamApps(steamApps);
    }

    private static uint ScanManifestsInSteamApps(string steamApps)
    {
        try
        {
            string direct = Path.Combine(steamApps, "appmanifest_" + SteamAppId.ToString() + ".acf");
            uint directId = TryParseAcfFile(direct);
            if (directId != 0)
                return directId;

            string[] files = Directory.GetFiles(steamApps, "appmanifest_*.acf");
            uint best = 0;
            for (int i = 0; i < files.Length; i++)
            {
                uint id = TryParseAcfFile(files[i]);
                if (id != 0)
                    best = id;
            }
            return best;
        }
        catch
        {
            return 0;
        }
    }

    internal static void SelfCheck()
    {
        GameRootOverride = null;
        if (!ShouldReject(24837841u, 24450017u) || ShouldReject(24837841u, 24837841u))
            throw new InvalidOperationException("SatmLanIp LanBuild ShouldReject");
        if (ShouldReject(0, 24837841u) || ShouldReject(24837841u, 0))
            throw new InvalidOperationException("SatmLanIp LanBuild zero allows");
        if (FormatMismatch(1, 2) != "游戏版本不一致 (build 1 vs 2)")
            throw new InvalidOperationException("SatmLanIp LanBuild FormatMismatch");
        if (FormatMismatch(24837841u, 0) != "游戏版本不一致 (本机 build 24837841，主机 build 未知)")
            throw new InvalidOperationException("SatmLanIp LanBuild FormatMismatch remote zero");
    }
}
