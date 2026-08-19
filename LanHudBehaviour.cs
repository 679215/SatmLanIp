using System;
using UnityEngine;

namespace SatmLanIp;

public sealed class LanHudBehaviour : MonoBehaviour
{
    private bool _wasEnabled = true;
    private static LanHudBehaviour _instance;
    private float _nextPoseSend;

    private void Awake()
    {
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    internal static void NotifyFusionBlocked(string where)
    {
        if (Plugin.LogSrc != null)
            Plugin.LogSrc.LogWarning("[SatmLanIp] Fusion blocked " + where);
    }

    internal static string CurrentHostAdvertise => "";

    internal static void NotifyBanner(string msg, float seconds)
    {
        if (Plugin.LogSrc != null && msg != null && msg.Length > 0)
            Plugin.LogSrc.LogInfo("[SatmLanIp] " + msg);
    }

    internal static void NotifyHostAdvertise(string adv)
    {
    }

    internal static void NotifyJoinPromptClosed()
    {
    }

    internal static void NotifyMatchReset()
    {
    }

    private void Update()
    {
        if (!Plugin.Enabled)
        {
            if (_wasEnabled)
            {
                Plugin.Transport?.Disconnect();
                Plugin.LogSrc.LogInfo("[SatmLanIp] Enabled=false -> Disconnect");
            }
            _wasEnabled = false;
            Plugin.IsActive = false;
            return;
        }
        _wasEnabled = true;

        if (Plugin.Transport != null)
            Plugin.Transport.Poll();

        LanMenuPanel.Tick();
        LanMenuInjector.Tick();
        LanCloneUi.Tick();

        FusionCloudBypassPatches.Pump();
        NoPhotonProbe.Poll();

        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        LanState st = s != null ? s.State : LanState.Idle;
        Plugin.IsActive = st == LanState.Listen || st == LanState.Connecting || st == LanState.Connected;

        LanMatch.Tick();
        if (s != null && st == LanState.Connected && s.MatchActive)
        {
            float now = Time.unscaledTime;
            if (now >= _nextPoseSend)
            {
                _nextPoseSend = now + 0.1f;
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Transform t = cam.transform;
                    Plugin.Transport.SendPose(t.position.x, t.position.y, t.position.z, t.eulerAngles.y);
                }
            }
        }

        bool matchOn = s != null && s.MatchActive;
        if (!matchOn && Input.GetKeyDown(KeyCode.Escape))
        {
            if (LanCloneUi.HasJoinPrompt)
                LanCloneUi.HideJoinPrompt();
            else if (LanMenuFlow.HostSavePending)
                LanMenuPanel.AbortHostSave();
            else if (LanMenuFlow.InLobby)
                LanMenuPanel.LeaveToCreate();
            else if (LanMenuFlow.PanelOpen)
                LanMenuPanel.Back();
        }
    }

    internal static string FormatLine(string role, string state, string peer, int rttMs)
    {
        string rtt = rttMs < 0 ? "--ms" : (rttMs.ToString() + "ms");
        return "LAN  " + role + "  " + state + "  peer=" + peer + "  RTT=" + rtt;
    }

    internal static void SelfCheck()
    {
        string expect = "LAN  HOST  CONNECTED  peer=127.0.0.1:37241  RTT=12ms";
        if (FormatLine("HOST", "CONNECTED", "127.0.0.1:37241", 12) != expect)
            throw new InvalidOperationException("SatmLanIp FormatLine self-check failed");

        LanHostParse.SelfCheck();
        LanLocalIp.SelfCheck();
        LanRoom.SelfCheck();
        LanPose.SelfCheck();
    }
}
