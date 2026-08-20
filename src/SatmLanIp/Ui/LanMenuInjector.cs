using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SatmLanIp;

/// <summary>
/// Inject 局域网联机 ONLY into the Play submenu list (单人游戏 / 创建房间 / …).
/// MainMenu.playButton is 开始游戏 on the title screen — never clone that.
/// </summary>
[HarmonyPatch]
internal static class LanMenuInjector
{
    private const string ButtonName = "SatmLanIp_LanButton";
    private const string ButtonLabel = "局域网联机";

    private static bool _dumpedFail;
    private static bool _dumpedOk;
    private static MainMenu _menu;
    private static bool _playWasOpen;
    private static Transform _listParent;
    private static readonly List<GameObject> HiddenPlayButtons = new List<GameObject>();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainMenu), "Start")]
    private static void MainMenuStartPostfix(MainMenu __instance)
    {
        _menu = __instance;
        CleanupWrongPlaces(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainMenu), "OnEnable")]
    private static void MainMenuOnEnablePostfix(MainMenu __instance)
    {
        _menu = __instance;
        CleanupWrongPlaces(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.StartGame))]
    private static bool StartGamePrefix(MainMenu __instance)
    {
        if (!LanMenuFlow.Pending && !LanMenuFlow.PanelOpen && !LanMenuFlow.InLobby
            && !LanMenuFlow.HostSavePending)
            return true;
        Plugin.LogSrc.LogInfo("[SatmLanIp] skip stock StartGame (LAN menu flow)");
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.StartGame))]
    private static void StartGamePostfix(MainMenu __instance)
    {
        if (LanMenuFlow.Pending || LanMenuFlow.PanelOpen || LanMenuFlow.InLobby || LanMenuFlow.HostSavePending)
            return;
        _menu = __instance;
        TryInjectNow(__instance, "StartGame");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.SetAsSoloMode))]
    private static bool SetAsSoloModePrefix(bool isSolo)
    {
        if (!LanMenuFlow.Pending && !LanMenuFlow.PanelOpen && !LanMenuFlow.InLobby
            && !LanMenuFlow.HostSavePending)
            return true;
        if (isSolo)
        {
            Plugin.LogSrc.LogInfo("[SatmLanIp] skip SetAsSoloMode(true) (LAN clone leftover)");
            return false;
        }
        return true;
    }

    internal static void Tick()
    {
        if (!Plugin.Enabled || !Plugin.ShowNativeMenu)
            return;

        MainMenu menu = _menu;
        if (menu == null)
        {
            try { menu = Object.FindObjectOfType<MainMenu>(); }
            catch { return; }
            _menu = menu;
        }
        if (menu == null)
            return;

        CleanupWrongPlaces(menu);

        bool playOpen = menu.playMenu != null && menu.playMenu.activeInHierarchy;
        bool opened = playOpen && !_playWasOpen;
        _playWasOpen = playOpen;
        if (!playOpen)
        {
            _dumpedFail = false;
            return;
        }

        TryInjectNow(menu, opened ? "playMenu.On" : "Tick");
    }

    private static void TryInjectNow(MainMenu menu, string why)
    {
        if (menu == null || menu.playMenu == null || !menu.playMenu.activeInHierarchy)
            return;
        if (!TryFindPlaySubmenuSolo(menu, out GameObject soloGo, out Transform listParent))
        {
            if (!_dumpedFail)
            {
                _dumpedFail = true;
                Plugin.LogSrc.LogWarning("[SatmLanIp] playMenu open but 单人游戏 list not found yet via=" + why);
                DumpPlayMenu(menu);
            }
            return;
        }

        Transform lan = FindNamed(listParent, ButtonName);
        if (lan != null)
        {
            RememberListParent(listParent);
            return;
        }

        TryInjectIntoList(menu, soloGo, listParent, why);
    }

    private static void TryInjectIntoList(MainMenu menu, GameObject soloGo, Transform listParent, string why)
    {
        try
        {
            DumpChildrenOnce(listParent);
            DumpMenuRoots(menu);
            DumpHostWiring(listParent, soloGo.transform);

            RectTransform soloRt = soloGo.GetComponent<RectTransform>();
            if (soloRt == null)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] inject skip (solo has no RectTransform)");
                return;
            }

            RectTransform hostRt = FindHostRect(listParent, soloGo.transform);
            float gap = ComputeGap(
                soloRt.anchoredPosition.y,
                hostRt != null ? hostRt.anchoredPosition.y : soloRt.anchoredPosition.y - 70f);
            Vector2 lanPos = new Vector2(soloRt.anchoredPosition.x, ComputeLanY(soloRt.anchoredPosition.y, gap));

            GameObject lanGo = Object.Instantiate(soloGo, listParent);
            lanGo.name = ButtonName;
            lanGo.SetActive(false);
            StripStockClicks(lanGo);
            int killed = KillLanguageText(lanGo);
            WireLanClick(lanGo, menu);

            // Do NOT move stock buttons. Park LAN in the empty slot above 单人游戏.
            RectTransform lanRt = lanGo.GetComponent<RectTransform>();
            if (lanRt != null)
                lanRt.anchoredPosition = lanPos;

            lanGo.transform.SetSiblingIndex(soloGo.transform.GetSiblingIndex());
            lanGo.SetActive(true);
            RememberListParent(listParent);
            // LanguageText.OnEnable rewrites TMP from its loc key. Kill it first, then set once.
            string now = ApplyLabel(lanGo);

            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] menu_inject ok parent=" + listParent.name
                + " gap=" + gap.ToString("0.0")
                + " lanY=" + lanPos.y.ToString("0.0")
                + " soloY=" + soloRt.anchoredPosition.y.ToString("0.0")
                + " killedLang=" + killed
                + " label=" + now
                + " via=" + why);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] menu_inject fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>soloY is higher on screen than hostY. Fallback if they share a slot.</summary>
    internal static float ComputeGap(float soloY, float hostY)
    {
        float g = soloY - hostY;
        if (g < 8f)
            g = 70f;
        return g;
    }

    internal static float ComputeLanY(float soloY, float gap)
    {
        return soloY + gap;
    }

    private static RectTransform FindHostRect(Transform parent, Transform solo)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c == null || c == solo)
                continue;
            string n = c.name ?? "";
            if (n.IndexOf("Host", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                RectTransform rt = c.GetComponent<RectTransform>();
                if (rt != null)
                    return rt;
            }
            TMP_Text tmp = c.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null && IsCreateRoomLabel(tmp.text != null ? tmp.text.Trim() : ""))
                return c.GetComponent<RectTransform>();
        }
        return null;
    }

    private static void StripStockClicks(GameObject go)
    {
        EventTrigger[] triggers = go.GetComponentsInChildren<EventTrigger>(true);
        for (int i = 0; i < triggers.Length; i++)
        {
            if (triggers[i] == null)
                continue;
            triggers[i].triggers.Clear();
        }

        Button[] buttons = go.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null)
                continue;
            buttons[i].onClick = new Button.ButtonClickedEvent();
        }
    }

    private static void DestroyExtraLanClones(Transform parent)
    {
        int kept = 0;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform c = parent.GetChild(i);
            if (c == null || c.name != ButtonName)
                continue;
            kept++;
            if (kept > 1)
                Object.Destroy(c.gameObject);
        }
    }

    private static void WireLanClick(GameObject go, MainMenu menu)
    {
        Button btn = go.GetComponent<Button>();
        if (btn == null)
            btn = go.GetComponentInChildren<Button>(true);
        if (btn == null)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] LAN clone has no Button");
            return;
        }
        btn.onClick = new Button.ButtonClickedEvent();
        btn.onClick.AddListener((UnityAction)(() => OnLanClicked(menu)));
        btn.interactable = true;
    }

    /// <summary>
    /// Find 单人游戏 in the play submenu. Require a sibling that looks like 创建房间
    /// so we never match title-screen 开始游戏 / PlayButtonHolder.
    /// </summary>
    private static bool TryFindPlaySubmenuSolo(MainMenu menu, out GameObject soloGo, out Transform listParent)
    {
        soloGo = null;
        listParent = null;
        if (menu?.playMenu == null || !menu.playMenu.activeInHierarchy)
            return false;

        TMP_Text[] texts = menu.playMenu.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text t = texts[i];
            if (t == null || t.text == null)
                continue;
            string s = t.text.Trim();
            if (!IsSoloLabel(s))
                continue;

            Button b = t.GetComponentInParent<Button>();
            Transform host = b != null ? b.transform : t.transform;
            Transform p = host.parent;
            if (p == null)
                continue;

            // Reject title-screen holder.
            if (IsTitlePlayHolder(p))
                continue;

            if (!ListHasCreateRoomSibling(p, host))
                continue;

            soloGo = host.gameObject;
            listParent = p;
            return true;
        }

        return false;
    }

    private static bool IsSoloLabel(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 20)
            return false;
        if (s.IndexOf("单人", StringComparison.Ordinal) >= 0)
            return true;
        // Localized English short labels only — not "Singleplayer? Click HOST…"
        if (s.Equals("Singleplayer", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Single Player", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Solo", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool IsCreateRoomLabel(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 24)
            return false;
        return s.IndexOf("创建房间", StringComparison.Ordinal) >= 0
            || s.IndexOf("创建", StringComparison.Ordinal) >= 0 && s.IndexOf("房间", StringComparison.Ordinal) >= 0
            || s.IndexOf("Create Room", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("Host Lobby", StringComparison.OrdinalIgnoreCase) >= 0
            || s.Equals("Host", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ListHasCreateRoomSibling(Transform parent, Transform solo)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c == null || c == solo)
                continue;
            TMP_Text tmp = c.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null && IsCreateRoomLabel(tmp.text != null ? tmp.text.Trim() : ""))
                return true;
        }
        return false;
    }

    private static bool IsTitlePlayHolder(Transform p)
    {
        if (p == null)
            return true;
        string n = p.name ?? "";
        if (n.IndexOf("PlayButtonHolder", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        // Kids look like title play button only.
        bool sawStart = false;
        bool sawCreate = false;
        for (int i = 0; i < p.childCount; i++)
        {
            Transform c = p.GetChild(i);
            if (c == null)
                continue;
            TMP_Text tmp = c.GetComponentInChildren<TMP_Text>(true);
            if (tmp == null || tmp.text == null)
                continue;
            string s = tmp.text.Trim();
            if (s.IndexOf("开始游戏", StringComparison.Ordinal) >= 0
                || s.Equals("Play", StringComparison.OrdinalIgnoreCase)
                || s.IndexOf("Start Game", StringComparison.OrdinalIgnoreCase) >= 0)
                sawStart = true;
            if (IsCreateRoomLabel(s) || IsSoloLabel(s))
                sawCreate = true;
        }
        return sawStart && !sawCreate;
    }

    /// <summary>Remove LAN clones on title screen; restore 开始游戏 label if we overwrote it.</summary>
    private static void CleanupWrongPlaces(MainMenu menu)
    {
        if (menu == null)
            return;
        try
        {
            // Any LAN button under PlayButtonHolder / next to 开始游戏.
            if (menu.playButton != null)
            {
                Transform holder = menu.playButton.transform.parent;
                if (holder != null)
                {
                    for (int i = holder.childCount - 1; i >= 0; i--)
                    {
                        Transform c = holder.GetChild(i);
                        if (c != null && c.name == ButtonName)
                        {
                            Plugin.LogSrc.LogInfo("[SatmLanIp] destroy LAN clone on title PlayButtonHolder");
                            Object.Destroy(c.gameObject);
                        }
                    }
                }

                // If Start Game text was overwritten to 局域网联机, put it back.
                TMP_Text[] texts = menu.playButton.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] == null || texts[i].text == null)
                        continue;
                    if (texts[i].text.IndexOf("局域网", StringComparison.Ordinal) >= 0
                        && texts[i].text != "开始游戏")
                    {
                        texts[i].text = "开始游戏";
                        Plugin.LogSrc.LogInfo("[SatmLanIp] restored 开始游戏 label on playButton");
                    }
                }
            }

            // Orphans under playMenu root (SFX/TITLE level) from early inject mistakes.
            if (menu.playMenu != null)
            {
                Transform root = menu.playMenu.transform;
                for (int i = root.childCount - 1; i >= 0; i--)
                {
                    Transform c = root.GetChild(i);
                    if (c != null && c.name == ButtonName && IsTitlePlayHolder(c.parent))
                    {
                        Object.Destroy(c.gameObject);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] cleanup fail: " + ex.Message);
        }
    }

    private static GameObject FindHostButton(Transform parent, Transform solo)
    {
        RectTransform rt = FindHostRect(parent, solo);
        return rt != null ? rt.gameObject : null;
    }

    internal static bool InvokeStockHostClick()
    {
        if (_listParent == null)
            return false;
        GameObject host = FindHostButton(_listParent, null);
        if (host == null)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] stock Host BTN not found");
            return false;
        }

        try
        {
            EventSystem es = EventSystem.current;
            var ped = new PointerEventData(es);
            EventTrigger[] ets = host.GetComponents<EventTrigger>();
            for (int t = 0; t < ets.Length; t++)
            {
                EventTrigger et = ets[t];
                if (et == null || et.triggers == null)
                    continue;
                for (int i = 0; i < et.triggers.Count; i++)
                {
                    EventTrigger.Entry e = et.triggers[i];
                    if (e == null || e.callback == null)
                        continue;
                    if (e.eventID == EventTriggerType.PointerClick
                        || e.eventID == EventTriggerType.PointerDown
                        || e.eventID == EventTriggerType.Submit)
                        e.callback.Invoke(ped);
                }
            }

            Button btn = host.GetComponent<Button>();
            if (btn != null)
                btn.onClick.Invoke();
            Plugin.LogSrc.LogInfo("[SatmLanIp] invoked stock Host BTN");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] Host invoke fail: " + ex.Message);
            return false;
        }
    }

    private static void DumpMenuRoots(MainMenu menu)
    {
        if (menu == null)
            return;
        Plugin.LogSrc.LogInfo(
            "[SatmLanIp] menu roots play=" + GoInfo(menu.playMenu)
            + " save=" + GoInfo(menu.selectSaveFileMenu)
            + " lobby=" + GoInfo(menu.createLobbySettingsMenu)
            + " saveUnderLobby=" + IsChildOf(menu.selectSaveFileMenu, menu.createLobbySettingsMenu));
    }

    private static void DumpHostWiring(Transform listParent, Transform solo)
    {
        GameObject host = FindHostButton(listParent, solo);
        if (host == null)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] Host BTN missing at inject");
            return;
        }
        var sb = new StringBuilder("[SatmLanIp] Host wiring ");
        sb.Append(host.name);
        Button btn = host.GetComponent<Button>();
        if (btn != null)
        {
            int n = btn.onClick.GetPersistentEventCount();
            sb.Append(" onClickPersistent=").Append(n);
            for (int i = 0; i < n && i < 8; i++)
            {
                UnityEngine.Object tgt = btn.onClick.GetPersistentTarget(i);
                sb.Append(" | ").Append(tgt != null ? tgt.GetType().Name : "null")
                    .Append('.').Append(btn.onClick.GetPersistentMethodName(i));
            }
        }
        EventTrigger et = host.GetComponent<EventTrigger>();
        if (et != null && et.triggers != null)
        {
            sb.Append(" triggerEntries=").Append(et.triggers.Count);
            for (int i = 0; i < et.triggers.Count && i < 8; i++)
            {
                EventTrigger.Entry e = et.triggers[i];
                if (e == null)
                    continue;
                sb.Append(" | ").Append(e.eventID);
            }
        }
        Plugin.LogSrc.LogInfo(sb.ToString());
    }

    private static string GoInfo(GameObject go)
    {
        if (go == null)
            return "null";
        return go.name + (go.activeInHierarchy ? "+" : "-")
            + " parent=" + (go.transform.parent != null ? go.transform.parent.name : "-");
    }

    private static bool IsChildOf(GameObject child, GameObject parent)
    {
        if (child == null || parent == null)
            return false;
        return child.transform.IsChildOf(parent.transform);
    }

    private static Transform FindNamed(Transform parent, string name)
    {
        if (parent == null)
            return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c != null && c.name == name)
                return c;
        }
        return null;
    }

    private static void DumpChildrenOnce(Transform parent)
    {
        if (_dumpedOk || parent == null)
            return;
        _dumpedOk = true;
        DumpChildren(parent);
    }

    private static void DumpPlayMenu(MainMenu menu)
    {
        if (menu.playMenu == null)
            return;
        DumpChildren(menu.playMenu.transform);
        TMP_Text[] texts = menu.playMenu.GetComponentsInChildren<TMP_Text>(true);
        var sb = new StringBuilder("[SatmLanIp] playMenu TMP:");
        int n = 0;
        for (int i = 0; i < texts.Length && n < 20; i++)
        {
            if (texts[i] == null || string.IsNullOrEmpty(texts[i].text))
                continue;
            sb.Append(" | ").Append(texts[i].text.Replace('\n', ' '));
            n++;
        }
        Plugin.LogSrc.LogInfo(sb.ToString());
    }

    private static void DumpChildren(Transform parent)
    {
        var sb = new StringBuilder();
        sb.Append("[SatmLanIp] buttonList kids n=").Append(parent.childCount);
        int n = parent.childCount < 16 ? parent.childCount : 16;
        for (int i = 0; i < n; i++)
        {
            Transform c = parent.GetChild(i);
            if (c == null)
                continue;
            sb.Append(" | ").Append(i).Append(':').Append(c.name)
                .Append(c.gameObject.activeSelf ? "+" : "-");
            TMP_Text tmp = c.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
                sb.Append('{').Append(tmp.text).Append('}');
        }
        Plugin.LogSrc.LogInfo(sb.ToString());
    }

    private static void OnLanClicked(MainMenu menu)
    {
        try
        {
            if (!Plugin.Enabled)
            {
                LanHudBehaviour.NotifyBanner("LAN disabled — set Enabled=true in cfg", 4f);
                return;
            }

            ConflictGuard.Refresh();
            if (ConflictGuard.ConflictsPresent)
            {
                LanHudBehaviour.NotifyBanner("LAN blocked: " + ConflictGuard.ConflictSummary, 5f);
                return;
            }

            LanMenuFlow.Arm();
            LanMenuPanel.OpenPanel("lan-click");
            Plugin.LogSrc.LogInfo("[SatmLanIp] menu_lan_click → create/join");
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] menu_lan_click fail " + ex.GetType().Name + ": " + ex.Message);
            LanHudBehaviour.NotifyBanner("LAN menu failed: " + ex.Message, 5f);
        }
    }

    internal static void RememberListParent(Transform listParent)
    {
        if (listParent != null)
            _listParent = listParent;
    }

    internal static void HidePlayListButtons()
    {
        Transform p = _listParent;
        if (p == null)
            return;
        int n = 0;
        for (int i = 0; i < p.childCount; i++)
        {
            Transform c = p.GetChild(i);
            if (c == null || !c.gameObject.activeSelf)
                continue;
            if (!IsPlayListButton(c.name))
                continue;
            HiddenPlayButtons.Add(c.gameObject);
            c.gameObject.SetActive(false);
            n++;
        }
        if (n > 0)
            Plugin.LogSrc.LogInfo("[SatmLanIp] play list hidden n=" + HiddenPlayButtons.Count);
    }

    internal static void RestorePlayListButtons()
    {
        for (int i = 0; i < HiddenPlayButtons.Count; i++)
        {
            GameObject go = HiddenPlayButtons[i];
            if (go != null)
                go.SetActive(true);
        }
        HiddenPlayButtons.Clear();
    }

    private static bool IsPlayListButton(string n)
    {
        if (string.IsNullOrEmpty(n))
            return false;
        if (n == ButtonName)
            return true;
        if (n.IndexOf("SoloMode", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("Host BTN", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("Join BTN", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("Menu BTN", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("CONTROLLER BUTTON PROMPT", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return false;
    }

    /// <summary>
    /// Stock buttons use LanguageText, not I2. OnEnable/Start/SetText look up <c>key</c>
    /// and write 「单人游戏」 back onto TMP. Disable+destroy on the clone so the label
    /// can be set once. Do not fight this every frame.
    /// </summary>
    private static int KillLanguageText(GameObject go)
    {
        LanguageText[] loc = go.GetComponentsInChildren<LanguageText>(true);
        int n = 0;
        for (int i = 0; i < loc.Length; i++)
        {
            LanguageText lt = loc[i];
            if (lt == null)
                continue;
            lt.enabled = false;
            try { Object.DestroyImmediate(lt); }
            catch { Object.Destroy(lt); }
            n++;
        }
        if (n == 0)
            DumpBehaviours(go);
        return n;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LanguageText), "SetText")]
    private static bool LanguageTextSetTextPrefix(LanguageText __instance)
    {
        return !IsOnLanClone(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LanguageText), "OnEnable")]
    private static bool LanguageTextOnEnablePrefix(LanguageText __instance)
    {
        return !IsOnLanClone(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LanguageText), "Start")]
    private static bool LanguageTextStartPrefix(LanguageText __instance)
    {
        return !IsOnLanClone(__instance);
    }

    private static bool IsOnLanClone(Component c)
    {
        Transform t = c != null ? c.transform : null;
        while (t != null)
        {
            string n = t.name ?? "";
            if (n == ButtonName || n.StartsWith("SatmLanIp_", StringComparison.Ordinal))
                return true;
            t = t.parent;
        }
        return false;
    }

    private static void DumpBehaviours(GameObject go)
    {
        Behaviour[] all = go.GetComponentsInChildren<Behaviour>(true);
        var sb = new StringBuilder("[SatmLanIp] LAN clone behaviours");
        int n = 0;
        for (int i = 0; i < all.Length && n < 24; i++)
        {
            if (all[i] == null)
                continue;
            sb.Append(" | ").Append(all[i].GetType().Name);
            n++;
        }
        Plugin.LogSrc.LogWarning(sb.ToString());
    }

    private static string ApplyLabel(GameObject go)
    {
        string now = "";
        TMP_Text[] texts = go.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text t = texts[i];
            if (t == null)
                continue;
            if (LanCloneUi.IsLatinOnlyFontName(t.font != null ? t.font.name : null))
                continue;
            if (!LanCloneUi.ShouldWriteTmp(t.text, ButtonLabel))
            {
                now = t.text;
                continue;
            }
            t.SetText(ButtonLabel);
            now = t.text;
        }
        return now;
    }

    internal static void LogPatchStatus(Harmony harmony)
    {
        int n = 0;
        foreach (var p in harmony.GetPatchedMethods())
        {
            if (p.DeclaringType == typeof(MainMenu) && (p.Name == "Start" || p.Name == "OnEnable"))
                n++;
        }
        Plugin.LogSrc.LogInfo("[SatmLanIp] Harmony MainMenu inject patches count~=" + n);
    }

    internal static void SelfCheck()
    {
        if (ComputeGap(100f, 30f) != 70f)
            throw new InvalidOperationException("SatmLanIp ComputeGap host-delta");
        if (ComputeGap(10f, 10f) != 70f)
            throw new InvalidOperationException("SatmLanIp ComputeGap fallback");
        if (ComputeLanY(30f, 70f) != 100f)
            throw new InvalidOperationException("SatmLanIp ComputeLanY");
    }
}
