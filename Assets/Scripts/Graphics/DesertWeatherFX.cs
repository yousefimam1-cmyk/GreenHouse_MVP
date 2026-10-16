using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Desert atmosphere: blowing dust around the camera on windy (Cloudy) and Heatwave days, sandy distance fog that
/// thickens in dust and darkens at night, and a subtle heat haze (a second post-processing volume next to
/// GraphicsEnhancer's: warm grade, slight wobble from lens distortion, a touch of chromatic aberration) on hot
/// afternoons. Wind01 (0..1) is shared with the audio. Everything scales with the graphics preset.
/// </summary>
[MazraaSystem(72)]
public class DesertWeatherFX : MonoBehaviour
{
    /// <summary>True while a farm event (haboob) controls the fog; the desert fog then leaves RenderSettings alone.</summary>
    public static bool FogOverridden;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => FogOverridden = false;

    public static DesertWeatherFX Instance { get; private set; }
    /// <summary>0 = calm .. 1 = sandstorm-ish. Smoothed.</summary>
    public static float Wind01 { get; private set; }
    /// <summary>0..1 strength of the heat haze.</summary>
    public static float Heat01 { get; private set; }
    public float DustRate { get; private set; }
    /// <summary>0..1 river haze (Nile Delta mornings), from the map's RegionLook.humidity.</summary>
    public static float Humidity01 { get; private set; }

    public static readonly Color DustColor = new Color(0.86f, 0.74f, 0.55f, 0.55f);
    private static readonly Color fogDay = new Color(0.86f, 0.78f, 0.64f);
    private static readonly Color fogNight = new Color(0.1f, 0.11f, 0.15f);

    private ParticleSystem dust;
    private Volume haze;
    private LensDistortion wobble;
    private Vector3 windDir = new Vector3(-0.6f, 0f, -0.8f);   // shamal: from the north-west
    private bool origFog; private Color origFogColor; private float origFogDensity; private FogMode origFogMode;

    private void Awake() => Instance = this;

    private void Start()
    {
        origFog = RenderSettings.fog; origFogColor = RenderSettings.fogColor; origFogDensity = RenderSettings.fogDensity; origFogMode = RenderSettings.fogMode;
        BuildDust();
        BuildHaze();
        Wind01 = 0f; Heat01 = 0f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RenderSettings.fog = origFog; RenderSettings.fogColor = origFogColor; RenderSettings.fogDensity = origFogDensity; RenderSettings.fogMode = origFogMode;
    }

    private void BuildDust()
    {
        var go = new GameObject("Blowing Dust");
        go.transform.SetParent(transform, false);
        dust = go.AddComponent<ParticleSystem>();
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = dust.main;
        main.startLifetime = 4f; main.startSpeed = 7f; main.startSize = 1.6f; main.maxParticles = 1500;
        main.startColor = DustColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.01f;
        var em = dust.emission; em.rateOverTime = 0f;
        var shape = dust.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(70f, 8f, 4f);
        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sharedMaterial = DustMaterial();
        pr.shadowCastingMode = ShadowCastingMode.Off;
        pr.receiveShadows = false;
    }

    /// <summary>Soft round transparent particle (URP Particles/Unlit); falls back to a plain lit colour.</summary>
    public static Material DustMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) return Build.Mat(DustColor, 0f);
        var m = new Material(shader);
        m.name = "Dust (runtime)";
        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", DustColor);
        m.SetFloat("_Surface", 1f);       // transparent
        m.SetFloat("_Blend", 0f);         // alpha
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3000;
        return m;
    }

    private void BuildHaze()
    {
        var go = new GameObject("Heat Haze");
        go.transform.SetParent(transform, false);
        haze = go.AddComponent<Volume>();
        haze.isGlobal = true;
        haze.priority = 51f;
        haze.weight = 0f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Heat haze (runtime)";
        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.12f);
        color.contrast.Override(-6f);
        color.colorFilter.Override(new Color(1f, 0.95f, 0.86f));
        var wb = profile.Add<WhiteBalance>(true);
        wb.temperature.Override(14f);
        var ca = profile.Add<ChromaticAberration>(true);
        ca.intensity.Override(0.08f);
        wobble = profile.Add<LensDistortion>(true);
        wobble.intensity.Override(0f);
        haze.sharedProfile = profile;
    }

    private void Update()
    {
        var clock = WorldClock.Instance;
        Weather w = clock != null ? clock.Weather : Weather.Sunny;
        float sun = clock != null ? clock.SunHeight : 0.7f;           // -1..1
        float day = Mathf.Clamp01(sun * 2f + 0.3f);
        var look = Regions.Look;
        float targetWind = w == Weather.Heatwave ? 0.85f : w == Weather.Cloudy ? 0.45f : w == Weather.Rain ? 0.3f : 0.12f;
        targetWind *= look.wind;                                       // the Delta is calmer than the open desert
        targetWind *= 0.85f + 0.15f * Mathf.Sin(Time.time * 0.13f);   // slow gusts
        Wind01 = Mathf.MoveTowards(Wind01, targetWind, Time.deltaTime * 0.08f);

        bool summer = clock != null && clock.Season == Season.Summer;
        float targetHeat = (w == Weather.Heatwave ? 1f : (w == Weather.Sunny && summer ? 0.55f : w == Weather.Sunny ? 0.25f : 0f)) * Mathf.Clamp01(sun * 1.6f);
        Heat01 = Mathf.MoveTowards(Heat01, targetHeat, Time.deltaTime * 0.1f);

        // Humid river haze: thick at dawn over the canals, thin by noon (0 in the desert).
        float morning = clock != null ? 1f - Mathf.Clamp01(clock.DayProgress / 0.2f) : 0f;
        Humidity01 = Mathf.Clamp01(look.humidity * (0.35f + 0.65f * morning) * (w == Weather.Rain || w == Weather.Cloudy ? 1.3f : 1f));

        UpdateDust(w, look);
        UpdateFog(day, look);
        UpdateHaze();
    }

    private void UpdateDust(Weather w, RegionLook look)
    {
        if (dust == null) return;
        float density = GraphicsEnhancer.DressingDensity;
        float dustiness = w == Weather.Rain ? 0f : Mathf.Clamp01((Wind01 - 0.25f) / 0.6f);
        DustRate = dustiness * 420f * density * look.dust;            // mild dust on the Delta, no big dust storms
        var em = dust.emission; em.rateOverTime = DustRate;
        if (DustRate > 1f && !dust.isPlaying) dust.Play();
        else if (DustRate <= 1f && dust.isPlaying) dust.Stop();

        // Keep the emitter upwind of the camera, blowing across the view.
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 dir = windDir.normalized;
        var main = dust.main; main.startSpeed = 4f + Wind01 * 10f;
        dust.transform.position = cam.transform.position - dir * 30f + Vector3.up * 1.5f;
        dust.transform.rotation = Quaternion.LookRotation(dir);
    }

    private void UpdateFog(float day, RegionLook look)
    {
        if (FogOverridden) return;
        float density = look.fogBase + 0.016f * Mathf.Clamp01((Wind01 - 0.3f) / 0.55f) * Mathf.Min(1f, look.dust * 2f) + 0.008f * Humidity01;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = density;
        RenderSettings.fogColor = Color.Lerp(fogNight, RegionLook.C(look.fog, fogDay), day);
    }

    private void UpdateHaze()
    {
        if (haze == null) return;
        haze.weight = GraphicsEnhancer.PostEnabled ? Heat01 : 0f;
        if (wobble != null) wobble.intensity.Override(Heat01 * 0.045f * Mathf.Sin(Time.time * 3.7f) * Mathf.Sin(Time.time * 1.3f + 0.7f));
    }
}
