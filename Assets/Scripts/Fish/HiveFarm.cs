using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bee hives for Sidr honey (عسل سدر). Hives are bought in the shop (page "Animals", max 6) and stand in a small apiary.
/// Each hive makes honey over time with no feed; the Sidr tree flowers in autumn and winter, so honey comes fastest then
/// and slowest in the summer heat. Honey waits in the hives (capped) until collected with E.
/// </summary>
[MazraaSystem(63)]
public class HiveFarm : MonoBehaviour
{
    public static HiveFarm Instance;
    public const string HiveId = "bee_hive";
    public const int MaxHives = 6;
    public const int HivePrice = 1500;
    public const float KgPerHivePerDay = 1.5f;
    public const float CapPerHive = 5f;

    public int Hives { get; private set; }
    public float Waiting { get; private set; }

    private Transform apiary;
    private readonly List<GameObject> hiveObjects = new List<GameObject>();
    private readonly List<Transform> bees = new List<Transform>();
    private float saveTimer;

    private void Awake()
    {
        Instance = this;
        Hives = SaveData.GetInt("mazraa.fish.hives", 0);
        Waiting = SaveData.GetFloat("mazraa.fish.honey", 0f);
        ShopCatalog.Register(new ShopEntry
        {
            id = HiveId, category = "Animals",
            name = () => FishText.Raw("Bee hive (Sidr honey)"),
            detail = () => "(" + (Instance != null ? Instance.Hives : 0) + "/" + MaxHives + ")",
            price = () => HivePrice,
            blockReason = () => Instance != null && Instance.Hives >= MaxHives ? "The apiary is full" : null,
            onBought = () => { if (Instance != null) { Instance.Hives++; Instance.Save(); Instance.SyncHives(); } }
        });
    }

    private void Start()
    {
        BuildApiary(FishSite.At("ApiarySpot", 4f, -32f), FishSite.Facing());
        GameEvents.NewGameStarted += ResetAll;
        if (!HudLines.Status.Contains(HudLine)) HudLines.Status.Add(HudLine);
    }

    private void OnDestroy()
    {
        GameEvents.NewGameStarted -= ResetAll;
        if (Instance == this) Instance = null;
    }

    /// <summary>Sidr flowering: autumn 1.5, winter 1.1, spring 0.7, summer 0.35.</summary>
    public static float SeasonFactor(Season s)
    {
        switch (s)
        {
            case Season.Autumn: return 1.5f;
            case Season.Winter: return 1.1f;
            case Season.Spring: return 0.7f;
            default: return 0.35f;
        }
    }

    private void Update()
    {
        if (Hives > 0)
        {
            float day = WorldClock.Instance != null ? WorldClock.Instance.secondsPerDay : 360f;
            float season = SeasonFactor(WorldClock.Instance != null ? WorldClock.Instance.Season : Season.Spring);
            Waiting = Mathf.Min(Hives * CapPerHive, Waiting + Time.deltaTime / Mathf.Max(10f, day) * KgPerHivePerDay * Hives * season);
            if ((saveTimer -= Time.deltaTime) <= 0f) { saveTimer = 10f; Save(); }
        }
        // A few bees buzzing around the hives.
        float t = Time.time;
        for (int i = 0; i < bees.Count; i++)
        {
            bool on = Hives > 0;
            if (bees[i].gameObject.activeSelf != on) bees[i].gameObject.SetActive(on);
            if (!on) continue;
            float a = t * (2f + i * 0.37f) + i;
            bees[i].localPosition = new Vector3(Mathf.Cos(a) * (1.2f + i % 3 * 0.5f), 1.2f + Mathf.Sin(a * 1.7f) * 0.4f, Mathf.Sin(a) * (0.8f + i % 2 * 0.6f));
        }
    }

    public string Collect()
    {
        string msg;
        var eco = EconomyManager.Instance;
        float kg = Mathf.Floor(Waiting * 10f) / 10f;
        if (Hives <= 0) msg = FishText.T("Buy bee hives at the shop (Animals)");
        else if (kg < 0.1f) msg = FishText.T("The bees are still making honey");
        else
        {
            if (eco != null) eco.AddGood("honey", kg);
            Waiting -= kg; Save();
            GameEvents.RaiseCustom("collectHoney", kg);
            GameEvents.RaiseProductsCollected();
            var g = eco != null ? eco.Good("honey") : null;
            msg = Loc.Display(FishText.Raw("Collected") + ": " + kg.ToString("F1") + " " + EconomyManager.Unit(g) + " " + EconomyManager.GoodName(g));
        }
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg);
        return msg;
    }

    private string Prompt()
    {
        if (Hives <= 0) return FishText.T("Apiary (buy bee hives at the shop)");
        return FishText.T("[E] Collect honey ({0} kg)", Waiting.ToString("F1"));
    }

    private static string HudLine()
    {
        var h = Instance;
        if (h == null || h.Hives <= 0 || h.Waiting < 0.5f) return null;
        return FishText.T("Honey ready: {0} kg", h.Waiting.ToString("F1"));
    }

    private void BuildApiary(Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("Apiary");
        root.transform.SetPositionAndRotation(pos, rot);
        apiary = root.transform;
        var spot = FishSpot.Create("Apiary Hives", root.transform, Vector3.zero, new Vector3(0, 0.6f, 0), new Vector3(7f, 1.2f, 2.4f), Prompt, () => Collect());
        apiary = spot.transform;
        var wood = Build.Mat(new Color(0.5f, 0.34f, 0.2f), 0.15f);
        Build.Part(PrimitiveType.Cube, spot.transform, new Vector3(0, 0.25f, 0), new Vector3(7f, 0.1f, 1.2f), wood, "Stand");
        var beeMat = Build.Mat(new Color(0.95f, 0.75f, 0.1f), 0.3f);
        for (int i = 0; i < 8; i++)
        {
            var b = Build.Part(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * 0.06f, beeMat, "Bee").transform;
            b.gameObject.SetActive(false);
            bees.Add(b);
        }
        // Sidr tree beside the hives
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(5f, 1.2f, 1.5f), new Vector3(0.3f, 1.2f, 0.3f), Build.Mat(new Color(0.4f, 0.3f, 0.2f), 0.1f), "Sidr trunk");
        Build.Part(PrimitiveType.Sphere, root.transform, new Vector3(5f, 2.9f, 1.5f), new Vector3(3f, 2f, 3f), Build.Mat(new Color(0.42f, 0.55f, 0.25f), 0.1f), "Sidr crown");
        string label = "Apiary";
        Build.Label(root.transform, new Vector3(0, 2.4f, 0), label, 2.5f, Color.white);
        SyncHives();
    }

    private void SyncHives()
    {
        if (apiary == null) return;
        var white = Build.Mat(new Color(0.95f, 0.93f, 0.86f), 0.3f);
        var lid = Build.Mat(new Color(0.85f, 0.6f, 0.15f), 0.3f);
        while (hiveObjects.Count < Hives)
        {
            int i = hiveObjects.Count;
            var h = new GameObject("Hive " + (i + 1));
            h.transform.SetParent(apiary, false);
            h.transform.localPosition = new Vector3(-2.75f + i * 1.1f, 0.3f, 0);
            Build.Part(PrimitiveType.Cube, h.transform, new Vector3(0, 0.3f, 0), new Vector3(0.7f, 0.55f, 0.55f), white, "Box");
            Build.Part(PrimitiveType.Cube, h.transform, new Vector3(0, 0.62f, 0), new Vector3(0.8f, 0.08f, 0.65f), lid, "Lid");
            FishSite.SetLayer(h, 7);
            hiveObjects.Add(h);
        }
        while (hiveObjects.Count > Hives) { var h = hiveObjects[hiveObjects.Count - 1]; hiveObjects.RemoveAt(hiveObjects.Count - 1); if (h != null) Destroy(h); }
    }

    private void Save()
    {
        SaveData.SetInt("mazraa.fish.hives", Hives);
        SaveData.SetFloat("mazraa.fish.honey", Waiting);
        SaveData.Save();
    }

    public void ResetAll()
    {
        Hives = 0; Waiting = 0f;
        Save();
        SyncHives();
    }
}
