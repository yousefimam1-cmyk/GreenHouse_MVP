using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;
using StarterAssets;

[Serializable]
public class ControlStationUI
{
    public ModuleType moduleType;
    public TextMeshProUGUI valueTxt;
    public GameObject optimizeButton;
}

[Serializable]
public class CropbedUI
{
    public CropAttributeType attributeType;
    public TextMeshProUGUI valueTxt;
    public TextMeshProUGUI satisfiedText;
}

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("References")]
    [SerializeField] private CanvasGroup controlStationUI;
    [SerializeField] private CanvasGroup cropbedUI;

    [Space]
    [Header("Control Station UI")]
    public ControlStationUI[] moduleStatsUI;

    [Space]
    [Header("Crop Bed UI")]
    public CropbedUI[] cropbedStatsUI;

    [SerializeField] private TextMeshProUGUI alertMsg;
    [SerializeField] private float alertDuration = 1.6f;

    public bool IsUIOpened { get; private set; } = false;
    public ConfigurationData configurationData;

    private GreenhouseManager currentGreenHouse;
    private Cropbed currentCropbed;
    private CropbedData currentCropData;
    private StarterAssetsInputs inputs;
    private TMP_Text cropTitleText;
    private bool controlStationOpen, cropbedOpen;

    /// <summary>Raised when the crop bed panel opens (bed) or closes (null). Used by the HUD info panel.</summary>
    public event Action<Cropbed> OnCropbedPanelChanged;
    /// <summary>Raised when the control station panel opens (greenhouse) or closes (null). Used for partner slots.</summary>
    public event Action<GreenhouseManager> OnControlStationPanelChanged;

    private void Awake()
    {
        Instance = this;
        GreenHouseHelper.Initialize(configurationData);
        // Find the title before any localization changes its text.
        if (cropbedUI != null)
            foreach (var t in cropbedUI.GetComponentsInChildren<TMP_Text>(true))
                if (t.text.Trim() == "Crop Stats") { cropTitleText = t; break; }
        // Texts this script rewrites itself must not be reset by the scene-wide translation pass.
        Loc.ExcludeFromScene(alertMsg);
        Loc.ExcludeFromScene(cropTitleText);
        if (moduleStatsUI != null) foreach (var m in moduleStatsUI) Loc.ExcludeFromScene(m.valueTxt);
        if (cropbedStatsUI != null) foreach (var c in cropbedStatsUI) { Loc.ExcludeFromScene(c.valueTxt); Loc.ExcludeFromScene(c.satisfiedText); }
    }

    private void Start()
    {
        inputs = FindAnyObjectByType<StarterAssetsInputs>();
        Loc.OnLanguageChanged += RefreshOpenPanels;
        Loc.LocalizeScene();
    }

    private void OnDestroy()
    {
        Loc.OnLanguageChanged -= RefreshOpenPanels;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !IsUIOpened) return;
        if (kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)
        {
            if (cropbedOpen) HideCropbedUI();
            if (controlStationOpen) HideControlStationUI();
        }
    }

    private bool externalOpen;

    /// <summary>For panels built in code (shop, pause): unlocks the cursor and pauses interaction while open.</summary>
    public void SetExternalPanel(bool open)
    {
        externalOpen = open;
        if (!controlStationOpen && !cropbedOpen) SetGameplayInput(!open);
    }

    private void SetGameplayInput(bool enabled)
    {
        IsUIOpened = !enabled;
        if (inputs != null)
        {
            inputs.cursorInputForLook = enabled;
            inputs.SetCursorState(enabled);
            if (!enabled) inputs.LookInput(Vector2.zero);
        }
        else
        {
            Cursor.lockState = enabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !enabled;
        }
    }

    private static void FadeIn(CanvasGroup g)
    {
        if (g == null) return;
        g.DOKill();
        g.gameObject.SetActive(true);
        g.DOFade(1.0f, 0.3f);
    }

    private static void FadeOut(CanvasGroup g)
    {
        if (g == null) return;
        g.DOKill();
        // Kill + check prevents a quick close/reopen from hiding the panel that was just reopened.
        g.DOFade(0.0f, 0.3f).OnComplete(() => g.gameObject.SetActive(false));
    }

    // ---------------- Control station ----------------

    public void ShowControlStationUI(GreenhouseManager greenhouseManager)
    {
        if (greenhouseManager == null) return;
        if (cropbedOpen) HideCropbedUI();
        currentGreenHouse = greenhouseManager;
        controlStationOpen = true;
        FadeIn(controlStationUI);
        UpdateControlStationUI();
        SetGameplayInput(false);
        OnControlStationPanelChanged?.Invoke(greenhouseManager);
    }

    private void UpdateControlStationUI()
    {
        if (currentGreenHouse == null) return;
        foreach (var moduleUI in moduleStatsUI)
        {
            if (moduleUI.valueTxt != null)
                moduleUI.valueTxt.text = currentGreenHouse.GetModuleValue(moduleUI.moduleType).ToString("F1");
            if (moduleUI.optimizeButton != null)
                moduleUI.optimizeButton.SetActive(!currentGreenHouse.IsModuleOptimized(moduleUI.moduleType));
        }
    }

    public void HideControlStationUI()
    {
        currentGreenHouse = null;
        controlStationOpen = false;
        if (!cropbedOpen && !externalOpen) SetGameplayInput(true);
        FadeOut(controlStationUI);
        OnControlStationPanelChanged?.Invoke(null);
    }

    public void OptimizeButtonCallback(string module)
    {
        if (currentGreenHouse == null) return;
        if (!Enum.TryParse(module, out ModuleType moduleType))
        {
            Debug.LogWarning("[UIManager] Unknown module name on Optimize button: " + module);
            return;
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX();
        currentGreenHouse.OptimizeModule(moduleType);
        UpdateControlStationUI();
    }

    // ---------------- Crop bed ----------------

    public void ShowCropbedUI(Cropbed bed)
    {
        currentCropbed = bed;
        ShowCropbedUI(bed != null ? bed.data : null);
    }

    public void ShowCropbedUI(CropbedData data)
    {
        if (data == null) return;
        if (controlStationOpen) HideControlStationUI();
        currentCropData = data;
        cropbedOpen = true;
        FadeIn(cropbedUI);
        UpdateCropbedUI();
        SetGameplayInput(false);
        OnCropbedPanelChanged?.Invoke(currentCropbed);
    }

    private void UpdateCropbedUI()
    {
        if (currentCropData == null) return;
        foreach (var row in cropbedStatsUI)
        {
            float value = GreenHouseHelper.Get(currentCropData, row.attributeType);
            bool ok = GreenHouseHelper.IsValueSatisfied(value, row.attributeType);
            if (row.valueTxt != null) row.valueTxt.text = value.ToString("F1");
            if (row.satisfiedText != null)
            {
                Loc.Set(row.satisfiedText, ok ? "Satisfied" : "Not Satisfied");
                row.satisfiedText.color = ok ? Color.green : Color.red;
            }
        }
        if (cropTitleText != null)
        {
            if (currentCropbed != null && currentCropbed.Crop != null)
                Loc.SetDisplay(cropTitleText, Loc.Raw("Crop Stats") + " - " + Loc.CropName(currentCropbed.Crop));
            else
                Loc.Set(cropTitleText, "Crop Stats");
        }
    }

    public void HideCropbedUI()
    {
        cropbedOpen = false;
        currentCropbed = null;
        currentCropData = null;
        if (!controlStationOpen && !externalOpen) SetGameplayInput(true);
        FadeOut(cropbedUI);
        OnCropbedPanelChanged?.Invoke(null);
    }

    private void RefreshOpenPanels()
    {
        if (controlStationOpen) UpdateControlStationUI();
        if (cropbedOpen) { UpdateCropbedUI(); OnCropbedPanelChanged?.Invoke(currentCropbed); }
    }

    // ---------------- Alerts ----------------

    /// <summary>Show a short message. Text should already be localized (Loc.T). A new alert replaces the current one.</summary>
    public void ShowAlertMessage(string msg)
    {
        if (alertMsg == null) { Debug.Log(msg); return; }
        CancelInvoke(nameof(FadeAlertAway));
        alertMsg.DOKill();
        alertMsg.gameObject.SetActive(true);
        Loc.ApplyFont(alertMsg);
        alertMsg.text = msg;
        alertMsg.alpha = Mathf.Min(alertMsg.alpha, 1f);
        alertMsg.DOFade(1.0f, 0.2f).OnComplete(() => Invoke(nameof(FadeAlertAway), alertDuration));
    }

    private void FadeAlertAway()
    {
        alertMsg.DOFade(0.0f, 0.3f).OnComplete(() => alertMsg.gameObject.SetActive(false));
    }
}
