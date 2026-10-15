using UnityEngine;

/// <summary>
/// Farm pets: the AmbientLife dog becomes a saluki (سلوقي) you can pet with E once a day (hearts + a small gift:
/// seeds, feed or a few riyals), and a falcon perch next to the farmhouse: E flies the falcon (it circles and comes
/// back) once a day for a small reward; five flights earn the Falconer title. Saved under mazraa.cozy.*.
/// </summary>
[MazraaSystem(61)]
public class PetSystem : MonoBehaviour
{
    public static PetSystem Instance;

    public GameObject Saluki { get; private set; }
    public GameObject Perch { get; private set; }
    public bool FalconFlying => flight != null;
    public int FalconFlights => SaveData.GetInt("mazraa.cozy.falconFlights", 0);

    private GameObject perchedBird, flight;
    private float flightTime;
    private Vector3 flightCenter;
    private const float FlightSeconds = 12f;

    private void Awake() => Instance = this;
    private void Start()
    {
        Invoke(nameof(Setup), 2f);   // AmbientLife spawns the dog at 1.2 s
        GameEvents.NewGameStarted += OnNewGame;
    }
    private void OnDestroy() { GameEvents.NewGameStarted -= OnNewGame; if (Instance == this) Instance = null; }

    private void OnNewGame() { if (flight != null) EndFlight(false); }

    private void Setup()
    {
        SetupSaluki();
        BuildPerch();
    }

    // ---------------- saluki ----------------

    private void SetupSaluki()
    {
        Actor dog = null;
        foreach (var a in FindObjectsByType<Actor>(FindObjectsSortMode.None))
            if (a.mode == ActorMode.FollowPlayer) { dog = a; break; }
        GameObject go;
        if (dog != null)
        {
            go = dog.gameObject;
            go.name = "Saluki";
            // Salukis are slim, long-legged sighthounds: stretch the dog model and give it a sandy coat.
            foreach (Transform child in go.transform) child.localScale = Vector3.Scale(child.localScale, new Vector3(0.8f, 1.15f, 1.2f));
            CozyFx.Tint(go, new Color(0.88f, 0.76f, 0.58f), 0.6f);
        }
        else
        {
            go = new GameObject("Saluki");
            go.transform.position = Build.OnGround(CozyFx.FarmStart + new Vector3(2.5f, 0, 2f));
            var coat = Build.Mat(new Color(0.86f, 0.74f, 0.56f), 0.3f);
            Build.Part(PrimitiveType.Capsule, go.transform, new Vector3(0, 0.55f, 0), new Vector3(0.3f, 0.45f, 0.3f), coat, "Body", new Vector3(90, 0, 0));
            Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0, 0.85f, 0.45f), new Vector3(0.16f, 0.18f, 0.32f), coat, "Head");
            for (int i = 0; i < 4; i++)
                Build.Part(PrimitiveType.Cube, go.transform, new Vector3(i % 2 == 0 ? -0.1f : 0.1f, 0.22f, i < 2 ? 0.28f : -0.28f), new Vector3(0.06f, 0.45f, 0.06f), coat, "Leg");
        }
        CozyFx.MakeInteractable(go, new Vector3(0, 0.45f, 0), new Vector3(0.6f, 0.9f, 1.2f), SalukiPrompt, () => Pet());
        Saluki = go;
    }

    public bool PettedToday => SaveData.GetInt("mazraa.cozy.petDay", 0) == CozyFx.Today;

    private string SalukiPrompt() => PettedToday ? Loc.T("Saluki (happy - petted today)") : Loc.T("[E] Pet the saluki");

    /// <summary>Pet the saluki: once per in-game day, hearts and a small gift. Returns false if already petted today.</summary>
    public bool Pet()
    {
        Vector3 pos = Saluki != null ? Saluki.transform.position + Vector3.up * 1.1f : CozyFx.FarmStart;
        if (PettedToday)
        {
            CozyFx.Hearts(pos);
            CozyFx.Alert(Loc.T("The saluki wags its tail. Come back tomorrow for another gift."));
            return false;
        }
        SaveData.SetInt("mazraa.cozy.petDay", CozyFx.Today);
        SaveData.Save();
        CozyFx.Hearts(pos);
        var actor = Saluki != null ? Saluki.GetComponent<Actor>() : null;
        if (actor != null) actor.React("emote-yes", 1f);
        string gift = CozyFx.RandomGift(10, 30);
        CozyFx.Alert(Loc.Display(Loc.Raw("The saluki found a gift") + ": " + gift));
        GameEvents.RaiseCustom("petSaluki", 1f);
        return true;
    }

    // ---------------- falcon ----------------

    private void BuildPerch()
    {
        var p = CozyFx.Player;
        Vector3 fwd = p != null ? Vector3.ProjectOnPlane(p.forward, Vector3.up).normalized : Vector3.forward;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        var root = new GameObject("Falcon Perch");
        root.transform.SetPositionAndRotation(Build.OnGround(CozyFx.FarmStart - right * 4f - fwd * 2.5f), Quaternion.LookRotation(fwd));
        var wood = Build.Mat(new Color(0.45f, 0.3f, 0.18f), 0.2f);
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.7f, 0), new Vector3(0.12f, 0.7f, 0.12f), wood, "Post");
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0, 1.4f, 0), new Vector3(0.08f, 0.35f, 0.08f), wood, "Bar", new Vector3(0, 0, 90));
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.03f, 0), new Vector3(0.7f, 0.03f, 0.7f), Build.Mat(new Color(0.75f, 0.62f, 0.42f), 0.1f), "Base");
        perchedBird = NewFalcon(root.transform, new Vector3(0, 1.48f, 0));
        CozyFx.Label(root.transform, new Vector3(0, 2.4f, 0), "Falcon", 2.2f, new Color(1f, 0.85f, 0.55f));
        CozyFx.MakeInteractable(root, new Vector3(0, 0.9f, 0), new Vector3(0.9f, 1.8f, 0.9f), FalconPrompt, () => FlyFalcon());
        Perch = root;
    }

    private static GameObject NewFalcon(Transform parent, Vector3 localPos)
    {
        var holder = new GameObject("Falcon");
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = localPos;
        if (Build.TryModel("eagle", holder.transform, 0.55f, 0f) == null)
        {
            var feathers = Build.Mat(new Color(0.55f, 0.45f, 0.35f), 0.3f);
            Build.Part(PrimitiveType.Sphere, holder.transform, new Vector3(0, 0.18f, 0), new Vector3(0.22f, 0.3f, 0.22f), feathers, "Body");
            Build.Part(PrimitiveType.Sphere, holder.transform, new Vector3(0, 0.38f, 0.03f), Vector3.one * 0.14f, feathers, "Head");
            Build.Part(PrimitiveType.Cube, holder.transform, new Vector3(0, 0.37f, 0.11f), new Vector3(0.04f, 0.04f, 0.06f), Build.Mat(new Color(0.9f, 0.75f, 0.2f), 0.4f), "Beak");
        }
        return holder;
    }

    public bool FlownToday => SaveData.GetInt("mazraa.cozy.falconDay", 0) == CozyFx.Today;

    private string FalconPrompt()
    {
        if (flight != null) return Loc.T("The falcon is hunting...");
        return FlownToday ? Loc.T("The falcon is resting - fly it again tomorrow") : Loc.T("[E] Fly the falcon");
    }

    /// <summary>Send the falcon up: it circles over the farm and returns with a reward. Once per in-game day.</summary>
    public bool FlyFalcon()
    {
        if (flight != null || Perch == null) return false;
        if (FlownToday) { CozyFx.Alert(Loc.T("The falcon is resting - fly it again tomorrow")); return false; }
        SaveData.SetInt("mazraa.cozy.falconDay", CozyFx.Today);
        SaveData.Save();
        if (perchedBird != null) perchedBird.SetActive(false);
        flightCenter = Perch.transform.position;
        flight = new GameObject("Falcon (flying)");
        flight.transform.position = flightCenter + Vector3.up * 1.5f;
        var model = Build.TryModel("eagle", flight.transform, 1.1f, 0f);
        if (model == null) NewFalcon(flight.transform, Vector3.zero);
        else
        {
            var anim = model.GetComponentInChildren<Animation>();
            if (anim != null) foreach (AnimationState s in anim) if (s.name.ToLowerInvariant().Contains("fly")) { s.wrapMode = WrapMode.Loop; anim.CrossFade(s.name, 0.2f); break; }
        }
        flightTime = 0f;
        CozyFx.Alert(Loc.T("The falcon takes off!"));
        return true;
    }

    private void Update()
    {
        if (flight == null) return;
        flightTime += Time.deltaTime;
        float t = flightTime / FlightSeconds;
        // up, three wide circles, then glide back down to the perch
        float climb = Mathf.Clamp01(t * 4f) * Mathf.Clamp01((1f - t) * 4f);
        float ang = t * Mathf.PI * 6f;
        float radius = 16f * climb;
        Vector3 p = flightCenter + new Vector3(Mathf.Cos(ang) * radius, 1.5f + 14f * climb, Mathf.Sin(ang) * radius);
        Vector3 dir = p - flight.transform.position;
        flight.transform.position = p;
        if (dir.sqrMagnitude > 1e-4f) flight.transform.rotation = Quaternion.LookRotation(dir.normalized);
        if (flightTime >= FlightSeconds) EndFlight(true);
    }

    private void EndFlight(bool reward)
    {
        if (flight != null) Destroy(flight);
        flight = null;
        if (perchedBird != null) perchedBird.SetActive(true);
        if (!reward) return;
        int flights = FalconFlights + 1;
        SaveData.SetInt("mazraa.cozy.falconFlights", flights);
        int sar = Random.Range(20, 51);
        if (EconomyManager.Instance != null) EconomyManager.Instance.AddMoney(sar);
        string msg = Loc.Raw("The falcon is back on the perch") + "  +" + sar + " " + Loc.Raw("SAR");
        if (flights >= 5 && SaveData.GetInt("mazraa.cozy.falconer", 0) == 0)
        {
            SaveData.SetInt("mazraa.cozy.falconer", 1);
            if (EconomyManager.Instance != null) EconomyManager.Instance.AddMoney(150);
            msg = Loc.Raw("Achievement: Falconer") + "  +150 " + Loc.Raw("SAR");
            CollectionBook.Record("feats", "falconer");
        }
        SaveData.Save();
        if (Perch != null) FeedbackFX.Float(Perch.transform.position + Vector3.up * 2.5f, "+" + sar + " SAR", new Color(1f, 0.8f, 0.25f));
        CozyFx.Alert(Loc.Display(msg));
        GameEvents.RaiseCustom("flyFalcon", 1f);
    }

    /// <summary>For tests: finish the current flight now.</summary>
    public void LandNow() { if (flight != null) EndFlight(true); }
}
