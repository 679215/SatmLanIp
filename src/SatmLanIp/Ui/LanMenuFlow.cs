using System;

namespace SatmLanIp;

internal static class LanMenuFlow
{
    public static bool Pending { get; private set; }
    public static bool PanelOpen { get; set; }
    public static bool SavePicked { get; private set; }
    public static bool InLobby { get; set; }
    public static bool HostSavePending { get; private set; }
    public static int PendingMaxPlayers { get; private set; } = 3;
    /// <summary>≥0 when host finished save/mode UI with an explicit slot.</summary>
    public static int HostSaveSlot { get; private set; } = -1;

    public static void Arm()
    {
        Pending = true;
        PanelOpen = false;
        SavePicked = false;
        InLobby = false;
        HostSavePending = false;
        HostSaveSlot = -1;
        if (Plugin.LogSrc != null)
            Plugin.LogSrc.LogInfo("[SatmLanIp] menu_flow armed (LAN panel → create/join; save after create)");
    }

    public static void BeginHostSave(int maxPlayers)
    {
        HostSavePending = true;
        PanelOpen = false;
        SavePicked = false;
        HostSaveSlot = -1;
        PendingMaxPlayers = maxPlayers < 2 ? 2 : maxPlayers;
    }

    public static void ClearHostSavePending()
    {
        HostSavePending = false;
    }

    public static void CancelHostSave()
    {
        HostSavePending = false;
        PanelOpen = true;
        SavePicked = false;
        HostSaveSlot = -1;
    }

    public static void MarkSavePicked()
    {
        MarkSavePicked(-1);
    }

    public static void MarkSavePicked(int slot)
    {
        SavePicked = true;
        HostSaveSlot = slot;
    }

    public static void EnterLobby()
    {
        PanelOpen = false;
        InLobby = true;
    }

    public static void ReturnToCreate()
    {
        InLobby = false;
        PanelOpen = true;
    }

    public static void Clear()
    {
        Pending = false;
        PanelOpen = false;
        SavePicked = false;
        InLobby = false;
        HostSavePending = false;
        HostSaveSlot = -1;
    }

    public static bool ShouldHijackLobbySettings()
    {
        return (Pending || HostSavePending) && Plugin.Enabled && Plugin.ShowNativeMenu;
    }

    public static void ConsumePending()
    {
        Pending = false;
    }

    internal static void SelfCheck()
    {
        bool wasPending = Pending;
        bool wasOpen = PanelOpen;
        bool wasSave = SavePicked;
        bool wasLobby = InLobby;
        bool wasHostSave = HostSavePending;
        Arm();
        if (!Pending || SavePicked || PanelOpen || InLobby || HostSavePending)
            throw new InvalidOperationException("SatmLanIp LanMenuFlow Arm state");
        BeginHostSave(3);
        if (!HostSavePending || PanelOpen || PendingMaxPlayers != 3)
            throw new InvalidOperationException("SatmLanIp LanMenuFlow BeginHostSave");
        CancelHostSave();
        if (HostSavePending || !PanelOpen)
            throw new InvalidOperationException("SatmLanIp LanMenuFlow CancelHostSave");
        BeginHostSave(6);
        ClearHostSavePending();
        if (HostSavePending || PendingMaxPlayers != 6)
            throw new InvalidOperationException("SatmLanIp LanMenuFlow ClearHostSavePending");
        MarkSavePicked(2);
        if (!SavePicked || HostSaveSlot != 2)
            throw new InvalidOperationException("SatmLanIp LanMenuFlow MarkSavePicked slot");
        ConsumePending();
        if (Pending)
            throw new InvalidOperationException("SatmLanIp LanMenuFlow ConsumePending");
        EnterLobby();
        if (!InLobby || PanelOpen)
            throw new InvalidOperationException("SatmLanIp LanMenuFlow EnterLobby");
        ReturnToCreate();
        if (InLobby || !PanelOpen)
            throw new InvalidOperationException("SatmLanIp LanMenuFlow ReturnToCreate");
        Clear();
        if (Pending || PanelOpen || SavePicked || InLobby || HostSavePending || HostSaveSlot >= 0)
            throw new InvalidOperationException("SatmLanIp LanMenuFlow Clear");
        Pending = wasPending;
        PanelOpen = wasOpen;
        SavePicked = wasSave;
        InLobby = wasLobby;
        HostSavePending = wasHostSave;
    }
}
