using UnityEngine;

public enum Season { Spring, Summer, Autumn, Winter }
public enum Weather { Sunny, Cloudy, Rain, Heatwave }

/// <summary>
/// In-game calendar: days, four seasons and daily weather (rolled per Saudi climate, Al-Qassim: rare rain, mostly in winter
/// and spring; long hot summers with frequent heatwaves). Rain waters every field; heatwaves dry them twice as fast. Saved.
/// </summary>
public class WorldClock : MonoBehaviour
{
    public static WorldClock Instance;

    [Tooltip("Real seconds per in-game day")] public float secondsPerDay = 360f;
    [Tooltip("Days per season")] public int daysPerSeason = 5;

    public int Day { get; private set; }               // 1-based day of the whole game
    public float DayProgress { get; private set; }     // 0..1 through the current day
    public Weather Weather { get; private set; }
    public Season Season => (Season)(((Day - 1) / Mathf.Max(1, daysPerSeason)) % 4);
    public int DayOfSeason => (Day - 1) % Mathf.Max(1, daysPerSeason) + 1;

    /// <summary>Co-op client: the host's calendar is applied every second (ApplyRemote); the day doesn't roll over here.</summary>
    public bool FollowRemote { get; set; }

    public static event System.Action<int> DayStarted;
    public static event System.Action<Season> SeasonChanged;
    public static event System.Action<Weather> WeatherChanged;

    private ParticleSystem rain;
    private Light sun;
    private float sunBaseIntensity = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { DayStarted = null; SeasonChanged = null; WeatherChanged = null; }

    private void Awake()
    {
        Instance = this;
        Day = Mathf.Max(1, SaveData.GetInt("mazraa.day", 1));
        DayProgress = SaveData.GetFloat("mazraa.dayProgress", 0.25f);
        Weather = (Weather)SaveData.GetInt("mazraa.weather", 0);
    }

    private void Start()
    {
        sun = RenderSettings.sun;
        if (sun == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null) sunBaseIntensity = sun.intensity;
        BuildRain();
        ApplyWeatherVisuals();
        InvokeRepeating(nameof(SaveState), 10f, 10f);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        DayProgress += Time.deltaTime / Mathf.Max(10f, secondsPerDay);
        if (FollowRemote) DayProgress = Mathf.Min(DayProgress, 0.9999f);
        else if (DayProgress >= 1f)
        {
            DayProgress -= 1f;
            NextDay();
        }
        if (rain != null && Camera.main != null) rain.transform.position = Camera.main.transform.position + Vector3.up * 12f;
        else if (rain != null && Vehicle.Current != null) rain.transform.position = Vehicle.Current.transform.position + Vector3.up * 14f;
    }

    /// <summary>Skip to the next morning (used by beds, and for testing).</summary>
    public void NextDay()
    {
        Season before = Season;
        Day++;
        RollWeather();
        DayStarted?.Invoke(Day);
        if (Season != before) SeasonChanged?.Invoke(Season);
        SaveState();
        if (UIManager.Instance != null)
            UIManager.Instance.ShowAlertMessage(Loc.Display(Loc.Raw("Day") + " " + Day + " - " + SeasonName() + " - " + WeatherName()));
    }

    private void RollWeather()
    {
        float r = Random.value;
        switch (Season)
        {
            case Season.Winter: Weather = r < 0.15f ? Weather.Rain : r < 0.35f ? Weather.Cloudy : Weather.Sunny; break;
            case Season.Spring: Weather = r < 0.10f ? Weather.Rain : r < 0.25f ? Weather.Heatwave : r < 0.40f ? Weather.Cloudy : Weather.Sunny; break;
            case Season.Summer: Weather = r < 0.55f ? Weather.Heatwave : r < 0.62f ? Weather.Cloudy : Weather.Sunny; break;
            default: Weather = r < 0.05f ? Weather.Rain : r < 0.25f ? Weather.Heatwave : r < 0.40f ? Weather.Cloudy : Weather.Sunny; break;
        }
        ApplyWeatherVisuals();
        WeatherChanged?.Invoke(Weather);
    }

    private void BuildRain()
    {
        var go = new GameObject("Rain");
        go.transform.SetParent(transform, false);
        rain = go.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = rain.main;
        main.startLifetime = 1.4f; main.startSpeed = 18f; main.startSize = 0.05f; main.maxParticles = 3000;
        main.startColor = new Color(0.75f, 0.82f, 0.9f, 0.6f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.4f;
        var em = rain.emission; em.rateOverTime = 1500f;
        var shape = rain.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(40f, 40f, 1f);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Stretch;
        pr.lengthScale = 6f;
        pr.sharedMaterial = Build.Mat(new Color(0.75f, 0.82f, 0.9f, 1f), 0.8f);
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void ApplyWeatherVisuals()
    {
        if (rain != null)
        {
            if (Weather == Weather.Rain && !rain.isPlaying) rain.Play();
            else if (Weather != Weather.Rain && rain.isPlaying) rain.Stop();
        }
        if (sun != null && DayNightCycle.Instance == null) sun.intensity = sunBaseIntensity * WeatherLight;
    }

    private void SaveState()
    {
        SaveData.SetInt("mazraa.day", Day);
        SaveData.SetFloat("mazraa.dayProgress", DayProgress);
        SaveData.SetInt("mazraa.weather", (int)Weather);
        SaveData.Save();
    }

    /// <summary>Co-op: take the host's day, time and weather (also used to restore your own calendar after leaving).</summary>
    public void ApplyRemote(int day, float progress, Weather weather)
    {
        Season before = Season;
        bool newDay = day > Day;
        Day = Mathf.Max(1, day);
        DayProgress = Mathf.Clamp(progress, 0f, 0.9999f);
        if (weather != Weather) { Weather = weather; ApplyWeatherVisuals(); WeatherChanged?.Invoke(Weather); }
        if (newDay) DayStarted?.Invoke(Day);
        if (Season != before) SeasonChanged?.Invoke(Season);
    }

    public void ResetCalendar()
    {
        Day = 1; DayProgress = 0.25f; Weather = Weather.Sunny;
        ApplyWeatherVisuals();
        SaveState();
    }

    /// <summary>Sunlight multiplier from the weather.</summary>
    public float WeatherLight => Weather == Weather.Rain ? 0.45f : Weather == Weather.Cloudy ? 0.7f : Weather == Weather.Heatwave ? 1.2f : 1f;

    /// <summary>Clock text for the HUD, e.g. 07:30 (day starts at 06:00).</summary>
    public string TimeText()
    {
        float hours = (6f + DayProgress * 24f) % 24f;
        int h = Mathf.FloorToInt(hours), m = Mathf.FloorToInt((hours - h) * 60f / 10f) * 10;
        return h.ToString("00") + ":" + m.ToString("00");
    }

    /// <summary>0 at midnight .. 1 at noon (sun height), from the clock (day starts at 06:00).</summary>
    public float SunHeight => Mathf.Sin(((6f + DayProgress * 24f) % 24f - 6f) / 24f * Mathf.PI * 2f);

    public static string SeasonId(Season s) => s.ToString().ToLowerInvariant();
    public string SeasonName() => Loc.Raw(Season.ToString());
    public string WeatherName() => Loc.Raw(Weather.ToString());
}
