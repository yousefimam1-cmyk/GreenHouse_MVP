using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Moves the sun with the in-game clock (day starts 06:00; full cycle = WorldClock.secondsPerDay), dims light
/// and sky at night, and turns on vehicle headlights and lamps at the pads after dusk. Weather dims the sun too.
/// The scene's baked lighting stays as it is; this only drives the real-time sun, ambient and skybox exposure.
/// </summary>
public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance;
    public bool IsNight { get; private set; }

    private Light sun;
    private float sunIntensity = 1f, ambientIntensity = 1f, skyExposure = 1f;
    private Color ambientColor;
    private Material sky;
    private Quaternion sunBaseRot;
    private readonly List<Light> nightLights = new List<Light>();

    private void Awake() => Instance = this;

    private void Start()
    {
        sun = RenderSettings.sun;
        if (sun == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null) { sunIntensity = sun.intensity; sunBaseRot = sun.transform.rotation; }
        ambientIntensity = RenderSettings.ambientIntensity;
        ambientColor = RenderSettings.ambientLight;
        if (RenderSettings.skybox != null)
        {
            // Work on a copy so Play mode never edits the skybox asset.
            sky = new Material(RenderSettings.skybox);
            RenderSettings.skybox = sky;
            if (sky.HasProperty("_Exposure")) skyExposure = sky.GetFloat("_Exposure");
        }
        Invoke(nameof(AddLights), 1.5f);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void AddLights()
    {
        foreach (var v in FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
            foreach (var x in new[] { -0.6f, 0.6f })
            {
                var go = new GameObject("Headlight");
                go.transform.SetParent(v.transform, false);
                go.transform.localPosition = new Vector3(x, 0.8f, 2.2f);
                go.transform.localRotation = Quaternion.Euler(8f, 0, 0);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.range = 28f; l.spotAngle = 55f; l.intensity = 2.5f; l.color = new Color(1f, 0.95f, 0.85f);
                l.shadows = LightShadows.None;
                nightLights.Add(l);
            }
        foreach (var z in new[] { DeliveryZone.LoadingBay, DeliveryZone.Market })
        {
            if (z == null) continue;
            var go = new GameObject("Pad Lamp");
            go.transform.SetParent(z.transform, false);
            go.transform.localPosition = new Vector3(6.4f, 8.5f, 0);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = 22f; l.intensity = 2f; l.color = new Color(1f, 0.8f, 0.55f);
            nightLights.Add(l);
        }
    }

    private void Update()
    {
        var clock = WorldClock.Instance;
        if (clock == null) return;
        float h = clock.SunHeight;                  // -1 .. 1
        float day = Mathf.Clamp01(h * 3f + 0.15f);  // soft dawn/dusk
        IsNight = day < 0.25f;
        if (sun != null && GraphicsEnhancer.High)
        {
            float hours = (6f + clock.DayProgress * 24f) % 24f;
            float elevation = (hours - 6f) / 12f * 180f;   // 0 at 06:00, 90 at noon, 180 at 18:00
            sun.transform.rotation = Quaternion.Euler(Mathf.Clamp(elevation, -10f, 190f), sunBaseRot.eulerAngles.y, 0f);
        }
        if (sun != null)
        {
            sun.intensity = sunIntensity * clock.WeatherLight * Mathf.Lerp(0.03f, 1f, day);
            sun.color = Color.Lerp(new Color(1f, 0.6f, 0.35f), Color.white, Mathf.Clamp01(h * 2f));
        }
        RenderSettings.ambientIntensity = ambientIntensity * Mathf.Lerp(0.25f, 1f, day);
        RenderSettings.ambientLight = ambientColor * Mathf.Lerp(0.25f, 1f, day);
        if (sky != null && sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", skyExposure * Mathf.Lerp(0.15f, 1f, day));
        foreach (var l in nightLights) if (l != null) l.enabled = IsNight;
    }
}
