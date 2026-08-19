using System;
using HarmonyLib;

namespace SatmLanIp;

[HarmonyPatch]
internal static class PhotonGuardPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FusionNetworkManager), nameof(FusionNetworkManager.StartAsHost))]
    private static bool StartAsHostPrefix(string sessionName)
    {
        return Gate("StartAsHost", sessionName);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FusionNetworkManager), nameof(FusionNetworkManager.StartAsClient))]
    private static bool StartAsClientPrefix(string sessionName, string region)
    {
        return Gate("StartAsClient", sessionName + "/" + region);
    }

    // Do not Prefix FusionNetworkManager.StartGame (returns Task): skipping without __result can NRE awaiters.
    // UI goes through StartAsHost / StartAsClient which we block when Active.
    // Do not Prefix StartAsSolo: stock Play path calls it after save/setup.

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.StartGame))]
    private static void MenuStartGamePostfix()
    {
        Plugin.LogSrc.LogInfo("[SatmLanIp] menu_StartGame fired");
    }

    private static bool Gate(string where, string detail)
    {
        if (LanMatch.AllowFusionStart)
        {
            Plugin.LogSrc.LogInfo("[SatmLanIp] allow Fusion " + where + " (" + detail + ")");
            return true;
        }

        if (!Plugin.IsActive || !Plugin.BlockFusionStart)
        {
            NoPhotonProbe.NoteFusionStartAttempt();
            return true;
        }

        if (Plugin.Transport != null)
            Plugin.Transport.Session.FusionStartsBlocked++;

        Plugin.LogSrc.LogWarning($"[SatmLanIp] blocked Fusion {where} ({detail})");
        LanHudBehaviour.NotifyFusionBlocked(where);
        return false;
    }

    internal static void LogPatchStatus(Harmony harmony)
    {
        int n = 0;
        foreach (var p in harmony.GetPatchedMethods())
        {
            string name = p.Name;
            if (name == "StartAsHost" || name == "StartAsClient")
                n++;
        }
        Plugin.LogSrc.LogInfo($"[SatmLanIp] Harmony PhotonGuard patches touching StartAsHost/Client count~={n}");
    }
}
