using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fusion;
using Fusion.Sockets;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine.SceneManagement;
using Unsafe = System.Runtime.CompilerServices.Unsafe;

namespace SatmLanIp;

/// <summary>
/// Drive stock Fusion Host/Client onto the LAN IP the operator typed.
/// IL2CPP Nullable&lt;NetAddress&gt; layout writes for Direct bind/connect.
/// </summary>
[HarmonyPatch]
internal static class FusionLanPatches
{
    private static FieldInfo _cpaFieldInfo;
    private static FieldInfo _addrFieldInfo;
    private static FieldInfo _nullableHasValueFieldInfo;
    private static FieldInfo _nullableValueFieldInfo;
    private static int _objectHeader = -1;
    private static int _cpaOffset = -1;
    private static int _addrOffset = -1;
    private static int _initAddrOffset = -1;
    private static int _initPubOffset = -1;
    private static int _hasValueRel = -1;
    private static int _valueRel = -1;
    private static bool _loggedLayout;
    private static int _connectLogs;
    private static FieldInfo _initAddrFieldInfo;
    private static FieldInfo _initPubFieldInfo;
    private static bool _menuLoadSent;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.StartGame), new Type[] { typeof(StartGameArgs) })]
    private static void StartGamePrefix(StartGameArgs args)
    {
        if (!LanMatch.AllowFusionStart || args == null)
            return;

        _menuLoadSent = false;
        try
        {
            LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
            bool host = s != null && s.IsHost;

            if (args.GameMode == GameMode.Single)
            {
                args.GameMode = host ? GameMode.Host : GameMode.Client;
                args.SessionName = LanMatch.SessionName;
                Plugin.LogSrc.LogInfo(
                    "[SatmLanIp] fusion_rewrite Single->" + args.GameMode + " session=" + LanMatch.SessionName);
            }

            args.DisableNATPunchthrough = true;

            ushort bindPort = LanFusionStart.HostBindPort(Plugin.ListenPort);

            if (LanFusionStart.ShouldBindHostAddress(host)
                && (args.GameMode == GameMode.Host || args.GameMode == GameMode.Server
                    || args.GameMode == GameMode.AutoHostOrClient))
            {
                NetAddress bind = NetAddress.Any(bindPort);
                string bindSt = WriteNullableNetAddress(args, isCustomPublic: false, bind);
                Plugin.LogSrc.LogInfo("[SatmLanIp] fusion_bind :" + bindPort + " " + bindSt);

                string ip = HostLanIp();
                if (ip.Length > 0)
                {
                    NetAddress cpa = NetAddress.CreateFromIpPort(ip, bindPort);
                    string st = WriteNullableNetAddress(args, isCustomPublic: true, cpa);
                    Plugin.LogSrc.LogInfo("[SatmLanIp] fusion_cpa " + ip + ":" + bindPort + " " + st);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] fusion_StartGame fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.Connect),
        new Type[] { typeof(NetAddress), typeof(Il2CppStructArray<byte>), typeof(Il2CppStructArray<byte>) })]
    private static void ConnectPrefix(ref NetAddress address)
    {
        if (!Plugin.IsActive)
            return;
        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        if (s == null || s.IsHost)
            return;

        string ip = ClientHostIp();
        if (ip.Length == 0)
            return;

        string before = SafeAddr(address);
        // Always force Fusion port (LAN+1). Do not trust stock/addr port — it may still be ListenPort.
        string ipResolved = LanFusionStart.ResolveClientConnectIp(ip);
        ushort port = LanFusionStart.HostBindPort(Plugin.JoinPort);
        address = NetAddress.CreateFromIpPort(ipResolved, port);
        _connectLogs++;
        if (_connectLogs <= 8 || (_connectLogs % 20) == 0)
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] fusion_connect " + before + " -> " + ipResolved + ":" + port
                + (ipResolved != ip ? " (loopback same-PC)" : ""));
    }

    /// <summary>
    /// LAN Direct has no Photon leave notify. Stock Shutdown waits ConnectionShutdownTime
    /// (host stuck on 退出游戏?, client OnDisconnectedFromServer: Timeout). Force only
    /// while a LAN match is running — never stock Photon / menu StartGame.
    /// </summary>
    internal static bool ShouldForceLanFusionShutdown(bool enabled, bool matchActive, LanState state)
    {
        if (!enabled || !matchActive)
            return false;
        return state == LanState.Connected || state == LanState.Listen || state == LanState.Drop;
    }

    private static bool ShouldForceLanFusionShutdown()
    {
        if (!Plugin.Enabled)
            return false;
        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        if (s == null)
            return false;
        return ShouldForceLanFusionShutdown(true, s.MatchActive, s.State);
    }

    internal static bool ShouldLoadMenuBeforeDetach(string scene, bool matchActive)
    {
        if (!matchActive || string.IsNullOrEmpty(scene))
            return false;
        return !LanMatch.IsMenuSceneName(scene);
    }

    private static void BeginLanSessionExit(string via)
    {
        if (!ShouldForceLanFusionShutdown())
            return;
        Plugin.Transport?.NotifyMatchLeaving();
        Plugin.LogSrc.LogInfo("[SatmLanIp] lan_session_exit via=" + via);
        LoadMenuNow(via);
    }

    internal static void LoadMenuNow(string via)
    {
        if (_menuLoadSent)
            return;
        string scene;
        try { scene = SceneManager.GetActiveScene().name ?? ""; }
        catch { return; }
        if (LanMatch.IsMenuSceneName(scene))
            return;
        _menuLoadSent = true;
        try
        {
            Plugin.LogSrc.LogInfo("[SatmLanIp] load MainMenu via=" + via + " scene=" + scene);
            SceneManager.LoadScene("MainMenu");
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] load MainMenu fail " + ex.GetType().Name + ": " + ex.Message);
            _menuLoadSent = false;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.Shutdown),
        new Type[] { typeof(bool), typeof(ShutdownReason), typeof(bool) })]
    private static void ShutdownPrefix(ref bool forceShutdownProcedure)
    {
        if (!ShouldForceLanFusionShutdown())
            return;
        if (!forceShutdownProcedure)
        {
            forceShutdownProcedure = true;
            Plugin.LogSrc.LogInfo("[SatmLanIp] fusion_shutdown force=true (LAN match)");
        }
        BeginLanSessionExit("Shutdown");
        LoadMenuNow("Shutdown");
    }

    internal static void ApplyLeavePatches(Harmony harmony)
    {
        if (harmony == null)
            return;
        int n = PatchAllNamed(harmony, typeof(FusionNetworkManager), "LeaveGame", nameof(LeaveGamePrefix));
        n += PatchAllNamed(harmony, typeof(PlatformManager_Steam), "HandleEndSessionReturn", nameof(EndSessionPrefix));
        Plugin.LogSrc.LogInfo("[SatmLanIp] fusion_leave patches count~=" + n);
    }

    private static int PatchAllNamed(Harmony harmony, Type type, string name, string prefix)
    {
        if (type == null)
            return 0;
        int n = 0;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo m = methods[i];
            if (m == null || m.Name != name)
                continue;
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(FusionLanPatches), prefix));
            n++;
        }
        return n;
    }

    private static void LeaveGamePrefix()
    {
        BeginLanSessionExit("LeaveGame");
    }

    private static void EndSessionPrefix()
    {
        BeginLanSessionExit("HandleEndSessionReturn");
    }

    internal static void LogPatchStatus(Harmony harmony)
    {
        int n = 0;
        foreach (var p in harmony.GetPatchedMethods())
        {
            if (p.Name == "StartGame" || p.Name == "Connect" || p.Name == "Shutdown"
                || p.Name == "LeaveGame" || p.Name == "HandleEndSessionReturn")
                n++;
        }
        Plugin.LogSrc.LogInfo("[SatmLanIp] Harmony FusionLan patches StartGame/Connect/Shutdown/Leave count~=" + n);
    }

    internal static void SelfCheck()
    {
        if (ShouldForceLanFusionShutdown(true, true, LanState.Connected) == false
            || ShouldForceLanFusionShutdown(true, true, LanState.Listen) == false
            || ShouldForceLanFusionShutdown(true, true, LanState.Drop) == false)
            throw new InvalidOperationException("SatmLanIp force shutdown during LAN match");
        if (ShouldForceLanFusionShutdown(false, true, LanState.Connected)
            || ShouldForceLanFusionShutdown(true, false, LanState.Connected)
            || ShouldForceLanFusionShutdown(true, true, LanState.Idle)
            || ShouldForceLanFusionShutdown(true, true, LanState.Connecting))
            throw new InvalidOperationException("SatmLanIp force shutdown must stay LAN-match only");
        if (!ShouldLoadMenuBeforeDetach("Game", true)
            || ShouldLoadMenuBeforeDetach("MainMenu", true)
            || ShouldLoadMenuBeforeDetach("Game", false)
            || ShouldLoadMenuBeforeDetach("", true))
            throw new InvalidOperationException("SatmLanIp load menu before detach gate");
    }

    internal static string ClientJoinIp() => ClientHostIp();

    internal static unsafe void WriteInitArgsAddress(
        NetworkRunnerInitializeArgs args, bool isPublic, NetAddress net)
    {
        if (args == null)
            return;
        EnsureInitLayout();
        EnsureLayout(net);
        int fieldOff = isPublic ? _initPubOffset : _initAddrOffset;
        if (fieldOff < 0)
            return;
        IntPtr argsPtr = IL2CPP.Il2CppObjectBaseToPtrNotNull(args);
        byte* basePtr = (byte*)argsPtr + fieldOff;
        *(bool*)(basePtr + _hasValueRel) = true;
        Unsafe.Write(basePtr + _valueRel, net);
        try
        {
            var box = BuildBoxedNullable(net);
            if (isPublic)
                args.PublicAddress = box;
            else
                args.Address = box;
        }
        catch
        {
            /* raw write above is enough for Initialize */
        }
    }

    private static void EnsureInitLayout()
    {
        if (_initAddrOffset >= 0 && _initPubOffset >= 0)
            return;
        _initAddrFieldInfo ??= AccessTools.Field(typeof(NetworkRunnerInitializeArgs), "NativeFieldInfoPtr_Address")
            ?? throw new MissingFieldException(typeof(NetworkRunnerInitializeArgs).FullName, "NativeFieldInfoPtr_Address");
        _initPubFieldInfo ??= AccessTools.Field(typeof(NetworkRunnerInitializeArgs), "NativeFieldInfoPtr_PublicAddress")
            ?? throw new MissingFieldException(typeof(NetworkRunnerInitializeArgs).FullName, "NativeFieldInfoPtr_PublicAddress");
        _initAddrOffset = (int)IL2CPP.il2cpp_field_get_offset((IntPtr)_initAddrFieldInfo.GetValue(null));
        _initPubOffset = (int)IL2CPP.il2cpp_field_get_offset((IntPtr)_initPubFieldInfo.GetValue(null));
    }

    private static string HostLanIp()
    {
        var ips = LanLocalIp.ListIPv4();
        return ips.Count > 0 ? ips[0] : "";
    }

    private static string ClientHostIp()
    {
        string ip = (Plugin.JoinAddress ?? "").Trim();
        int colon = ip.IndexOf(':');
        if (colon > 0)
            ip = ip.Substring(0, colon);
        return ip;
    }

    private static string SafeAddr(NetAddress addr)
    {
        try { return addr.ToString(); }
        catch { return "(addr)"; }
    }

    private static unsafe string WriteNullableNetAddress(StartGameArgs args, bool isCustomPublic, NetAddress net)
    {
        EnsureLayout(net);
        IntPtr argsPtr = IL2CPP.Il2CppObjectBaseToPtrNotNull(args);
        int fieldOff = isCustomPublic ? _cpaOffset : _addrOffset;
        if (fieldOff < 0)
            return "no-field";

        byte* basePtr = (byte*)argsPtr + fieldOff;
        *(bool*)(basePtr + _hasValueRel) = true;
        Unsafe.Write(basePtr + _valueRel, net);

        try
        {
            var box = BuildBoxedNullable(net);
            if (isCustomPublic)
                args.CustomPublicAddress = box;
            else
                args.Address = box;
        }
        catch (Exception ex)
        {
            return "setter-fail:" + ex.GetType().Name;
        }

        if (!*(bool*)(basePtr + _hasValueRel))
            return "raw-false";
        try
        {
            return "ok-raw:" + Unsafe.Read<NetAddress>(basePtr + _valueRel).ToString();
        }
        catch
        {
            return "ok-raw";
        }
    }

    private static unsafe Il2CppSystem.Nullable<NetAddress> BuildBoxedNullable(NetAddress net)
    {
        var box = new Il2CppSystem.Nullable<NetAddress>(net);
        IntPtr boxPtr = IL2CPP.Il2CppObjectBaseToPtrNotNull(box);
        byte* baseVal = (byte*)IL2CPP.il2cpp_object_unbox(boxPtr);
        *(bool*)(baseVal + _hasValueRel) = true;
        Unsafe.Write(baseVal + _valueRel, net);
        return box;
    }

    private static unsafe void EnsureLayout(NetAddress probeNet)
    {
        if (_cpaOffset >= 0 && _addrOffset >= 0 && _hasValueRel >= 0 && _valueRel >= 0 && _objectHeader >= 0)
            return;

        _cpaFieldInfo ??= AccessTools.Field(typeof(StartGameArgs), "NativeFieldInfoPtr_CustomPublicAddress")
            ?? throw new MissingFieldException(typeof(StartGameArgs).FullName, "NativeFieldInfoPtr_CustomPublicAddress");
        _addrFieldInfo ??= AccessTools.Field(typeof(StartGameArgs), "NativeFieldInfoPtr_Address")
            ?? throw new MissingFieldException(typeof(StartGameArgs).FullName, "NativeFieldInfoPtr_Address");
        _nullableHasValueFieldInfo ??= AccessTools.Field(typeof(Il2CppSystem.Nullable<NetAddress>), "NativeFieldInfoPtr_hasValue")
            ?? throw new MissingFieldException("Il2CppSystem.Nullable<NetAddress>", "NativeFieldInfoPtr_hasValue");
        _nullableValueFieldInfo ??= AccessTools.Field(typeof(Il2CppSystem.Nullable<NetAddress>), "NativeFieldInfoPtr_value")
            ?? throw new MissingFieldException("Il2CppSystem.Nullable<NetAddress>", "NativeFieldInfoPtr_value");

        IntPtr cpaField = (IntPtr)_cpaFieldInfo.GetValue(null);
        IntPtr addrField = (IntPtr)_addrFieldInfo.GetValue(null);
        IntPtr hvField = (IntPtr)_nullableHasValueFieldInfo.GetValue(null);
        IntPtr valField = (IntPtr)_nullableValueFieldInfo.GetValue(null);

        _cpaOffset = (int)IL2CPP.il2cpp_field_get_offset(cpaField);
        _addrOffset = (int)IL2CPP.il2cpp_field_get_offset(addrField);
        int hvRaw = (int)IL2CPP.il2cpp_field_get_offset(hvField);
        int valRaw = (int)IL2CPP.il2cpp_field_get_offset(valField);

        var probe = new Il2CppSystem.Nullable<NetAddress>(probeNet);
        IntPtr probeObj = IL2CPP.Il2CppObjectBaseToPtrNotNull(probe);
        IntPtr probeUnbox = IL2CPP.il2cpp_object_unbox(probeObj);
        _objectHeader = (int)((long)probeUnbox - (long)probeObj);
        if (_objectHeader < 0 || _objectHeader > 64)
            _objectHeader = 16;

        _hasValueRel = hvRaw >= _objectHeader ? hvRaw - _objectHeader : hvRaw;
        _valueRel = valRaw >= _objectHeader ? valRaw - _objectHeader : valRaw;

        if (!_loggedLayout)
        {
            _loggedLayout = true;
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] fusion_layout header=" + _objectHeader + " cpa=" + _cpaOffset
                + " hasValueRel=" + _hasValueRel + " valueRel=" + _valueRel);
        }

        if (_hasValueRel < 0 || _valueRel <= _hasValueRel)
            throw new InvalidOperationException("Bad Nullable layout");
    }
}
