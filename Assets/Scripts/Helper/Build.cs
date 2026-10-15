using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Helpers for building simple runtime geometry (placeholder models until real CC0 models are imported)
/// and for finding the ground. If a prefab exists at Resources/Models/&lt;name&gt; it is used instead.
/// </summary>
public static class Build
{
    private static Shader litShader;
    private static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

    /// <summary>Layers to ignore when looking for the ground (Outline, Interactable, Player, Ignore Raycast).</summary>
    public const int NotGroundMask = (1 << 2) | (1 << 6) | (1 << 7) | (1 << 8);
    public static int GroundMask => ~NotGroundMask;

    public static Material Mat(Color c, float smooth = 0.25f, float metal = 0f, Color? emission = null)
    {
        string key = ColorUtility.ToHtmlStringRGBA(c) + smooth + metal + (emission.HasValue ? ColorUtility.ToHtmlStringRGB(emission.Value) : "");
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Standard");
        m = new Material(litShader);
        m.SetColor("_BaseColor", c);
        m.color = c;
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        cache[key] = m;
        return m;
    }

    /// <summary>A primitive part without a collider.</summary>
    public static GameObject Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat, string name = null, Vector3? euler = null)
    {
        var go = GameObject.CreatePrimitive(type);
        if (name != null) go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        return go;
    }

    /// <summary>Instantiate Resources/Models/name if it exists, else null.</summary>
    public static GameObject TryModel(string name, Transform parent)
    {
        var prefab = Resources.Load<GameObject>("Models/" + name);
        if (prefab == null) return null;
        var go = Object.Instantiate(prefab, parent, false);
        go.name = name;
        return go;
    }

    public static bool GroundAt(Vector3 pos, out RaycastHit hit)
    {
        return Physics.Raycast(new Vector3(pos.x, pos.y + 60f, pos.z), Vector3.down, out hit, 200f, GroundMask, QueryTriggerInteraction.Ignore);
    }

    public static float GroundY(Vector3 pos)
    {
        if (GroundAt(pos, out var hit)) return hit.point.y;
        var t = Terrain.activeTerrain;
        return t != null ? t.SampleHeight(pos) + t.transform.position.y : pos.y;
    }

    public static Vector3 OnGround(Vector3 pos, float lift = 0f) => new Vector3(pos.x, GroundY(pos) + lift, pos.z);

    /// <summary>A world-space label that always faces the camera.</summary>
    public static TMPro.TextMeshPro Label(Transform parent, Vector3 localPos, string englishKey, float size, Color color)
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
        var l = go.AddComponent<LocalizedLabel>();
        l.key = englishKey;
        return t;
    }
}

