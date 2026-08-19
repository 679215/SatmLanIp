using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SatmLanIp;

/// <summary>
/// Clone stock CreateRoom / lobby shells. Never mutate the originals.
/// </summary>
internal static class LanCloneUi
{
    internal const string CreateName = "SatmLanIp_CreatePanel";
    internal const string LobbyName = "SatmLanIp_LobbyPanel";
    internal const string JoinName = "SatmLanIp_JoinBtn";

    private static GameObject _create;
    private static GameObject _lobby;
    private static Button _joinBtn;
    private static Button _startBtn;
    private static Button _readyBtn;
    private static Button _leaveBtn;
    private static TMP_Text _lobbyTitle;
    private static TMP_Text _lobbyStatus;
    private static TMP_Text _createHint;
    private static GameObject _noticeRoot;
    private static GameObject _joinPrompt;
    private static TMP_InputField _joinIpField;
    private static string _createNotice = "";
    private static int _maxPlayers = 3;
    private static bool _dumpedCreate;

    internal static int ReadMaxPlayers()
    {
        int n = TryReadMaxFromClone();
        if (n > 0)
            _maxPlayers = LanRoom.ClampMax(n);
        return _maxPlayers;
    }

    internal static bool HasCreateUi => _create != null;
    internal static bool HasJoinPrompt => _joinPrompt != null;

    internal static void ShowCreate()
    {
        HideJoinPrompt();
        HideCreate();
        MainMenu menu = FindMenu();
        if (menu == null || menu.createLobbySettingsMenu == null)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] clone create skip (no createLobbySettingsMenu)");
            ShowOverlayCreate();
            return;
        }

        GameObject src = menu.createLobbySettingsMenu;
        bool srcOn = src.activeSelf;
        try
        {
            if (!srcOn)
                src.SetActive(true);
            GameObject clone = Object.Instantiate(src, src.transform.parent);
            clone.name = CreateName;
            DumpTmp(clone, "create-raw");
            StripPhotonBits(clone);
            DestroySaveCopy(menu, src, clone);
            Button createBtn = FindButton(clone, IsCreateRoomLabel);
            Button backBtn = FindButton(clone, IsBackLabel);
            if (createBtn == null)
                createBtn = FindPrimaryActionButton(clone);
            DestroyLabeledGroups(clone);
            KillLanguageText(clone);
            if (createBtn == null)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] clone create: no 创建房间 button");
                DumpTmp(clone, "create");
                Object.Destroy(clone);
                ShowOverlayCreate();
                return;
            }

            StripClicks(createBtn.gameObject);
            ApplyLabel(createBtn.gameObject, "创建房间");
            createBtn.onClick.AddListener((UnityAction)OnCreateClicked);

            GameObject joinGo = Object.Instantiate(createBtn.gameObject, createBtn.transform.parent);
            joinGo.name = JoinName;
            KillLanguageText(joinGo);
            ApplyLabel(joinGo, "加入房间");
            StripClicks(joinGo);
            Button joinBtn = joinGo.GetComponent<Button>();
            if (joinBtn == null)
                joinBtn = joinGo.GetComponentInChildren<Button>(true);
            if (joinBtn != null)
            {
                joinBtn.onClick.AddListener((UnityAction)OnJoinClicked);
                _joinBtn = joinBtn;
            }

            if (backBtn != null)
            {
                StripClicks(backBtn.gameObject);
                ApplyLabel(backBtn.gameObject, "返回");
                backBtn.onClick.AddListener((UnityAction)OnCreateBack);
            }

            CenterHostJoin(createBtn.GetComponent<RectTransform>(), joinGo.GetComponent<RectTransform>());
            StripNoise(clone, hideMaxPlayers: false);
            clone.SetActive(true);
            _create = clone;
            RefreshCreateChrome();
            UnlockCursor();
            Plugin.LogSrc.LogInfo("[SatmLanIp] clone create shown max~=" + ReadMaxPlayers());
            if (!_dumpedCreate)
            {
                _dumpedCreate = true;
                DumpTmp(clone, "create-ok");
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] clone create fail " + ex.GetType().Name + ": " + ex.Message);
            ShowOverlayCreate();
        }
        finally
        {
            try
            {
                if (!srcOn && src != null)
                    src.SetActive(false);
            }
            catch
            {
            }
        }
    }

    internal static void ShowLobby()
    {
        HideLobby();

        Button template = null;
        TMP_Text tmpSample = null;
        if (_create != null)
        {
            template = FindPrimaryActionButton(_create);
            if (template == null)
                template = FindButton(_create, IsCreateRoomLabel);
            tmpSample = _create.GetComponentInChildren<TMP_Text>(true);
        }
        if (template == null)
            template = FindAnyMenuButton();

        Plugin.LogSrc.LogInfo(
            "[SatmLanIp] overlay lobby template=" + (template != null ? template.name : "null"));
        // Clone from create template BEFORE HideCreate — Instantiate of a destroyed GO is empty.
        ShowOverlayLobby(template, tmpSample);
        HideJoinPrompt();
        HideCreate();
    }

    private static void ShowOverlayLobby(Button template, TMP_Text tmpSample)
    {
        GameObject root = new GameObject(LobbyName);
        Object.DontDestroyOnLoad(root);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5100;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        root.AddComponent<GraphicRaycaster>();

        // Dim panel
        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(root.transform, false);
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);
        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(520f, 400f);
        panelRt.anchoredPosition = Vector2.zero;

        TMP_Text fontSample = OverlayFontSample(tmpSample, template);
        _lobbyTitle = MakeOverlayText(panel.transform, fontSample, "局域网房间", 36f, new Vector2(0f, 140f), 48f);
        _lobbyStatus = MakeOverlayText(panel.transform, fontSample, "", 20f, new Vector2(0f, 60f), 100f);
        if (_lobbyStatus != null)
        {
            _lobbyStatus.alignment = TextAlignmentOptions.Left;
            _lobbyStatus.lineSpacing = _lobbyStatus.fontSize;
        }

        if (template != null)
        {
            _startBtn = OverlayClone(template, panel.transform, "开始游戏",
                (UnityAction)LanMenuActions.StartMatch, new Vector2(0f, 10f));
            _readyBtn = OverlayClone(template, panel.transform, "准备",
                (UnityAction)LanMenuActions.ToggleReady, new Vector2(0f, -60f));
            _leaveBtn = OverlayClone(template, panel.transform, "取消",
                (UnityAction)OnLeaveLobby, new Vector2(0f, -40f));
            CopyTmpFont(_startBtn, _lobbyTitle, _lobbyStatus);
        }
        else
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] overlay lobby: no button template — title only");
        }

        _lobby = root;
        UnlockCursor();
        RefreshLobbyChrome();
        Plugin.LogSrc.LogInfo(
            "[SatmLanIp] overlay lobby shown start=" + (_startBtn != null)
            + " ready=" + (_readyBtn != null));
    }

    private static TMP_Text MakeOverlayText(Transform parent, TMP_Text sample, string text, float size, Vector2 pos, float height)
    {
        GameObject go;
        TMP_Text tmp;
        if (sample != null)
        {
            go = Object.Instantiate(sample.gameObject, parent);
            KillLanguageText(go);
            tmp = go.GetComponent<TMP_Text>();
            if (tmp == null)
                tmp = go.GetComponentInChildren<TMP_Text>(true);
        }
        else
        {
            go = new GameObject("Title");
            go.transform.SetParent(parent, false);
            tmp = go.AddComponent<TextMeshProUGUI>();
        }
        if (tmp == null)
            return null;
        go.name = "SatmLanIp_LobbyText";
        tmp.SetText(text);
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(480f, height);
            rt.anchoredPosition = pos;
        }
        go.SetActive(true);
        return tmp;
    }

    private static TMP_Text OverlayFontSample(TMP_Text createFirst, Button template)
    {
        TMP_Text buttonTmp = template != null ? FirstCjkTmp(template.gameObject) : null;
        TMP_Text createTmp = createFirst != null && !IsLatinOnlyTmp(createFirst) ? createFirst : null;
        return PreferButtonSample(createTmp, buttonTmp) ?? createFirst;
    }

    private static TMP_Text FirstCjkTmp(GameObject go)
    {
        if (go == null)
            return null;
        TMP_Text[] texts = go.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && !IsLatinOnlyTmp(texts[i]))
                return texts[i];
        }
        return null;
    }

    private static bool IsLatinOnlyTmp(TMP_Text tmp)
    {
        if (tmp == null)
            return false;
        string name = tmp.font != null ? tmp.font.name : null;
        return IsLatinOnlyFontName(name);
    }

    private static void CopyTmpFont(Button srcBtn, params TMP_Text[] dst)
    {
        TMP_Text src = srcBtn != null ? srcBtn.GetComponentInChildren<TMP_Text>(true) : null;
        if (src == null || src.font == null || dst == null || IsLatinOnlyTmp(src))
            return;
        for (int i = 0; i < dst.Length; i++)
        {
            if (dst[i] == null)
                continue;
            dst[i].font = src.font;
            dst[i].fontSharedMaterial = src.fontSharedMaterial;
        }
    }

    internal static void HideCreate()
    {
        if (_create != null)
        {
            Object.Destroy(_create);
            _create = null;
        }
        _joinBtn = null;
    }

    internal static void HideLobby()
    {
        if (_lobby != null)
        {
            Object.Destroy(_lobby);
            _lobby = null;
        }
        _startBtn = null;
        _readyBtn = null;
        _leaveBtn = null;
        _lobbyTitle = null;
        _lobbyStatus = null;
    }

    internal static void DestroyAll()
    {
        HideCreate();
        HideLobby();
        HideJoinPrompt();
        HideNotice();
    }

    internal static void Tick()
    {
        if (_create != null)
            RefreshCreateChrome();

        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        if (s != null && s.MatchActive)
        {
            if (_lobby != null)
                HideLobby();
            return;
        }

        if (LanMenuFlow.InLobby && s != null && !s.IsHost && !s.MatchActive
            && (s.State == LanState.Fail || s.State == LanState.Drop))
        {
            ReturnClientToCreate(s.State == LanState.Drop ? "host left" : s.FailReason);
            return;
        }

        if (_lobby != null)
            RefreshLobbyChrome();
    }

    private static void ReturnClientToCreate(string why)
    {
        Plugin.LogSrc.LogWarning("[SatmLanIp] join failed → create panel (" + (why ?? "") + ")");
        SetCreateNotice(why);
        LanMenuActions.Disconnect();
        LanMenuFlow.ReturnToCreate();
        HideLobby();
        ShowCreate();
    }

    internal static bool IsLanClone(Transform t)
    {
        while (t != null)
        {
            string n = t.name ?? "";
            if (n.StartsWith("SatmLanIp_", StringComparison.Ordinal))
                return true;
            t = t.parent;
        }
        return false;
    }

    private static void OnCreateClicked()
    {
        int max = ReadMaxPlayers();
        LanMenuFlow.BeginHostSave(max);
        HideCreate();
        MainMenu menu = FindMenu();
        try
        {
            if (menu != null)
                menu.SetAsSoloMode(false);
        }
        catch { }
        bool hostOk = LanMenuInjector.InvokeStockHostClick();
        LanMenuPanel.PushSaveUi(hostOk ? "create-room" : "create-no-host-btn");
    }

    private static void OnJoinClicked()
    {
        ShowJoinPrompt();
    }

    private static void OnCreateBack()
    {
        DestroyAll();
        LanMenuPanel.Back();
    }

    private static void OnLeaveLobby()
    {
        LanMenuPanel.LeaveToCreate();
    }

    internal static void HideJoinPrompt()
    {
        if (_joinPrompt != null)
        {
            Object.Destroy(_joinPrompt);
            _joinPrompt = null;
        }
        _joinIpField = null;
    }

    private static void ShowJoinPrompt()
    {
        HideJoinPrompt();
        ClearCreateNotice();
        Button template = null;
        TMP_Text sample = null;
        if (_create != null)
        {
            template = FindPrimaryActionButton(_create);
            sample = _create.GetComponentInChildren<TMP_Text>(true);
        }
        if (template == null)
            template = FindAnyMenuButton();

        GameObject root = new GameObject("SatmLanIp_JoinPrompt");
        Object.DontDestroyOnLoad(root);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5200;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        root.AddComponent<GraphicRaycaster>();

        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(root.transform, false);
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.82f);
        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(520f, 340f);
        panelRt.anchoredPosition = Vector2.zero;

        TMP_Text fontSample = OverlayFontSample(sample, template);
        MakeOverlayText(panel.transform, fontSample, "加入房间", 32f, new Vector2(0f, 120f), 44f);
        MakeOverlayText(panel.transform, fontSample, "填写房主 IP", 20f, new Vector2(0f, 70f), 36f);

        TMP_InputField stock = FindStockInputField();
        if (stock != null)
        {
            GameObject go = Object.Instantiate(stock.gameObject, panel.transform);
            go.name = "SatmLanIp_JoinIp";
            KillLanguageText(go);
            _joinIpField = go.GetComponent<TMP_InputField>();
            if (_joinIpField == null)
                _joinIpField = go.GetComponentInChildren<TMP_InputField>(true);
            if (_joinIpField != null)
            {
                _joinIpField.contentType = TMP_InputField.ContentType.Standard;
                _joinIpField.text = Plugin.JoinAddress ?? "";
            }
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 10f);
                rt.sizeDelta = new Vector2(360f, 44f);
            }
            go.SetActive(true);
        }
        else
            Plugin.LogSrc.LogWarning("[SatmLanIp] join prompt: no TMP_InputField — using cfg JoinAddress");

        if (template != null)
        {
            OverlayClone(template, panel.transform, "连接", (UnityAction)OnJoinConfirm, new Vector2(0f, -70f));
            OverlayClone(template, panel.transform, "返回", (UnityAction)HideJoinPrompt, new Vector2(0f, -130f));
        }

        _joinPrompt = root;
        UnlockCursor();
        Plugin.LogSrc.LogInfo("[SatmLanIp] join prompt shown field=" + (_joinIpField != null));
    }

    private static void OnJoinConfirm()
    {
        string ip = "";
        if (_joinIpField != null && _joinIpField.text != null)
            ip = _joinIpField.text.Trim();
        if (ip.Length == 0)
            ip = (Plugin.JoinAddress ?? "").Trim();
        if (ip.Length == 0)
        {
            SetCreateNotice("填写房主 IP 再连接");
            return;
        }
        HideJoinPrompt();
        LanMenuActions.Join(ip);
    }

    private static TMP_InputField FindStockInputField()
    {
        MainMenu menu = FindMenu();
        GameObject src = menu != null ? menu.createLobbySettingsMenu : null;
        if (src != null)
        {
            TMP_InputField[] inMenu = src.GetComponentsInChildren<TMP_InputField>(true);
            if (inMenu != null)
            {
                for (int i = 0; i < inMenu.Length; i++)
                {
                    if (inMenu[i] != null && !IsLanClone(inMenu[i].transform))
                        return inMenu[i];
                }
            }
        }
        try
        {
            TMP_InputField[] all = Object.FindObjectsOfType<TMP_InputField>();
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && !IsLanClone(all[i].transform))
                        return all[i];
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static void RefreshJoinInteractable()
    {
        RefreshCreateChrome();
    }

    internal static void SetCreateNotice(string msg)
    {
        _createNotice = msg ?? "";
        RefreshCreateChrome();
    }

    internal static void ClearCreateNotice()
    {
        _createNotice = "";
        RefreshCreateChrome();
    }

    private static void RefreshCreateChrome()
    {
        if (_joinBtn != null)
            _joinBtn.interactable = true;
        if (_createNotice == null || _createNotice.Length == 0)
        {
            HideNotice();
            return;
        }
        EnsureNotice();
        if (_createHint != null)
            _createHint.SetText(_createNotice);
    }

    private static void EnsureNotice()
    {
        if (_noticeRoot != null && _createHint != null)
        {
            _noticeRoot.SetActive(true);
            return;
        }
        HideNotice();
        GameObject root = new GameObject("SatmLanIp_Notice");
        Object.DontDestroyOnLoad(root);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5300;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();
        TMP_Text sample = OverlayFontSample(
            _create != null ? _create.GetComponentInChildren<TMP_Text>(true) : null,
            FindAnyMenuButton());
        _createHint = MakeOverlayText(root.transform, sample, _createNotice, 22f, new Vector2(0f, 360f), 80f);
        if (_createHint != null)
            _createHint.alignment = TextAlignmentOptions.Center;
        _noticeRoot = root;
    }

    private static void HideNotice()
    {
        if (_noticeRoot != null)
        {
            Object.Destroy(_noticeRoot);
            _noticeRoot = null;
        }
        _createHint = null;
    }

    private static void RefreshLobbyChrome()
    {
        LanSession s = Plugin.Transport != null ? Plugin.Transport.Session : null;
        bool inRoom = ShowLobbyReady(s);
        bool host = s != null && s.IsHost && inRoom;
        if (_startBtn != null)
        {
            if (_startBtn.gameObject.activeSelf != host)
                _startBtn.gameObject.SetActive(host);
            _startBtn.interactable = host && s.AllReady;
        }
        if (_readyBtn != null)
        {
            if (_readyBtn.gameObject.activeSelf != inRoom)
                _readyBtn.gameObject.SetActive(inRoom);
            _readyBtn.interactable = inRoom;
            if (inRoom)
                ApplyLabel(_readyBtn.gameObject, s != null && s.LocalReady ? "取消准备" : "准备");
        }
        if (_leaveBtn != null)
        {
            ApplyLabel(_leaveBtn.gameObject, LeaveLabel(s));
            RectTransform rt = _leaveBtn.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = inRoom ? new Vector2(0f, -130f) : new Vector2(0f, -40f);
        }
        if (_lobbyTitle != null)
        {
            string title = FormatLobbyTitle(s, _maxPlayers);
            if (ShouldWriteTmp(_lobbyTitle.text, title))
                _lobbyTitle.SetText(title);
        }
        if (_lobbyStatus != null)
        {
            string status = FormatLobbyStatus(s);
            if (ShouldWriteTmp(_lobbyStatus.text, status))
                _lobbyStatus.SetText(status);
        }
    }

    internal static bool ShowLobbyReady(LanSession s)
    {
        if (s == null)
            return false;
        if (s.IsHost)
            return s.State == LanState.Listen || s.State == LanState.Connected;
        return s.State == LanState.Connected;
    }

    internal static string LeaveLabel(LanSession s)
    {
        return ShowLobbyReady(s) ? "离开房间" : "取消";
    }

    internal static string FormatLobbyTitle(LanSession s, int fallbackMax)
    {
        if (s == null || s.State == LanState.Connecting || s.State == LanState.Fail || s.State == LanState.Drop)
            return "局域网房间";
        int pc = s.PlayerCount < 1 ? 1 : s.PlayerCount;
        int max = s.MaxPlayers > 0 ? s.MaxPlayers : fallbackMax;
        return "局域网房间  " + pc.ToString() + "/" + max.ToString();
    }

    internal static string FormatLobbyStatus(LanSession s)
    {
        if (s == null)
            return "";
        var sb = new StringBuilder();
        switch (s.State)
        {
            case LanState.Listen:
                sb.Append("等待加入\n");
                sb.Append(LanLocalIp.FormatAdvertise(Plugin.ListenPort));
                break;
            case LanState.Connecting:
                string peer = s.PeerEndPoint != null && s.PeerEndPoint.Length > 0 ? s.PeerEndPoint : "";
                sb.Append(peer.Length > 0 ? ("连接中  " + peer) : "连接中");
                int left = Plugin.Transport != null ? Plugin.Transport.ConnectSecondsLeft() : -1;
                if (left >= 0)
                    sb.Append("\n剩余 ").Append(left.ToString()).Append("s");
                break;
            case LanState.Connected:
                int readyN = LanRoom.CountSeatedReady(
                    s.ReadyMask, s.OccupiedMask, s.PlayerCount, s.MaxPlayers,
                    s.LocalReady, s.LocalSlot, out int seated);
                string ready = seated >= 2 && readyN >= seated
                    ? "全员已准备"
                    : (s.LocalReady ? "已准备，等待其他人" : "未准备");
                sb.Append(ready).Append("  ").Append(readyN.ToString()).Append('/').Append(seated.ToString());
                break;
            case LanState.Fail:
                return s.FailReason != null && s.FailReason.Length > 0 ? ("失败: " + s.FailReason) : "连接失败";
            case LanState.Drop:
                return "主机已离开";
            default:
                return "";
        }
        return sb.ToString();
    }

    private static int TryReadMaxFromClone()
    {
        GameObject root = _create;
        if (root == null)
            return _maxPlayers;
        TMP_Dropdown[] tmps = root.GetComponentsInChildren<TMP_Dropdown>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            int n = ParseMaxDropdownTmp(tmps[i]);
            if (n > 0)
                return n;
        }
        Dropdown[] uis = root.GetComponentsInChildren<Dropdown>(true);
        for (int i = 0; i < uis.Length; i++)
        {
            int n = ParseMaxDropdownUi(uis[i]);
            if (n > 0)
                return n;
        }
        return _maxPlayers;
    }

    private static int ParseMaxDropdownTmp(TMP_Dropdown dd)
    {
        if (dd == null)
            return 0;
        try
        {
            if (dd.options != null && dd.value >= 0 && dd.value < dd.options.Count)
            {
                int n = ParseMaxText(dd.options[dd.value].text);
                if (n > 0)
                    return n;
            }
        }
        catch
        {
        }
        return 0;
    }

    private static int ParseMaxDropdownUi(Dropdown dd)
    {
        if (dd == null)
            return 0;
        try
        {
            if (dd.options != null && dd.value >= 0 && dd.value < dd.options.Count)
            {
                int n = ParseMaxText(dd.options[dd.value].text);
                if (n > 0)
                    return n;
            }
        }
        catch
        {
        }
        return 0;
    }

    private static int ParseMaxText(string s)
    {
        if (string.IsNullOrEmpty(s))
            return 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] < '0' || s[i] > '9')
                continue;
            int v = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                v = v * 10 + (s[i] - '0');
                i++;
            }
            if (v == 2 || v == 3 || v == 6)
                return v;
            return LanRoom.ClampMax(v);
        }
        return 0;
    }

    private static void CenterHostJoin(RectTransform createRt, RectTransform joinRt)
    {
        if (createRt == null || joinRt == null)
            return;
        Vector2 c = createRt.anchoredPosition;
        createRt.anchoredPosition = new Vector2(c.x, 36f);
        joinRt.anchoredPosition = new Vector2(c.x, -36f);
    }

    private static void StripNoise(GameObject root, bool hideMaxPlayers)
    {
        if (root == null)
            return;
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = texts.Length - 1; i >= 0; i--)
        {
            TMP_Text t = texts[i];
            if (t == null || t.text == null)
                continue;
            string s = t.text.Trim();
            bool kill = s.IndexOf("找人同玩", StringComparison.Ordinal) >= 0
                || s.IndexOf("Fill all fields", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Discord", StringComparison.OrdinalIgnoreCase) >= 0
                || (hideMaxPlayers && (s.IndexOf("人数上限", StringComparison.Ordinal) >= 0
                    || s.IndexOf("Max Player", StringComparison.OrdinalIgnoreCase) >= 0));
            if (!kill)
                continue;
            Transform group = t.transform.parent;
            if (group == null || group.gameObject == root)
            {
                t.gameObject.SetActive(false);
                continue;
            }
            try { Object.DestroyImmediate(group.gameObject); }
            catch { group.gameObject.SetActive(false); }
        }

        if (!hideMaxPlayers)
            return;
        TMP_Dropdown[] tmps = root.GetComponentsInChildren<TMP_Dropdown>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            if (tmps[i] != null)
                tmps[i].gameObject.SetActive(false);
        }
        Dropdown[] uis = root.GetComponentsInChildren<Dropdown>(true);
        for (int i = 0; i < uis.Length; i++)
        {
            if (uis[i] != null)
                uis[i].gameObject.SetActive(false);
        }
    }

    private static void EnsureLobbyTitle(GameObject root, Button near)
    {
        _lobbyTitle = FindTitle(root);
        if (_lobbyTitle != null)
            return;
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null)
            {
                _lobbyTitle = texts[i];
                return;
            }
        }
    }

    private static void DestroyLabeledGroups(GameObject clone)
    {
        TMP_Text[] texts = clone.GetComponentsInChildren<TMP_Text>(true);
        for (int i = texts.Length - 1; i >= 0; i--)
        {
            TMP_Text t = texts[i];
            if (t == null || t.text == null)
                continue;
            if (!IsRemovedFieldLabel(t.text.Trim()))
                continue;
            Transform group = t.transform.parent;
            if (group == null || group.gameObject == clone)
                continue;
            Object.DestroyImmediate(group.gameObject);
        }
    }

    private static void DestroySaveCopy(MainMenu menu, GameObject src, GameObject clone)
    {
        GameObject save = menu.selectSaveFileMenu;
        if (save == null || src == null || !save.transform.IsChildOf(src.transform))
            return;
        string rel = RelPath(src.transform, save.transform);
        Transform copy = string.IsNullOrEmpty(rel) ? null : clone.transform.Find(rel);
        if (copy != null)
        {
            Object.Destroy(copy.gameObject);
            Plugin.LogSrc.LogInfo("[SatmLanIp] clone stripped save copy " + rel);
        }
    }

    private static void StripPhotonBits(GameObject go)
    {
        Behaviour[] all = go.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Behaviour b = all[i];
            if (b == null)
                continue;
            string n = b.GetType().Name;
            if (n == "CreateLobbySettings" || n == "LobbyUIManager" || n == "FusionNetworkManager")
            {
                b.enabled = false;
                try { Object.DestroyImmediate(b); }
                catch { Object.Destroy(b); }
            }
        }
    }

    private static Button FindPrimaryActionButton(GameObject root)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        Button best = null;
        float bestArea = 0f;
        for (int i = 0; i < buttons.Length; i++)
        {
            Button b = buttons[i];
            if (b == null)
                continue;
            if (b.GetComponentInParent<TMP_Dropdown>() != null)
                continue;
            if (b.GetComponentInParent<Dropdown>() != null)
                continue;
            RectTransform rt = b.GetComponent<RectTransform>();
            if (rt == null)
                continue;
            float area = Mathf.Abs(rt.rect.width * rt.rect.height);
            if (area > bestArea)
            {
                bestArea = area;
                best = b;
            }
        }
        return best;
    }

    private static void ShowOverlayCreate()
    {
        HideCreate();
        Button template = FindAnyMenuButton();
        GameObject root = new GameObject(CreateName);
        Object.DontDestroyOnLoad(root);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();

        if (template != null)
        {
            Button host = OverlayClone(template, root.transform, "创建房间", (UnityAction)OnCreateClicked, new Vector2(0f, 70f));
            Button join = OverlayClone(template, root.transform, "加入房间", (UnityAction)OnJoinClicked, new Vector2(0f, 0f));
            OverlayClone(template, root.transform, "返回", (UnityAction)OnCreateBack, new Vector2(0f, -70f));
            _joinBtn = join;
            if (host != null)
                host.interactable = true;
        }
        else
            Plugin.LogSrc.LogWarning("[SatmLanIp] overlay create: no template Button");

        _create = root;
        RefreshCreateChrome();
        UnlockCursor();
        Plugin.LogSrc.LogInfo("[SatmLanIp] overlay create shown");
    }

    private static Button OverlayClone(Button template, Transform parent, string label, UnityAction click, Vector2 pos)
    {
        GameObject go = Object.Instantiate(template.gameObject, parent);
        go.name = "SatmLanIp_" + label;
        KillLanguageText(go);
        StripClicks(go);
        ApplyLabel(go, label);
        Button btn = go.GetComponent<Button>();
        if (btn == null)
            btn = go.GetComponentInChildren<Button>(true);
        if (btn != null)
        {
            btn.onClick.AddListener(click);
            btn.interactable = true;
        }
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            if (rt.sizeDelta.x < 120f)
                rt.sizeDelta = new Vector2(280f, 48f);
        }
        go.SetActive(true);
        return btn;
    }

    private static Button FindAnyMenuButton()
    {
        MainMenu menu = FindMenu();
        if (menu != null && menu.playMenu != null)
        {
            Button[] playBtns = menu.playMenu.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < playBtns.Length; i++)
            {
                if (playBtns[i] != null)
                    return playBtns[i];
            }
        }
        try
        {
            Button[] all = Object.FindObjectsOfType<Button>();
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && !IsLanClone(all[i].transform))
                        return all[i];
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static Button FindButtonByName(GameObject root, params string[] names)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button b = buttons[i];
            if (b == null)
                continue;
            string n = b.gameObject.name ?? "";
            for (int k = 0; k < names.Length; k++)
            {
                if (n.IndexOf(names[k], StringComparison.OrdinalIgnoreCase) >= 0)
                    return b;
            }
        }
        return null;
    }

    private static Button FindButton(GameObject root, Func<string, bool> match)
    {
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text t = texts[i];
            if (t == null || t.text == null)
                continue;
            if (!match(t.text.Trim()))
                continue;
            Button b = t.GetComponentInParent<Button>();
            if (b != null)
                return b;
        }
        return null;
    }

    private static TMP_Text FindTitle(GameObject root)
    {
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text best = null;
        float bestSize = 0f;
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text t = texts[i];
            if (t == null || t.text == null)
                continue;
            string s = t.text.Trim();
            if (s.IndexOf("房间", StringComparison.Ordinal) < 0 &&
                s.IndexOf("Room", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (IsLeaveLabel(s) || IsCreateRoomLabel(s) || IsInviteLabel(s) || IsReadyLabel(s))
                continue;
            float sz = t.fontSize;
            if (sz > bestSize)
            {
                bestSize = sz;
                best = t;
            }
        }
        return best;
    }

    private static bool IsRemovedFieldLabel(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 24)
            return false;
        if (s.IndexOf("人数", StringComparison.Ordinal) >= 0 ||
            s.IndexOf("Max Player", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        return s.IndexOf("大厅名称", StringComparison.Ordinal) >= 0
            || s.IndexOf("房间类型", StringComparison.Ordinal) >= 0
            || s.IndexOf("创建密码", StringComparison.Ordinal) >= 0
            || s.IndexOf("Lobby Name", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("Room Type", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("Create Password", StringComparison.OrdinalIgnoreCase) >= 0
            || s.Equals("名称", StringComparison.Ordinal)
            || s.Equals("类型", StringComparison.Ordinal)
            || s.Equals("密码", StringComparison.Ordinal)
            || s.Equals("加入地址", StringComparison.Ordinal)
            || s.Equals("端口", StringComparison.Ordinal);
    }

    private static bool IsCreateRoomLabel(string s)
    {
        return s.IndexOf("创建房间", StringComparison.Ordinal) >= 0
            || s.IndexOf("Create Room", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("Host Lobby", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsBackLabel(string s)
    {
        return s.Equals("返回", StringComparison.Ordinal)
            || s.Equals("Back", StringComparison.OrdinalIgnoreCase)
            || s.IndexOf("返回", StringComparison.Ordinal) >= 0 && s.Length <= 8;
    }

    private static bool IsLeaveLabel(string s)
    {
        return s.IndexOf("离开房间", StringComparison.Ordinal) >= 0
            || s.IndexOf("Leave Room", StringComparison.OrdinalIgnoreCase) >= 0
            || s.Equals("离开", StringComparison.Ordinal);
    }

    private static bool IsInviteLabel(string s)
    {
        return s.IndexOf("邀请", StringComparison.Ordinal) >= 0
            || s.IndexOf("Invite", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsReadyLabel(string s)
    {
        return s.Equals("准备", StringComparison.Ordinal)
            || s.Equals("Ready", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStartLabel(string s)
    {
        return s.IndexOf("开始游戏", StringComparison.Ordinal) >= 0
            || s.Equals("开始", StringComparison.Ordinal)
            || s.IndexOf("Start Game", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void StripClicks(GameObject go)
    {
        EventTrigger[] triggers = go.GetComponentsInChildren<EventTrigger>(true);
        for (int i = 0; i < triggers.Length; i++)
        {
            if (triggers[i] != null)
                triggers[i].triggers.Clear();
        }
        Button[] buttons = go.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null)
                buttons[i].onClick = new Button.ButtonClickedEvent();
        }
    }

    private static void KillLanguageText(GameObject go)
    {
        LanguageText[] loc = go.GetComponentsInChildren<LanguageText>(true);
        for (int i = 0; i < loc.Length; i++)
        {
            LanguageText lt = loc[i];
            if (lt == null)
                continue;
            lt.enabled = false;
            try { Object.DestroyImmediate(lt); }
            catch { Object.Destroy(lt); }
        }
    }

    private static void ApplyLabel(GameObject go, string label)
    {
        TMP_Text[] texts = go.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text t = texts[i];
            if (t == null || IsLatinOnlyTmp(t))
                continue;
            if (!ShouldWriteTmp(t.text, label))
                continue;
            t.SetText(label);
        }
    }

    private static string RelPath(Transform root, Transform t)
    {
        if (root == null || t == null)
            return "";
        var sb = new StringBuilder();
        Transform cur = t;
        while (cur != null && cur != root)
        {
            if (sb.Length > 0)
                sb.Insert(0, "/");
            sb.Insert(0, cur.name);
            cur = cur.parent;
        }
        return cur == root ? sb.ToString() : "";
    }

    private static void DumpTmp(GameObject go, string tag)
    {
        TMP_Text[] texts = go.GetComponentsInChildren<TMP_Text>(true);
        var sb = new StringBuilder("[SatmLanIp] dump " + tag);
        int n = 0;
        for (int i = 0; i < texts.Length && n < 24; i++)
        {
            if (texts[i] == null || string.IsNullOrEmpty(texts[i].text))
                continue;
            sb.Append(" | ").Append(texts[i].text.Replace('\n', ' '));
            n++;
        }
        Plugin.LogSrc.LogInfo(sb.ToString());
    }

    private static GameObject FindByLeaveLabel()
    {
        TMP_Text[] texts;
        try { texts = Resources.FindObjectsOfTypeAll<TMP_Text>(); }
        catch { return null; }
        if (texts == null)
            return null;
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text t = texts[i];
            if (t == null || t.text == null)
                continue;
            if (!IsLeaveLabel(t.text.Trim()))
                continue;
            if (IsLanClone(t.transform))
                continue;
            Transform root = t.transform;
            while (root.parent != null)
            {
                if (root.parent.GetComponent<Canvas>() != null)
                    break;
                string pn = root.parent.name ?? "";
                if (pn.IndexOf("Lobby", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    root = root.parent;
                    break;
                }
                root = root.parent;
            }
            return root.gameObject;
        }
        return null;
    }

    private static MainMenu FindMenu()
    {
        try { return Object.FindObjectOfType<MainMenu>(); }
        catch { return null; }
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

    /// Overlay labels Instantiating the create-panel's first TMP hit a latin/number face
    /// (screenshot: 局域网房间 → □□□□). Action-button TMP already draws CJK — prefer that.
    internal static T PreferButtonSample<T>(T createFirst, T buttonLabel) where T : class
    {
        return buttonLabel ?? createFirst;
    }

    internal static bool IsLatinOnlyFontName(string fontName)
    {
        if (string.IsNullOrEmpty(fontName))
            return false;
        return fontName.IndexOf("ENGLISH FONT", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool ShouldWriteTmp(string current, string next)
    {
        if (next == null)
            next = "";
        return current != next;
    }

    internal static void SelfCheck()
    {
        if (PreferButtonSample("create-first", "button") != "button")
            throw new InvalidOperationException("SatmLanIp overlay TMP must use button CJK face");
        if (PreferButtonSample("create-first", (string)null) != "create-first")
            throw new InvalidOperationException("SatmLanIp overlay TMP fallback sample");
        if (!IsLatinOnlyFontName("ENGLISH FONT") || IsLatinOnlyFontName("NotoSansSC"))
            throw new InvalidOperationException("SatmLanIp latin-only font name");
        if (ShouldWriteTmp("局域网房间  1/3", "局域网房间  1/3")
            || !ShouldWriteTmp("准备", "取消准备"))
            throw new InvalidOperationException("SatmLanIp skip unchanged TMP write");
        if (ParseMaxText("3") != 3 || ParseMaxText("人数 6") != 6 || LanRoom.ClampMax(ParseMaxText("2")) != 2)
            throw new InvalidOperationException("SatmLanIp LanCloneUi ParseMaxText");
        var listen = new LanSession { State = LanState.Listen, IsHost = true, PlayerCount = 1, MaxPlayers = 3 };
        if (FormatLobbyTitle(listen, 3) != "局域网房间  1/3")
            throw new InvalidOperationException("SatmLanIp listen title is player/max not ready");
        if (!FormatLobbyStatus(listen).StartsWith("等待加入\n", StringComparison.Ordinal))
            throw new InvalidOperationException("SatmLanIp listen status");
        var connecting = new LanSession { State = LanState.Connecting, PlayerCount = 1, MaxPlayers = 2 };
        if (FormatLobbyTitle(connecting, 3) != "局域网房间")
            throw new InvalidOperationException("SatmLanIp LanCloneUi connecting title");
        if (ShowLobbyReady(connecting) || LeaveLabel(connecting) != "取消")
            throw new InvalidOperationException("SatmLanIp LanCloneUi connecting chrome");
        var fail = new LanSession { State = LanState.Fail, FailReason = "connect timeout" };
        if (FormatLobbyStatus(fail) != "失败: connect timeout")
            throw new InvalidOperationException("SatmLanIp LanCloneUi fail status");
        var host = new LanSession
        {
            State = LanState.Connected,
            IsHost = true,
            PlayerCount = 2,
            MaxPlayers = 3,
            OccupiedMask = 3,
            ReadyMask = 3,
        };
        if (FormatLobbyTitle(host, 3) != "局域网房间  2/3")
            throw new InvalidOperationException("SatmLanIp LanCloneUi connected title");
        if (FormatLobbyStatus(host) != "全员已准备  2/2")
            throw new InvalidOperationException("SatmLanIp LanCloneUi ready vs seat: " + FormatLobbyStatus(host));
        host.ReadyMask = 1;
        host.LocalReady = true;
        if (FormatLobbyStatus(host) != "已准备，等待其他人  1/2")
            throw new InvalidOperationException("SatmLanIp LanCloneUi partial ready: " + FormatLobbyStatus(host));
        var three = new LanSession
        {
            State = LanState.Connected,
            IsHost = true,
            PlayerCount = 3,
            MaxPlayers = 3,
            OccupiedMask = 0x3F,
            ReadyMask = 1,
            LocalReady = true,
            LocalSlot = 0,
        };
        if (FormatLobbyStatus(three) != "已准备，等待其他人  1/3")
            throw new InvalidOperationException("SatmLanIp LanCloneUi 3p ready num: " + FormatLobbyStatus(three));
        three.ReadyMask = 7;
        if (FormatLobbyStatus(three) != "全员已准备  3/3")
            throw new InvalidOperationException("SatmLanIp LanCloneUi 3p all ready: " + FormatLobbyStatus(three));
        if (!ShowLobbyReady(host) || LeaveLabel(host) != "离开房间")
            throw new InvalidOperationException("SatmLanIp LanCloneUi host chrome");
    }
}
