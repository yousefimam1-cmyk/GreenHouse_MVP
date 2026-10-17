using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stable ids for world objects so machines can name the same thing: the hierarchy path ("GreenHouse/CropA").
/// Every machine builds the same farm (scene + code), so paths match. Same-named siblings get "#2", "#3"...
/// in a fixed order (by position).
/// </summary>
public static class NetIds
{
    private static readonly Dictionary<string, Component> byId = new Dictionary<string, Component>();
    private static readonly Dictionary<Component, string> byObj = new Dictionary<Component, string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear() { byId.Clear(); byObj.Clear(); lastRebuild = -1; }

    private static int lastRebuild = -1;

    public static string Path(Transform t)
    {
        if (t == null) return "";
        var parts = new List<string>();
        for (var x = t; x != null; x = x.parent) parts.Add(x.name);
        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    /// <summary>Id of an interactable or vehicle ("" if none).</summary>
    public static string Of(Component c)
    {
        if (c == null) return "";
        if (byObj.TryGetValue(c, out var id) && byId.TryGetValue(id, out var known) && known == c) return id;
        Rebuild();
        return byObj.TryGetValue(c, out id) ? id : "";
    }

    public static Component Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (byId.TryGetValue(id, out var c) && c != null) return c;
        Rebuild();
        return byId.TryGetValue(id, out c) && c != null ? c : null;
    }

    public static T Find<T>(string id) where T : Component => Find(id) as T;

    /// <summary>Index every Interactable (wells, beds, plots, crates, stalls, vehicles...).</summary>
    public static void Rebuild()
    {
        if (Time.frameCount == lastRebuild && byId.Count > 0) return;   // at most once a frame (unknown ids from the network)
        lastRebuild = Time.frameCount;
        byId.Clear(); byObj.Clear();
        var all = new List<Interactable>(Object.FindObjectsByType<Interactable>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        var withPath = new List<KeyValuePair<string, Interactable>>();
        foreach (var i in all) if (i != null) withPath.Add(new KeyValuePair<string, Interactable>(Path(i.transform), i));
        withPath.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.Key, b.Key);
            if (c != 0) return c;
            Vector3 pa = a.Value.transform.position, pb = b.Value.transform.position;
            c = Mathf.RoundToInt(pa.x * 10f).CompareTo(Mathf.RoundToInt(pb.x * 10f));
            if (c != 0) return c;
            return Mathf.RoundToInt(pa.z * 10f).CompareTo(Mathf.RoundToInt(pb.z * 10f));
        });
        var seen = new Dictionary<string, int>();
        foreach (var kv in withPath)
        {
            int n = seen.TryGetValue(kv.Key, out var k) ? k + 1 : 1;
            seen[kv.Key] = n;
            string id = n == 1 ? kv.Key : kv.Key + "#" + n;
            byId[id] = kv.Value;
            byObj[kv.Value] = id;
        }
    }
}
