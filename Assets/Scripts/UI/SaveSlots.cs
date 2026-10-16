using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Three save slots on top of the single SaveData file: saving copies persistentDataPath/mazraa_save.json to
/// mazraa_slotN.json; loading copies it back and reloads the scene so every system reads the slot's values.
/// Slot summaries ("Day 4 - 1,250 SAR - date") and the active slot live in PlayerPrefs ("mazraa.ui.*"),
/// so a New Game does not erase them. Autosave (each in-game day) writes the save file and the active slot.
/// </summary>
public static class SaveSlots
{
    public const int Count = 3;
    private const string InfoKey = "mazraa.ui.slot";
    private const string ActiveKey = "mazraa.ui.activeSlot";

    private static int pendingSlot;
    private static bool pendingBootstrap;

    /// <summary>Set when a slot is being loaded through a scene reload (the main menu is skipped once).</summary>
    public static bool JustLoaded { get; set; }

    public static string SlotPath(int slot) => Path.Combine(Application.persistentDataPath, "mazraa_slot" + slot + ".json");
    public static bool HasSlot(int slot) => slot >= 1 && slot <= Count && File.Exists(SlotPath(slot));

    /// <summary>Slot the game autosaves into (0 = none: only the main save file is written).</summary>
    public static int ActiveSlot
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(ActiveKey, 0), 0, Count);
        set { PlayerPrefs.SetInt(ActiveKey, Mathf.Clamp(value, 0, Count)); PlayerPrefs.Save(); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Hook()
    {
        pendingSlot = 0; pendingBootstrap = false; JustLoaded = false;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>Write the current game into a slot (1-3).</summary>
    public static bool Save(int slot)
    {
        if (slot < 1 || slot > Count) return false;
        try
        {
            SaveData.Flush(true);
            if (!File.Exists(SaveData.Path)) return false;
            File.Copy(SaveData.Path, SlotPath(slot), true);
        }
        catch (Exception e) { Debug.LogWarning("[SaveSlots] Could not save slot " + slot + ": " + e.Message); return false; }
        int day = WorldClock.Instance != null ? WorldClock.Instance.Day : SaveData.GetInt("mazraa.day", 1);
        int money = EconomyManager.Instance != null ? EconomyManager.Instance.Money : 0;
        PlayerPrefs.SetString(InfoKey + slot, day + "|" + money + "|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        ActiveSlot = slot;
        return true;
    }

    /// <summary>Raw slot info for the main-menu farm cards. False when the slot is empty.</summary>
    public static bool Info(int slot, out int day, out int money, out string saved)
    {
        day = 0; money = 0; saved = "";
        if (!HasSlot(slot)) return false;
        string[] p = PlayerPrefs.GetString(InfoKey + slot, "").Split('|');
        if (p.Length >= 1) int.TryParse(p[0], out day);
        if (p.Length >= 2) int.TryParse(p[1], out money);
        if (p.Length >= 3) saved = p[2];
        return true;
    }

    /// <summary>Translated summary of a slot, or "Empty".</summary>
    public static string Describe(int slot)
    {
        if (!HasSlot(slot)) return UiText.Raw("Empty");
        string[] p = PlayerPrefs.GetString(InfoKey + slot, "").Split('|');
        if (p.Length < 3) return UiText.Raw("Saved game");
        int money; int.TryParse(p[1], out money);
        return Loc.Raw("Day") + " " + p[0] + " - " + money.ToString("N0") + " " + Loc.Raw("SAR") + " - " + p[2];
    }

    /// <summary>Copy a slot over the save file and re-read it (no scene change). Used by Load() and tests.</summary>
    public static bool CopySlotToSave(int slot)
    {
        if (!HasSlot(slot)) return false;
        try { File.Copy(SlotPath(slot), SaveData.Path, true); }
        catch (Exception e) { Debug.LogWarning("[SaveSlots] Could not load slot " + slot + ": " + e.Message); return false; }
        SaveData.Reload();
        ActiveSlot = slot;
        return true;
    }

    /// <summary>Load a slot: reload the scene, swapping the save file once the old scene is gone (so it can't write over it).</summary>
    public static bool Load(int slot)
    {
        if (!HasSlot(slot)) return false;
        pendingSlot = slot;
        pendingBootstrap = true;
        JustLoaded = true;
        Time.timeScale = 1f;
        // Feature registries are refilled by the systems of the new scene.
        HudLines.Status.Clear();
        ShopCatalog.Entries.Clear();
        LoadingScreen.Show();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        return true;
    }

    private static void OnSceneUnloaded(Scene s)
    {
        if (pendingSlot == 0) return;
        int slot = pendingSlot; pendingSlot = 0;
        CopySlotToSave(slot);
    }

    // GameManager bootstraps only on the first scene load; recreate it after a slot reload.
    private static void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        if (!pendingBootstrap) return;
        pendingBootstrap = false;
        if (pendingSlot != 0) { int slot = pendingSlot; pendingSlot = 0; CopySlotToSave(slot); }
        if (GameManager.Instance == null && UnityEngine.Object.FindAnyObjectByType<GreenhouseManager>() != null)
            new GameObject("GameManager (auto)").AddComponent<GameManager>();
    }

    /// <summary>Daily autosave: the save file, plus the active slot if the player uses one.</summary>
    public static void Autosave()
    {
        SaveData.Flush(true);
        if (ActiveSlot > 0) Save(ActiveSlot);
    }
}
