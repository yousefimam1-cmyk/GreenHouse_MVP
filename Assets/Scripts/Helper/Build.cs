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
    public static GameObject TryModel(string name, Transform parent) => TryModel(name, parent, 0f, 0f);

    /// <summary>
    /// Instantiate Resources/Models/name, scaled so its longest horizontal side is fitSize (0 = keep scale),
    /// centred on the parent and with its lowest point at bottomY (parent space). Works whatever units the FBX uses.
    /// </summary>
    public static GameObject TryModel(string name, Transform parent, float fitSize, float bottomY)
    {
        var prefab = Resources.Load<GameObject>("Models/" + name);
        if (prefab == null) return null;
        var go = Object.Instantiate(prefab, parent, false);
        go.name = name;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        if (fitSize <= 0f || parent == null) return go;
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return go;
        // Bounds in parent space
        Bounds b = default; bool first = true;
        foreach (var r in rs)
        {
            var wb = r.bounds;
            foreach (var corner in new[] { wb.min, wb.max, new Vector3(wb.min.x, wb.min.y, wb.max.z), new Vector3(wb.max.x, wb.max.y, wb.min.z) })
            {
                var lp = parent.InverseTransformPoint(corner);
                if (first) { b = new Bounds(lp, Vector3.zero); first = false; } else b.Encapsulate(lp);
            }
        }
        float longest = Mathf.Max(b.size.x, b.size.z);
        if (longest < 1e-4f) return go;
        float k = fitSize / longest;
        go.transform.localScale *= k;
        Vector3 c0 = b.center * k, min = b.min * k;
        go.transform.localPosition = go.transform.localPosition * k - new Vector3(c0.x, min.y - bottomY, c0.z) + new Vector3(0, 0, 0);
        return go;
    }

    /// <summary>For vehicle models with child wheels named "wheel-front-*": turn the model to face +Z and return its wheels.</summary>
    public static Transform[] FaceForwardAndFindWheels(GameObject model, Transform vehicleRoot)
    {
        var wheels = new List<Transform>();
        Transform front = null, back = null;
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (!n.StartsWith("wheel")) continue;
            wheels.Add(t);
            if (n.Contains("front") && front == null) front = t;
            if (n.Contains("back") && back == null) back = t;
        }
        if (front != null && back != null)
        {
            float fz = vehicleRoot.InverseTransformPoint(front.position).z, bz = vehicleRoot.InverseTransformPoint(back.position).z;
            if (fz < bz)
            {
                // Rotate 180 degrees about the root's vertical axis, keeping the model centred.
                Vector3 lp = model.transform.localPosition;
                model.transform.localRotation = Quaternion.Euler(0, 180, 0) * model.transform.localRotation;
                model.transform.localPosition = new Vector3(-lp.x, lp.y, -lp.z);
            }
        }
        return wheels.ToArray();
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

