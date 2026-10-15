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
        var truck = FarmTruck.Instance;
        var eco = EconomyManager.Instance;
        if (truck == null || eco == null) return;
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
                notes.Insert(0, Loc.T("Sold the load for {0} EGP", earned.ToString("N0")));
                Alert(string.Join("\n", notes));
            }
        }
        if (!inside) warned = false;
    }

    private static void Alert(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }
}
