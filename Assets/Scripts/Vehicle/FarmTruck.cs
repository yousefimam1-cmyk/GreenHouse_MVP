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
    }

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
