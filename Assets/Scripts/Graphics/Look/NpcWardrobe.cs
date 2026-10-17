using System.Collections.Generic;
using UnityEngine;

/// <summary>Clothing sets of the Arab-world farms.</summary>
public enum Outfit
{
    /// <summary>Najd man: white / cream thobe, ghutra or red shemagh with the black agal.</summary>
    Thobe,
    /// <summary>Najd woman: black abaya and shayla headscarf.</summary>
    Abaya,
    /// <summary>Egyptian fellah: wide galabeya (grey-blue, brown, beige...), white emma turban over a taqiyah cap.</summary>
    Galabeya,
    /// <summary>Egyptian woman: dark galabeya with a long tarha headscarf.</summary>
    Tarha,
}

/// <summary>
/// Real clothing for the character models (Kenney mini characters, the player, drivers): a draped robe mesh
/// (thobe / abaya / galabeya) with folds and a flared hem, a stand-up thobe collar, a ghutra / shemagh (cap +
/// drape over the shoulders, red-white check woven pattern) with a double black agal cord, a shayla / tarha
/// headscarf, or the Egyptian emma turban wrapped over a taqiyah. All cloth uses the CC0 linen weave (normal map)
/// tinted per colour; materials are shared per colour. Headwear follows the head bone (BoneFollower), the robe
/// stays on the character root (legs move under it like under a real thobe).
/// Region: DressNpc picks Najd clothes on the Saudi maps and Egyptian clothes on the Nile Delta.
/// </summary>
public static class NpcWardrobe
{
    public const string RobeName = "CozyRobe";

    public static readonly Color[] GalabeyaColors =
    {
        new Color(0.42f, 0.48f, 0.56f), new Color(0.52f, 0.42f, 0.32f), new Color(0.74f, 0.7f, 0.6f),
        new Color(0.3f, 0.32f, 0.38f), new Color(0.6f, 0.6f, 0.58f), new Color(0.36f, 0.42f, 0.36f),
    };
    public static readonly Color[] EmmaColors = { new Color(0.95f, 0.94f, 0.9f), new Color(0.86f, 0.84f, 0.78f), new Color(0.7f, 0.7f, 0.68f) };
    public static readonly Color[] EgyptWomanColors = { new Color(0.07f, 0.07f, 0.08f), new Color(0.12f, 0.13f, 0.22f), new Color(0.24f, 0.16f, 0.12f) };

    private static Texture2D shemaghTex;
    private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    /// <summary>How many characters were dressed (tests / stats).</summary>
    public static int Dressed { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { shemaghTex = null; mats.Clear(); meshes.Clear(); Dressed = 0; }

    /// <summary>Egyptian clothes on the Delta (and other Egyptian maps), Najd clothes elsewhere.</summary>
    public static bool EgyptianRegion => Vegetation.Current == Biome.Delta;

    /// <summary>The outfit an NPC wears on the current map.</summary>
    public static Outfit RegionalOutfit(bool female) => EgyptianRegion ? (female ? Outfit.Tarha : Outfit.Galabeya) : (female ? Outfit.Abaya : Outfit.Thobe);

    /// <summary>Dress an NPC for the current map (seed picks colours). Used by AppearancePanel.DressNpc.</summary>
    public static void DressRegional(GameObject go, bool female, int seed)
    {
        if (!EgyptianRegion)
        {
            if (female) Dress(go, Outfit.Abaya, Appearance.AbayaColors[Mathf.Abs(seed) % 2], Appearance.ScarfColors[0]);
            else Dress(go, Outfit.Thobe, Appearance.ThobeColors[Mathf.Abs(seed) % 3 == 2 ? 1 : 0], Appearance.ShemaghColors[Mathf.Abs(seed) % 2]);
            return;
        }
        int s = Mathf.Abs(seed);
        if (female) Dress(go, Outfit.Tarha, EgyptWomanColors[s % EgyptWomanColors.Length], EgyptWomanColors[(s / 3) % 2]);
        else Dress(go, Outfit.Galabeya, GalabeyaColors[s % GalabeyaColors.Length], EmmaColors[(s / 2) % EmmaColors.Length]);
    }

    /// <summary>Remove clothes added by Dress (before re-dressing).</summary>
    public static void Undress(GameObject character)
    {
        if (character == null) return;
        foreach (var t in character.GetComponentsInChildren<Transform>(true))
            if (t != null && t != character.transform && (t.name == Appearance.HeadwearName || t.name == RobeName))
            {
                t.SetParent(null, false);
                Object.Destroy(t.gameObject);
            }
    }

    // ------------------------------------------------------------------ dressing
    private struct Body { public Vector3 headCentre; public float headR, height, bottom, shoulderR; public Transform head; }

    /// <summary>
    /// Dress a character: robe + headwear for the outfit. The character's own materials are tinted toward the robe
    /// colour first (sleeves / arms match). Children: "CozyRobe" (robe) and "CozyHeadwear" (headwear pieces).
    /// </summary>
    public static void Dress(GameObject character, Outfit style, Color robe, Color head, bool withRobe = true)
    {
        if (character == null) return;
        Undress(character);
        CozyFx.Tint(character, robe, style == Outfit.Abaya || style == Outfit.Tarha ? 0.6f : 0.4f, Appearance.HeadwearName);
        Body b;
        if (!Measure(character, out b)) return;
        var actor = character.GetComponent<Actor>();
        if (actor != null && actor.mode == ActorMode.Driver) withRobe = false;   // seated in a cab: no standing robe
        Vector3 s = character.transform.lossyScale;
        Vector3 inv = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));

        // ---- headwear (follows the head bone)
        var hw = new GameObject(Appearance.HeadwearName);
        hw.transform.SetParent(character.transform, false);
        hw.transform.position = b.headCentre;
        hw.transform.localScale = inv;
        float r = b.headR;
        switch (style)
        {
            case Outfit.Thobe:
                {
                    bool red = head.r > 0.5f && head.g < 0.5f;
                    var cloth = red ? ShemaghMaterial() : Cloth(head, 0.22f);
                    Piece(hw.transform, "Shemagh", HoodCap(r * 1.07f, 72f, 104f), cloth);
                    Piece(hw.transform, "Drape", HoodDrape(r * 1.07f, 104f, 62f, r * 1.9f, r * 1.42f), cloth);
                    Piece(hw.transform, "Agal", Torus(r * 0.86f, r * 0.075f, r * 0.17f, 2), Cloth(new Color(0.05f, 0.05f, 0.05f), 0.3f, LookMat.Wool));
                    break;
                }
            case Outfit.Abaya:
                Piece(hw.transform, "Scarf", HoodCap(r * 1.08f, 82f, 106f), Cloth(head, 0.3f));
                Piece(hw.transform, "Drape", HoodDrape(r * 1.08f, 106f, 48f, r * 2.3f, r * 1.55f), Cloth(head, 0.3f));
                break;
            case Outfit.Galabeya:
                Piece(hw.transform, "Taqiyah", HoodCap(r * 1.04f, 64f, 64f), Cloth(head, 0.2f));
                Piece(hw.transform, "Emma", Turban(r), Cloth(head, 0.22f));
                break;
            case Outfit.Tarha:
                Piece(hw.transform, "Tarha", HoodCap(r * 1.08f, 80f, 106f), Cloth(head, 0.25f));
                Piece(hw.transform, "Drape", HoodDrape(r * 1.08f, 106f, 46f, r * 3f, r * 1.7f), Cloth(head, 0.25f));
                break;
        }
        foreach (var rr in hw.GetComponentsInChildren<Renderer>(true)) { rr.gameObject.layer = character.layer; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
        if (b.head != null) BoneFollower.Attach(hw, b.head);

        // ---- robe (on the root)
        if (withRobe)
        {
            var robeGo = new GameObject(RobeName);
            robeGo.transform.SetParent(character.transform, false);
            robeGo.transform.localScale = inv;
            Vector3 root = character.transform.position;
            float neckY = b.headCentre.y - r * 0.95f - root.y;
            float hemY = b.bottom - root.y + b.height * 0.035f;
            float flare = style == Outfit.Thobe ? 1.12f : style == Outfit.Galabeya ? 1.32f : 1.45f;
            string pieceName = style == Outfit.Abaya ? "Abaya" : style == Outfit.Tarha ? "Galabeya" : style.ToString();
            Piece(robeGo.transform, pieceName, Robe(neckY, hemY, b.shoulderR, flare, style != Outfit.Thobe), Cloth(robe, style == Outfit.Abaya ? 0.38f : 0.2f));
            if (style == Outfit.Thobe)
                Piece(robeGo.transform, "Collar", Torus(b.shoulderR * 0.42f, b.shoulderR * 0.07f, 0f, 1, neckY + 0.01f), Cloth(robe, 0.22f));
            if (style == Outfit.Abaya)
                Piece(robeGo.transform, "Hem", Torus(b.shoulderR * flare * 1.02f, b.height * 0.012f, 0f, 1, hemY + b.height * 0.012f), Cloth(robe * 0.9f, 0.3f));
            foreach (var rr in robeGo.GetComponentsInChildren<Renderer>(true)) { rr.gameObject.layer = character.layer; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
        }
        Dressed++;
    }

    private static bool Measure(GameObject character, out Body b)
    {
        b = new Body();
        Bounds all = default, headB = default; bool any = false, hasHead = false;
        foreach (var rr in character.GetComponentsInChildren<Renderer>(true))
        {
            if (rr == null || rr is ParticleSystemRenderer) continue;
            if (!any) { all = rr.bounds; any = true; } else all.Encapsulate(rr.bounds);
            if (rr.name.ToLowerInvariant().Contains("head")) { if (!hasHead) { headB = rr.bounds; hasHead = true; } else headB.Encapsulate(rr.bounds); }
        }
        if (!any) return false;
        b.height = Mathf.Max(0.3f, all.size.y);
        b.bottom = all.min.y;
        if (hasHead && headB.size.y > 0.05f && headB.size.y < b.height * 0.7f)
        {
            b.headR = Mathf.Clamp(Mathf.Max(headB.size.x, headB.size.z) * 0.5f, b.height * 0.06f, b.height * 0.22f);
            b.headCentre = headB.center;
        }
        else
        {
            b.headR = Mathf.Clamp(b.height * 0.12f, 0.06f, 0.25f);
            b.headCentre = new Vector3(all.center.x, all.max.y - b.headR, all.center.z);
        }
        b.shoulderR = Mathf.Clamp(Mathf.Min(all.size.x, all.size.z) * 0.42f, b.height * 0.07f, b.height * 0.17f);
        b.head = FindBone(character.transform, "head");
        return true;
    }

    private static Transform FindBone(Transform root, string key)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (n == key || (n.Contains(key) && !n.Contains("mesh") && !n.Contains("end") && t.GetComponent<Renderer>() == null)) return t;
        }
        return null;
    }

    private static GameObject Piece(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    // ------------------------------------------------------------------ materials
    public static Material Cloth(Color c, float smooth, LookMat fabric = LookMat.Linen)
    {
        string key = fabric + ColorUtility.ToHtmlStringRGB(c) + Mathf.RoundToInt(smooth * 100f);
        Material m;
        if (mats.TryGetValue(key, out m) && m != null) return m;
        m = LookLibrary.Tinted(fabric, c, smooth);
        mats[key] = m;
        return m;
    }

    /// <summary>Red-and-white shemagh: woven check pattern on the linen weave.</summary>
    public static Material ShemaghMaterial()
    {
        Material m;
        if (mats.TryGetValue("shemagh", out m) && m != null) return m;
        m = new Material(Cloth(Color.white, 0.22f));
        m.name = "Shemagh";
        m.SetTexture("_BaseMap", ShemaghTexture());
        m.SetTextureScale("_BaseMap", new Vector2(10f, 10f));
        mats["shemagh"] = m;
        return m;
    }

    public static Texture2D ShemaghTexture()
    {
        if (shemaghTex != null) return shemaghTex;
        const int n = 32;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
        t.name = "Shemagh check";
        t.wrapMode = TextureWrapMode.Repeat;
        var px = new Color32[n * n];
        var red = new Color32(176, 24, 30, 255); var white = new Color32(238, 234, 226, 255);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // houndstooth-like check: 8 px cells with diagonal teeth
                int cx = x / 8, cy = y / 8, fx = x % 8, fy = y % 8;
                bool cell = ((cx + cy) & 1) == 0;
                bool tooth = fx + fy < 4 || fx + fy > 11;
                px[y * n + x] = cell ^ tooth ? red : white;
            }
        t.SetPixels32(px);
        t.Apply(true, true);
        shemaghTex = t;
        return t;
    }

    // ------------------------------------------------------------------ meshes (cached by rounded size)
    private static Mesh Cached(string key, System.Func<Mesh> make)
    {
        Mesh m;
        if (meshes.TryGetValue(key, out m) && m != null) return m;
        m = make();
        meshes[key] = m;
        return m;
    }

    private static string K(params float[] v) { var s = new System.Text.StringBuilder(); foreach (var f in v) s.Append(Mathf.RoundToInt(f * 1000f)).Append(','); return s.ToString(); }

    /// <summary>Hood cap over the head (origin = head centre, +z = face): down to frontDeg at the face, sideDeg elsewhere.</summary>
    public static Mesh HoodCap(float radius, float frontDeg, float sideDeg)
    {
        return Cached("cap" + K(radius, frontDeg, sideDeg), () =>
        {
            var k = new MeshKit(1);
            const int around = 24, down = 7;
            float tile = LookLibrary.TileSize(LookMat.Linen);
            for (int j = 0; j <= down; j++)
                for (int i = 0; i <= around; i++)
                {
                    float th = i / (float)around * Mathf.PI * 2f;
                    float front = Mathf.Abs(Mathf.DeltaAngle(0f, th * Mathf.Rad2Deg));
                    float lim = Mathf.Lerp(frontDeg, sideDeg, Mathf.Clamp01((front - 35f) / 45f)) * Mathf.Deg2Rad;
                    float ph = lim * j / down;
                    float puff = 1f + 0.06f * Mathf.Cos(ph);
                    Vector3 n = new Vector3(Mathf.Sin(ph) * Mathf.Sin(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Cos(th));
                    k.Vert(n * radius * puff, n, new Vector2(th * radius / tile, ph * radius / tile), Color.white);
                }
            for (int j = 0; j < down; j++)
                for (int i = 0; i < around; i++)
                {
                    int a = j * (around + 1) + i, b = a + around + 1;
                    k.Tri(0, a, b, a + 1); k.Tri(0, a + 1, b, b + 1);   // clockwise seen from outside (Unity front face)
                }
            return k.ToMesh("Hood cap", true);
        });
    }

    /// <summary>Cloth falling from the hood's edge (at startDeg below the crown) to the shoulders, open in front (gapDeg each side).</summary>
    public static Mesh HoodDrape(float radius, float startDeg, float gapDeg, float length, float flareRadius)
    {
        return Cached("drape" + K(radius, startDeg, gapDeg, length, flareRadius), () =>
        {
            var k = new MeshKit(1);
            const int around = 20, down = 5;
            float tile = LookLibrary.TileSize(LookMat.Linen);
            float ph = startDeg * Mathf.Deg2Rad;
            for (int j = 0; j <= down; j++)
                for (int i = 0; i <= around; i++)
                {
                    float th = (gapDeg + (360f - 2f * gapDeg) * i / around) * Mathf.Deg2Rad;
                    float t = j / (float)down;
                    float rr = Mathf.Lerp(radius * Mathf.Sin(ph), flareRadius, Mathf.Sqrt(t)) * (1f + 0.05f * t * Mathf.Sin(th * 7f));
                    float y = radius * Mathf.Cos(ph) - length * t;
                    Vector3 dir = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
                    Vector3 p = dir * rr + Vector3.up * y;
                    Vector3 n = (dir + Vector3.up * 0.25f).normalized;
                    k.Vert(p, n, new Vector2(th * radius / tile, (y - radius) / tile), Color.white);
                }
            for (int j = 0; j < down; j++)
                for (int i = 0; i < around; i++)
                {
                    int a = j * (around + 1) + i, b = a + around + 1;
                    // both faces (the inside of the drape is visible from the front)
                    k.Tri(0, a, b, a + 1); k.Tri(0, a + 1, b, b + 1);
                    k.Tri(0, a, a + 1, b); k.Tri(0, a + 1, b + 1, b);
                }
            return k.ToMesh("Hood drape", true);
        });
    }

    /// <summary>Rings (agal cords, collar, hem): count stacked tori of major / minor radius, step apart, at height y.</summary>
    public static Mesh Torus(float major, float minor, float yOffset, int count, float y = 0f)
    {
        return Cached("torus" + K(major, minor, yOffset, count, y), () =>
        {
            var k = new MeshKit(1);
            const int seg = 24, tube = 6;
            for (int c = 0; c < count; c++)
            {
                float cy = y + yOffset + c * minor * 2.3f;
                int start = k.V.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    for (int j = 0; j <= tube; j++)
                    {
                        float b = j / (float)tube * Mathf.PI * 2f;
                        Vector3 n = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                        k.Vert(dir * major + n * minor + Vector3.up * cy, n, new Vector2(i / (float)seg * 6f, j / (float)tube), Color.white);
                    }
                }
                for (int i = 0; i < seg; i++)
                    for (int j = 0; j < tube; j++)
                    {
                        int a = start + i * (tube + 1) + j, b = a + tube + 1;
                        k.Tri(0, a, b, a + 1); k.Tri(0, a + 1, b, b + 1);
                    }
            }
            return k.ToMesh("Torus", true);
        });
    }

    /// <summary>Egyptian emma: three thick wraps around the head, slightly tilted, over the taqiyah.</summary>
    public static Mesh Turban(float headR)
    {
        return Cached("turban" + K(headR), () =>
        {
            var k = new MeshKit(1);
            const int seg = 26, tube = 7;
            for (int c = 0; c < 3; c++)
            {
                float major = headR * (1.02f - c * 0.1f), minor = headR * (0.2f - c * 0.02f);
                float cy = headR * (0.12f + c * 0.26f);
                float tilt = (c - 1) * 6f;
                var rot = Quaternion.Euler(tilt, c * 40f, 0f);
                int start = k.V.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    for (int j = 0; j <= tube; j++)
                    {
                        float b = j / (float)tube * Mathf.PI * 2f;
                        Vector3 n = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                        Vector3 p = rot * (dir * major + n * minor) + Vector3.up * cy;
                        k.Vert(p, rot * n, new Vector2(i / (float)seg * 8f, j / (float)tube + c), Color.white);
                    }
                }
                for (int i = 0; i < seg; i++)
                    for (int j = 0; j < tube; j++)
                    {
                        int a = start + i * (tube + 1) + j, b = a + tube + 1;
                        k.Tri(0, a, b, a + 1); k.Tri(0, a + 1, b, b + 1);
                    }
            }
            return k.ToMesh("Emma turban", true);
        });
    }

    /// <summary>Robe (thobe, abaya, galabeya) on the character root: neck to hem, shoulders, flared skirt and folds.</summary>
    public static Mesh Robe(float neckY, float hemY, float shoulderR, float flare, bool wide)
    {
        return Cached("robe" + K(neckY, hemY, shoulderR, flare, wide ? 1 : 0), () =>
        {
            var k = new MeshKit(1);
            const int around = 26, rings = 12;
            float tile = LookLibrary.TileSize(LookMat.Linen) * 1.3f;
            float len = Mathf.Max(0.1f, neckY - hemY);
            for (int j = 0; j <= rings; j++)
            {
                float t = j / (float)rings;
                float y = Mathf.Lerp(neckY, hemY, t);
                float r = t < 0.12f ? Mathf.Lerp(shoulderR * 0.45f, shoulderR, Mathf.SmoothStep(0f, 1f, t / 0.12f))
                                    : Mathf.Lerp(shoulderR, shoulderR * flare, Mathf.Pow((t - 0.12f) / 0.88f, 1.4f));
                for (int i = 0; i <= around; i++)
                {
                    float a = i / (float)around * Mathf.PI * 2f;
                    float folds = 1f + (wide ? 0.06f : 0.035f) * t * Mathf.Sin(a * 9f + t * 2.3f) + 0.02f * t * Mathf.Sin(a * 4f + 1.1f);
                    Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a) * 0.86f);
                    Vector3 p = dir * r * folds + Vector3.up * y;
                    Vector3 n = new Vector3(Mathf.Sin(a), t < 0.12f ? 0.6f : 0.1f, Mathf.Cos(a)).normalized;
                    k.Vert(p, n, new Vector2(a * shoulderR / tile, (neckY - y) / tile), Color.white);
                }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < around; i++)
                {
                    int a = j * (around + 1) + i, b = a + around + 1;
                    k.Tri(0, a, b, a + 1); k.Tri(0, a + 1, b, b + 1);
                }
            var m = k.ToMesh("Robe", false);
            m.RecalculateNormals();
            if (m.vertexCount > 0) m.RecalculateTangents();
            return m;
        });
    }
}
