using System.Text;
using UnityEngine;

/// <summary>
/// Scene object of one FarmCore field: a collider to look at and drive on, the ground drawn as one quad with a texture of
/// 4 x 4 pixels per cell (worked / sown / stubble / weeds / stones / straw, darker when wet, furrow stripes along the rows),
/// redrawn only for the cell chunks that changed, and the minimap icon coloured by the map layer. E does the next hand job
/// where you look (FarmFields.HandJob); Q opens the Fields page on this field. The prompt shows the field info overlay.
/// </summary>
public class FarmFieldObject : Interactable
{
    public FarmField Field { get; private set; }
    public int PixelsPerCell { get; private set; } = 4;
    public Texture2D GroundTexture { get; private set; }
    public int TextureUploads { get; private set; }

    private Color32[] pixels;
    private int texW, texH, shownVersion = -1;
    private MeshRenderer ground;
    private MinimapIcon icon;
    private float iconTimer, redrawTimer;

    public void Setup(FarmField f)
    {
        Field = f;
        gameObject.layer = 7;
        var col = gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.12f, 0f);
        col.size = new Vector3(f.width, 0.24f, f.depth);
        PixelsPerCell = Mathf.Max(1, Mathf.Min(4, 2048 / Mathf.Max(f.cols, f.rows)));
        texW = f.cols * PixelsPerCell; texH = f.rows * PixelsPerCell;
        pixels = new Color32[texW * texH];
        GroundTexture = new Texture2D(texW, texH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = f.name + " ground" };
        var go = new GameObject("Ground");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.04f, 0f);
        go.layer = 7;
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = Quad(f.width, f.depth);
        ground = go.AddComponent<MeshRenderer>();
        ground.sharedMaterial = GroundMaterial(f, GroundTexture);
        icon = MinimapIcon.Add(gameObject, MinimapIconKind.Field, MinimapIcon.DefaultColor(MinimapIconKind.Field), "", Mathf.Clamp(Mathf.Sqrt(f.Area) * 0.6f, 12f, 40f));
        Redraw(true);
    }

    /// <summary>
    /// URP Lit with the cell texture as base map (field state per cell) and the graphics makeover's tilled-soil PBR texture
    /// (CC0) as a tiled detail map (albedo x2 + its normal map), so the soil keeps its grain at any field size.
    /// </summary>
    public static Material GroundMaterial(FarmField f, Texture2D cells)
    {
        Material mat;
        if (LookLibrary.IsTextured(LookMat.Tilled))
        {
            var src = LookLibrary.Get(LookMat.Tilled);
            mat = new Material(src) { name = f.name + " soil" };
            float tile = Mathf.Max(0.5f, LookLibrary.TileSize(LookMat.Tilled) * 0.6f);
            var soil = src.GetTexture("_BaseMap"); var normal = src.GetTexture("_BumpMap");
            if (soil != null) { mat.SetTexture("_DetailAlbedoMap", soil); mat.SetTextureScale("_DetailAlbedoMap", new Vector2(f.width / tile, f.depth / tile)); mat.EnableKeyword("_DETAIL_MULX2"); }
            if (normal != null) { mat.SetTexture("_DetailNormalMap", normal); mat.EnableKeyword("_DETAIL_MULX2"); }
            mat.SetTexture("_BumpMap", null); mat.DisableKeyword("_NORMALMAP");   // the main normal map would stretch over the whole field
            mat.SetTextureScale("_BaseMap", Vector2.one);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            mat.color = Color.white;
        }
        else mat = new Material(Build.Mat(Color.white, 0.08f)) { name = f.name + " soil" };
        mat.SetTexture("_BaseMap", cells);
        mat.mainTexture = cells;
        return mat;
    }

    private static Mesh Quad(float w, float d)
    {
        var m = new Mesh { name = "Field quad" };
        float x = w * 0.5f, z = d * 0.5f;
        m.vertices = new[] { new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(-x, 0, z), new Vector3(x, 0, z) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    private void LateUpdate()
    {
        if (Field == null) return;
        redrawTimer -= Time.deltaTime;
        if (Field.Version != shownVersion && redrawTimer <= 0f) { redrawTimer = 0.15f; Redraw(false); }   // at most ~7 uploads a second while working
        iconTimer -= Time.deltaTime;
        if (iconTimer <= 0f) { iconTimer = 2f; RefreshIcon(); }
    }

    /// <summary>Repaint the cell chunks that changed (all = every chunk) and upload the texture once.</summary>
    public void Redraw(bool all)
    {
        var f = Field;
        bool any = false;
        for (int c = 0; c < f.chunkDirty.Length; c++)
        {
            if (!all && !f.chunkDirty[c]) continue;
            int cx0 = (c % f.chunksX) * FarmField.ChunkCells, cz0 = (c / f.chunksX) * FarmField.ChunkCells;
            for (int cz = cz0; cz < Mathf.Min(f.rows, cz0 + FarmField.ChunkCells); cz++)
                for (int cx = cx0; cx < Mathf.Min(f.cols, cx0 + FarmField.ChunkCells); cx++) PaintCell(cz * f.cols + cx);
            if (FarmFieldRenderer.Instance != null) FarmFieldRenderer.Instance.MarkDirty(f, c);
            f.chunkDirty[c] = false;
            any = true;
        }
        shownVersion = f.Version;
        if (any && GroundTexture != null) { GroundTexture.SetPixels32(pixels); GroundTexture.Apply(false, false); TextureUploads++; }
    }

    private static readonly Color32 untilled = new Color32(150, 128, 92, 255), cultivated = new Color32(128, 98, 68, 255), ploughed = new Color32(110, 82, 56, 255),
        sown = new Color32(104, 80, 54, 255), stubble = new Color32(176, 152, 96, 255), weed = new Color32(92, 128, 52, 255), stone = new Color32(160, 158, 150, 255),
        straw = new Color32(196, 170, 104, 255), swath = new Color32(150, 170, 80, 255), dead = new Color32(120, 100, 60, 255);

    /// <summary>Base colour of a cell (before furrows, weeds and stones).</summary>
    public static Color32 CellColor(FarmField f, int i)
    {
        Color32 c;
        switch ((Ground)f.ground[i])
        {
            case Ground.Cultivated: c = cultivated; break;
            case Ground.Ploughed: c = ploughed; break;
            case Ground.Sown: c = f.Has(i, CellFlags.Withered) ? dead : sown; break;
            case Ground.Stubble: c = stubble; break;
            default: c = untilled; break;
        }
        if (f.Has(i, CellFlags.Mulched)) c = Lerp(c, straw, 0.7f);
        float wet = Mathf.Clamp01(f.Get(FieldLayer.Moisture, i));
        if (f.ground[i] != (byte)Ground.Untilled || wet > 0.6f) c = Lerp(c, new Color32((byte)(c.r * 0.55f), (byte)(c.g * 0.5f), (byte)(c.b * 0.45f), 255), wet * 0.8f);
        if (f.Get(FieldLayer.Moisture, i) > 1.03f) c = Lerp(c, new Color32(70, 80, 84, 255), 0.5f);   // standing water
        return c;
    }

    private void PaintCell(int i)
    {
        var f = Field;
        int n = PixelsPerCell, px0 = (i % f.cols) * n, py0 = (i / f.cols) * n;
        Color32 baseC = CellColor(f, i);
        var g = (Ground)f.ground[i];
        bool rows = g == Ground.Ploughed || g == Ground.Sown || g == Ground.Cultivated;
        float weeds = f.Get(FieldLayer.Weeds, i);
        int stones = f.stones[i];
        bool sw = f.Has(i, CellFlags.Swath);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Color32 c = baseC;
                if (rows && (x % 2 == 1)) c = Lerp(c, new Color32(0, 0, 0, 255), g == Ground.Cultivated ? 0.08f : 0.16f);   // furrows along the rows
                float h = f.Hash01(i * 16 + y * n + x, 5);
                if (weeds > 0.15f && h < weeds * 0.5f) c = Lerp(c, weed, 0.75f);
                if (stones > 0 && h > 1f - 0.08f * stones) c = stone;
                if (sw && x == n / 2) c = swath;
                pixels[(py0 + y) * texW + px0 + x] = c;
            }
    }

    private static Color32 Lerp(Color32 a, Color32 b, float t) => new Color32((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t), 255);

    /// <summary>Minimap colour for the map layer (Fields page / map: crop, moisture, ground or yield).</summary>
    public void RefreshIcon()
    {
        if (icon == null || Field == null) return;
        var ff = FarmFields.Instance;
        var s = FarmSim.Summarize(Field);
        var layer = ff != null ? ff.MapLayer : FieldMapLayer.Crop;
        icon.color = LayerColor(s, layer);
        string crop = s.Planted ? FcText.CropName(s.mainCrop) : "";
        icon.letter = crop.Length > 0 ? crop.Substring(0, 1) : "";
    }

    public static Color LayerColor(FieldSummary s, FieldMapLayer layer)
    {
        switch (layer)
        {
            case FieldMapLayer.Moisture: return Color.Lerp(new Color(0.85f, 0.6f, 0.3f), new Color(0.2f, 0.45f, 0.95f), Mathf.Clamp01(s.moisture));
            case FieldMapLayer.Ground: return s.sown > 0 ? new Color(0.3f, 0.6f, 0.25f) : s.ploughed + s.cultivated > s.cells / 2 ? new Color(0.45f, 0.32f, 0.2f) : new Color(0.75f, 0.65f, 0.45f);
            case FieldMapLayer.Yield: return s.Planted ? Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.25f, 0.85f, 0.3f), Mathf.Clamp01(s.yieldFactor)) : new Color(0.5f, 0.5f, 0.5f);
            default:
                if (!s.Planted) return MinimapIcon.DefaultColor(MinimapIconKind.Field);
                var eco = EconomyManager.Instance;
                var c = eco != null ? eco.FieldCrop(s.mainCrop) : null;
                return c != null ? new Color(c.r, c.g, c.b) : new Color(0.4f, 0.7f, 0.3f);
        }
    }

    // ------------------------------------------------------------------ interaction

    /// <summary>The point of the field the player is looking at (camera ray on the field plane), else the field centre.</summary>
    public Vector3 LookPoint()
    {
        var cam = Camera.main;
        if (cam != null && Field != null)
        {
            Ray r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float dy = r.direction.y;
            if (Mathf.Abs(dy) > 0.01f)
            {
                float t = (Field.center.y + 0.05f - r.origin.y) / dy;
                if (t > 0f && t < 12f) { Vector3 p = r.origin + r.direction * t; if (Field.Contains(p, 0.5f)) return p; }
            }
            Vector3 o = cam.transform.position;
            if (Field.Contains(o, 1f)) return o;
        }
        return Field != null ? Field.center : transform.position;
    }

    public override string InteractionPrompt => Loc.Display(PromptRaw(LookPoint()));

    public string PromptRaw(Vector3 at)
    {
        var ff = FarmFields.Instance;
        if (ff == null || Field == null) return "";
        return ActionText(ff, Field, at) + "\n" + FieldOverlay.Line(Field, at) + "   " + FcText.R("[Q] Field details");
    }

    public static string ActionText(FarmFields ff, FarmField f, Vector3 at)
    {
        var plan = ff.PlanHand(f, at, false);
        var eco = EconomyManager.Instance;
        switch (plan.action)
        {
            case FarmFields.HandAction.Harvest: return FcText.R("[E] Harvest");
            case FarmFields.HandAction.Sow: return FcText.R("[E] Plant") + " " + FcText.CropName(plan.detail) + " (" + (eco != null ? eco.Seeds(plan.detail) : 0) + ")";
            case FarmFields.HandAction.Hoe: return FcText.R("[E] Hoe the soil");
            case FarmFields.HandAction.Sample: return FcText.R("[E] Take a soil sample") + " (" + ff.Items(FarmFields.KitId) + ")";
            case FarmFields.HandAction.Spread: return FcText.R("[E] Spread") + " " + FcData.AmendName(FcData.Amendment(plan.detail)) + " (" + ff.Bags(plan.detail) + ")";
            case FarmFields.HandAction.Leach: return FcText.R("[E] Leach the salts");
            case FarmFields.HandAction.Treat: var cure = FieldPests.OwnedCure(f); return FcText.R("[E] Treat") + " " + CropsData.Name(CropsData.Pest(f.pest)) + ": " + CropsData.Name(cure);
            case FarmFields.HandAction.Water: return FcText.R("[E] Water");
        }
        var s = FarmSim.Summarize(f);
        if (s.Planted) return FcText.CropName(s.mainCrop) + " " + Mathf.FloorToInt(s.growth * 100f) + "%";
        return ff.HasAnySeed() ? FcText.R("Plow the field with the tractor") : FcText.R("No seeds - buy them at a Farm Shop");
    }

    public override void Interact()
    {
        base.Interact();
        var ff = FarmFields.Instance;
        if (ff == null || Field == null) return;
        if (NetSession.IsClient) { FarmNet.ForwardHandJob(LookPoint()); return; }   // co-op: the host does the job where you look
        string msg = ff.HandJob(LookPoint());
        if (!string.IsNullOrEmpty(msg) && UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg);
    }

    /// <summary>Q / Y on foot: open the Fields page on this field.</summary>
    public bool SecondaryInteract()
    {
        if (FieldsPage.Instance == null) return false;
        FieldsPage.Instance.Open(Field);
        return true;
    }
}

/// <summary>The field info overlay: what you see when you look at a field (crop, stage, water, soil test, expected yield, limiting factor).</summary>
public static class FieldOverlay
{
    /// <summary>One line for the prompt (translated, not shaped).</summary>
    public static string Line(FarmField f, Vector3 at)
    {
        var ff = FarmFields.Instance;
        var s = FarmSim.Summarize(f, GameSettings.PlowingRequired);
        int i = f.CellAt(at);
        var sb = new StringBuilder();
        sb.Append(FarmFields.FieldName(f)).Append(" · ").Append(FcText.Area(f.Area));
        if (s.Planted)
        {
            sb.Append(" · ").Append(FcText.CropName(s.mainCrop));
            if (i >= 0 && f.ground[i] == (byte)Ground.Sown && f.crop[i] != 0)
                sb.Append(" ").Append(FcText.R(FarmAgronomy.Stage(f.Get(FieldLayer.Growth, i)))).Append(" ").Append(Mathf.FloorToInt(Mathf.Min(1f, f.Get(FieldLayer.Growth, i)) * 100f)).Append("%");
            sb.Append(" · ").Append(FcText.R("Expected")).Append(" ").Append(Kg(s.expectedKg));
        }
        float m = i >= 0 ? f.Get(FieldLayer.Moisture, i) : s.moisture;
        sb.Append(" · ").Append(FcText.R("Water")).Append(" ").Append(FcText.Pct(m));
        if (ff != null && ff.Sampled(f)) sb.Append(" · N ").Append(s.n.ToString("F0")).Append(" P ").Append(s.p.ToString("F0")).Append(" K ").Append(s.k.ToString("F0")).Append(" pH ").Append(s.ph.ToString("F1")).Append(" EC ").Append(s.ec.ToString("F1"));
        if (s.weeds > 0.3f) sb.Append(" · ").Append(FcText.R("Weeds")).Append(" ").Append(FcText.Pct(s.weeds));
        if (!string.IsNullOrEmpty(f.pest)) sb.Append(" · ").Append(CropsData.Name(CropsData.Pest(f.pest))).Append("!");
        if (s.limiting >= 0 && (ff == null || ff.Sampled(f) || !CellFactors.NeedsTest(s.limiting)))
            sb.Append(" · ").Append(FcText.R("Limiting")).Append(": ").Append(FcText.R(CellFactors.Names[s.limiting]));
        return sb.ToString();
    }

    public static string Kg(float kg) => kg >= 1000f ? (kg / 1000f).ToString("0.0") + " t" : kg.ToString("F0") + " " + FcText.R("kg");
}
