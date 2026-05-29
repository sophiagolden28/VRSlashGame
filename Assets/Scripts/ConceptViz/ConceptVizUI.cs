using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Floating world-space UI panel for the MR Concept Visualization scene.
///
/// Panels:
///   • Loading    — shown while JSON config and assets load.
///   • Main menu  — concept title, layer toggle buttons (auto-generated from config),
///                  a Recenter button, and a "Place / Move" surface mode button.
///
/// Layer buttons are generated dynamically when the config is loaded.
/// Each button label shows the layer's displayName and its current toggle state.
///
/// Canvas rules (same as GameUI):
///   • renderMode = WorldSpace — never parented to the camera.
///   • BillboardUI component on the same GameObject handles facing + distance.
///   • All text uses TextMeshProUGUI; no legacy UI.Text.
///   • TMP font must be assigned explicitly in the Inspector — same null-font
///     issue as GameUI if created while parent is inactive.
///
/// Canvas layout (set in Inspector after scene setup):
///   Width 500 × Height 420 canvas units, scale 0.002 (= 1.0 m × 0.84 m world).
/// </summary>
[RequireComponent(typeof(Canvas))]
public class ConceptVizUI : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Panels")]
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private GameObject mainPanel;

    [Header("Loading Panel")]
    [SerializeField] private TMP_Text loadingText;

    [Header("Main Panel")]
    [SerializeField] private TMP_Text  conceptTitleText;
    [SerializeField] private Transform layerButtonContainer;  // parent for dynamic layer buttons
    [SerializeField] private Button    recenterButton;
    [SerializeField] private Button    surfaceModeButton;
    [SerializeField] private TMP_Text  surfaceModeButtonLabel;

    [Header("Layer Button Prefab")]
    [Tooltip("Prefab with a Button + TMP_Text child. Instantiated per layer.")]
    [SerializeField] private GameObject layerButtonPrefab;

    [Header("Fonts & Style")]
    [Tooltip("TMP font for all generated text. MUST be assigned.")]
    [SerializeField] private TMP_FontAsset uiFont;
    [SerializeField] private Color activeLayerColor   = new Color(0.25f, 0.75f, 1.00f);
    [SerializeField] private Color inactiveLayerColor = new Color(0.50f, 0.50f, 0.50f);

    // ── State ─────────────────────────────────────────────────────────────────

    private ToggleLayerManager               _layerManager;
    private bool                             _surfaceModeActive;
    private readonly List<(string id, Button btn, TMP_Text lbl)> _layerButtons
        = new List<(string, Button, TMP_Text)>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        ConceptVizEvents.OnConfigLoaded  += HandleConfigLoaded;
        ConceptVizEvents.OnStateChanged  += HandleStateChanged;
        ConceptVizEvents.OnLayerToggled  += HandleLayerToggled;
    }

    private void OnDisable()
    {
        ConceptVizEvents.OnConfigLoaded  -= HandleConfigLoaded;
        ConceptVizEvents.OnStateChanged  -= HandleStateChanged;
        ConceptVizEvents.OnLayerToggled  -= HandleLayerToggled;
    }

    private void Start()
    {
        _layerManager = FindAnyObjectByType<ToggleLayerManager>();
        ShowPanel(loadingPanel);
        if (loadingText != null) loadingText.text = "Loading…";

        // Wire static buttons.
        recenterButton?.onClick.AddListener(OnRecenterClicked);
        surfaceModeButton?.onClick.AddListener(OnSurfaceModeClicked);
        RefreshSurfaceModeLabel();

        Canvas.ForceUpdateCanvases();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleConfigLoaded(ConceptConfig config)
    {
        if (conceptTitleText != null)
            conceptTitleText.text = config.conceptTitle ?? "Concept";

        BuildLayerButtons(config);
        ShowPanel(mainPanel);
        Canvas.ForceUpdateCanvases();
    }

    private void HandleStateChanged(MRVizState state)
    {
        switch (state)
        {
            case MRVizState.Loading:
                ShowPanel(loadingPanel);
                break;
            case MRVizState.Placing:
            case MRVizState.Exploring:
                ShowPanel(mainPanel);
                break;
        }
        Canvas.ForceUpdateCanvases();
    }

    private void HandleLayerToggled(string layerId, bool visible)
    {
        foreach (var (id, btn, lbl) in _layerButtons)
        {
            if (id != layerId) continue;
            UpdateLayerButtonStyle(btn, lbl, visible);
        }
        Canvas.ForceUpdateCanvases();
    }

    // ── Button callbacks ──────────────────────────────────────────────────────

    private void OnRecenterClicked() => ConceptVizEvents.RaiseRecenterRequested();

    private void OnSurfaceModeClicked()
    {
        _surfaceModeActive = !_surfaceModeActive;
        ConceptVizEvents.RaiseSurfaceModeToggled(_surfaceModeActive);
        RefreshSurfaceModeLabel();
        Canvas.ForceUpdateCanvases();
    }

    private void OnLayerToggleClicked(string layerId)
        => _layerManager?.ToggleLayer(layerId);

    // ── Layer buttons ─────────────────────────────────────────────────────────

    private void BuildLayerButtons(ConceptConfig config)
    {
        if (layerButtonContainer == null || config.layers == null) return;

        // Destroy previous buttons.
        foreach (Transform child in layerButtonContainer)
            Destroy(child.gameObject);
        _layerButtons.Clear();

        foreach (var layer in config.layers)
        {
            if (string.IsNullOrEmpty(layer.id)) continue;

            GameObject btnGO;
            if (layerButtonPrefab != null)
            {
                btnGO = Instantiate(layerButtonPrefab, layerButtonContainer);
            }
            else
            {
                btnGO = BuildFallbackLayerButton(layer.displayName ?? layer.id);
                btnGO.transform.SetParent(layerButtonContainer, worldPositionStays: false);
            }

            var btn = btnGO.GetComponent<Button>();
            var lbl = btnGO.GetComponentInChildren<TMP_Text>();

            if (lbl != null)
            {
                lbl.text = layer.displayName ?? layer.id;
                if (uiFont != null) lbl.font = uiFont;
            }

            if (btn != null)
            {
                string capturedId = layer.id; // capture for closure
                btn.onClick.AddListener(() => OnLayerToggleClicked(capturedId));
            }

            bool visible = _layerManager?.IsLayerVisible(layer.id) ?? layer.defaultVisible;
            UpdateLayerButtonStyle(btn, lbl, visible);

            _layerButtons.Add((layer.id, btn, lbl));
        }
    }

    private GameObject BuildFallbackLayerButton(string label)
    {
        // Minimal programmatic button when no prefab is assigned.
        var go = new GameObject("LayerBtn_" + label);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(200f, 50f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

        var btn = go.AddComponent<Button>();
        var cbk = btn.colors;
        cbk.normalColor      = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        cbk.highlightedColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
        cbk.pressedColor     = new Color(0.10f, 0.10f, 0.10f, 1.0f);
        btn.colors           = cbk;

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, worldPositionStays: false);

        var labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 4f);
        labelRect.offsetMax = new Vector2(-4f, -4f);

        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 18f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        if (uiFont != null) tmp.font = uiFont;

        return go;
    }

    private void UpdateLayerButtonStyle(Button btn, TMP_Text lbl, bool visible)
    {
        if (lbl == null) return;
        lbl.color = visible ? activeLayerColor : inactiveLayerColor;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ShowPanel(GameObject panel)
    {
        if (loadingPanel != null) loadingPanel.SetActive(panel == loadingPanel);
        if (mainPanel    != null) mainPanel.SetActive(panel == mainPanel);
    }

    private void RefreshSurfaceModeLabel()
    {
        if (surfaceModeButtonLabel != null)
            surfaceModeButtonLabel.text = _surfaceModeActive ? "Move: ON" : "Move: OFF";
    }
}
