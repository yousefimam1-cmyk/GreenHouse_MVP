using UnityEngine;

public enum ZoneKind { LoadingBay, Market }

/// <summary>
/// A glowing pad. The loading bay fills the truck from farm storage; the market sells the cargo.
/// Uses a distance check (no physics trigger needed).
/// </summary>
public class DeliveryZone : MonoBehaviour
{
    public ZoneKind kind;
    public float radius = 6f;
    private bool warned;
    private float cooldown;

    public static DeliveryZone LoadingBay, Market;

    private void Start()
    {
        if (kind == ZoneKind.LoadingBay) LoadingBay = this; else Market = this;
    }

    private void Update()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        if (kind == ZoneKind.LoadingBay) RefuelIfParked(eco);
        var truck = FarmTruck.Instance;
        if (truck == null) return;
        Vector3 d = truck.transform.position - transform.position; d.y = 0f;
        bool inside = d.magnitude < radius;
        cooldown -= Time.deltaTime;

        if (inside && truck.IsDriving && Mathf.Abs(truck.Speed) < 3f && cooldown <= 0f)
        {
            cooldown = 1.5f;
            if (kind == ZoneKind.LoadingBay)
            {
                float room = eco.TruckCapacity - truck.CargoWeight;
                float moved = room > 0.01f ? eco.LoadInto(truck.Cargo, room) : 0f;
                if (moved > 0.01f)
                {
                    truck.SaveCargo();
                    GameEvents.RaiseCargoLoaded(moved);
                    Alert(Loc.T("Loaded {0} kg onto the truck", moved.ToString("F0")));
                }
                else if (!warned)
                {
                    warned = true;
                    Alert(room <= 0.01f ? Loc.T("Truck is full - drive to the market") : Loc.T("Farm storage is empty - harvest first"));
                }
            }
            else if (truck.Cargo.Count > 0)
            {
                var notes = new System.Collections.Generic.List<string>();
                int earned = eco.SellAll(truck.Cargo, notes);
                truck.SaveCargo();
                notes.Insert(0, Loc.T("Sold the load for {0} SAR", earned.ToString("N0")));
                Alert(string.Join("\n", notes));
            }
        }
        if (!inside) warned = false;
    }

    public const int FuelPricePerLitre = 3;
    private float refuelTimer;

    // Any vehicle parked (stopped) on the loading bay is refuelled, paid per litre.
    private void RefuelIfParked(EconomyManager eco)
    {
        var v = Vehicle.Current;
        if (v == null) return;
        Vector3 d = v.transform.position - transform.position; d.y = 0f;
        if (d.magnitude > radius || Mathf.Abs(v.Speed) > 0.5f || v.Fuel >= v.fuelCapacity - 0.5f) { refuelTimer = 0f; return; }
        refuelTimer += Time.deltaTime;
        if (refuelTimer < 1.2f) return;
        refuelTimer = 0f;
        int affordable = eco.Money / FuelPricePerLitre;
        float added = v.Refuel(Mathf.Min(affordable, v.fuelCapacity));
        if (added > 0.5f)
        {
            int cost = Mathf.CeilToInt(added) * FuelPricePerLitre;
            eco.AddMoney(-cost);
            Alert(Loc.T("Refuelled {0} L for {1} SAR", added.ToString("F0"), cost.ToString("N0")));
        }
        else Alert(Loc.T("Not enough money"));
    }

    private static void Alert(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }
}
