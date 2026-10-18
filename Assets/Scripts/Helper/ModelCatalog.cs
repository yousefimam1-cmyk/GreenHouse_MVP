using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Our wrapper around third-party (CC0 placeholder) models: Resources/Models/catalog.json maps a logical model id the
/// game asks for ("Barn", "Utilities/SolarPanel", "TownSedan") to the pack's Resources path
/// (Assets/ThirdParty/&lt;Pack&gt;/Resources/TP_&lt;Pack&gt;/...), plus import fixes (fit size, yaw) and a fallback id.
/// Game code never names third-party paths: it calls Build.TryModel(id), which asks this catalog first, then the
/// fallback id, then Resources/Models/&lt;id&gt;; when nothing loads the caller builds its primitive placeholder.
/// Swapping a placeholder for the final art is one catalog line. See PLACEHOLDERS.md.
/// </summary>
public static class ModelCatalog
{
    public const string CatalogPath = "Models/catalog";

    [Serializable]
    public class Entry
    {
        public string id;          // logical id used by game code
        public string path;        // Resources path of the model (third-party pack)
        public float fit;          // longest horizontal side in metres when the caller passes 0 (0 = keep)
        public float yaw;          // turn so the model faces +Z
        public string fallback;    // id tried when this model is missing (e.g. a headless run or a stripped pack)
        public string group;       // variant group ("town_car")
        public string pack;        // Assets/ThirdParty folder (documentation)
    }

    [Serializable]
    private class FileData { public Entry[] models; }

    private static Dictionary<string, Entry> byId;
    private static Dictionary<string, List<Entry>> byGroup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { byId = null; byGroup = null; }

    private static void Ensure()
    {
        if (byId != null) return;
        byId = new Dictionary<string, Entry>();
        byGroup = new Dictionary<string, List<Entry>>();
        var t = Resources.Load<TextAsset>(CatalogPath);
        if (t == null || string.IsNullOrEmpty(t.text)) return;
        FileData f = null;
        try { f = JsonUtility.FromJson<FileData>(t.text); } catch (Exception e) { Debug.LogWarning("Model catalog: " + e.Message); }
        if (f == null || f.models == null) return;
        foreach (var e in f.models)
        {
            if (e == null || string.IsNullOrEmpty(e.id)) continue;
            byId[e.id] = e;
            if (string.IsNullOrEmpty(e.group)) continue;
            if (!byGroup.TryGetValue(e.group, out var l)) byGroup[e.group] = l = new List<Entry>();
            l.Add(e);
        }
    }

    public static int Count { get { Ensure(); return byId.Count; } }

    public static Entry Find(string id)
    {
        Ensure();
        return id != null && byId.TryGetValue(id, out var e) ? e : null;
    }

    /// <summary>Id of variant <paramref name="index"/> of a group (wraps), or null when the group is empty.</summary>
    public static string Variant(string group, int index)
    {
        Ensure();
        if (group == null || !byGroup.TryGetValue(group, out var l) || l.Count == 0) return null;
        return l[((index % l.Count) + l.Count) % l.Count].id;
    }

    /// <summary>Load the model for an id: catalog path, then its fallback chain, then Resources/Models/id.</summary>
    public static GameObject Load(string id, out Entry entry)
    {
        entry = null;
        string cur = id;
        for (int hop = 0; hop < 4 && !string.IsNullOrEmpty(cur); hop++)
        {
            var e = Find(cur);
            if (e != null && !string.IsNullOrEmpty(e.path))
            {
                var p = Resources.Load<GameObject>(e.path);
                if (p != null) { entry = e; return p; }
            }
            var legacy = Resources.Load<GameObject>("Models/" + cur);
            if (legacy != null) return legacy;
            cur = e != null ? e.fallback : null;
        }
        return null;
    }
}
