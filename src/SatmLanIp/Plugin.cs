using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SatmLanIp;

[BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static bool Enabled;
    internal static int ListenPort = 37241;
    internal static string JoinAddress = "";
    internal static int JoinPort = 37241;
    internal static bool BlockFusionStart = true;
    internal static bool ShowHud = true;
    internal static bool HideHudInGame = true;
    internal static bool ShowNativeMenu = true;
    internal static int ConnectTimeoutSec = LanConfig.DefaultTimeoutSec;
    internal static bool VerboseNetworkLog;
    internal static bool IsActive;
    internal static LanTransport Transport;

    private static ConfigEntry<string> JoinAddressEntry;
    private static ConfigEntry<int> ListenPortEntry;
    private static ConfigEntry<int> JoinPortEntry;
    private ConfigEntry<bool> _enabled;
    private ConfigEntry<int> _listenPort;
    private ConfigEntry<int> _joinPort;
    private ConfigEntry<bool> _blockFusion;
    private ConfigEntry<bool> _showHud;
    private ConfigEntry<bool> _hideHudInGame;
    private ConfigEntry<bool> _showNativeMenu;
    private ConfigEntry<int> _timeout;
    private ConfigEntry<bool> _verboseNetworkLog;
    private GameObject _hudGo;
    private static bool _syncingPorts;

    public override void Load()
    {
        LogSrc = Log;

        if (PluginInfo.GUID != "com.satmlanip" || string.IsNullOrEmpty(PluginInfo.Version))
            throw new InvalidOperationException("SatmLanIp PluginInfo self-check failed");

        LanProtocol.SelfCheck();
        LanBuild.SelfCheck();
        LanHostParse.SelfCheck();
        LanRoom.SelfCheck();
        LanPose.SelfCheck();
        LanLocalIp.SelfCheck();
        LanFusionStart.SelfCheck();
        FusionLanPatches.SelfCheck();
        LanMatch.SelfCheck();
        FusionCloudBypassPatches.SelfCheck();
        ConflictGuard.SelfCheck();
        LanHudBehaviour.SelfCheck();
        LanMenuActions.SelfCheck();
        LanMenuFlow.SelfCheck();
        LanMenuInjector.SelfCheck();
        LanConfig.SelfCheck();
        LanCloneUi.SelfCheck();

        LanBuild.DataPathProvider = () => Application.dataPath;
        LanBuild.Resolve();

        _enabled = Config.Bind("General", "Enabled", LanConfig.DefaultEnabled,
            "Master switch. Default true.");
        _listenPort = Config.Bind("General", "ListenPort", LanConfig.DefaultPort,
            "Host UDP listen port. UI Port writes the same value to JoinPort.");
        ListenPortEntry = _listenPort;
        JoinAddressEntry = Config.Bind("General", "JoinAddress", LanConfig.DefaultJoinAddress,
            "Host IP for joiners only. Default empty.");
        _joinPort = Config.Bind("General", "JoinPort", LanConfig.DefaultPort,
            "Join UDP port. Synced with UI Port and ListenPort.");
        JoinPortEntry = _joinPort;
        _blockFusion = Config.Bind("General", "BlockFusionStart", true,
            "While LAN Active, block FusionNetworkManager StartAsHost / StartAsClient (plugin still starts Fusion when ready).");
        _showHud = Config.Bind("General", "ShowHud", true,
            "Show LAN lobby / status UI.");
        _hideHudInGame = Config.Bind("General", "HideHudInGame", true,
            "Hide this mod's HUD after entering the Game scene.");
        _showNativeMenu = Config.Bind("General", "ShowNativeMenu", true,
            "Inject 局域网联机 into the play menu list (above Solo).");
        _timeout = Config.Bind("General", "ConnectTimeoutSec", LanConfig.DefaultTimeoutSec,
            "Client connect timeout in seconds. Default 30.");
        _verboseNetworkLog = Config.Bind("General", "VerboseNetworkLog", false,
            "Verbose Fusion socket / packet-header logs. Off by default; redact IPs before sharing.");

        try { Config.Remove(new ConfigDefinition("General", "ConfigRevision")); }
        catch { /* old key may be absent */ }

        Enabled = _enabled.Value;
        ListenPort = NormalizePortEntry(ListenPortEntry);
        JoinAddress = JoinAddressEntry.Value ?? "";
        JoinPort = NormalizePortEntry(JoinPortEntry);
        BlockFusionStart = _blockFusion.Value;
        ShowHud = _showHud.Value;
        HideHudInGame = _hideHudInGame.Value;
        ShowNativeMenu = _showNativeMenu.Value;
        ConnectTimeoutSec = _timeout.Value;
        VerboseNetworkLog = _verboseNetworkLog.Value;

        _enabled.SettingChanged += (_, __) => Enabled = _enabled.Value;
        ListenPortEntry.SettingChanged += (_, __) =>
        {
            if (_syncingPorts)
                return;
            SetLanPort(NormalizePortEntry(ListenPortEntry));
        };
        JoinAddressEntry.SettingChanged += (_, __) => JoinAddress = JoinAddressEntry.Value ?? "";
        JoinPortEntry.SettingChanged += (_, __) =>
        {
            if (_syncingPorts)
                return;
            SetLanPort(NormalizePortEntry(JoinPortEntry));
        };
        _blockFusion.SettingChanged += (_, __) => BlockFusionStart = _blockFusion.Value;
        _showHud.SettingChanged += (_, __) => ShowHud = _showHud.Value;
        _hideHudInGame.SettingChanged += (_, __) => HideHudInGame = _hideHudInGame.Value;
        _showNativeMenu.SettingChanged += (_, __) => ShowNativeMenu = _showNativeMenu.Value;
        _timeout.SettingChanged += (_, __) => ConnectTimeoutSec = _timeout.Value;
        _verboseNetworkLog.SettingChanged += (_, __) => VerboseNetworkLog = _verboseNetworkLog.Value;

        Transport = new LanTransport();

        ConflictGuard.Refresh();
        if (ConflictGuard.ConflictsPresent)
        {
            LogSrc.LogError(
                $"[SatmLanIp] conflict: {ConflictGuard.ConflictSummary}; LAN Active disabled until those DLLs are renamed/removed");
        }

        try
        {
            var harmony = new Harmony(PluginInfo.GUID);
            harmony.PatchAll(typeof(PhotonGuardPatches));
            harmony.PatchAll(typeof(FusionLanPatches));
            harmony.PatchAll(typeof(FusionCloudBypassPatches));
            harmony.PatchAll(typeof(LanMenuInjector));
            harmony.PatchAll(typeof(LanMenuPanel));
            FusionCloudBypassPatches.ApplySimulationLocalPlayerPatches(harmony);
            FusionCloudBypassPatches.ApplySocketPatches(harmony);
            FusionLanPatches.ApplyLeavePatches(harmony);
            PhotonGuardPatches.LogPatchStatus(harmony);
            FusionLanPatches.LogPatchStatus(harmony);
            FusionCloudBypassPatches.LogPatchStatus(harmony);
            LanMenuInjector.LogPatchStatus(harmony);
            LanMenuPanel.LogPatchStatus(harmony);
            Plugin.LogSrc.LogInfo("[SatmLanIp] LAN Fusion hooks + native menu active");
        }
        catch (Exception ex)
        {
            LogSrc.LogError($"[SatmLanIp] Harmony failed: {ex}");
        }

        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<LanHudBehaviour>();
            _hudGo = new GameObject("SatmLanIpHud");
            Object.DontDestroyOnLoad(_hudGo);
            _hudGo.AddComponent<LanHudBehaviour>();
        }
        catch (Exception ex)
        {
            LogSrc.LogError($"[SatmLanIp] HUD failed: {ex}");
        }

        // Joiners (and hosts) may alt-tab; Unity otherwise pauses Update and lobby UDP starves.
        try
        {
            Application.runInBackground = true;
        }
        catch (Exception ex)
        {
            LogSrc.LogWarning("[SatmLanIp] runInBackground: " + ex.Message);
        }

        LogSrc.LogInfo(
            $"[SatmLanIp] {PluginInfo.Name} {PluginInfo.Version} loaded (Enabled={Enabled}; " +
            "ShowNativeMenu=" + ShowNativeMenu + "; buildid=" + LanBuild.Current.ToString() +
            (LanBuild.Current == 0 ? " 无法校验版本" : "") + "). " +
            "菜单:局域网联机→创建/加入（创建房间后再选档/模式）。");
    }

    internal static void SetJoinAddress(string ip)
    {
        JoinAddress = ip ?? "";
        if (JoinAddressEntry != null)
            JoinAddressEntry.Value = JoinAddress;
    }

    internal static void SetLanPort(int port)
    {
        if (!LanConfig.IsValidPort(port))
            return;
        if (ListenPort == port && JoinPort == port
            && (ListenPortEntry == null || ListenPortEntry.Value == port)
            && (JoinPortEntry == null || JoinPortEntry.Value == port))
            return;
        _syncingPorts = true;
        try
        {
            ListenPort = port;
            JoinPort = port;
            if (ListenPortEntry != null)
                ListenPortEntry.Value = port;
            if (JoinPortEntry != null)
                JoinPortEntry.Value = port;
        }
        finally
        {
            _syncingPorts = false;
        }
    }

    private static int NormalizePortEntry(ConfigEntry<int> entry)
    {
        int port = LanConfig.NormalizePort(entry.Value);
        if (entry.Value != port)
            entry.Value = port;
        return port;
    }
}

internal static class PluginInfo
{
    public const string GUID = "com.satmlanip";
    public const string Name = "SatmLanIp";
    public const string Version = "1.0.3";
}
