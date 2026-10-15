using UnityEngine;

/// <summary>
/// Floating orange diamond above the place the current mission needs (well, control station, truck, shop, field...),
/// with the distance under it. Hidden while a panel is open.
/// </summary>
public class ObjectiveMarker : MonoBehaviour
{
    private Transform marker;
    private TMPro.TextMeshPro distanceText;
    private float retarget;
    private Transform target;
    private float height = 3f;

    private void Start()
    {
        var root = new GameObject("Objective Marker");
        marker = root.transform;
        var glow = Build.Mat(new Color(1f, 0.5f, 0.1f), 0.6f, 0f, new Color(3f, 1.3f, 0.2f));
        Build.Part(PrimitiveType.Cube, marker, Vector3.zero, new Vector3(0.55f, 0.55f, 0.55f), glow, "Diamond", new Vector3(45, 0, 45));
        var go = new GameObject("Distance");
        go.transform.SetParent(marker, false);
        go.transform.localPosition = new Vector3(0, -0.7f, 0);
        distanceText = go.AddComponent<TMPro.TextMeshPro>();
        distanceText.fontSize = 3f;
        distanceText.alignment = TMPro.TextAlignmentOptions.Center;
        distanceText.color = new Color(1f, 0.85f, 0.6f);
        Loc.ExcludeFromScene(distanceText);
        go.AddComponent<Billboard>();
        GameEvents.MissionStarted += OnMission;
    }

    private void OnDestroy() => GameEvents.MissionStarted -= OnMission;
    private void OnMission(MissionDef m) => retarget = 0f;

    private void Update()
    {
        retarget -= Time.deltaTime;
        if (retarget <= 0f) { retarget = 1f; target = FindTarget(out height); }
        bool uiOpen = UIManager.Instance != null && UIManager.Instance.IsUIOpened;
        bool show = target != null && !uiOpen;
        if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
        if (!show) return;

        marker.position = target.position + Vector3.up * (height + Mathf.Sin(Time.time * 2.5f) * 0.25f);
        marker.GetChild(0).Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);
        var cam = Camera.main != null && Camera.main.isActiveAndEnabled ? Camera.main : FindViewCamera();
        if (cam != null)
        {
            float d = Vector3.Distance(cam.transform.position, target.position);
            distanceText.text = d.ToString("F0") + " m";
            marker.localScale = Vector3.one * Mathf.Clamp(d / 18f, 1f, 6f);
        }
    }

    private static Camera FindViewCamera()
    {
        foreach (var c in Camera.allCameras) if (c.isActiveAndEnabled) return c;
        return null;
    }

    private static Transform FindTarget(out float h)
    {
        h = 3f;
        var m = MissionManager.Instance?.Current;
        if (m == null) return null;
        switch (m.type)
        {
            case "startWell": h = 4f; return First<WellController>();
            case "optimize": h = 3f; return First<ControllStation>();
            case "healthyGreenhouse": case "allHealthy": case "harvestKg":
                h = 3f;
                foreach (var gh in FindObjectsByType<GreenhouseManager>(FindObjectsSortMode.None))
                    foreach (var b in gh.Cropbeds)
                        if (b != null && (m.type == "harvestKg" ? b.IsReadyToHarvest : !b.IsActive)) return b.transform;
                return m.type == "harvestKg" ? null : First<ControllStation>();
            case "enterVehicle": h = 4f; return Vehicle.Current == null && FarmTruck.Instance != null ? FarmTruck.Instance.transform : null;
            case "loadKg": h = 9f; return DeliveryZone.LoadingBay != null ? DeliveryZone.LoadingBay.transform : null;
            case "deliverKg": case "earnMoney": case "completeContract": h = 9f; return DeliveryZone.Market != null ? DeliveryZone.Market.transform : null;
            case "buyFeed": case "buyAnimal": case "buyUpgrade": h = 4.5f; return First<ShopStall>();
            case "collectProducts": h = 2.5f; return First<PenCrate>();
            case "cleanPen": h = 4f; foreach (var t in FindObjectsByType<PenTask>(FindObjectsSortMode.None)) if (t.kind == PenTaskKind.Clean) return t.transform; return null;
            case "plantField": case "harvestField":
                h = 2.5f;
                if (FieldManager.Instance != null) foreach (var p in FieldManager.Instance.Plots)
                    if (p != null && (m.type == "plantField" ? p.State != PlotState.Growing : p.State == PlotState.Ripe)) return p.transform;
                return null;
            case "tractorWork": case "tractorPlow": case "tractorHarvest":
                h = 4f;
                if (Vehicle.Current is Tractor && FieldManager.Instance != null && FieldManager.Instance.Plots.Count > 0) return FieldManager.Instance.Plots[0].transform;
                return Tractor.Instance != null ? Tractor.Instance.transform : null;
            default: return null;
        }
    }

    private static Transform First<T>() where T : Component
    {
        var c = FindAnyObjectByType<T>();
        return c != null ? c.transform : null;
    }
}
