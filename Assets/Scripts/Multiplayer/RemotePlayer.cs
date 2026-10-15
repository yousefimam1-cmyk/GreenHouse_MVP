using TMPro;
using UnityEngine;

/// <summary>
/// Another player on the farm: a character model (Resources/Models/Characters) with a name tag, moved smoothly to
/// the poses its owner sends. Shows emotes and chat as a bubble. Hidden while that player drives a vehicle
/// (the vehicle itself is moved instead, see Vehicle.ApplyRemotePose).
/// </summary>
public class RemotePlayer : MonoBehaviour
{
    public static readonly string[] Models = { "Characters/character-male-a", "Characters/character-female-b", "Characters/character-male-c", "Characters/character-female-d" };

    public ulong Id { get; private set; }
    public string PlayerName { get; private set; } = "";
    public string VehicleId { get; private set; } = "";
    public Vector3 TargetPosition => target;

    private Actor actor;
    private GameObject placeholder;
    private TextMeshPro nameTag, bubble;
    private Vector3 target;
    private float targetYaw, speed, bubbleTimer, danceTimer;
    private bool hasPose;

    public static RemotePlayer Create(ulong id, string playerName, Transform parent)
    {
        var go = new GameObject("Remote Player " + id);
        if (parent != null) go.transform.SetParent(parent, false);
        var rp = go.AddComponent<RemotePlayer>();
        rp.Id = id;
        rp.BuildBody();
        rp.SetName(playerName);
        return rp;
    }

    private void BuildBody()
    {
        string model = Models[(int)(Id % (ulong)Models.Length)];
        actor = Actor.Spawn(model, 1.75f, true, ActorMode.Remote, transform.position, transform);
        if (actor == null)
        {   // model missing: a simple capsule so the player is still visible
            placeholder = Build.Part(PrimitiveType.Capsule, transform, new Vector3(0, 0.9f, 0), new Vector3(0.6f, 0.9f, 0.6f), Build.Mat(new Color(0.95f, 0.55f, 0.15f)), "Body");
            foreach (var c in placeholder.GetComponentsInChildren<Collider>(true)) Destroy(c);
        }
        nameTag = Label(new Vector3(0, 2.15f, 0), 2.4f, new Color(1f, 0.85f, 0.45f));
        bubble = Label(new Vector3(0, 2.65f, 0), 3f, Color.white);
        bubble.gameObject.SetActive(false);
    }

    private TextMeshPro Label(Vector3 localPos, float size, Color color)
    {
        var go = new GameObject("Tag");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var t = go.AddComponent<TextMeshPro>();
        t.alignment = TextAlignmentOptions.Center;
        t.fontSize = size;
        t.color = color;
        t.richText = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        Loc.ExcludeFromScene(t);
        go.AddComponent<Billboard>();
        return t;
    }

    private static void SetText(TextMeshPro t, string s)
    {
        if (t == null) return;
        bool arabic = Mp.HasArabic(s);
        if (arabic && Loc.ArabicFont != null) t.font = Loc.ArabicFont;
        t.text = Mp.UserText(s);
    }

    public void SetName(string playerName)
    {
        PlayerName = string.IsNullOrEmpty(playerName) ? "Player " + Id : playerName;
        SetText(nameTag, PlayerName);
    }

    public void ApplyPose(NetPose p)
    {
        VehicleId = p.vehicle ?? "";
        target = p.position;
        targetYaw = p.yaw;
        speed = p.speed;
        if (!hasPose) { hasPose = true; transform.position = target; transform.rotation = Quaternion.Euler(0, targetYaw, 0); }
    }

    /// <summary>Show a line above the player's head for a few seconds (emote text or chat, already display-ready).</summary>
    public void Say(string text, float seconds = 6f)
    {
        if (bubble == null) return;
        bubble.gameObject.SetActive(true);
        SetText(bubble, text);
        bubbleTimer = seconds;
    }

    public void PlayEmote(int index)
    {
        Say(Mp.Tr(MultiplayerManager.EmoteText(index)), 4f);
        if (index == 1) danceTimer = 2.5f;
        else if (actor != null) actor.React("emote-yes", 1.6f);
    }

    private void Update()
    {
        bool driving = VehicleId.Length > 0;
        SetVisible(!driving);
        if (bubbleTimer > 0f && (bubbleTimer -= Time.deltaTime) <= 0f && bubble != null) bubble.gameObject.SetActive(false);
        if (!hasPose || driving) return;

        float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
        Vector3 before = transform.position;
        transform.position = (target - before).sqrMagnitude > 400f ? target : Vector3.Lerp(before, target, k);   // teleport if far behind
        float yaw = targetYaw;
        if (danceTimer > 0f) { danceTimer -= Time.deltaTime; yaw = transform.eulerAngles.y + 540f * Time.deltaTime; k = 1f; }
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, yaw, 0), k);
        if (actor != null && danceTimer <= 0f)
        {
            float moving = Mathf.Max(speed, (transform.position - before).magnitude / Mathf.Max(1e-4f, Time.deltaTime));
            if (moving > 3.5f) actor.Play("run", "sprint", "walking", "walk");
            else if (moving > 0.3f) actor.Play("walking", "walk", "run");
            else actor.Play("idle");
        }
    }

    private void SetVisible(bool on)
    {
        if (actor != null && actor.transform.childCount > 0)
        {
            var m = actor.transform.GetChild(0).gameObject;
            if (m.activeSelf != on) m.SetActive(on);
        }
        if (placeholder != null && placeholder.activeSelf != on) placeholder.SetActive(on);
        if (nameTag != null && nameTag.gameObject.activeSelf != on) nameTag.gameObject.SetActive(on);
    }
}
