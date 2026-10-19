using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public class PlacedDecor { public string id; public float x, y, z, rot; }
[Serializable] class DecorSave { public List<PlacedDecor> placed = new List<PlacedDecor>(); public List<string> stock = new List<string>(); }

/// <summary>
/// Decorations bought in the shop page "Decor" (majlis tent, lantern, palm-frond fence, flower pot). After buying, a
/// preview follows in front of the player: E places it, Esc keeps it in storage; X places the next stored piece.
/// Placed pieces can be picked up again with E. Positions are saved (mazraa.cozy.decor).
/// </summary>
[MazraaSystem(64)]
public class Decorations : MonoBehaviour
{
    public static Decorations Instance;

    public static readonly string[] Kinds = { "decor_majlis", "decor_lantern", "decor_fence", "decor_pot" };
    private static readonly int[] Prices = { 900, 120, 200, 60 };
    private static readonly string[] Names = { "Majlis tent", "Lantern", "Palm-frond fence", "Flower pot" };

    private DecorSave data = new DecorSave();
    private readonly List<GameObject> placedObjects = new List<GameObject>();
    private GameObject ghost;
    private string placing;
    private int placeFrame;

    /// <summary>
    /// Extra decor kinds added by other features (farm life decor, festival prizes): id -> builder that adds the model's
    /// parts under the given root and returns the pick-up box size. Bought / won pieces go in with AddToStock.
    /// </summary>
    public static readonly Dictionary<string, Func<Transform, Vector3>> ExtraModels = new Dictionary<string, Func<Transform, Vector3>>();

    public string Placing => placing;
    public List<PlacedDecor> Placed => data.placed;
    public int Stock(string id) { int n = 0; foreach (var s in data.stock) if (s == id) n++; return n; }

    private void Awake()
    {
        Instance = this;
        for (int i = 0; i < Kinds.Length; i++) Register(i);
        HudLines.Status.Remove(HudLine);
        HudLines.Status.Add(HudLine);
    }

    private void Start()
    {
        Load();
        GameEvents.NewGameStarted += OnNewGame;
        Invoke(nameof(SpawnSaved), 1.5f);
    }

    private void OnDestroy()
    {
        GameEvents.NewGameStarted -= OnNewGame;
        HudLines.Status.Remove(HudLine);
        if (Instance == this) Instance = null;
    }

    private static void Register(int i)
    {
        string id = Kinds[i]; int price = Prices[i]; string key = Names[i];
        ShopCatalog.Register(new ShopEntry
        {
            id = id,
            category = "Decor",
            name = () => Loc.Raw(key),
            detail = () => Instance != null && Instance.Stock(id) > 0 ? "(" + Loc.Raw("in storage") + ": " + Instance.Stock(id) + ")" : "",
            price = () => price,
            onBought = () => { if (Instance != null) { Instance.data.stock.Add(id); Instance.Save(); Instance.BeginPlacing(id); } },
        });
    }

    /// <summary>Put a piece in storage (a purchase or prize from another feature) without starting the preview.</summary>
    public void AddToStock(string id) { if (string.IsNullOrEmpty(id)) return; data.stock.Add(id); Save(); }

    private static string HudLine()
    {
        var d = Instance;
        if (d == null || d.placing == null) return null;
        return Loc.T("Placing decoration: [E] place   [Esc] keep in storage");
    }

    private void OnNewGame()
    {
        CancelPlacing();
        foreach (var o in placedObjects) if (o != null) Destroy(o);
        placedObjects.Clear();
        data = new DecorSave();
    }

    private void Load()
    {
        try { var s = JsonUtility.FromJson<DecorSave>(SaveData.GetString("mazraa.cozy.decor", "")); if (s != null) data = s; }
        catch (Exception) { }
        if (data.placed == null) data.placed = new List<PlacedDecor>();
        if (data.stock == null) data.stock = new List<string>();
    }

    private void Save() { SaveData.SetString("mazraa.cozy.decor", JsonUtility.ToJson(data)); SaveData.Save(); }

    private void SpawnSaved() { foreach (var p in data.placed) placedObjects.Add(Spawn(p)); }

    // ---------------- placing ----------------

    public void BeginPlacing(string id)
    {
        if (Stock(id) <= 0) return;
        CancelPlacing();
        placing = id;
        placeFrame = Time.frameCount;
        ghost = BuildModel(id, null);
        ghost.name = "Decor preview";
        UpdateGhost();
        CozyFx.Alert(Loc.T("Placing decoration: [E] place   [Esc] keep in storage"));
    }

    public void CancelPlacing()
    {
        if (ghost != null) Destroy(ghost);
        ghost = null;
        placing = null;
    }

    private bool PreviewPose(out Vector3 pos, out float rot)
    {
        var p = CozyFx.Player;
        pos = Vector3.zero; rot = 0f;
        if (p == null || !p.gameObject.activeInHierarchy) return false;
        Vector3 fwd = Vector3.ProjectOnPlane(p.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        float dist = placing == "decor_majlis" ? 5f : 2.5f;
        pos = Build.OnGround(p.position + fwd * dist);
        rot = Quaternion.LookRotation(-fwd).eulerAngles.y;
        return true;
    }

    private void UpdateGhost()
    {
        if (ghost == null) return;
        if (PreviewPose(out var pos, out var rot)) { ghost.SetActive(true); ghost.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rot, 0)); }
        else ghost.SetActive(false);
    }

    private void Update()
    {
        if (placing != null)
        {
            UpdateGhost();
            bool uiOpen = UIManager.Instance != null && UIManager.Instance.IsUIOpened;
            if (uiOpen || Time.frameCount == placeFrame || UIManager.PanelClosedFrame == Time.frameCount) return;
            if (CozyKeys.Down("e")) PlaceHere();
            else if (CozyKeys.Down("esc")) { CancelPlacing(); CozyFx.Alert(Loc.T("Decoration kept in storage")); }
            return;
        }
        if (CozyKeys.Free && CozyKeys.Down("x"))
        {
            foreach (var k in Kinds) if (Stock(k) > 0) { BeginPlacing(k); return; }
            if (data.stock.Count > 0) { BeginPlacing(data.stock[0]); return; }
            CozyFx.Alert(Loc.T("No decorations in storage - buy some in the shop (Decor)"));
        }
    }

    /// <summary>Place the piece being previewed in front of the player.</summary>
    public bool PlaceHere()
    {
        if (placing == null || !PreviewPose(out var pos, out var rot)) return false;
        return PlaceAt(placing, pos, rot);
    }

    /// <summary>Place a stored piece at a position (also used by tests).</summary>
    public bool PlaceAt(string id, Vector3 pos, float rotY)
    {
        if (Stock(id) <= 0) return false;
        data.stock.Remove(id);
        var p = new PlacedDecor { id = id, x = pos.x, y = pos.y, z = pos.z, rot = rotY };
        data.placed.Add(p);
        Save();
        placedObjects.Add(Spawn(p));
        if (placing == id) CancelPlacing();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX();
        GameEvents.RaiseCustom("placeDecor", 1f);
        return true;
    }

    /// <summary>Put a placed piece back into storage.</summary>
    public void PickUp(GameObject go)
    {
        int i = placedObjects.IndexOf(go);
        if (i < 0) return;
        var p = data.placed[i];
        data.placed.RemoveAt(i);
        placedObjects.RemoveAt(i);
        data.stock.Add(p.id);
        Save();
        Destroy(go);
        CozyFx.Alert(Loc.T("Decoration kept in storage"));
    }

    private GameObject Spawn(PlacedDecor p)
    {
        var go = BuildModel(p.id, null);
        go.transform.SetPositionAndRotation(new Vector3(p.x, p.y, p.z), Quaternion.Euler(0, p.rot, 0));
        var size = ExtraModels.ContainsKey(p.id) ? ExtraSize(p.id) : p.id == "decor_majlis" ? new Vector3(4.2f, 2.6f, 3.2f) : p.id == "decor_fence" ? new Vector3(3.2f, 1.6f, 0.4f) : new Vector3(0.8f, 1.6f, 0.8f);
        var prop = CozyFx.MakeInteractable(go, new Vector3(0, size.y * 0.5f, 0), size, () => Loc.T("[E] Pick up decoration"), null);
        prop.use = () => { if (Instance != null) Instance.PickUp(go); };
        prop.available = () => Instance == null || Instance.placing == null;
        return go;
    }

    // ---------------- models (built from primitives) ----------------

    private static readonly Dictionary<string, Vector3> extraSizes = new Dictionary<string, Vector3>();
    private static Vector3 ExtraSize(string id) => extraSizes.TryGetValue(id, out var v) ? v : new Vector3(1f, 1.6f, 1f);

    private static GameObject BuildModel(string id, Transform parent)
    {
        var root = new GameObject("Decor " + id);
        if (parent != null) root.transform.SetParent(parent, false);
        var t = root.transform;
        if (ExtraModels.TryGetValue(id, out var build) && build != null) { extraSizes[id] = build(t); return root; }
        switch (id)
        {
            case "decor_majlis":
            {
                var tent = Build.Mat(new Color(0.12f, 0.1f, 0.09f), 0.1f);          // black goat-hair tent (bait al-sha'ar)
                var stripe = Build.Mat(new Color(0.75f, 0.15f, 0.12f), 0.2f);
                var cushion = Build.Mat(new Color(0.7f, 0.12f, 0.12f), 0.2f);
                var rug = Build.Mat(new Color(0.55f, 0.18f, 0.1f), 0.1f);
                var wood = Build.Mat(new Color(0.45f, 0.3f, 0.18f), 0.2f);
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.02f, 0), new Vector3(3.8f, 0.04f, 2.8f), rug, "Rug");
                for (int i = 0; i < 4; i++) Build.Part(PrimitiveType.Cylinder, t, new Vector3(i % 2 == 0 ? -1.8f : 1.8f, 1.1f, i < 2 ? -1.3f : 1.3f), new Vector3(0.08f, 1.1f, 0.08f), wood, "Pole");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 2.3f, 0), new Vector3(4.1f, 0.12f, 3.1f), tent, "Roof");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 2.2f, 1.52f), new Vector3(4.1f, 0.12f, 0.04f), stripe, "Trim");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 1.2f, -1.5f), new Vector3(4.1f, 2.2f, 0.06f), tent, "Back");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.18f, -1.1f), new Vector3(3.4f, 0.28f, 0.55f), cushion, "Cushions");
                Build.Part(PrimitiveType.Cube, t, new Vector3(-1.55f, 0.18f, 0), new Vector3(0.55f, 0.28f, 1.8f), cushion, "Cushions");
                Build.Part(PrimitiveType.Cube, t, new Vector3(1.55f, 0.18f, 0), new Vector3(0.55f, 0.28f, 1.8f), cushion, "Cushions");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.12f, 0.2f), new Vector3(0.5f, 0.12f, 0.5f), Build.Mat(new Color(0.8f, 0.65f, 0.2f), 0.7f, 0.6f), "Coffee tray");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0.1f, 0.33f, 0.2f), new Vector3(0.12f, 0.12f, 0.12f), Build.Mat(new Color(0.85f, 0.7f, 0.3f), 0.8f, 0.8f), "Dallah");
                break;
            }
            case "decor_lantern":
            {
                var metal = Build.Mat(new Color(0.3f, 0.24f, 0.14f), 0.6f, 0.6f);
                var glow = new Color(1f, 0.72f, 0.3f);
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.05f, 0), new Vector3(0.4f, 0.05f, 0.4f), metal, "Base");
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.65f, 0), new Vector3(0.05f, 0.6f, 0.05f), metal, "Stand");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 1.4f, 0), new Vector3(0.32f, 0.42f, 0.32f), Build.Mat(glow, 0.8f, 0f, glow * 2.2f), "Fanous");
                Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 1.68f, 0), new Vector3(0.34f, 0.2f, 0.34f), metal, "Dome");
                var l = new GameObject("Light").AddComponent<Light>();
                l.transform.SetParent(t, false); l.transform.localPosition = new Vector3(0, 1.4f, 0);
                l.type = LightType.Point; l.range = 5f; Rp.LegacyLight(l, 1f); l.color = glow;
                break;
            }
            case "decor_fence":
            {
                var frond = Build.Mat(new Color(0.62f, 0.5f, 0.3f), 0.05f);    // dried palm fronds (sa'af)
                var post = Build.Mat(new Color(0.42f, 0.3f, 0.18f), 0.1f);
                for (int i = 0; i < 13; i++)
                    Build.Part(PrimitiveType.Cube, t, new Vector3(-1.5f + i * 0.25f, 0.75f, 0), new Vector3(0.12f, 1.5f + (i % 3) * 0.08f, 0.05f), frond, "Frond", new Vector3(0, 0, (i % 2 == 0 ? 3f : -3f)));
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 1.1f, 0.04f), new Vector3(3.2f, 0.07f, 0.06f), post, "Rail");
                Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.4f, 0.04f), new Vector3(3.2f, 0.07f, 0.06f), post, "Rail");
                break;
            }
            default:
            {
                var clay = Build.Mat(new Color(0.72f, 0.42f, 0.25f), 0.15f);
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.25f, 0), new Vector3(0.5f, 0.25f, 0.5f), clay, "Pot");
                Build.Part(PrimitiveType.Sphere, t, new Vector3(0, 0.65f, 0), new Vector3(0.6f, 0.45f, 0.6f), Build.Mat(new Color(0.22f, 0.5f, 0.2f), 0.2f), "Leaves");
                var colors = new[] { new Color(0.95f, 0.3f, 0.5f), new Color(1f, 0.85f, 0.25f), new Color(0.95f, 0.95f, 0.95f) };
                for (int i = 0; i < 5; i++)
                    Build.Part(PrimitiveType.Sphere, t, new Vector3(Mathf.Cos(i * 1.3f) * 0.22f, 0.8f + (i % 2) * 0.08f, Mathf.Sin(i * 1.3f) * 0.22f), Vector3.one * 0.12f, Build.Mat(colors[i % 3], 0.4f), "Flower");
                break;
            }
        }
        return root;
    }
}
