using System;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SatmLanIp;

internal static class LanMatch
{
    private enum Phase
    {
        Idle,
        Playing,
        Waiting,
        Done,
        Failed,
    }

    public static bool AllowFusionStart;
    public static string SessionName = "satm37241";

    private static Phase _phase;
    private static float _since;
    private static bool _playFired;
    private static bool _clientFired;
    private static bool _movedToGame;
    private static string _lastScene = "";
    private static float _nextStuckLog;
    private static bool _stockLeaveRequested;

    public static void Reset()
    {
        _phase = Phase.Idle;
        _since = 0f;
        _playFired = false;
        _clientFired = false;
        _movedToGame = false;
        AllowFusionStart = false;
        _lastScene = "";
        _nextStuckLog = 0f;
        _stockLeaveRequested = false;
        FusionCloudBypassPatches.Reset();
    }

    internal static void RequestStockLeave()
    {
        if (_stockLeaveRequested)
            return;
        _stockLeaveRequested = true;
        try
        {
            Plugin.LogSrc.LogInfo("[SatmLanIp] stock leave via LAN goodbye");
            PlatformManager_Steam steam = UnityEngine.Object.FindObjectOfType<PlatformManager_Steam>();
            if (steam != null)
                steam.HandleEndSessionReturn(NetworkErrors.Disconnected);
            else
            {
                FusionNetworkManager mgr = FusionNetworkManager.Instance;
                if (mgr != null)
                    mgr.LeaveGame();
            }
            FusionLanPatches.LoadMenuNow("client-goodbye");
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] stock leave fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    public static void TryBegin(string why)
    {
        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        if (s == null)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] TryBegin skip via=" + why + " (no session)");
            return;
        }
        if (s.State != LanState.Connected)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] TryBegin skip via=" + why + " state=" + s.State);
            return;
        }

        SessionName = "satm" + (s.IsHost ? Plugin.ListenPort : Plugin.JoinPort).ToString();

        if (!s.MatchActive)
        {
            s.MatchActive = true;
            Plugin.LogSrc.LogInfo("[SatmLanIp] match_start via=" + why + " session=" + SessionName);
        }

        string scene = ActiveSceneName();
        if (!IsMenuSceneName(scene) && scene != "Lobby")
        {
            _phase = Phase.Done;
            Plugin.LogSrc.LogInfo("[SatmLanIp] match_enter already scene=" + scene);
            return;
        }

        // 4 while already Waiting: only nudge MoveToScene / log — never reset playFired.
        if (_phase == Phase.Waiting)
        {
            Plugin.LogSrc.LogInfo("[SatmLanIp] match_enter 4 while Waiting via=" + why);
            TryMoveToGameScene();
            return;
        }

        _phase = Phase.Playing;
        _playFired = false;
        _clientFired = false;
        _movedToGame = false;
        _since = Time.unscaledTime;
        _lastScene = scene;
    }

    public static void Tick()
    {
        FusionCloudBypassPatches.Pump();

        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        bool sessionLive = s != null && s.MatchActive && s.State == LanState.Connected;
        string scene = ActiveSceneName();
        if (ShouldTearDownMatchOnMenuReturn(_phase)
            && (!sessionLive || IsMenuSceneName(scene)))
        {
            EndMatchToMenu(s);
            return;
        }

        if (!sessionLive)
        {
            if (_phase != Phase.Idle)
                Reset();
            return;
        }

        if (scene != _lastScene)
        {
            Plugin.LogSrc.LogInfo("[SatmLanIp] match_scene from=" + _lastScene + " to=" + scene);
            _lastScene = scene;
        }

        if (!IsMenuSceneName(scene))
        {
            if (_phase != Phase.Done)
            {
                _phase = Phase.Done;
                Plugin.LogSrc.LogInfo("[SatmLanIp] match_enter ok scene=" + scene);
            }
            return;
        }

        if (ShouldTearDownMatchOnMenuReturn(_phase))
        {
            EndMatchToMenu(s);
            return;
        }

        if (_phase == Phase.Idle || _phase == Phase.Failed)
            return;

        float now = Time.unscaledTime;
        if (!_playFired)
        {
            DoPlay();
            _playFired = true;
            _since = now;
            _phase = Phase.Waiting;
            FusionCloudBypassPatches.Pump();
            return;
        }

        if (_phase != Phase.Waiting)
            return;

        LanSession sess = Plugin.Transport.Session;
        if (sess != null && !sess.IsHost && !_clientFired)
        {
            float delay = 1.5f + Math.Max(0, sess.LocalSlot - 1) * 0.5f;
            if (now - _since >= delay)
                TryStartClient();
        }

        if (!_movedToGame && now - _since >= 6f)
        {
            _movedToGame = true;
            TryMoveToGameScene();
        }

        if (now - _since >= 10f && now >= _nextStuckLog)
        {
            _nextStuckLog = now + 10f;
            Plugin.LogSrc.LogWarning("[SatmLanIp] match_enter stuck scene=" + scene);
        }

        // Client StartAsClient may park ConnectToCloud mid-Tick.
        FusionCloudBypassPatches.Pump();
    }

    private static bool ShouldTearDownMatchOnMenuReturn(Phase phase)
    {
        return phase == Phase.Done;
    }

    private static void EndMatchToMenu(LanSession s)
    {
        AllowFusionStart = false;
        _phase = Phase.Failed;
        if (s != null)
            s.MatchActive = false;
        Plugin.LogSrc.LogInfo("[SatmLanIp] match_end → main menu (no lobby overlay)");
        LanMenuPanel.Back();
        Reset();
    }

    internal static bool IsMenuSceneName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return true;
        string n = name.Trim();
        return n.Equals("MainMenu", StringComparison.OrdinalIgnoreCase)
            || n.Equals("Splash", StringComparison.OrdinalIgnoreCase)
            || n.Equals("SplashScreen", StringComparison.OrdinalIgnoreCase)
            || n.Equals("Boot", StringComparison.OrdinalIgnoreCase);
    }

    internal static void SelfCheck()
    {
        if (!IsMenuSceneName("MainMenu") || !IsMenuSceneName("") || IsMenuSceneName("Game")
            || IsMenuSceneName("Lobby"))
            throw new InvalidOperationException("SatmLanIp LanMatch scene-name self-check failed");
        if (!ShouldTearDownMatchOnMenuReturn(Phase.Done)
            || ShouldTearDownMatchOnMenuReturn(Phase.Waiting)
            || ShouldTearDownMatchOnMenuReturn(Phase.Idle))
            throw new InvalidOperationException("SatmLanIp tear down lobby overlay only after in-game");
    }

    private static string ActiveSceneName()
    {
        try
        {
            return SceneManager.GetActiveScene().name ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void DoPlay()
    {
        try
        {
            MainMenu menu = Find<MainMenu>();
            if (menu == null)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] match_enter skip (no MainMenu)");
                return;
            }

            menu.SetAsSoloMode(false);
            if (menu.selectSaveFileMenu != null)
                menu.selectSaveFileMenu.SetActive(true);

            int slot = EnsureSaveSlot();
            if (menu.started)
                menu.started = false;

            // ConfirmCreateNewSave can leave 选择模式 open — close it so it doesn't look like "we're in".
            if (menu.selectSaveFileMenu != null)
                menu.selectSaveFileMenu.SetActive(false);

            FusionNetworkManager mgr = FusionNetworkManager.Instance;
            if (mgr != null)
            {
                NetworkRunner runner = null;
                try { runner = mgr.GetRunner(); }
                catch { /* runner may already be tearing down */ }

                // Mid-start: do not Abort — 4 spam was killing the only Runner we just spawned.
                if (runner != null && (_phase == Phase.Waiting || AllowFusionStart))
                {
                    bool running = false;
                    try { running = runner.IsRunning; }
                    catch { /* IsRunning may throw while tearing down */ }
                    Plugin.LogSrc.LogInfo(
                        "[SatmLanIp] match_enter reuse runner IsRunning=" + running);
                    if (running)
                    {
                        TryMoveToGameScene();
                        return;
                    }
                    Plugin.LogSrc.LogInfo(
                        "[SatmLanIp] match_enter wait runner (no Abort) — do not spam 4");
                    return;
                }

                if (runner != null)
                {
                    Plugin.LogSrc.LogInfo("[SatmLanIp] match_enter abort leftover runner");
                    mgr.AbortStartGame();
                }
            }

            AllowFusionStart = true;
            LanSession s = Plugin.Transport.Session;
            if (s != null && s.IsHost)
            {
                if (mgr == null)
                {
                    Plugin.LogSrc.LogWarning("[SatmLanIp] fusion_host skip (no FusionNetworkManager)");
                    return;
                }
                // Keep LAN UDP on ListenPort; Fusion binds ListenPort+1 (see LanFusionStart.HostBindPort).
                ushort fusionPort = LanFusionStart.HostBindPort(Plugin.ListenPort);
                mgr.StartAsHost(SessionName);
                Plugin.LogSrc.LogInfo(
                    "[SatmLanIp] fusion_host skip_cloud session=" + SessionName
                    + " lan=" + Plugin.ListenPort + " fusion=" + fusionPort + " slot=" + slot);
            }
            else
            {
                Plugin.LogSrc.LogInfo("[SatmLanIp] fusion_client wait session=" + SessionName + " slot=" + slot);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] match_enter fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static int EnsureSaveSlot()
    {
        int slot = 0;
        try { slot = SaveManager.CurrentSaveSlot; }
        catch { slot = 0; }
        if (slot < 0)
            slot = 0;

        SaveFileManager sfm = Find<SaveFileManager>();
        if (sfm == null)
        {
            try { SaveManager.CurrentSaveSlot = slot; }
            catch { /* slot write is best-effort */ }
            Plugin.LogSrc.LogInfo("[SatmLanIp] save_select skip (no SaveFileManager) slot=" + slot);
            return slot;
        }

        int existing = FirstExistingSlot(sfm);
        if (existing >= 0)
        {
            slot = existing;
            sfm.SelectSaveFile(slot);
            Plugin.LogSrc.LogInfo("[SatmLanIp] save_select existing=" + slot);
            return slot;
        }

        sfm.CreateNewSave(0);
        sfm.ConfirmCreateNewSave(false);
        Plugin.LogSrc.LogInfo("[SatmLanIp] save_create slot=0 story");
        return 0;
    }

    private static void TryStartClient()
    {
        _clientFired = true;
        try
        {
            AllowFusionStart = true;
            FusionNetworkManager mgr = FusionNetworkManager.Instance;
            if (mgr == null)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] fusion_client skip (no FusionNetworkManager)");
                return;
            }
            mgr.StartAsClient(SessionName, "");
            LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
            int slot = s != null ? s.LocalSlot : 0;
            int max = s != null ? s.MaxPlayers : 0;
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] fusion_client session=" + SessionName
                + " slot=" + slot + "/" + max);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] fusion_client fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static int FirstExistingSlot(SaveFileManager sfm)
    {
        try
        {
            var holders = sfm.saveFileExistsHolders;
            if (holders == null)
                return -1;
            for (int i = 0; i < holders.Length; i++)
            {
                GameObject go = holders[i];
                if (go != null && go.activeSelf)
                    return i;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] save_scan fail " + ex.GetType().Name + ": " + ex.Message);
        }
        return -1;
    }

    private static void TryMoveToGameScene()
    {
        try
        {
            FusionNetworkManager mgr = FusionNetworkManager.Instance;
            if (mgr == null)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] match_enter move=skip (no FusionNetworkManager)");
                return;
            }

            NetworkRunner runner = null;
            try { runner = mgr.GetRunner(); }
            catch { }
            if (runner == null)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] match_enter move=skip (no runner)");
                return;
            }

            bool running = false;
            try { running = runner.IsRunning; }
            catch { }
            if (!running)
            {
                Plugin.LogSrc.LogWarning(
                    "[SatmLanIp] match_enter move=skip (runner not running — Join/Init still incomplete)");
                return;
            }

            SceneRef game = mgr._gameScene;
            mgr.MoveToScene(game);
            Plugin.LogSrc.LogInfo("[SatmLanIp] match_enter via=MoveToScene _gameScene");
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] match_enter move=fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static T Find<T>() where T : Object
    {
        try
        {
            return Object.FindFirstObjectByType<T>();
        }
        catch
        {
            return Object.FindObjectOfType<T>();
        }
    }
}
