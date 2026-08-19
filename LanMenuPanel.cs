using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SatmLanIp;

/// <summary>
/// LAN click opens Create/Join immediately.
/// 创建房间 then save/mode; 加入房间 skips save.
/// Stock StartLobby is blocked while LAN owns the path.
/// </summary>
[HarmonyPatch]
internal static class LanMenuPanel
{
    private static bool _saveBackWired;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CreateLobbySettings), nameof(CreateLobbySettings.StartLobby))]
    private static bool StartLobbyPrefix()
    {
        if (!LanMenuFlow.ShouldHijackLobbySettings() && !LanMenuFlow.PanelOpen && !LanMenuFlow.InLobby
            && !LanMenuFlow.HostSavePending)
            return true;
        if (LanMenuFlow.InLobby)
            return false;
        if (LanMenuFlow.HostSavePending)
        {
            PushSaveUi("StartLobby-host-save");
            return false;
        }
        if (LanMenuFlow.PanelOpen)
            return false;
        Plugin.LogSrc.LogInfo("[SatmLanIp] StartLobby → LAN create/join (skip save-first)");
        OpenPanel("StartLobby");
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CreateLobbySettings), "OnEnable")]
    private static void OnEnablePostfix()
    {
        if (LanMenuFlow.HostSavePending)
        {
            PushSaveUi("OnEnable-host-save");
            return;
        }
        if (!LanMenuFlow.ShouldHijackLobbySettings() || LanMenuFlow.PanelOpen || LanMenuFlow.InLobby)
            return;
        OpenPanel("OnEnable");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SaveFileManager), nameof(SaveFileManager.ConfirmCreateNewSave))]
    private static void ConfirmCreateNewSavePostfix(bool endlessMode)
    {
        if (!Plugin.Enabled || !Plugin.ShowNativeMenu)
            return;
        if (!LanMenuFlow.HostSavePending)
            return;
        Plugin.LogSrc.LogInfo("[SatmLanIp] mode confirmed endless=" + endlessMode + " → host lobby");
        FinishHostSave("ConfirmMode");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SaveFileManager), nameof(SaveFileManager.SelectSaveFile))]
    private static void SelectSaveFilePostfix(SaveFileManager __instance, int index)
    {
        if (!Plugin.Enabled || !Plugin.ShowNativeMenu)
            return;
        if (!LanMenuFlow.HostSavePending)
            return;
        if (__instance != null
            && __instance.selectModeMenu != null
            && __instance.selectModeMenu.activeInHierarchy)
            return;
        Plugin.LogSrc.LogInfo("[SatmLanIp] save selected index=" + index + " → host lobby");
        FinishHostSave("SelectSave");
    }

    internal static void PushSaveUi(string why)
    {
        try
        {
            MainMenu menu = Object.FindObjectOfType<MainMenu>();
            LanMenuInjector.HidePlayListButtons();
            bool saveOn = EnsureSaveUiVisible(menu);
            if (saveOn)
                WireSaveBack(menu.selectSaveFileMenu);
            UnlockCursor();
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] push_save via=" + why
                + " saveOn=" + saveOn
                + " photonOn=" + PhotonIsOn(menu));
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] push_save fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static void OpenPanel(string why)
    {
        try
        {
            LanMenuFlow.ConsumePending();
            LanMenuFlow.PanelOpen = true;
            LanMenuFlow.ClearHostSavePending();
            MainMenu menu = Object.FindObjectOfType<MainMenu>();
            LanMenuInjector.HidePlayListButtons();
            if (menu != null && menu.selectSaveFileMenu != null)
                menu.selectSaveFileMenu.SetActive(false);
            UnlockCursor();
            LanCloneUi.ShowCreate();
            HidePhoton(menu);
            Plugin.LogSrc.LogInfo("[SatmLanIp] lan_panel open via=" + why);
            if (!LanCloneUi.HasCreateUi)
                Plugin.LogSrc.LogWarning("[SatmLanIp] lan_panel open but create UI missing");
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] lan_panel open fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static void FinishHostSave(string why)
    {
        if (!LanMenuFlow.HostSavePending)
            return;
        try
        {
            LanMenuFlow.MarkSavePicked();
            LanMenuFlow.ClearHostSavePending();
            _saveBackWired = false;
            MainMenu menu = Object.FindObjectOfType<MainMenu>();
            if (menu != null && menu.selectSaveFileMenu != null)
                menu.selectSaveFileMenu.SetActive(false);
            HidePhoton(menu);
            UnlockCursor();
            LanMenuActions.Host(LanMenuFlow.PendingMaxPlayers);
            Plugin.LogSrc.LogInfo("[SatmLanIp] host after save via=" + why);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] host after save fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static void AbortHostSave()
    {
        if (!LanMenuFlow.HostSavePending)
            return;
        _saveBackWired = false;
        MainMenu menu = Object.FindObjectOfType<MainMenu>();
        if (menu != null && menu.selectSaveFileMenu != null)
            menu.selectSaveFileMenu.SetActive(false);
        HidePhoton(menu);
        LanMenuFlow.CancelHostSave();
        UnlockCursor();
        LanCloneUi.ShowCreate();
        Plugin.LogSrc.LogInfo("[SatmLanIp] host save aborted → create panel");
    }

    internal static void Hide()
    {
        LanMenuFlow.PanelOpen = false;
        if (!LanMenuFlow.InLobby)
            LanCloneUi.HideCreate();
    }

    internal static void Tick()
    {
        if (!Plugin.Enabled || !Plugin.ShowNativeMenu)
            return;
        MainMenu menu = Object.FindObjectOfType<MainMenu>();
        if (LanMenuFlow.PanelOpen || LanMenuFlow.InLobby)
        {
            HidePhoton(menu);
            return;
        }
        if (LanMenuFlow.HostSavePending)
        {
            LanMenuInjector.HidePlayListButtons();
            EnsureSaveUiVisible(menu);
            return;
        }
    }

    internal static void LeaveToCreate()
    {
        if (LanMenuFlow.InLobby)
            LanMenuActions.Disconnect();
        LanMenuFlow.ReturnToCreate();
        LanCloneUi.HideLobby();
        LanCloneUi.ShowCreate();
        UnlockCursor();
        Plugin.LogSrc.LogInfo("[SatmLanIp] leave → create panel");
    }

    internal static void Back()
    {
        if (LanMenuFlow.InLobby)
            LanMenuActions.Disconnect();
        LanCloneUi.DestroyAll();
        Hide();
        LanMenuFlow.Clear();
        MainMenu menu = Object.FindObjectOfType<MainMenu>();
        HidePhoton(menu);
        if (menu != null && menu.selectSaveFileMenu != null)
            menu.selectSaveFileMenu.SetActive(false);
        LanMenuInjector.RestorePlayListButtons();
        if (menu != null && menu.playMenu != null)
            menu.playMenu.SetActive(true);
        UnlockCursor();
        Plugin.LogSrc.LogInfo("[SatmLanIp] lan_panel back");
    }

    private static void HidePhoton(MainMenu menu)
    {
        if (LanMenuFlow.HostSavePending)
            return;

        GameObject save = menu != null ? menu.selectSaveFileMenu : null;

        if (menu != null && menu.createLobbySettingsMenu != null && menu.createLobbySettingsMenu.activeSelf)
        {
            if (save == null || !save.transform.IsChildOf(menu.createLobbySettingsMenu.transform))
                menu.createLobbySettingsMenu.SetActive(false);
        }

        CreateLobbySettings stock = Object.FindObjectOfType<CreateLobbySettings>();
        if (stock == null || stock.gameObject == null || !stock.gameObject.activeSelf)
            return;
        if (LanCloneUi.IsLanClone(stock.transform))
            return;
        if (save != null && save.transform.IsChildOf(stock.transform))
            return;
        stock.gameObject.SetActive(false);
    }

    private static bool EnsureSaveUiVisible(MainMenu menu)
    {
        if (menu == null || menu.selectSaveFileMenu == null)
            return false;
        GameObject save = menu.selectSaveFileMenu;
        if (menu.createLobbySettingsMenu != null
            && save.transform.IsChildOf(menu.createLobbySettingsMenu.transform)
            && !menu.createLobbySettingsMenu.activeSelf)
            menu.createLobbySettingsMenu.SetActive(true);
        Transform t = save.transform.parent;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            t = t.parent;
        }
        save.SetActive(true);
        bool on = save.activeInHierarchy;
        if (on)
            WireSaveBack(save);
        return on;
    }

    private static void WireSaveBack(GameObject saveRoot)
    {
        if (saveRoot == null || _saveBackWired)
            return;
        Button[] btns = saveRoot.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < btns.Length; i++)
        {
            Button b = btns[i];
            if (b == null)
                continue;
            TMP_Text tmp = b.GetComponentInChildren<TMP_Text>(true);
            string s = tmp != null && tmp.text != null ? tmp.text.Trim() : "";
            if (!s.Equals("返回", StringComparison.Ordinal)
                && !s.Equals("Back", StringComparison.OrdinalIgnoreCase))
                continue;
            b.onClick.AddListener((UnityEngine.Events.UnityAction)AbortHostSave);
            _saveBackWired = true;
            Plugin.LogSrc.LogInfo("[SatmLanIp] save back wired");
            return;
        }
    }

    private static bool PhotonIsOn(MainMenu menu)
    {
        if (menu != null && menu.createLobbySettingsMenu != null && menu.createLobbySettingsMenu.activeInHierarchy)
            return true;
        CreateLobbySettings stock = Object.FindObjectOfType<CreateLobbySettings>();
        return stock != null && stock.gameObject != null && stock.gameObject.activeInHierarchy;
    }

    private static void UnlockCursor()
    {
        try
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        catch
        {
        }
    }

    internal static void LogPatchStatus(Harmony harmony)
    {
        int n = 0;
        foreach (var p in harmony.GetPatchedMethods())
        {
            if (p.DeclaringType == typeof(CreateLobbySettings)
                || (p.DeclaringType == typeof(SaveFileManager)
                    && (p.Name == "ConfirmCreateNewSave" || p.Name == "SelectSaveFile")))
                n++;
        }
        Plugin.LogSrc.LogInfo("[SatmLanIp] Harmony LAN panel hijack patches count~=" + n);
    }
}
