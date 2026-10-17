using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// One JSON save file (persistentDataPath/mazraa_save.json) with a PlayerPrefs-style API.
/// Old PlayerPrefs values are read once as a fallback, so existing progress carries over.
/// Writes are batched: Save() marks the file dirty and it is flushed at most every 2 s and on quit.
/// </summary>
public static class SaveData
{
    [Serializable] private class Entry { public string k; public string v; public int t; } // t: 0 int, 1 float, 2 string
    [Serializable] private class FileData { public int version = 1; public string savedAt; public List<Entry> entries = new List<Entry>(); }

    private static Dictionary<string, Entry> map;
    private static Dictionary<string, Entry> homeMap;   // the player's own save while a co-op session uses a throwaway map
    private static bool dirty;
    private static float lastFlush;
    private static GameObject flusher;
    public static string Path => System.IO.Path.Combine(Application.persistentDataPath, "mazraa_save.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { map = null; homeMap = null; dirty = false; flusher = null; }

    private static void Load()
    {
        if (map != null) return;
        map = new Dictionary<string, Entry>();
        try
        {
            if (File.Exists(Path))
            {
                var f = JsonUtility.FromJson<FileData>(File.ReadAllText(Path));
                if (f?.entries != null) foreach (var e in f.entries) if (e != null && e.k != null) map[e.k] = e;
            }
        }
        catch (Exception ex) { Debug.LogWarning("[SaveData] Could not read save, starting fresh: " + ex.Message); }
        if (flusher != null) return;
        flusher = new GameObject("SaveData Flusher") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(flusher);
        flusher.AddComponent<SaveFlusher>();
    }

    /// <summary>Forget the in-memory values and read the file again (after a save slot was copied over it).</summary>
    public static void Reload() { map = null; dirty = false; }

    public static int GetInt(string k, int d = 0) { Load(); return map.TryGetValue(k, out var e) && int.TryParse(e.v, out var r) ? r : homeMap != null ? d : PlayerPrefs.GetInt(k, d); }
    public static float GetFloat(string k, float d = 0f) { Load(); return map.TryGetValue(k, out var e) && float.TryParse(e.v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : homeMap != null ? d : PlayerPrefs.GetFloat(k, d); }
    public static string GetString(string k, string d = "") { Load(); return map.TryGetValue(k, out var e) ? e.v : homeMap != null ? d : PlayerPrefs.GetString(k, d); }
    public static bool HasKey(string k) { Load(); return map.ContainsKey(k) || (homeMap == null && PlayerPrefs.HasKey(k)); }

    /// <summary>True while a co-op client session is active (writes go to a copy that is never saved).</summary>
    public static bool InSession => homeMap != null;

    /// <summary>
    /// Co-op client joined someone's farm: from now on reads and writes use a copy of the save, and the file on disk
    /// keeps the player's own farm. EndSession() switches back (the copy is thrown away).
    /// </summary>
    public static void BeginSession()
    {
        Load();
        if (homeMap != null) return;
        homeMap = map;
        map = new Dictionary<string, Entry>(homeMap);
    }

    public static void EndSession()
    {
        if (homeMap == null) return;
        map = homeMap;
        homeMap = null;
    }

    public static void SetInt(string k, int v) => Set(k, v.ToString(), 0);
    public static void SetFloat(string k, float v) => Set(k, v.ToString("R", System.Globalization.CultureInfo.InvariantCulture), 1);
    public static void SetString(string k, string v) => Set(k, v ?? "", 2);
    public static void DeleteKey(string k) { Load(); map.Remove(k); if (homeMap == null) PlayerPrefs.DeleteKey(k); dirty = true; }

    private static void Set(string k, string v, int t)
    {
        Load();
        if (map.TryGetValue(k, out var e) && e.v == v) return;
        map[k] = new Entry { k = k, v = v, t = t };
        dirty = true;
    }

    /// <summary>Mark for writing (flushed within ~2 s).</summary>
    public static void Save() => dirty = true;

    public static void Flush(bool force = false)
    {
        if (map == null || (!dirty && !force)) return;
        try
        {
            var f = new FileData { savedAt = DateTime.UtcNow.ToString("o") };
            f.entries.AddRange((homeMap ?? map).Values);   // during a co-op session only the player's own farm is written
            File.WriteAllText(Path, JsonUtility.ToJson(f, true));
            dirty = false;
        }
        catch (Exception ex) { Debug.LogWarning("[SaveData] Could not write save: " + ex.Message); }
    }

    /// <summary>Delete the save file and all keys (new game).</summary>
    public static void DeleteAll()
    {
        Load();
        foreach (var k in new List<string>(map.Keys))
        {
            if (k == "mazraa.language" || k.StartsWith("mazraa.gfx") || k.StartsWith("mazraa.vol.")) continue; // keep settings
            if (homeMap == null) PlayerPrefs.DeleteKey(k);
            map.Remove(k);
        }
        dirty = true;
        Flush();
    }

    /// <summary>Called every frame by SaveFlusher.</summary>
    public static void Tick() { if (dirty && Time.unscaledTime - lastFlush > 2f) { lastFlush = Time.unscaledTime; Flush(); } }
}
