using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>Game-feel feedback driven by GameEvents: floating "+72 kg" text and a pop on harvested plants.</summary>
public class FeedbackFX : MonoBehaviour
{
    private void OnEnable()
    {
        GameEvents.Harvested += OnHarvest;
        GameEvents.Delivered += OnDelivered;
    }

    private void OnDisable()
    {
        GameEvents.Harvested -= OnHarvest;
        GameEvents.Delivered -= OnDelivered;
    }

    private void OnHarvest(Cropbed bed, float kg)
    {
        if (bed == null) return;
        Float(bed.transform.position + Vector3.up * 1.6f, "+" + kg.ToString("F0") + " kg", new Color(0.55f, 0.95f, 0.4f));
        bed.transform.DOKill(true);
        bed.transform.DOPunchScale(Vector3.one * 0.06f, 0.35f, 6, 0.6f);
    }

    private void OnDelivered(float kg, int money)
    {
        var truck = FarmTruck.Instance;
        if (truck != null) Float(truck.transform.position + Vector3.up * 3f, "+" + money.ToString("N0") + " SAR", new Color(1f, 0.8f, 0.25f));
    }

    public static void Float(Vector3 pos, string text, Color color)
    {
        var go = new GameObject("Float Text");
        go.transform.position = pos;
        var t = go.AddComponent<TextMeshPro>();
        t.text = text;
        t.fontSize = 5f;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.fontStyle = FontStyles.Bold;
        Loc.ExcludeFromScene(t);
        go.AddComponent<Billboard>();
        go.transform.DOMoveY(pos.y + 1.8f, 1.4f).SetEase(Ease.OutCubic);
        t.DOFade(0f, 1.4f).SetEase(Ease.InQuad).OnComplete(() => Destroy(go));
    }
}
