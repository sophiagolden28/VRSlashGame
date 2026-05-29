using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks the visibility state of all concept layers and propagates changes to
/// any registered layer listeners via ConceptVizEvents.OnLayerToggled.
///
/// Objects, annotations, and field lines subscribe to OnLayerToggled and show /
/// hide themselves accordingly. ToggleLayerManager is the single source of truth
/// for current visibility state.
///
/// Layers are registered from ConceptConfig when the config is loaded, then
/// toggled by the player through ConceptVizUI buttons.
/// </summary>
public class ToggleLayerManager : MonoBehaviour
{
    // ── State ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<string, bool> _layerVisibility = new Dictionary<string, bool>();
    private readonly Dictionary<string, string> _layerNames     = new Dictionary<string, string>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()  => ConceptVizEvents.OnConfigLoaded += HandleConfigLoaded;
    private void OnDisable() => ConceptVizEvents.OnConfigLoaded -= HandleConfigLoaded;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Manually register a layer (useful when creating layers outside of JSON config).
    /// </summary>
    public void RegisterLayer(string layerId, string displayName, bool defaultVisible = true)
    {
        if (!_layerVisibility.ContainsKey(layerId))
        {
            _layerVisibility[layerId] = defaultVisible;
            _layerNames[layerId]      = displayName ?? layerId;
        }
    }

    /// <summary>Toggle a layer's visibility and broadcast the change.</summary>
    public void SetLayerVisible(string layerId, bool visible)
    {
        if (!_layerVisibility.ContainsKey(layerId))
        {
            Debug.LogWarning($"[ToggleLayerManager] Unknown layer '{layerId}'.");
            return;
        }

        if (_layerVisibility[layerId] == visible) return; // no change

        _layerVisibility[layerId] = visible;
        ConceptVizEvents.RaiseLayerToggled(layerId, visible);
    }

    /// <summary>Toggle a layer between visible / hidden.</summary>
    public void ToggleLayer(string layerId)
    {
        if (!_layerVisibility.TryGetValue(layerId, out bool current))
        {
            Debug.LogWarning($"[ToggleLayerManager] Unknown layer '{layerId}'.");
            return;
        }
        SetLayerVisible(layerId, !current);
    }

    /// <summary>Returns whether the layer is currently visible.</summary>
    public bool IsLayerVisible(string layerId)
        => _layerVisibility.TryGetValue(layerId, out bool v) && v;

    /// <summary>Returns the human-readable name for a layer ID, or the ID if unknown.</summary>
    public string GetLayerDisplayName(string layerId)
        => _layerNames.TryGetValue(layerId, out string name) ? name : layerId;

    /// <summary>Returns all registered layer IDs (snapshot; safe to iterate).</summary>
    public IEnumerable<string> AllLayerIds => _layerVisibility.Keys;

    /// <summary>Show all layers at once.</summary>
    public void ShowAllLayers()
    {
        foreach (string id in new List<string>(_layerVisibility.Keys))
            SetLayerVisible(id, true);
    }

    /// <summary>Hide all layers at once.</summary>
    public void HideAllLayers()
    {
        foreach (string id in new List<string>(_layerVisibility.Keys))
            SetLayerVisible(id, false);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleConfigLoaded(ConceptConfig config)
    {
        _layerVisibility.Clear();
        _layerNames.Clear();

        if (config.layers == null) return;

        foreach (var layer in config.layers)
        {
            if (string.IsNullOrEmpty(layer.id)) continue;
            _layerVisibility[layer.id] = layer.defaultVisible;
            _layerNames[layer.id]      = layer.displayName ?? layer.id;
        }
    }
}
