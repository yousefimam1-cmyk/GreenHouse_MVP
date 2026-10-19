using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a tractor implement model (plow, seeder, harvester). While it is not on the tractor it stands on the ground
/// (Parked = true) with its hitch point at its root, shows on the map, and the tractor can attach it from within
/// Tractor.AttachRange (TractorHitch.cs).
/// </summary>
public class ParkedImplement : MonoBehaviour
{
    /// <summary>Implements standing on the ground right now (attached and sold ones are not listed).</summary>
    public static readonly List<ParkedImplement> All = new List<ParkedImplement>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => All.Clear();

    public Implement kind;
    public bool Parked { get; private set; }

    /// <summary>Where the tractor's hitch has to be to pick it up.</summary>
    public Vector3 HitchPoint => transform.position;

    public void SetParked(bool on)
    {
        Parked = on;
        if (on && !All.Contains(this)) All.Add(this);
        if (!on) All.Remove(this);
        var icon = GetComponent<MinimapIcon>();
        if (icon == null && on) icon = MinimapIcon.Add(gameObject, MinimapIconKind.Custom, new Color(0.95f, 0.75f, 0.1f), "", 12f);
        if (icon != null) icon.enabled = on;
    }

    private void OnDisable() { All.Remove(this); }
    private void OnEnable() { if (Parked && !All.Contains(this)) All.Add(this); }
    private void OnDestroy() { All.Remove(this); }

    /// <summary>Closest parked implement within range of a point (flat distance), or null.</summary>
    public static ParkedImplement Nearest(Vector3 p, float range)
    {
        ParkedImplement best = null; float bestD = range * range;
        foreach (var i in All)
        {
            if (i == null || !i.Parked || !i.isActiveAndEnabled) continue;
            Vector3 d = i.HitchPoint - p; d.y = 0f;
            if (d.sqrMagnitude <= bestD) { bestD = d.sqrMagnitude; best = i; }
        }
        return best;
    }
}

/// <summary>
/// Where the tractor's implements are when they are not hitched: the yard beside the tractor's parking spot (a row of
/// slots), or wherever the player detached them (pose saved per implement). Also sold implements (Vehicles tab) and
/// their value. Save keys: "mazraa.parity.impl.&lt;kind&gt;" = "x;y;z;yaw", "mazraa.parity.impl.sold.&lt;kind&gt;" = 1.
/// </summary>
public static class ImplementYard
{
    public const string PoseKey = "mazraa.parity.impl.", SoldKey = "mazraa.parity.impl.sold.";
    public static readonly int[] Prices = { 0, 3000, 4500, 9000, 3500, 2500, 4000, 2000, 3000, 2500, 7000 };   // None, Plow, Seeder, Harvester + FarmCore Cultivator, Spreader, Sprayer, Roller, Mulcher, Mower, Baler (Farm Shop buy-back price)

    private static Vector3 origin; private static Quaternion originRot = Quaternion.identity; private static float lift = 0.75f;
    public static bool HasOrigin { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { HasOrigin = false; }

    /// <summary>The tractor's spawn pose: the yard is a row of slots on its right.</summary>
    public static void SetOrigin(Vector3 tractorPos, Quaternion rot, float rideHeight) { origin = tractorPos; originRot = rot; lift = rideHeight; HasOrigin = true; }

    public static Vector3 YardSlot(Implement kind)
    {
        int i = Mathf.Max(0, (int)kind - 1);
        Vector3 local = new Vector3(5f + (i / 5) * 6f, 0f, 2.5f - (i % 5) * 4.5f);   // FarmCore: rows of five (11 implements)
        return Build.OnGround(origin + originRot * local, lift);
    }
    public static float YardYaw => originRot.eulerAngles.y;

    public static bool IsSold(Implement kind) => kind != Implement.None && SaveData.GetInt(SoldKey + kind, 0) == 1;
    public static int Price(Implement kind) => Prices[Mathf.Clamp((int)kind, 0, Prices.Length - 1)];
    public static int SellValue(Implement kind) => Mathf.RoundToInt(Price(kind) * 0.5f);

    public static void Attached(Implement kind, GameObject go, Transform hitch)
    {
        if (go == null) return;
        go.SetActive(true);
        if (hitch != null) go.transform.SetParent(hitch, false);
        go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
        Marker(kind, go).SetParked(false);
        SaveData.DeleteKey(PoseKey + kind);
    }

    public static void Park(Implement kind, GameObject go, Vector3 pos, float yaw)
    {
        if (go == null) return;
        go.transform.SetParent(null, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = Vector3.one;
        go.SetActive(!IsSold(kind));
        Marker(kind, go).SetParked(!IsSold(kind));
        SaveData.SetString(PoseKey + kind, pos.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ";" + pos.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + ";" + pos.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ";" + yaw.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Put a parked implement back where it was left (saved pose), else in its yard slot.</summary>
    public static void Restore(Implement kind, GameObject go)
    {
        if (go == null) return;
        Vector3 pos; float yaw;
        if (!TryPose(kind, out pos, out yaw)) { pos = YardSlot(kind); yaw = YardYaw; }
        Park(kind, go, pos, yaw);
    }

    public static bool TryPose(Implement kind, out Vector3 pos, out float yaw)
    {
        pos = Vector3.zero; yaw = 0f;
        string s = SaveData.GetString(PoseKey + kind, "");
        if (string.IsNullOrEmpty(s)) return false;
        var parts = s.Split(';');
        if (parts.Length != 4) return false;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        float x, y, z;
        if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out x) || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out y)
            || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out z) || !float.TryParse(parts[3], System.Globalization.NumberStyles.Float, inv, out yaw)) return false;
        pos = new Vector3(x, y, z);
        return true;
    }

    public static void ClearPose(Implement kind) => SaveData.DeleteKey(PoseKey + kind);

    public static void SetSold(Implement kind, bool sold, GameObject go)
    {
        if (sold) SaveData.SetInt(SoldKey + kind, 1); else SaveData.DeleteKey(SoldKey + kind);
        if (go == null) return;
        go.SetActive(!sold);
        Marker(kind, go).SetParked(!sold);
    }

    /// <summary>New game: forget poses and sales.</summary>
    public static void ClearSaved()
    {
        foreach (Implement k in System.Enum.GetValues(typeof(Implement))) { SaveData.DeleteKey(PoseKey + k); SaveData.DeleteKey(SoldKey + k); }
    }

    private static ParkedImplement Marker(Implement kind, GameObject go)
    {
        var m = go.GetComponent<ParkedImplement>();
        if (m == null) m = go.AddComponent<ParkedImplement>();
        m.kind = kind;
        return m;
    }
}
