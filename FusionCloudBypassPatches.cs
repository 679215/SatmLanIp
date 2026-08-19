using System;
using System.Reflection;
using System.Text;
using Fusion;
using Fusion.Async;
using Fusion.Sockets;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Runtime.CompilerServices;
using Il2CppSystem.Threading;
using Il2CppSystem.Threading.Tasks;

namespace SatmLanIp;

/// <summary>
/// LAN Fusion Direct path hooks: skip cloud Connect/EnterRoom state machines on IL2CPP,
/// park ConnectToCloud + fake EnterRoom; pending _startGameOperation then Initialize+Pump
/// before SetResult(Ok); scoped IsCloudReady only during KickInitialize; IP NetAddress keep
/// ActorId=-1 (Direct); LocalPlayer lie on Simulation getters; UniqueId must be 8 bytes +
/// RegisterUniqueIdPlayerMapping; never Harmony Assert.Check (INT3 pad crash);
/// CreateCloudSocket→NetSocketNative; NetworkInit force Native; Host Accept OnConnectionRequest
/// (trusted LAN/VPN only); peer UniqueId premap; same-PC Connect→loopback; EncryptionConfig
/// off for Direct without Photon keys; Fusion binds ListenPort+1 while room UDP stays on ListenPort.
/// </summary>
[HarmonyPatch]
internal static class FusionCloudBypassPatches
{
    private const int AsyncCompleted = -2;
    private static int _connectLogs;
    private static int _joinLogs;
    private static int _cloudStartLogs;
    private static int _readyLogs;
    private static bool _forceCloudReady;
    private static CloudServices._ConnectToCloud_d__68 _parkedConnect;
    private static bool _pumpLogged;
    private static Task<bool> _pendingInit;
    private static AsyncOperationHandler<ShutdownReason> _pendingInitOp;
    private static NetworkRunner _pendingInitRunner;

    private static int _fromActorLogs;
    private static int _localPlayerLogs;
    private static int _createCloudSocketHits;
    private static int _nativeBindLogs;
    private static int _relayBindLogs;
    private static int _nativeRecvHits;
    private static int _nativeRecvBytes;
    private static int _nativeSendHits;
    private static int _setupEncHits;
    private static int _nativeRecvCalls;
    private static int _nativeSendCalls;
    private static int _hybridRecvCalls;
    private static int _hybridSendCalls;
    private static int _networkInitSwaps;
    private static int _connReqLogs;
    private static int _handleConnectLogs;
    private static int _allocConnLogs;
    private static int _hexDumps;

    internal static void Reset()
    {
        _connectLogs = 0;
        _joinLogs = 0;
        _cloudStartLogs = 0;
        _readyLogs = 0;
        _fromActorLogs = 0;
        _localPlayerLogs = 0;
        _createCloudSocketHits = 0;
        _nativeBindLogs = 0;
        _relayBindLogs = 0;
        _nativeRecvHits = 0;
        _nativeRecvBytes = 0;
        _nativeSendHits = 0;
        _setupEncHits = 0;
        _nativeRecvCalls = 0;
        _nativeSendCalls = 0;
        _hybridRecvCalls = 0;
        _hybridSendCalls = 0;
        _networkInitSwaps = 0;
        _connReqLogs = 0;
        _handleConnectLogs = 0;
        _allocConnLogs = 0;
        _hexDumps = 0;
        _forceCloudReady = false;
        _parkedConnect = null;
        _pumpLogged = false;
        _pendingInit = null;
        _pendingInitOp = null;
        _pendingInitRunner = null;
    }

    internal static void Pump()
    {
        if (!LanMatch.AllowFusionStart)
            return;

        PumpParkedConnect();
        PumpPendingInitialize();
    }

    private static void PumpParkedConnect()
    {
        if (_parkedConnect == null)
            return;

        CloudServices._ConnectToCloud_d__68 parked = _parkedConnect;
        _parkedConnect = null;
        try
        {
            CompleteVoidBuilder(parked.__t__builder, v => parked.__t__builder = v);
            parked.__1__state = AsyncCompleted;
            if (!_pumpLogged)
            {
                _pumpLogged = true;
                Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion ConnectToCloud parked→complete");
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion pump fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void PumpPendingInitialize()
    {
        if (_pendingInit == null || _pendingInitOp == null)
            return;
        if (!_pendingInit.IsCompleted)
            return;

        AsyncOperationHandler<ShutdownReason> op = _pendingInitOp;
        NetworkRunner runner = _pendingInitRunner;
        Task<bool> init = _pendingInit;
        _pendingInit = null;
        _pendingInitOp = null;
        _pendingInitRunner = null;
        _forceCloudReady = false;

        try
        {
            bool ok = false;
            try { ok = init.Result; }
            catch (Exception ex)
            {
                Plugin.LogSrc.LogWarning(
                    "[SatmLanIp] lan_fusion Initialize threw " + ex.GetType().Name + ": " + ex.Message);
            }

            bool running = false;
            try { if (runner != null) running = runner.IsRunning; }
            catch { /* tear-down */ }

            op.SetResult(ok ? ShutdownReason.Ok : ShutdownReason.Error);
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion Initialize done ok=" + ok + " IsRunning=" + running
                + " LocalPlayer=" + SafeLocalPlayer(runner)
                + " Mode=" + SafeMode(runner)
                + " Socket=" + SafeSocket(runner)
                + " createCloudHits=" + _createCloudSocketHits
                + " nativeBind=" + _nativeBindLogs
                + " relayBind=" + _relayBindLogs
                + " enc=" + SafeEncryption(runner)
                + " initSwap=" + _networkInitSwaps
                + " nRecv=" + _nativeRecvCalls + "/" + _nativeRecvHits
                + " hRecv=" + _hybridRecvCalls);

            if (ok && running)
            {
                EnsureSimulationMode(runner);
                Il2CppStructArray<byte> uid = EnsurePlayerMapping(runner);
                TryClientConnectAfterInit(runner, uid);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion Initialize complete fail " + ex.GetType().Name + ": " + ex.Message);
            try { op.SetResult(ShutdownReason.Error); }
            catch { /* already completed */ }
        }
    }

    private static string SafeLocalPlayer(NetworkRunner runner)
    {
        try
        {
            if (runner == null)
                return "null";
            PlayerRef p = runner.LocalPlayer;
            return "idx=" + p.PlayerId + " real=" + p.IsRealPlayer;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    private static string SafeMode(NetworkRunner runner)
    {
        try
        {
            Simulation sim = runner != null ? runner._simulation : null;
            return sim != null ? sim.Mode.ToString() : "no-sim";
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    private static string SafeSocket(NetworkRunner runner)
    {
        try
        {
            Simulation sim = runner != null ? runner._simulation : null;
            INetSocket sock = sim != null ? sim._netSocket : null;
            return DescribeSocket(sock);
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    // Host path needs SimulationModes.Host (2) so IsPlayer==true; cloud skip may leave Server(1).
    private static void EnsureSimulationMode(NetworkRunner runner)
    {
        try
        {
            Simulation sim = runner._simulation;
            if (sim == null)
                return;
            SimulationModes want = ResolveLanActorId() == 1 ? SimulationModes.Host : SimulationModes.Client;
            SimulationModes cur = sim.Mode;
            if (cur == want)
            {
                Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion Mode ok=" + cur);
                return;
            }
            var field = AccessTools.Field(typeof(Simulation), "_mode");
            if (field == null)
            {
                Plugin.LogSrc.LogWarning(
                    "[SatmLanIp] lan_fusion Mode=" + cur + " want=" + want + " (no _mode field)");
                return;
            }
            field.SetValue(sim, want);
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion Mode " + cur + "→" + want);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion Mode fix fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static Il2CppStructArray<byte> MakeUniqueId(int actorId)
    {
        // Fusion.Simulation.RegisterUniqueIdPlayerMapping keys Dictionary<ulong,…>
        // and Asserts id.Length == sizeof(ulong). 16-byte GUID → AssertException.
        // Port bytes must match on Host+Client (use ListenPort; both cfg default 37241).
        int port = Plugin.ListenPort;
        if (port < 1 || port > 65535)
            port = 37241;
        byte[] raw = new byte[8];
        raw[0] = (byte)'S';
        raw[1] = (byte)'A';
        raw[2] = (byte)'T';
        raw[3] = (byte)'M';
        raw[4] = (byte)actorId;
        raw[5] = (byte)(port & 0xFF);
        raw[6] = (byte)((port >> 8) & 0xFF);
        raw[7] = 0xA7;
        var arr = new Il2CppStructArray<byte>(raw.Length);
        for (int i = 0; i < raw.Length; i++)
            arr[i] = raw[i];
        return arr;
    }

    private static Il2CppStructArray<byte> EnsurePlayerMapping(NetworkRunner runner)
    {
        int actorId = ResolveLanActorId();
        Il2CppStructArray<byte> uid = MakeUniqueId(actorId);
        try
        {
            Simulation sim = runner._simulation;
            if (sim == null)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion mapping skip (no Simulation)");
                return uid;
            }
            PlayerRef pref = PlayerRef.FromIndex(actorId);
            sim.RegisterUniqueIdPlayerMapping(actorId, uid, pref);
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion RegisterUniqueIdPlayerMapping actor=" + actorId
                + " player=" + pref.PlayerId + " LocalPlayer=" + SafeLocalPlayer(runner));

            // Host must already know Client UniqueId before OnConnected, or Fusion throws
            // "no player mapping for <ulong>" and Shutdown Reason=Error (1.5.23 disconnect).
            if (actorId == 1)
            {
                Il2CppStructArray<byte> peerUid = MakeUniqueId(2);
                sim.RegisterUniqueIdPlayerMapping(2, peerUid, PlayerRef.FromIndex(2));
                Plugin.LogSrc.LogInfo(
                    "[SatmLanIp] lan_fusion RegisterUniqueIdPlayerMapping actor=2 (peer premap)");
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion mapping fail " + ex.GetType().Name + ": " + ex.Message);
        }
        return uid;
    }

    private static void TryClientConnectAfterInit(NetworkRunner runner, Il2CppStructArray<byte> uniqueId)
    {
        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        if (s == null || s.IsHost)
            return;
        try
        {
            string ip = FusionLanPatches.ClientJoinIp();
            if (ip.Length == 0)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion client Connect skip (no JoinAddress)");
                return;
            }
            string destIp = LanFusionStart.ResolveClientConnectIp(ip);
            ushort port = LanFusionStart.HostBindPort(Plugin.JoinPort);
            NetAddress dest = NetAddress.CreateFromIpPort(destIp, port);
            runner.Connect(dest, null, uniqueId ?? MakeUniqueId(ResolveLanActorId()));
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion client Connect " + destIp + ":" + port + " +UniqueId"
                + (destIp != ip ? " via=" + ip : ""));
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion client Connect fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static void LogPatchStatus(Harmony harmony)
    {
        int n = 0;
        foreach (var p in harmony.GetPatchedMethods())
        {
            string nme = p.Name;
            if (nme == "MoveNext" && p.DeclaringType != null
                && (p.DeclaringType.Name.Contains("ConnectToCloud")
                    || p.DeclaringType.Name.Contains("StartGameModeCloud")
                    || p.DeclaringType.Name.Contains("_Join_d__")))
                n++;
        }
        Plugin.LogSrc.LogInfo("[SatmLanIp] Harmony FusionCloudBypass MoveNext patches count~=" + n);
    }

    // v1.5.13 soft-pass Harmony on Assert.Check(bool,string) → APPCRASH GameAssembly
    // 0x80000003 BREAKPOINT at RVA 0x822496 (INT3 padding after Check). Do NOT re-patch Assert.

    // NetworkRunner.get_LocalPlayer only jmp's to Simulation.LocalPlayer; BeforeFirstTick
    // reads Simulation.Server.LocalPlayer directly — Runner Postfix never runs there.
    internal static void ApplySimulationLocalPlayerPatches(Harmony harmony)
    {
        string[] names = { "Fusion.Simulation+Server", "Fusion.Simulation+Client" };
        for (int i = 0; i < names.Length; i++)
        {
            Type ty = AccessTools.TypeByName(names[i]);
            if (ty == null)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion type missing " + names[i]);
                continue;
            }
            var getter = AccessTools.PropertyGetter(ty, "LocalPlayer");
            if (getter == null)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion LocalPlayer getter missing " + names[i]);
                continue;
            }
            harmony.Patch(getter,
                postfix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(SimLocalPlayerPostfix)));
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion patched " + names[i] + ".LocalPlayer");
        }
    }

    private static void SimLocalPlayerPostfix(ref PlayerRef __result)
    {
        if (!LanMatch.AllowFusionStart)
            return;
        if (__result.IsRealPlayer)
            return;
        __result = PlayerRef.FromIndex(ResolveLanActorId());
        _localPlayerLogs++;
        if (_localPlayerLogs <= 8)
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion Sim.LocalPlayer→FromIndex(" + ResolveLanActorId() + ")");
    }

    // Without Photon room, Simulation leaves LocalPlayer as None (IsRealPlayer false → Assert).
    // PlayerRef.IsRealPlayer is (_index > 0); FromIndex(1/2) matches Host/Client actor ids.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.LocalPlayer), MethodType.Getter)]
    private static void LocalPlayerPostfix(ref PlayerRef __result)
    {
        SimLocalPlayerPostfix(ref __result);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkObject), nameof(NetworkObject.AssignInputAuthority))]
    private static void AssignInputAuthorityPrefix(ref PlayerRef player)
    {
        if (!LanMatch.AllowFusionStart)
            return;
        if (player.IsRealPlayer)
            return;
        player = PlayerRef.FromIndex(ResolveLanActorId());
    }

    // Bind/NetPeer during Initialize calls FromActorId(LocalPlayer.ActorNumber=-1).
    // Only rewrite in the init window (_forceCloudReady). After init, leave -1 alone —
    // FromActorId(1) sets IsRelayAddr and poisons Direct Connect handshake (HOST_ALONE + Timeout).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetAddress), nameof(NetAddress.FromActorId))]
    private static void FromActorIdPrefix(ref int actorId)
    {
        if (!LanMatch.AllowFusionStart || !_forceCloudReady)
            return;
        if (actorId >= 0)
            return;
        int fixedId = ResolveLanActorId();
        _fromActorLogs++;
        if (_fromActorLogs <= 6)
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion FromActorId " + actorId + "→" + fixedId + " (init-only)");
        actorId = fixedId;
    }

    private static string SafeEncryption(NetworkRunner runner)
    {
        try
        {
            NetworkProjectConfig cfg = runner != null ? runner.Config : null;
            EncryptionConfig enc = cfg != null ? cfg.EncryptionConfig : null;
            if (enc == null)
                return "null";
            return enc.EnableEncryption ? "on" : "off";
        }
        catch { return "?"; }
    }

    private static void TryDisableEncryption(NetworkRunner runner)
    {
        try
        {
            NetworkProjectConfig cfg = runner != null ? runner.Config : null;
            EncryptionConfig enc = cfg != null ? cfg.EncryptionConfig : null;
            if (enc == null)
                return;
            if (!enc.EnableEncryption)
                return;
            enc.EnableEncryption = false;
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion EncryptionConfig.EnableEncryption→false");
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion enc disable fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FusionNetworkManager), nameof(FusionNetworkManager.OnConnectFailed))]
    private static void OnConnectFailedPostfix(NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        if (!LanMatch.AllowFusionStart)
            return;
        Plugin.LogSrc.LogWarning(
            "[SatmLanIp] lan_fusion OnConnectFailed " + reason
            + " remote=" + SafeAddrLog(remoteAddress)
            + " nRecv=" + _nativeRecvCalls + "/" + _nativeRecvHits
            + " nSend=" + _nativeSendCalls + "/" + _nativeSendHits
            + " hRecv=" + _hybridRecvCalls + " hSend=" + _hybridSendCalls
            + " initSwap=" + _networkInitSwaps
            + " handleConnect=" + _handleConnectLogs
            + " alloc=" + _allocConnLogs
            + " connReq=" + _connReqLogs);
    }

    // Stock kicks local player 1 on MainMenu ("unexpected join…kicking!") which races LAN Host
    // init. Skip the callback while still on menu; MoveToScene Game handles real spawn.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FusionNetworkManager), nameof(FusionNetworkManager.OnPlayerJoined))]
    private static bool OnPlayerJoinedPrefix(NetworkRunner runner, PlayerRef player)
    {
        if (!LanMatch.AllowFusionStart)
            return true;
        try
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
            if (scene == "MainMenu" || scene == "Lobby")
            {
                Plugin.LogSrc.LogInfo(
                    "[SatmLanIp] lan_fusion skip OnPlayerJoined scene=" + scene
                    + " player=" + player.PlayerId + " (avoid menu kick)");
                return false;
            }
        }
        catch { /* fall through to stock */ }
        return true;
    }

    // Host must Accept pending ConnectRequest on LAN Direct (trusted LAN/VPN only — no auth token).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(FusionNetworkManager), nameof(FusionNetworkManager.OnConnectRequest))]
    private static void OnConnectRequestPostfix(
        NetworkRunner runner,
        NetworkRunnerCallbackArgs.ConnectRequest request,
        Il2CppStructArray<byte> token)
    {
        if (!LanMatch.AllowFusionStart || request == null)
            return;
        try
        {
            if (Plugin.VerboseNetworkLog)
            {
                Plugin.LogSrc.LogInfo(
                    "[SatmLanIp] lan_fusion OnConnectRequest remote=" + request.RemoteAddress
                    + " result=" + (request.Result.HasValue ? request.Result.Value.ToString() : "unset"));
            }
            if (!request.Result.HasValue)
            {
                request.Accept();
                if (Plugin.VerboseNetworkLog)
                    Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion OnConnectRequest Accept()");
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion OnConnectRequest fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // Forced IsCloudReady may call CreateCloudSocket (Relay). Attribute patch did not fire in
    // 1.5.17 — apply explicitly via AccessTools. Also probe Bind/Recv/Send on Native/Hybrid/Relay.
    // 1.5.21: createCloudHits=0 + zero Native.Receive logs → traffic is on Hybrid, not Native.
    // Force NetworkInit onto NetSocketNative for LAN Direct.
    internal static void ApplySocketPatches(Harmony harmony)
    {
        var create = AccessTools.Method(typeof(NetworkRunner), "CreateCloudSocket");
        if (create == null)
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion CreateCloudSocket method missing");
        else
        {
            harmony.Patch(create,
                prefix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(CreateCloudSocketPrefix)));
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion CreateCloudSocket patched (AccessTools)");
        }

        var netInit = AccessTools.Method(typeof(Simulation), "NetworkInit");
        if (netInit != null)
        {
            harmony.Patch(netInit,
                prefix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(NetworkInitPrefix)));
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion Simulation.NetworkInit→Native patched");
        }
        else
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion Simulation.NetworkInit missing");

        var nativeBind = AccessTools.Method(typeof(NetSocketNative), "Bind");
        if (nativeBind != null)
        {
            harmony.Patch(nativeBind,
                postfix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(NativeBindPostfix)));
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion NetSocketNative.Bind probed");
        }
        else
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion NetSocketNative.Bind missing");

        var nativeRecv = AccessTools.Method(typeof(NetSocketNative), "Receive");
        if (nativeRecv != null)
        {
            harmony.Patch(nativeRecv,
                prefix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(NativeReceivePrefix)),
                postfix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(NativeReceivePostfix)));
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion NetSocketNative.Receive probed");
        }
        else
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion NetSocketNative.Receive missing");

        var nativeSend = AccessTools.Method(typeof(NetSocketNative), "Send");
        if (nativeSend != null)
        {
            harmony.Patch(nativeSend,
                prefix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(NativeSendPrefix)),
                postfix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(NativeSendPostfix)));
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion NetSocketNative.Send probed");
        }

        var nativeSetup = AccessTools.Method(typeof(NetSocketNative), "SetupEncryption");
        if (nativeSetup != null)
        {
            harmony.Patch(nativeSetup,
                postfix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(NativeSetupEncPostfix)));
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion NetSocketNative.SetupEncryption probed");
        }

        Type hybridTy = AccessTools.TypeByName("Fusion.Sockets.NetSocketHybrid");
        if (hybridTy != null)
        {
            var hRecv = AccessTools.Method(hybridTy, "Receive");
            if (hRecv != null)
            {
                harmony.Patch(hRecv,
                    prefix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(HybridReceivePrefix)));
                Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion NetSocketHybrid.Receive probed");
            }
            var hSend = AccessTools.Method(hybridTy, "Send");
            if (hSend != null)
            {
                harmony.Patch(hSend,
                    prefix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(HybridSendPrefix)));
                Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion NetSocketHybrid.Send probed");
            }
        }
        else
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion NetSocketHybrid type missing");

        Type relayTy = AccessTools.TypeByName("Fusion.Sockets.NetSocketRelay");
        var relayBind = relayTy != null ? AccessTools.Method(relayTy, "Bind") : null;
        if (relayBind != null)
        {
            harmony.Patch(relayBind,
                postfix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(RelayBindPostfix)));
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion NetSocketRelay.Bind probed");
        }
        else
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion NetSocketRelay.Bind missing");

        // Peer-layer Accept: Host gets NetCommandConnect (152B) but FusionNetworkManager.OnConnectRequest
        // never fired in 1.5.22 — force Simulation INetPeerGroupCallbacks.OnConnectionRequest → Ok.
        ApplyConnectionRequestPatches(harmony);
        ApplyNetPeerGroupProbes(harmony);
    }

    private static void ApplyConnectionRequestPatches(Harmony harmony)
    {
        int n = 0;
        var simMethods = AccessTools.GetDeclaredMethods(typeof(Simulation));
        foreach (MethodInfo m in simMethods)
        {
            if (m == null || m.Name == null)
                continue;
            if (m.Name.IndexOf("OnConnectionRequest", StringComparison.Ordinal) < 0)
                continue;
            harmony.Patch(m,
                prefix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(SimOnConnectionRequestPrefix)));
            n++;
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion patched Simulation." + m.Name);
        }
        var runnerMethods = AccessTools.GetDeclaredMethods(typeof(NetworkRunner));
        foreach (MethodInfo m in runnerMethods)
        {
            if (m == null || m.Name == null)
                continue;
            if (m.Name.IndexOf("OnConnectionRequest", StringComparison.Ordinal) < 0)
                continue;
            harmony.Patch(m,
                prefix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(RunnerOnConnectionRequestPrefix)));
            n++;
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion patched NetworkRunner." + m.Name);
        }
        if (n == 0)
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion OnConnectionRequest methods missing");
    }

    private static void ApplyNetPeerGroupProbes(Harmony harmony)
    {
        Type groupTy = AccessTools.TypeByName("Fusion.Sockets.NetPeerGroup");
        if (groupTy == null)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion NetPeerGroup type missing");
            return;
        }
        var handle = AccessTools.Method(groupTy, "HandleCommandConnect");
        if (handle != null)
        {
            harmony.Patch(handle,
                prefix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(HandleCommandConnectPrefix)));
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion HandleCommandConnect probed");
        }
        else
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion HandleCommandConnect missing");

        var alloc = AccessTools.Method(groupTy, "AllocateConnection");
        if (alloc != null)
        {
            harmony.Patch(alloc,
                postfix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(AllocateConnectionPostfix)));
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion AllocateConnection probed");
        }
        else
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion AllocateConnection missing");

        var unconn = AccessTools.Method(groupTy, "HandlePacketUnconnected");
        if (unconn != null)
        {
            harmony.Patch(unconn,
                prefix: new HarmonyMethod(typeof(FusionCloudBypassPatches), nameof(HandlePacketUnconnectedPrefix)));
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion HandlePacketUnconnected probed");
        }
    }

    // LAN Host: Accept peer Connect + ensure UniqueId→PlayerRef exists (OnConnected asserts it).
    private static bool SimOnConnectionRequestPrefix(
        Simulation __instance,
        Il2CppStructArray<byte> uniqueid,
        ref OnConnectionRequestReply __result)
    {
        if (!LanMatch.AllowFusionStart)
            return true;
        try
        {
            if (__instance != null && uniqueid != null && uniqueid.Length == 8)
            {
                int actor = uniqueid[4];
                if (actor < 1 || actor > 7)
                    actor = 2;
                __instance.RegisterUniqueIdPlayerMapping(
                    actor, uniqueid, PlayerRef.FromIndex(actor));
                Plugin.LogSrc.LogInfo(
                    "[SatmLanIp] lan_fusion OnConnectionRequest map actor=" + actor
                    + " uid0=" + uniqueid[0] + uniqueid[1] + uniqueid[2] + uniqueid[3]);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion OnConnectionRequest map fail "
                + ex.GetType().Name + ": " + ex.Message);
        }
        __result = OnConnectionRequestReply.Ok;
        _connReqLogs++;
        if (_connReqLogs <= 8)
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion Sim.OnConnectionRequest→Ok #" + _connReqLogs);
        return false;
    }

    private static bool RunnerOnConnectionRequestPrefix(ref OnConnectionRequestReply __result)
    {
        if (!LanMatch.AllowFusionStart)
            return true;
        __result = OnConnectionRequestReply.Ok;
        Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion Runner.OnConnectionRequest→Ok");
        return false;
    }

    private static void HandleCommandConnectPrefix()
    {
        if (!LanMatch.AllowFusionStart)
            return;
        _handleConnectLogs++;
        if (_handleConnectLogs <= 12)
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion HandleCommandConnect #" + _handleConnectLogs);
    }

    private static void AllocateConnectionPostfix()
    {
        if (!LanMatch.AllowFusionStart)
            return;
        _allocConnLogs++;
        if (_allocConnLogs <= 8)
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion AllocateConnection #" + _allocConnLogs);
    }

    private static void HandlePacketUnconnectedPrefix()
    {
        if (!LanMatch.AllowFusionStart)
            return;
        // noisy — only first few
        if (_handleConnectLogs + _allocConnLogs < 4)
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion HandlePacketUnconnected");
    }

    // Swap Hybrid/Relay for pure Native before NetPeer binds — LAN Direct has no Photon keys.
    private static void NetworkInitPrefix(ref INetSocket socket)
    {
        if (!LanMatch.AllowFusionStart)
            return;
        try
        {
            string was = DescribeSocket(socket);
            if (socket != null && socket.TryCast<NetSocketNative>() != null)
            {
                Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion NetworkInit already Native");
                return;
            }
            var native = new NetSocketNative();
            INetSocket iface = native.TryCast<INetSocket>();
            if (iface == null)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion NetworkInit Native cast fail");
                return;
            }
            socket = iface;
            _networkInitSwaps++;
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion NetworkInit " + was + "→Native swap=" + _networkInitSwaps);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion NetworkInit swap fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string DescribeSocket(INetSocket sock)
    {
        if (sock == null)
            return "null";
        try
        {
            if (sock.TryCast<NetSocketNative>() != null)
                return "Native";
            string s = sock.ToString() ?? "";
            if (s.IndexOf("Hybrid", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Hybrid";
            if (s.IndexOf("Relay", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Relay";
            string tn = sock.GetType().Name ?? "";
            if (tn.IndexOf("Hybrid", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Hybrid";
            if (tn.IndexOf("Relay", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Relay";
            return tn.Length > 0 ? tn : s;
        }
        catch { return "?"; }
    }

    private static bool CreateCloudSocketPrefix(ref INetSocket __result)
    {
        if (!LanMatch.AllowFusionStart)
            return true;
        _createCloudSocketHits++;
        try
        {
            var native = new NetSocketNative();
            __result = native.TryCast<INetSocket>();
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion CreateCloudSocket→NetSocketNative ok=" + (__result != null)
                + " hit=" + _createCloudSocketHits);
            return false;
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion CreateCloudSocket native fail "
                + ex.GetType().Name + ": " + ex.Message);
            return true;
        }
    }

    private static void NativeBindPostfix(NetAddress __result)
    {
        if (!LanMatch.AllowFusionStart)
            return;
        _nativeBindLogs++;
        if (Plugin.VerboseNetworkLog && _nativeBindLogs <= 4)
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion NetSocketNative.Bind → " + SafeAddrLog(__result));
    }

    private static void NativeReceivePrefix()
    {
        if (!LanMatch.AllowFusionStart)
            return;
        _nativeRecvCalls++;
        if (Plugin.VerboseNetworkLog && (_nativeRecvCalls <= 8 || (_nativeRecvCalls % 200) == 0))
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion Native.Receive call=" + _nativeRecvCalls);
    }

    private static void NativeReceivePostfix(int __result, IntPtr buffer)
    {
        if (!LanMatch.AllowFusionStart || __result <= 0)
            return;
        _nativeRecvHits++;
        _nativeRecvBytes += __result;
        if (!Plugin.VerboseNetworkLog)
            return;
        if (_hexDumps < 4 && __result == 152 && buffer != IntPtr.Zero)
        {
            _hexDumps++;
            try
            {
                var sb = new StringBuilder(48);
                unsafe
                {
                    byte* p = (byte*)buffer.ToPointer();
                    for (int i = 0; i < 8; i++)
                        sb.Append(p[i].ToString("x2"));
                }
                Plugin.LogSrc.LogInfo(
                    "[SatmLanIp] lan_fusion Native.Receive Connect152 hdr=" + sb
                    + " (want type=05 cmd=01)");
            }
            catch (Exception ex)
            {
                Plugin.LogSrc.LogWarning(
                    "[SatmLanIp] lan_fusion hex dump fail " + ex.GetType().Name);
            }
        }
        if (_nativeRecvHits <= 12 || (_nativeRecvHits % 40) == 0)
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion Native.Receive bytes=" + __result
                + " hits=" + _nativeRecvHits + " total=" + _nativeRecvBytes
                + " handleConnect=" + _handleConnectLogs
                + " alloc=" + _allocConnLogs
                + " connReq=" + _connReqLogs);
    }

    private static void NativeSendPrefix()
    {
        if (!LanMatch.AllowFusionStart)
            return;
        _nativeSendCalls++;
        if (Plugin.VerboseNetworkLog && (_nativeSendCalls <= 8 || (_nativeSendCalls % 200) == 0))
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion Native.Send call=" + _nativeSendCalls);
    }

    private static void NativeSendPostfix(int __result)
    {
        if (!LanMatch.AllowFusionStart || __result <= 0)
            return;
        _nativeSendHits++;
        if (Plugin.VerboseNetworkLog && (_nativeSendHits <= 12 || (_nativeSendHits % 40) == 0))
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion Native.Send bytes=" + __result + " hits=" + _nativeSendHits);
    }

    private static void HybridReceivePrefix()
    {
        if (!LanMatch.AllowFusionStart)
            return;
        _hybridRecvCalls++;
        if (Plugin.VerboseNetworkLog && (_hybridRecvCalls <= 8 || (_hybridRecvCalls % 200) == 0))
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion Hybrid.Receive call=" + _hybridRecvCalls);
    }

    private static void HybridSendPrefix()
    {
        if (!LanMatch.AllowFusionStart)
            return;
        _hybridSendCalls++;
        if (Plugin.VerboseNetworkLog && (_hybridSendCalls <= 8 || (_hybridSendCalls % 200) == 0))
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion Hybrid.Send call=" + _hybridSendCalls);
    }

    private static void NativeSetupEncPostfix()
    {
        if (!LanMatch.AllowFusionStart)
            return;
        _setupEncHits++;
        if (Plugin.VerboseNetworkLog)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion Native.SetupEncryption hit=" + _setupEncHits);
        }
    }

    private static void RelayBindPostfix(NetAddress __result)
    {
        if (!LanMatch.AllowFusionStart)
            return;
        _relayBindLogs++;
        if (Plugin.VerboseNetworkLog && _relayBindLogs <= 4)
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion NetSocketRelay.Bind → " + SafeAddrLog(__result));
    }

    private static string SafeAddrLog(NetAddress a)
    {
        try { return a.ToString(); }
        catch { return "?"; }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CloudServices), nameof(CloudServices.IsCloudReady), MethodType.Getter)]
    private static bool IsCloudReadyPrefix(ref bool __result)
    {
        if (!_forceCloudReady)
            return true;
        __result = true;
        _readyLogs++;
        if (_readyLogs <= 4)
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion IsCloudReady=true (init-scoped)");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.IsCloudReady), MethodType.Getter)]
    private static bool RunnerIsCloudReadyPrefix(ref bool __result)
    {
        if (!_forceCloudReady)
            return true;
        __result = true;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CloudServices._ConnectToCloud_d__68), nameof(CloudServices._ConnectToCloud_d__68.MoveNext))]
    private static bool ConnectToCloudMoveNextPrefix(CloudServices._ConnectToCloud_d__68 __instance)
    {
        if (!LanMatch.AllowFusionStart)
            return true;
        if (__instance.__1__state == AsyncCompleted)
            return true;

        _parkedConnect = __instance;
        _connectLogs++;
        if (_connectLogs <= 4)
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion ConnectToCloud park (force yield)");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CloudServices._Join_d__84), nameof(CloudServices._Join_d__84.MoveNext))]
    private static bool JoinMoveNextPrefix(CloudServices._Join_d__84 __instance)
    {
        if (!LanMatch.AllowFusionStart)
            return true;
        if (__instance.__1__state == AsyncCompleted)
            return true;

        // Finish parked ConnectToCloud before Initialize so StartGameModeCloud await chain is sane.
        PumpParkedConnect();
        // Pending _startGameOperation + real Initialize. StartGameModeCloud awaits the op;
        // Pump SetResult(Ok) only after Initialize finishes — early Ok left Runner not running.
        KickInitializeAfterFakeJoin(__instance.__4__this);

        CompleteVoidBuilder(__instance.__t__builder, v => __instance.__t__builder = v);
        __instance.__1__state = AsyncCompleted;
        _joinLogs++;
        if (_joinLogs <= 4)
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion Join → Initialize kicked");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkRunner._StartGameModeCloud_d__436),
        nameof(NetworkRunner._StartGameModeCloud_d__436.MoveNext))]
    private static void StartGameModeCloudMoveNextPrefix(NetworkRunner._StartGameModeCloud_d__436 __instance)
    {
        if (!LanMatch.AllowFusionStart)
            return;

        int state = __instance.__1__state;
        if (state != 0)
            return;

        if (!TryInjectCompletedEnterRoomAwaiter(__instance))
            return;

        __instance.__1__state = 1;
        _cloudStartLogs++;
        if (_cloudStartLogs <= 4)
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion StartGameModeCloud 0→1 (fake EnterRoom awaiter)");
    }

    private static void KickInitializeAfterFakeJoin(CloudServices cloud)
    {
        if (cloud == null)
            return;
        NetworkRunner runner = cloud._runner;
        if (runner == null)
            return;

        try
        {
            AsyncOperationHandler<ShutdownReason> op = runner._startGameOperation;
            if (op == null)
            {
                op = new AsyncOperationHandler<ShutdownReason>(default(CancellationToken), 30f, null);
                runner._startGameOperation = op;
            }

            CloudServicesMetadata meta = cloud._metadata;
            if (meta == null)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion Initialize skip (no metadata)");
                op.SetResult(ShutdownReason.Error);
                return;
            }

            NetworkRunnerInitializeArgs args = meta.RunnerInitializeArgs;
            // Client: do NOT stamp Address into init args — Initialize auto-Connects, then our
            // post-init Connect hits "NetCommandConnect with connection status Connecting".
            // Connect(+UniqueId) runs only in TryClientConnectAfterInit after Initialize completes.
            TryDisableEncryption(runner);
            _pendingInitOp = op;
            _pendingInitRunner = runner;
            // Initialize sync-gate checks IsCloudReady; keep forced until Pump sees completion
            // (async body may call CreateCloudSocket which re-checks).
            _forceCloudReady = true;
            _pendingInit = runner.Initialize(args);
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] lan_fusion Initialize() started lanActor=" + ResolveLanActorId()
                + " createCloudHits=" + _createCloudSocketHits);
        }
        catch (Exception ex)
        {
            _forceCloudReady = false;
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion Initialize kick fail " + ex.GetType().Name + ": " + ex.Message);
            try
            {
                if (runner._startGameOperation != null)
                    runner._startGameOperation.SetResult(ShutdownReason.Error);
            }
            catch { /* already completed */ }
        }
    }

    /// <summary>Photon room ActorNumber; Host=1 Client=2 without a real room.</summary>
    internal static int ResolveLanActorId()
    {
        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        return (s != null && s.IsHost) ? 1 : 2;
    }

    // Kept for reference — do not call during Initialize (causes double-Connect). Address is
    // applied only via NetworkRunner.Connect after init.
    private static void EnsureClientInitAddress(NetworkRunnerInitializeArgs args)
    {
        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        if (args == null || s == null || s.IsHost)
            return;
        string ip = FusionLanPatches.ClientJoinIp();
        if (ip.Length == 0)
            return;
        ushort port = LanFusionStart.HostBindPort(Plugin.JoinPort);
        NetAddress dest = NetAddress.CreateFromIpPort(ip, port);
        FusionLanPatches.WriteInitArgsAddress(args, isPublic: false, dest);
        Plugin.LogSrc.LogInfo("[SatmLanIp] lan_fusion client init Address " + ip + ":" + port);
    }

    private static bool TryInjectCompletedEnterRoomAwaiter(NetworkRunner._StartGameModeCloud_d__436 sm)
    {
        try
        {
            Task<short> done = Task.FromResult<short>(0);
            sm.__u__2 = done.GetAwaiter();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_fusion inject EnterRoom awaiter fail " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    private static void CompleteVoidBuilder(
        AsyncTaskMethodBuilder builder,
        Action<AsyncTaskMethodBuilder> writeBack)
    {
        try
        {
            builder.SetResult();
            writeBack(builder);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] lan_fusion SetResult fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static void SelfCheck()
    {
        if (AsyncCompleted != -2)
            throw new InvalidOperationException("SatmLanIp FusionCloudBypass async completed sentinel wrong");
        Task<short> t = Task.FromResult<short>(0);
        if (t == null || !t.IsCompleted)
            throw new InvalidOperationException("SatmLanIp FusionCloudBypass Task.FromResult self-check failed");
        NetAddress any = NetAddress.Any(1);
        if (any.ActorId >= 0)
            throw new InvalidOperationException("SatmLanIp expected Any() ActorId unset for Direct");
        NetAddress relay = NetAddress.FromActorId(1);
        if (relay.ActorId != 1 || !relay.IsRelayAddr)
            throw new InvalidOperationException("SatmLanIp FromActorId(1) self-check failed");
        PlayerRef p = PlayerRef.FromIndex(1);
        if (!p.IsRealPlayer)
            throw new InvalidOperationException("SatmLanIp PlayerRef.FromIndex(1) not IsRealPlayer");
        Il2CppStructArray<byte> uid = MakeUniqueId(1);
        if (uid == null || uid.Length != 8)
            throw new InvalidOperationException("SatmLanIp UniqueId must be 8 bytes (ulong)");
    }
}
