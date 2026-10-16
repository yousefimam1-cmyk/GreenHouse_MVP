using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

/// <summary>
/// Extends AudioManager with world sound (all procedural unless files are dropped in Resources/Sounds):
/// engine pitched by vehicle speed, footsteps while walking / sprinting, wind (stronger in dust, from
/// DesertWeatherFX.Wind01), birds by day and crickets at night, and animal calls from the pen when you are near.
/// Volumes follow MazraaAudio (Master via AudioListener.volume, Ambience, Effects).
/// </summary>
[MazraaSystem(80)]
public class AmbientAudio : MonoBehaviour
{
    public static AmbientAudio Instance { get; private set; }

    public float animalHearingRange = 32f;

    private AudioSource engine, wind, night, oneShot, steps, animal;
    private Transform player;
    private Vector3 lastPlayerPos;
    private bool hasLast;
    private float stepTimer, birdTimer = 6f, animalTimer = 4f, scanTimer;
    private int stepIndex;
    private readonly List<FarmAnimal> animals = new List<FarmAnimal>();
    private readonly System.Random rnd = new System.Random(77);

    /// <summary>For tests/debug: what played last and how often.</summary>
    public string LastCall { get; private set; } = "";
    public int Footsteps { get; private set; }
    public float EnginePitch => engine != null ? engine.pitch : 0f;
    public bool EnginePlaying => engine != null && engine.isPlaying;
    public float WindVolume => wind != null ? wind.volume : 0f;
    public float NightVolume => night != null ? night.volume : 0f;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        MazraaAudio.Apply();
        engine = Source("Engine", ProceduralAudio.Get("engine"), true, 0.55f);
        wind = Source("Wind", ProceduralAudio.Get("wind"), true, 0f);
        night = Source("Night", ProceduralAudio.Get("crickets"), true, 0f);
        oneShot = Source("Ambience", null, false, 0f);
        steps = Source("Footsteps", null, false, 0f);
        animal = Source("Animal", null, false, 0.9f);
        animal.minDistance = 4f; animal.maxDistance = 45f;
        wind.Play(); night.Play();
    }

    private AudioSource Source(string name, AudioClip clip, bool loop, float spatial)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.clip = clip; s.loop = loop; s.playOnAwake = false; s.spatialBlend = spatial; s.volume = 0f;
        s.dopplerLevel = 0f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 3f; s.maxDistance = 60f;
        return s;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (player == null)
        {
            var inputs = FindAnyObjectByType<StarterAssetsInputs>();
            if (inputs != null) player = inputs.transform;
        }
        float amb = MazraaAudio.Ambience, fx = MazraaAudio.Effects;
        // Settings > Sound: vehicles, effects (footsteps), environment, animals
        UpdateEngine(fx * GameSettings.VehicleVolume);
        UpdateSteps(dt, fx * GameSettings.SfxVolume);
        UpdateAmbience(dt, amb * GameSettings.AmbientVolume);
        UpdateAnimals(dt, amb * GameSettings.AnimalVolume);
    }

    // ------------------------------------------------------------------ engine
    private void UpdateEngine(float fx)
    {
        var v = Vehicle.Current;
        if (v == null || engine == null)
        {
            if (engine != null && engine.isPlaying) engine.Stop();
            return;
        }
        if (!engine.isPlaying) engine.Play();
        engine.transform.position = v.transform.position;
        float speed01 = Mathf.Clamp01(Mathf.Abs(v.Speed) / Mathf.Max(1f, v.maxSpeed));
        float basePitch = v is Tractor ? 0.75f : 0.95f;
        bool noFuel = v.Fuel <= 0f;
        float target = noFuel ? 0.5f : basePitch + speed01 * 1.1f;
        engine.pitch = Mathf.Lerp(engine.pitch <= 0f ? target : engine.pitch, target, Time.deltaTime * 4f);
        engine.volume = (noFuel ? 0.15f : 0.35f + 0.35f * speed01) * fx;
    }

    // ------------------------------------------------------------------ footsteps
    private void UpdateSteps(float dt, float fx)
    {
        if (player == null || !player.gameObject.activeInHierarchy || Vehicle.Current != null || dt <= 0f) { hasLast = false; return; }
        Vector3 p = player.position;
        if (!hasLast) { lastPlayerPos = p; hasLast = true; return; }
        Vector3 d = p - lastPlayerPos;
        lastPlayerPos = p;
        float vertical = Mathf.Abs(d.y) / dt;
        d.y = 0f;
        float speed = d.magnitude / dt;
        if (speed < 0.6f || speed > 12f || vertical > 2.5f) { stepTimer = Mathf.Min(stepTimer, 0.1f); return; }
        stepTimer -= dt;
        if (stepTimer > 0f) return;
        bool sprint = speed > 3.6f;
        stepTimer = sprint ? 0.3f : Mathf.Lerp(0.6f, 0.42f, Mathf.InverseLerp(1f, 3.5f, speed));
        var clip = ProceduralAudio.Get("step" + (stepIndex++ % 4));
        if (clip == null) return;
        steps.pitch = 0.9f + (float)rnd.NextDouble() * 0.2f;
        steps.PlayOneShot(clip, (sprint ? 0.5f : 0.35f) * fx);
        Footsteps++;
    }

    // ------------------------------------------------------------------ wind, birds, crickets
    private void UpdateAmbience(float dt, float amb)
    {
        float wind01 = DesertWeatherFX.Wind01;
        if (wind != null)
        {
            wind.volume = Mathf.MoveTowards(wind.volume, (0.05f + 0.5f * wind01) * amb, dt * 0.2f);
            wind.pitch = 0.85f + wind01 * 0.35f;
        }
        bool isNight = DayNightCycle.Instance != null ? DayNightCycle.Instance.IsNight : (WorldClock.Instance != null && WorldClock.Instance.SunHeight < -0.05f);
        bool rain = WorldClock.Instance != null && WorldClock.Instance.Weather == Weather.Rain;
        if (night != null) night.volume = Mathf.MoveTowards(night.volume, isNight && !rain ? 0.22f * amb : 0f, dt * 0.1f);

        birdTimer -= dt;
        if (birdTimer <= 0f)
        {
            birdTimer = 9f + (float)rnd.NextDouble() * 14f;
            if (!isNight && !rain && wind01 < 0.6f && oneShot != null)
            {
                var c = ProceduralAudio.Get("birds");
                if (c != null) { oneShot.pitch = 0.85f + (float)rnd.NextDouble() * 0.35f; oneShot.PlayOneShot(c, 0.25f * amb); LastCall = "birds"; }
            }
        }
    }

    // ------------------------------------------------------------------ animals
    private void UpdateAnimals(float dt, float amb)
    {
        scanTimer -= dt;
        if (scanTimer <= 0f)
        {
            scanTimer = 3f;
            animals.Clear();
            animals.AddRange(FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None));
        }
        animalTimer -= dt;
        if (animalTimer > 0f) return;
        animalTimer = 5f + (float)rnd.NextDouble() * 9f;
        Vector3 listener = player != null && player.gameObject.activeInHierarchy ? player.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        var near = new List<FarmAnimal>();
        foreach (var a in animals)
            if (a != null && (a.transform.position - listener).magnitude < animalHearingRange) near.Add(a);
        if (near.Count == 0 || animal == null) return;
        var pick = near[rnd.Next(near.Count)];
        var clip = ProceduralAudio.Get(CallFor(pick.animalId));
        if (clip == null) return;
        animal.transform.position = pick.transform.position + Vector3.up;
        animal.pitch = 0.92f + (float)rnd.NextDouble() * 0.16f;
        animal.PlayOneShot(clip, 0.7f * amb);
        LastCall = pick.animalId;
    }

    /// <summary>Sound name for an animal id (unknown animals use the closest voice).</summary>
    public static string CallFor(string animalId)
    {
        switch (animalId)
        {
            case "cow": case "calf": case "bull": return "cow";
            case "sheep": case "lamb": return "sheep";
            case "goat": return "goat";
            case "camel": return "camel";
            case "chicken": case "hen": case "rooster": case "duck": return "chicken";
            default: return "sheep";
        }
    }
}
