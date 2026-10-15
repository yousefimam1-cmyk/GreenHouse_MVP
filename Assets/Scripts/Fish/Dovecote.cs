using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pigeon tower (برج حمام). Buy the dovecote in the shop (page "Animals", comes with 4 pigeons) and more pairs after.
/// Pigeons eat a little animal feed each cycle and give squab (زغاليل) and pigeon manure; well-fed pigeons breed
/// one pair each morning. Collect at the tower with E.
/// </summary>
[MazraaSystem(64)]
public class Dovecote : MonoBehaviour
{
    public static Dovecote Instance;
    public const string TowerId = "dovecote";
    public const string PairId = "pigeon_pair";
    public const int TowerPrice = 2500, PairPrice = 200, MaxPigeons = 30, StartPigeons = 4;
    public const float Interval = 120f, FeedPerPigeon = 0.03f, SquabPerPair = 0.5f, ManurePerPigeon = 0.2f;

    public bool Built { get; private set; }
    public int Pigeons { get; private set; }
    public float Squab { get; private set; }
    public float Manure { get; private set; }
    public bool FedRecently { get; private set; }

    private float timer;
    private Transform tower;
    private readonly List<Transform> birds = new List<Transform>();

    private void Awake()
    {
        Instance = this;
        Built = SaveData.GetInt("mazraa.fish.dove.built", 0) == 1;
        Pigeons = SaveData.GetInt("mazraa.fish.dove.count", 0);
        Squab = SaveData.GetFloat("mazraa.fish.dove.squab", 0f);
        Manure = SaveData.GetFloat("mazraa.fish.dove.manure", 0f);
        ShopCatalog.Register(new ShopEntry
        {
            id = TowerId, category = "Animals",
            name = () => FishText.Raw("Dovecote (4 pigeons)"),
            price = () => TowerPrice,
            blockReason = () => Instance != null && Instance.Built ? "Already built" : null,
            onBought = () => { if (Instance != null) { Instance.Built = true; Instance.Pigeons = Mathf.Max(Instance.Pigeons, StartPigeons); Instance.Save(); Instance.Show(); } }
        });
        ShopCatalog.Register(new ShopEntry
        {
            id = PairId, category = "Animals",
            name = () => FishText.Raw("Pigeons x2"),
            detail = () => "(" + (Instance != null ? Instance.Pigeons : 0) + "/" + MaxPigeons + ")",
            price = () => PairPrice,
            blockReason = () => Instance == null || !Instance.Built ? "Build a dovecote first" : Instance.Pigeons >= MaxPigeons ? "The dovecote is full" : null,
            onBought = () => { if (Instance != null) { Instance.Pigeons = Mathf.Min(MaxPigeons, Instance.Pigeons + 2); Instance.Save(); } }
        });
    }

    private void Start()
    {
        BuildTower(FishSite.At("DovecoteSpot", 14f, -34f), FishSite.Facing());
        WorldClock.DayStarted += OnNewDay;
        GameEvents.NewGameStarted += ResetAll;
        if (!HudLines.Status.Contains(HudLine)) HudLines.Status.Add(HudLine);
    }

    private void OnDestroy()
    {
        WorldClock.DayStarted -= OnNewDay;
        GameEvents.NewGameStarted -= ResetAll;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (Built && Pigeons > 0)
        {
            timer += Time.deltaTime;
            if (timer >= Interval)
            {
                timer = 0f;
                var eco = EconomyManager.Instance;
                FedRecently = eco != null && eco.ConsumeFeed(FeedPerPigeon * Pigeons);
                if (FedRecently)
                {
                    Squab += (Pigeons / 2) * SquabPerPair;
                    Manure += Pigeons * ManurePerPigeon;
                    Save();
                }
            }
        }
        // Pigeons circling the tower.
        float t = Time.time;
        int show = Built ? Mathf.Min(birds.Count, Pigeons / 2) : 0;
        for (int i = 0; i < birds.Count; i++)
        {
            bool on = i < show;
            if (birds[i].gameObject.activeSelf != on) birds[i].gameObject.SetActive(on);
            if (!on) continue;
            float a = t * (0.8f + i * 0.11f) + i * 0.9f;
            birds[i].localPosition = new Vector3(Mathf.Cos(a) * (3f + i % 3), 6f + Mathf.Sin(a * 0.7f) * 1.2f, Mathf.Sin(a) * (3f + i % 2));
        }
    }

    private void OnNewDay(int day)
    {
        if (Built && FedRecently && Pigeons >= 2 && Pigeons < MaxPigeons)
        {
            Pigeons = Mathf.Min(MaxPigeons, Pigeons + 2);
            Save();
        }
    }

    public string Collect()
    {
        string msg;
        var eco = EconomyManager.Instance;
        if (!Built) msg = FishText.T("Buy the dovecote at the shop (Animals)");
        else if (Squab < 0.5f && Manure < 0.5f) msg = FishText.T(eco != null && eco.Feed > 0.01f ? "Nothing to collect yet" : "No feed - buy feed at the shop");
        else
        {
            float s = Mathf.Floor(Squab), m = Mathf.Floor(Manure * 10f) / 10f;
            if (eco != null) { eco.AddGood("squab", s); eco.AddGood("manure", m); }
            Squab -= s; Manure -= m; Save();
            GameEvents.RaiseCustom("collectPigeons", s);
            GameEvents.RaiseProductsCollected();
            var gs = eco != null ? eco.Good("squab") : null; var gm = eco != null ? eco.Good("manure") : null;
            msg = Loc.Display(FishText.Raw("Collected") + ": " + s.ToString("F0") + " " + EconomyManager.Unit(gs) + " " + EconomyManager.GoodName(gs) +
                ", " + m.ToString("F1") + " " + EconomyManager.Unit(gm) + " " + EconomyManager.GoodName(gm));
        }
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg);
        return msg;
    }

    private string Prompt()
    {
        if (!Built) return FishText.T("Dovecote site (buy it at the shop)");
        return FishText.T("[E] Collect from the dovecote ({0} pigeons)", Pigeons);
    }

    private static string HudLine()
    {
        var d = Instance;
        if (d == null || !d.Built) return null;
        return FishText.T("Pigeons: {0} - squab waiting {1}", d.Pigeons, Mathf.Floor(d.Squab).ToString("F0"));
    }

    private void BuildTower(Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("Dovecote");
        root.transform.SetPositionAndRotation(pos, rot);
        var spot = FishSpot.Create("Dovecote Tower", root.transform, Vector3.zero, new Vector3(0, 2.5f, 0), new Vector3(3f, 5f, 3f), Prompt, () => Collect());
        tower = spot.transform;
        var greyBirdMat = Build.Mat(new Color(0.55f, 0.57f, 0.62f), 0.3f);
        for (int i = 0; i < 8; i++)
        {
            var b = Build.Part(PrimitiveType.Sphere, root.transform, Vector3.zero, new Vector3(0.18f, 0.14f, 0.28f), greyBirdMat, "Pigeon").transform;
            b.gameObject.SetActive(false);
            birds.Add(b);
        }
        // Marker stake where the tower will stand.
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.02f, 0), new Vector3(3.2f, 0.02f, 3.2f), Build.Mat(new Color(0.6f, 0.5f, 0.38f), 0.05f), "Base");
        string label = "Dovecote";
        Build.Label(root.transform, new Vector3(0, 6.6f, 0), label, 2.5f, Color.white);
        Show();
    }

    /// <summary>Builds the mud-brick tower once the dovecote is bought.</summary>
    private void Show()
    {
        if (tower == null || !Built || tower.Find("Tower") != null) return;
        var body = new GameObject("Tower").transform;
        body.SetParent(tower, false);
        var mud = Build.Mat(new Color(0.78f, 0.62f, 0.45f), 0.1f);
        var dark = Build.Mat(new Color(0.2f, 0.16f, 0.12f), 0.1f);
        Build.Part(PrimitiveType.Cylinder, body, new Vector3(0, 2f, 0), new Vector3(2.6f, 2f, 2.6f), mud, "Walls");
        Build.Part(PrimitiveType.Cylinder, body, new Vector3(0, 4.4f, 0), new Vector3(1.8f, 0.4f, 1.8f), mud, "Top");
        Build.Part(PrimitiveType.Sphere, body, new Vector3(0, 5f, 0), new Vector3(1.4f, 1f, 1.4f), mud, "Dome");
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI * 2f / 6f;
            float y = 2.4f + (i / 6) * 1.1f;
            Build.Part(PrimitiveType.Cylinder, body, new Vector3(Mathf.Cos(a) * 1.31f, y, Mathf.Sin(a) * 1.31f), new Vector3(0.18f, 0.12f, 0.18f), dark, "Nest hole", new Vector3(0, 0, 90));
        }
        FishSite.SetLayer(tower.gameObject, 7);
    }

    private void Save()
    {
        SaveData.SetInt("mazraa.fish.dove.built", Built ? 1 : 0);
        SaveData.SetInt("mazraa.fish.dove.count", Pigeons);
        SaveData.SetFloat("mazraa.fish.dove.squab", Squab);
        SaveData.SetFloat("mazraa.fish.dove.manure", Manure);
        SaveData.Save();
    }

    public void ResetAll()
    {
        Built = false; Pigeons = 0; Squab = 0f; Manure = 0f; timer = 0f; FedRecently = false;
        Save();
        if (tower != null) { var t = tower.Find("Tower"); if (t != null) Destroy(t.gameObject); }
    }
}
