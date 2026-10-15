using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Farm truck: a Vehicle with a cargo bed. Loads farm storage at the green loading bay and sells at the
/// gold market (see DeliveryZone). Crates appear in the bed as it fills.
/// </summary>
public class FarmTruck : Vehicle
{
    public static FarmTruck Instance;

    public Transform cargoRoot;
    public readonly Dictionary<string, float> Cargo = new Dictionary<string, float>();
    private readonly List<GameObject> crateVisuals = new List<GameObject>();

    protected override string SaveId => "truck";
    public override string InteractionPrompt => Loc.T("[E] Drive truck");

    public float CargoWeight
    {
        get
        {
            float w = 0f;
            var eco = EconomyManager.Instance;
            foreach (var kv in Cargo) { var g = eco != null ? eco.Good(kv.Key) : null; w += kv.Value * (g != null ? g.weight : 1f); }
            return w;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        // Restore cargo saved as "id=amount;id=amount"
        foreach (var part in SaveData.GetString("mazraa.cargo", "").Split(';'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0f) Cargo[kv[0]] = v;
        }
    }

    /// <summary>Persist the cargo (call after loading or selling).</summary>
    public void SaveCargo()
    {
        var parts = new List<string>();
        foreach (var kv in Cargo) parts.Add(kv.Key + "=" + kv.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        SaveData.SetString("mazraa.cargo", string.Join(";", parts));
        SaveData.Save();
    }

    public void ClearCargo() { Cargo.Clear(); SaveCargo(); }

    protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

    protected override void Update()
    {
        base.Update();
        UpdateCargoVisual();
    }

    private void UpdateCargoVisual()
    {
        if (cargoRoot == null) return;
        var eco = EconomyManager.Instance;
        float cap = eco != null ? eco.TruckCapacity : 150f;
        int want = Mathf.Clamp(Mathf.CeilToInt(CargoWeight / cap * 6f), 0, 6);
        while (crateVisuals.Count < want)
        {
            int i = crateVisuals.Count;
            var crate = Build.Part(PrimitiveType.Cube, cargoRoot, new Vector3((i % 2 == 0 ? -0.42f : 0.42f), 0.25f + (i / 4) * 0.5f, -0.55f + (i / 2 % 2) * 0.75f),
                new Vector3(0.75f, 0.45f, 0.65f), Build.Mat(new Color(0.62f, 0.45f, 0.26f), 0.1f), "Crate");
            crateVisuals.Add(crate);
        }
        while (crateVisuals.Count > want)
        {
            Destroy(crateVisuals[crateVisuals.Count - 1]);
            crateVisuals.RemoveAt(crateVisuals.Count - 1);
        }
    }
}
