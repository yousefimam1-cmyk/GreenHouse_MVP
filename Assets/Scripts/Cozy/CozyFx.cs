using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>Shared helpers for the cozy features: player lookup, day, alerts, labels, interactable props, tints, gifts, saved sets.</summary>
public static class CozyFx
{
    private static Transform player;
    private static Vector3? start;
    private static readonly Dictionary<Material, Color> originalColors = new Dictionary<Material, Color>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { player = null; start = null; originalColors.Clear(); }

    public static Transform Player
    {
        get
        {
            if (player == null)
            {
                var p = UnityEngine.Object.FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
                if (p != null) player = p.transform;
            }
            return player;
        }
    }

    /// <summary>Where the player started (the farmhouse area). Recorded the first time it is asked.</summary>
    public static Vector3 FarmStart
    {
        get
        {
            if (!start.HasValue || player == null) { var p = Player; if (p != null) start = p.position; }
            return start ?? Vector3.zero;
        }
    }

    public static int Today => WorldClock.Instance != null ? WorldClock.Instance.Day : 1;

    public static void Alert(string shaped) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(shaped); }

    public static void SetLayer(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = layer;
    }

    /// <summary>Make a built object interactable with E (collider on the root, Interactable layer).</summary>
    public static CozyProp MakeInteractable(GameObject root, Vector3 center, Vector3 size, Func<string> prompt, Action use)
    {
        var col = root.GetComponent<BoxCollider>();
        if (col == null) col = root.AddComponent<BoxCollider>();
        col.center = center; col.size = size;
        SetLayer(root, 7);
        var p = root.AddComponent<CozyProp>();
        p.prompt = prompt;
        p.use = use;
        return p;
    }

    /// <summary>World label with an English key (translated by LocalizedLabel).</summary>
    public static TMPro.TextMeshPro Label(Transform parent, Vector3 localPos, string key, float size, Color color)
    {
        var t = NewLabel(parent, localPos, size, color);
        var l = t.gameObject.AddComponent<LocalizedLabel>();
        l.key = key;
        return t;
    }

    /// <summary>World label with data text (already translated, unshaped).</summary>
    public static TMPro.TextMeshPro DataLabel(Transform parent, Vector3 localPos, Func<string> text, float size, Color color)
    {
        var t = NewLabel(parent, localPos, size, color);
        var l = t.gameObject.AddComponent<CozyLabel>();
        l.text = text;
        return t;
    }

    private static TMPro.TextMeshPro NewLabel(Transform parent, Vector3 localPos, float size, Color color)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var t = go.AddComponent<TMPro.TextMeshPro>();
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.fontSize = size;
        t.color = color;
        t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        Loc.ExcludeFromScene(t);
        go.AddComponent<Billboard>();
        return t;
    }

    /// <summary>Little pink hearts floating up (petting, friendship).</summary>
    public static void Hearts(Vector3 pos)
    {
        var mat = Build.Mat(new Color(1f, 0.35f, 0.55f), 0.6f, 0f, new Color(1.2f, 0.3f, 0.5f));
        for (int i = 0; i < 4; i++)
        {
            var h = new GameObject("Heart");
            h.transform.position = pos + new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f), 0.2f * i, UnityEngine.Random.Range(-0.4f, 0.4f));
            Build.Part(PrimitiveType.Sphere, h.transform, new Vector3(-0.06f, 0, 0), Vector3.one * 0.14f, mat);
            Build.Part(PrimitiveType.Sphere, h.transform, new Vector3(0.06f, 0, 0), Vector3.one * 0.14f, mat);
            Build.Part(PrimitiveType.Cube, h.transform, new Vector3(0, -0.07f, 0), Vector3.one * 0.13f, mat, null, new Vector3(0, 0, 45));
            h.AddComponent<Billboard>();
            var go = h;
            var tw = h.transform.DOMoveY(h.transform.position.y + 1.5f, 1.6f);
            if (tw != null) tw.SetEase(Ease.OutCubic).OnComplete(() => UnityEngine.Object.Destroy(go));
            else UnityEngine.Object.Destroy(go, 1.6f);
        }
    }

    /// <summary>Tint every material under root towards a colour (amount 0..1). Original colours are kept, so tints don't stack.</summary>
    public static void Tint(GameObject root, Color color, float amount, string skipName = null)
    {
        if (root == null) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (skipName != null && IsUnder(r.transform, root.transform, skipName)) continue;
            var mats = r.materials;
            if (mats == null) continue;
            foreach (var m in mats)
            {
                if (m == null) continue;
                Color orig;
                if (!originalColors.TryGetValue(m, out orig)) { orig = m.color; originalColors[m] = orig; }
                Color c = Color.Lerp(orig, new Color(orig.r * color.r, orig.g * color.g, orig.b * color.b, orig.a), amount);
                c = Color.Lerp(c, color, amount * 0.35f);
                Rp.SetColor(m, c);
            }
        }
    }

    private static bool IsUnder(Transform t, Transform root, string name)
    {
        for (var x = t; x != null && x != root; x = x.parent) if (x.name == name) return true;
        return false;
    }

    /// <summary>Simple primitive person when a character model is missing.</summary>
    public static GameObject PrimitivePerson(Transform parent, Color robe)
    {
        var go = new GameObject("Person");
        go.transform.SetParent(parent, false);
        Build.Part(PrimitiveType.Capsule, go.transform, new Vector3(0, 0.75f, 0), new Vector3(0.5f, 0.75f, 0.4f), Build.Mat(robe, 0.2f), "Body");
        Build.Part(PrimitiveType.Sphere, go.transform, new Vector3(0, 1.6f, 0), Vector3.one * 0.32f, Build.Mat(new Color(0.76f, 0.56f, 0.4f), 0.3f), "Head");
        return go;
    }

    /// <summary>Random small gift: seeds, feed or a few riyals. Returns the translated (unshaped) text.</summary>
    public static string RandomGift(int minSar, int maxSar)
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return "";
        float r = UnityEngine.Random.value;
        var crops = eco.AvailableFieldCrops();   // this map's crops only
        if (r < 0.34f && crops.Count > 0)
        {
            var c = crops[UnityEngine.Random.Range(0, crops.Count)];
            eco.AddSeeds(c.id, 1);
            return Loc.Raw("Seeds") + ": " + FieldCropText.Name(c) + " +1";
        }
        if (r < 0.67f)
        {
            eco.AddFeed(2f);
            return Loc.Raw("Feed") + " +2";
        }
        int sar = UnityEngine.Random.Range(minSar, maxSar + 1);
        eco.AddMoney(sar);
        return "+" + sar + " " + Loc.Raw("SAR");
    }

    // ---------------- saved string sets ----------------

    public static HashSet<string> LoadSet(string key)
    {
        var s = new HashSet<string>();
        foreach (var x in SaveData.GetString(key, "").Split(',')) if (!string.IsNullOrEmpty(x)) s.Add(x);
        return s;
    }

    public static void SaveSet(string key, HashSet<string> set)
    {
        SaveData.SetString(key, string.Join(",", new List<string>(set).ToArray()));
        SaveData.Save();
    }

    /// <summary>Delete every saved key with one of these prefixes (new game).</summary>
    public static void ClearKeys(params string[] keys) { foreach (var k in keys) SaveData.DeleteKey(k); SaveData.Save(); }
}
