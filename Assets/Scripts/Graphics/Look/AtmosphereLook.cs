using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Sky, light and air of the graphics makeover (one system, added by GameManager):
/// - physically based procedural sky (Skybox/Procedural, tinted per map: dusty Najd, humid Delta) whose sun disk
///   follows the sun / moon light; ambient light and reflections are re-baked from it as the day goes on
///   (DynamicGI.UpdateEnvironment: High / Ultra every few seconds, Medium rarely, Low never);
/// - soft sun shadows (strength by time of day), shadow cascades and HDR colour grading by preset;
/// - a realtime reflection probe around the camera on High / Ultra (glass, metal, wet ground reflect the farm);
/// - a subtle extra grade (cool shadows, warm highlights; shadows-midtones-highlights) on top of GraphicsEnhancer's ACES;
/// - global wind for the foliage shader (_MazraaWind from DesertWeatherFX.Wind01), wet ground after rain;
/// - the terrain dressing (GroundLook) for the current map.
/// Everything it changes on the URP asset is restored on exit.
/// </summary>
[MazraaSystem(74)]
public class AtmosphereLook : MonoBehaviour
{
    public static AtmosphereLook Instance { get; private set; }
    public const string SkyMaterial = "Look/Materials/Sky";
    public const float MaxSkyExposure = 1f, ProbeIntensity = 0.85f;

    public bool SkyInstalled { get; private set; }
    public bool ProbeActive => probe != null && probe.enabled;
    public float Wetness { get; private set; }
    public int EnvironmentUpdates { get; private set; }
    public Volume GradeVolume => grade;
    public int Cascades { get; private set; }

    private Material originalSky;
    private ReflectionProbe probe;
    private Volume grade;
    private ShadowsMidtonesHighlights smh;
    private UniversalRenderPipelineAsset urp;
    private bool urpSaved;
    private int origCascades;
    private ColorGradingMode origGrading;
    private float envTimer, probeTimer;
    private Vector3 lastProbePos = new Vector3(1e6f, 0f, 0f);
    private Quaternion lastSunRot = Quaternion.identity;
    private static readonly Vector3 WindDir = new Vector3(-0.6f, 0f, -0.8f);   // shamal from the north-west (as DesertWeatherFX)

    private void Awake()
    {
        Instance = this;
        InstallSky();   // before DayNightCycle.Start copies the skybox
    }

    private void Start()
    {
        BuildGrade();
        GraphicsEnhancer.PresetChanged += OnPreset;
        GameEvents.RegionChanged += OnRegion;
        GameSettings.Changed += ApplyPreset;
        ApplyPreset();
        Invoke(nameof(DressTerrain), 0.5f);
    }

    private void OnDestroy()
    {
        GraphicsEnhancer.PresetChanged -= OnPreset;
        GameEvents.RegionChanged -= OnRegion;
        GameSettings.Changed -= ApplyPreset;
        if (Instance == this) Instance = null;
        if (urp != null && urpSaved) { urp.shadowCascadeCount = origCascades; urp.colorGradingMode = origGrading; }
        if (originalSky != null) RenderSettings.skybox = originalSky;
        Shader.SetGlobalVector("_MazraaWind", Vector4.zero);
        LookLibrary.SetWetness(0f);
    }

    private void OnPreset(int p) => ApplyPreset();

    private void OnRegion(string id)
    {
        TintSky();
        DressTerrain();
        NpcWardrobeRedress();
        envTimer = 0f;
    }

    /// <summary>Dress the active terrain(s) for the current map.</summary>
    public void DressTerrain()
    {
        foreach (var t in Terrain.activeTerrains ?? new Terrain[0])
        {
            // Open-world tiles paint their own roads/fields (Assets/Scripts/World); repainting them here would erase the roads.
            if (t != null && t.gameObject.name.StartsWith("World Tile")) continue;
            GroundLook.ApplyTerrain(t, Vegetation.Current);
        }
    }

    private static void NpcWardrobeRedress()
    {
        int i = 0;
        foreach (var a in FindObjectsByType<Actor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (a == null || a.mode == ActorMode.Driver || !a.name.Contains("Characters/")) continue;
            NpcWardrobe.DressRegional(a.gameObject, a.name.Contains("female"), a.name.Length * 7 + i++);
        }
    }

    // ------------------------------------------------------------------ sky
    private void InstallSky()
    {
        var src = Resources.Load<Material>(SkyMaterial);
        if (src == null || src.shader == null || !src.shader.isSupported) return;
        originalSky = RenderSettings.skybox;
        var sky = new Material(src);
        sky.name = "Mazraa Sky (runtime)";
        // the sky also lights the farm (ambient re-baked from it): keep it from washing the image out
        if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", Mathf.Min(sky.GetFloat("_Exposure"), MaxSkyExposure));
        RenderSettings.skybox = sky;
        SkyInstalled = true;
        TintSky();
    }

    /// <summary>Region tint of the procedural sky: dusty, thick Najd air; clearer, bluer Delta sky.</summary>
    public void TintSky()
    {
        var sky = RenderSettings.skybox;
        if (!SkyInstalled || sky == null) return;
        bool delta = Vegetation.Current == Biome.Delta;
        if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", delta ? new Color(0.48f, 0.52f, 0.58f) : new Color(0.55f, 0.52f, 0.48f));
        if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", delta ? 1.05f : 1.3f);
        // darker lower hemisphere: it is the ambient light from below (bright sand here washed every shadow out)
        if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", delta ? new Color(0.3f, 0.32f, 0.25f) : new Color(0.45f, 0.38f, 0.29f));
    }

    // ------------------------------------------------------------------ grade + probe + URP settings by preset
    private void BuildGrade()
    {
        var go = new GameObject("Mazraa Look Grade");
        go.transform.SetParent(transform, false);
        grade = go.AddComponent<Volume>();
        grade.isGlobal = true;
        grade.priority = 52f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Mazraa look grade (runtime)";
        smh = profile.Add<ShadowsMidtonesHighlights>(true);
        smh.shadows.Override(new Vector4(0.96f, 0.99f, 1.05f, -0.02f));      // slightly cool, deeper shadows
        smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
        smh.highlights.Override(new Vector4(1.04f, 1.01f, 0.96f, 0.02f));    // warm sunlight
        grade.sharedProfile = profile;
    }

    /// <summary>Apply the current preset: probe, grade weight, cascades, HDR grading, sun shadows.</summary>
    public void ApplyPreset()
    {
        int p = GraphicsEnhancer.Preset;
        if (grade != null) grade.weight = GraphicsEnhancer.PostEnabled ? (p >= 2 ? 1f : 0.6f) : 0f;
        if (urp == null) urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            if (!urpSaved) { origCascades = urp.shadowCascadeCount; origGrading = urp.colorGradingMode; urpSaved = true; }
            Cascades = p <= 0 ? 1 : p == 1 ? 2 : 4;
            urp.shadowCascadeCount = Cascades;
            urp.colorGradingMode = p >= 2 ? ColorGradingMode.HighDynamicRange : ColorGradingMode.LowDynamicRange;
        }
        SetupProbe(p >= 2);
        var sun = RenderSettings.sun;
        if (sun != null && GameSettings.ShadowQuality != 0) sun.shadows = p <= 0 ? LightShadows.Hard : LightShadows.Soft;
    }

    private void SetupProbe(bool on)
    {
        if (!on) { if (probe != null) probe.enabled = false; return; }
        if (probe == null)
        {
            var go = new GameObject("Mazraa Reflection Probe");
            go.transform.SetParent(transform, false);
            probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = new Vector3(4000f, 1200f, 4000f);
            probe.boxProjection = false;
            probe.hdr = true;
            probe.shadowDistance = 60f;
            probe.importance = 1;
            probe.intensity = ProbeIntensity;
        }
        probe.resolution = GraphicsEnhancer.Preset >= 3 ? 256 : 128;
        probe.farClipPlane = GraphicsEnhancer.Preset >= 3 ? 800f : 450f;
        probe.enabled = true;
        probeTimer = 0f;
    }

    // ------------------------------------------------------------------ per frame
    private void Update()
    {
        float dt = Time.deltaTime;
        // wind for Mazraa/Foliage
        float wind = 0.25f + DesertWeatherFX.Wind01 * 1.15f;
        Vector3 d = WindDir.normalized;
        Shader.SetGlobalVector("_MazraaWind", new Vector4(d.x, 0f, d.z, wind));

        // wet ground: soaks in a minute of rain, dries in a few minutes (faster in heat)
        var clock = WorldClock.Instance;
        bool raining = clock != null && clock.Weather == Weather.Rain;
        float dry = 1f / (240f - 120f * DesertWeatherFX.Heat01);
        Wetness = Mathf.Clamp01(Wetness + (raining ? dt / 60f : -dt * dry));
        LookLibrary.SetWetness(Wetness);
        GroundLook.SetWetness(Wetness);

        // sun shadows soften at night (moonlight) and in dust
        var sun = RenderSettings.sun;
        if (sun != null)
        {
            bool night = DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
            sun.shadowStrength = Mathf.MoveTowards(sun.shadowStrength, (night ? 0.55f : 0.9f) * (1f - 0.35f * DesertWeatherFX.Wind01), dt * 0.5f);
        }

        // ambient / reflections from the sky as the sun moves (cost: one small cubemap bake)
        int p = GraphicsEnhancer.Preset;
        float every = p >= 2 ? 4f : p == 1 ? 20f : -1f;
        envTimer -= dt;
        if (SkyInstalled && every > 0f && envTimer <= 0f)
        {
            Quaternion sr = sun != null ? sun.transform.rotation : Quaternion.identity;
            if (EnvironmentUpdates == 0 || Quaternion.Angle(sr, lastSunRot) > 1.5f)
            {
                DynamicGI.UpdateEnvironment();
                EnvironmentUpdates++;
                lastSunRot = sr;
            }
            envTimer = every;
        }

        // realtime reflection probe follows the camera (re-rendered when it moved or every 10 s)
        if (probe != null && probe.enabled)
        {
            probeTimer -= dt;
            var cam = FoliageGrass.ViewCamera();
            Vector3 at = cam != null ? cam.transform.position : transform.position;
            if (probeTimer <= 0f || (at - lastProbePos).sqrMagnitude > 40f * 40f)
            {
                probe.transform.position = at + Vector3.up * 1.5f;
                probe.RenderProbe();
                lastProbePos = at;
                probeTimer = 10f;
            }
        }
    }
}
