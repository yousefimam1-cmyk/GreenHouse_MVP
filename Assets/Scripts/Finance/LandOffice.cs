using System;
using UnityEngine;

/// <summary>
/// Land office: a sign beside the fields (E opens the "Land" shop page) that sells extra farmland.
/// Three parcels of 2 plots, then a large 6-plot block (used instead of a third greenhouse: a whole
/// greenhouse needs scene-authored beds, modules and crop profiles, so the expansion is an open-field block).
/// New plots extend the FieldManager grid (FieldManager.AddPlot) and keep their own save keys (mazraa.plot.N).
/// Parcels owned: mazraa.fin.land.
/// </summary>
[MazraaSystem(42)]
public class LandOffice : MonoBehaviour
{
    public static LandOffice Instance;

    [Serializable] public struct Parcel { public int plots, price; public bool large; }
    public static readonly Parcel[] Parcels =
    {
        new Parcel { plots = 2, price = 2500 },
        new Parcel { plots = 2, price = 4000 },
        new Parcel { plots = 2, price = 6000 },
        new Parcel { plots = 6, price = 15000, large = true },
    };

    public int Owned { get; private set; }
    public int ExtraPlots { get { int n = 0; for (int i = 0; i < Owned && i < Parcels.Length; i++) n += Parcels[i].plots; return n; } }
    public Parcel? Next => Owned < Parcels.Length ? Parcels[Owned] : (Parcel?)null;

    private int built;          // extra plots placed in the world
    private int basePlots = -1; // plots FieldManager made itself
    private LandOfficeSign sign;

    private void Awake()
    {
        Instance = this;
        Owned = Mathf.Clamp(SaveData.GetInt("mazraa.fin.land", 0), 0, Parcels.Length);
        ShopCatalog.Register(new ShopEntry
        {
            id = "land_next", category = "Land",
            name = () =>
            {
                var n = Instance != null ? Instance.Next : null;
                if (n == null) return FinText.R("Farmland - all land bought");
                return FinText.R(n.Value.large ? "Large plot block" : "Farmland parcel") + ": " + n.Value.plots + " " + FinText.R("plots");
            },
            detail = () => Instance != null ? "(" + Instance.Owned + "/" + Parcels.Length + ")" : "",
            price = () => Instance != null && Instance.Next != null ? Instance.Next.Value.price : -1,
            blockReason = () => Instance == null || Instance.Next == null ? "All land bought" : null,
            onBought = () => { if (Instance != null) Instance.Bought(); }
        });
    }

    private void OnEnable() => GameEvents.NewGameStarted += OnNewGame;
    private void OnDisable() => GameEvents.NewGameStarted -= OnNewGame;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        var fm = FieldManager.Instance;
        if (fm == null) return;
        if (basePlots < 0)
        {
            // Wait until FieldManager has laid out its own grid.
            if (fm.Plots.Count < fm.rows * fm.columns) return;
            basePlots = fm.rows * fm.columns;
        }
        while (built < ExtraPlots) AddPlot(fm);
        if (sign == null)
        {
            float half = (fm.columns * 0.5f + 0.9f) * (fm.plotSize.x + fm.gap);
            Vector3 pos = fm.GridCenter + fm.GridRotation * new Vector3(-half, 0, 0);
            sign = LandOfficeSign.Create(pos, fm.GridRotation * Quaternion.Euler(0, 90f, 0));
        }
    }

    private void AddPlot(FieldManager fm)
    {
        int k = built++;
        fm.AddPlot(fm.rows + k / fm.columns, k % fm.columns);
    }

    private void Bought()
    {
        if (Next == null) return;
        var p = Next.Value;
        Owned++;
        SaveData.SetInt("mazraa.fin.land", Owned);
        SaveData.Save();
        GameEvents.RaiseCustom("buyLand", 1f);
        FinText.Alert(FinText.T("New farmland: {0} plots added beyond the fields", p.plots));
    }

    private void OnNewGame()
    {
        Owned = 0;
        SaveData.SetInt("mazraa.fin.land", 0);
        var fm = FieldManager.Instance;
        if (fm != null && basePlots >= 0)
        {
            for (int i = fm.Plots.Count - 1; i >= basePlots; i--)
            {
                if (fm.Plots[i] != null) { fm.Plots[i].ResetPlot(); Destroy(fm.Plots[i].gameObject); }
                fm.Plots.RemoveAt(i);
            }
        }
        built = 0;
    }
}
