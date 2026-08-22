using System;
using System.IO;
using Xunit;

namespace SatmLanIp.Tests;

public sealed class LanBuildTests
{
    [Fact]
    public void LanBuild_SelfCheck_passes() => LanBuild.SelfCheck();

    [Fact]
    public void LanBuild_ShouldReject_only_when_both_nonzero_and_differ()
    {
        Assert.True(LanBuild.ShouldReject(24837841u, 24450017u));
        Assert.False(LanBuild.ShouldReject(24837841u, 24837841u));
        Assert.False(LanBuild.ShouldReject(0, 24837841u));
        Assert.False(LanBuild.ShouldReject(24837841u, 0));
    }

    [Fact]
    public void LanBuild_TryParseAcfFile_matches_appid_and_buildid()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SatmLanIp_build_" + Guid.NewGuid().ToString("N"));
        string steamApps = Path.Combine(dir, "steamapps");
        Directory.CreateDirectory(steamApps);
        string acf = Path.Combine(steamApps, "appmanifest_3722330.acf");
        File.WriteAllText(acf,
            "\"AppState\"\n{\n\t\"appid\"\t\t\"3722330\"\n\t\"buildid\"\t\t\"24837841\"\n}\n");
        try
        {
            Assert.Equal(24837841u, LanBuild.TryParseAcfFile(acf));
            File.WriteAllText(acf,
                "\"AppState\"\n{\n\t\"appid\"\t\t\"9999999\"\n\t\"buildid\"\t\t\"24837841\"\n}\n");
            Assert.Equal(0u, LanBuild.TryParseAcfFile(acf));
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { /* temp cleanup */ }
        }
    }

    [Fact]
    public void LanBuild_TryResolveFromManifest_uses_override()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SatmLanIp_root_" + Guid.NewGuid().ToString("N"));
        string steamApps = Path.Combine(dir, "steamapps");
        Directory.CreateDirectory(steamApps);
        string acf = Path.Combine(steamApps, "appmanifest_3722330.acf");
        File.WriteAllText(acf,
            "\"AppState\"\n{\n\t\"appid\"\t\t\"3722330\"\n\t\"buildid\"\t\t\"24450017\"\n}\n");
        try
        {
            LanBuild.GameRootOverride = dir;
            Assert.Equal(24450017u, LanBuild.TryResolveFromManifest());
        }
        finally
        {
            LanBuild.GameRootOverride = null;
            try { Directory.Delete(dir, true); }
            catch { /* temp cleanup */ }
        }
    }

    [Fact]
    public void LanBuild_TryResolveFromManifest_walks_from_game_data_to_steamapps()
    {
        string root = Path.Combine(Path.GetTempPath(), "SatmLanIp_layout_" + Guid.NewGuid().ToString("N"));
        string steamApps = Path.Combine(root, "steamapps");
        string gameData = Path.Combine(root, "common", "Shift At Midnight", "ShiftAtMidnight_Data");
        Directory.CreateDirectory(gameData);
        Directory.CreateDirectory(steamApps);
        string acf = Path.Combine(steamApps, "appmanifest_3722330.acf");
        File.WriteAllText(acf,
            "\"AppState\"\n{\n\t\"appid\"\t\t\"3722330\"\n\t\"buildid\"\t\t\"24837841\"\n}\n");
        Func<string> oldProvider = LanBuild.DataPathProvider;
        try
        {
            LanBuild.GameRootOverride = null;
            LanBuild.DataPathProvider = () => gameData;
            Assert.Equal(24837841u, LanBuild.TryResolveFromManifest());
        }
        finally
        {
            LanBuild.DataPathProvider = oldProvider;
            try { Directory.Delete(root, true); }
            catch { /* temp cleanup */ }
        }
    }

    [Fact]
    public void LanBuild_TryResolveFromManifest_reads_steamapps_when_walk_hits_it()
    {
        string root = Path.Combine(Path.GetTempPath(), "SatmLanIp_steamapps_" + Guid.NewGuid().ToString("N"));
        string steamApps = Path.Combine(root, "steamapps");
        string common = Path.Combine(steamApps, "common", "Game");
        Directory.CreateDirectory(common);
        Directory.CreateDirectory(steamApps);
        string acf = Path.Combine(steamApps, "appmanifest_3722330.acf");
        File.WriteAllText(acf,
            "\"AppState\"\n{\n\t\"appid\"\t\t\"3722330\"\n\t\"buildid\"\t\t\"24837841\"\n}\n");
        Func<string> oldProvider = LanBuild.DataPathProvider;
        try
        {
            LanBuild.GameRootOverride = null;
            LanBuild.DataPathProvider = () => common;
            Assert.Equal(24837841u, LanBuild.TryResolveFromManifest());
        }
        finally
        {
            LanBuild.DataPathProvider = oldProvider;
            try { Directory.Delete(root, true); }
            catch { /* temp cleanup */ }
        }
    }

    [Fact]
    public void LanBuild_CollectLibraryRoots_parses_libraryfolders_vdf()
    {
        string root = Path.Combine(Path.GetTempPath(), "SatmLanIp_vdf_" + Guid.NewGuid().ToString("N"));
        string steamApps = Path.Combine(root, "steamapps");
        Directory.CreateDirectory(steamApps);
        string lib = Path.Combine(root, "lib2");
        string libSteamApps = Path.Combine(lib, "steamapps");
        Directory.CreateDirectory(libSteamApps);
        string acf = Path.Combine(libSteamApps, "appmanifest_3722330.acf");
        File.WriteAllText(acf,
            "\"AppState\"\n{\n\t\"appid\"\t\t\"3722330\"\n\t\"buildid\"\t\t\"24450017\"\n}\n");
        File.WriteAllText(Path.Combine(steamApps, "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + lib.Replace("\\", "\\\\") + "\"\n\t}\n}\n");
        try
        {
            var roots = new System.Collections.Generic.List<string> { root };
            LanBuild.CollectLibraryRoots(root, roots);
            Assert.Contains(lib, roots);
            LanBuild.GameRootOverride = lib;
            Assert.Equal(24450017u, LanBuild.TryResolveFromManifest());
        }
        finally
        {
            LanBuild.GameRootOverride = null;
            try { Directory.Delete(root, true); }
            catch { /* temp cleanup */ }
        }
    }
}
