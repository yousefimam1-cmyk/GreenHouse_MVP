using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum Implement { None, Plow, Seeder, Harvester }

/// <summary>
/// Tractor with swappable implements (Q cycles: none, plow, seeder, harvester). Drive the implement over a field
/// plot (above walking speed) to plow, sow the best seed you own, or harvest it. Slower than the truck.
/// </summary>
public class Tractor : Vehicle
{
    public static Tractor Instance;
    public Implement ActiveImplement { get; private set; }
    public Transform hitch;

    private readonly Dictionary<Implement, GameObject> visuals = new Dictionary<Implement, GameObject>();
    private float workCooldown;

    protected override string SaveId => "tractor";
    public override string InteractionPrompt => Loc.T("[E] Drive tractor");

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        maxSpeed = 9f; acceleration = 4.5f; turnRate = 60f; fuelPerSecond = 0.25f;
        ActiveImplement = (Implement)SaveData.GetInt("mazraa.implement", 1);
    }

    protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

    public void RegisterVisual(Implement i, GameObject go) { visuals[i] = go; ShowImplement(); }

    private void ShowImplement() { foreach (var kv in visuals) if (kv.Value != null) kv.Value.SetActive(kv.Key == ActiveImplement); }

    protected override void DrivingUpdate(Keyboard kb)
    {
        if (kb.qKey.wasPressedThisFrame)
        {
            ActiveImplement = (Implement)(((int)ActiveImplement + 1) % 4);
            SaveData.SetInt("mazraa.implement", (int)ActiveImplement);
            ShowImplement();
            if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(Loc.Raw("Implement") + ": " + ImplementName(ActiveImplement)));
        }
        Work();
    }

    private void Work()
    {
        workCooldown -= Time.deltaTime;
        if (ActiveImplement == Implement.None || Mathf.Abs(Speed) < 1f || workCooldown > 0f || FieldManager.Instance == null) return;
        Vector3 point = hitch != null ? hitch.position : transform.position - transform.forward * 2.5f;
        foreach (var plot in FieldManager.Instance.Plots)
        {
            if (plot == null || !plot.Contains(point)) continue;
            bool done = ActiveImplement == Implement.Plow ? plot.TryPlow(false)
                      : ActiveImplement == Implement.Seeder ? plot.TrySeed(false)
                      : plot.TryHarvest(false);
            if (done) workCooldown = 0.4f;
        }
    }

    public static string ImplementName(Implement i) => Loc.Raw(i == Implement.None ? "No implement" : i.ToString());
}
