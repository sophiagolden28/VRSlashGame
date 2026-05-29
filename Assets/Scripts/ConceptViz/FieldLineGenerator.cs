using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates LineRenderer-based connections between spawned concept objects.
///
/// Each field line is a two-point LineRenderer (from → to) with an emissive
/// URP material, updated every frame so it tracks object position during
/// player manipulation (grab/rotate/scale).
///
/// Material rule: fieldLineMaterial is a serialized URP/Unlit reference assigned
/// in the Inspector. The actual line color is set via MaterialPropertyBlock so
/// no unique material instances are created per line.
///
/// Layer integration: field lines subscribe to OnLayerToggled and show/hide
/// their renderer accordingly.
///
/// Additive rendering (HDR color + Bloom) gives lines a glowing appearance
/// without requiring extra per-renderer materials.
/// </summary>
public class FieldLineGenerator : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Material")]
    [Tooltip("URP/Unlit material for field lines. HDR color + Bloom recommended. " +
             "Assign in Inspector — no Shader.Find() at runtime.")]
    [SerializeField] private Material fieldLineMaterial;

    [Header("Defaults")]
    [Tooltip("Default line width if not specified in config (metres).")]
    [SerializeField] private float defaultWidth = 0.004f;

    // ── State ─────────────────────────────────────────────────────────────────

    private struct LineEntry
    {
        public string         id;
        public string         layerId;
        public string         fromId;
        public string         toId;
        public LineRenderer   line;
    }

    private readonly List<LineEntry>                 _lines          = new List<LineEntry>();
    private readonly Dictionary<string, Transform>   _objectTransforms = new Dictionary<string, Transform>();
    private ConceptConfig                            _config;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        ConceptVizEvents.OnConfigLoaded    += HandleConfigLoaded;
        ConceptVizEvents.OnObjectSpawned   += HandleObjectSpawned;
        ConceptVizEvents.OnObjectDestroyed += HandleObjectDestroyed;
        ConceptVizEvents.OnLayerToggled    += HandleLayerToggled;
    }

    private void OnDisable()
    {
        ConceptVizEvents.OnConfigLoaded    -= HandleConfigLoaded;
        ConceptVizEvents.OnObjectSpawned   -= HandleObjectSpawned;
        ConceptVizEvents.OnObjectDestroyed -= HandleObjectDestroyed;
        ConceptVizEvents.OnLayerToggled    -= HandleLayerToggled;
    }

    private void LateUpdate()
    {
        // Update line endpoints every frame — objects may be moving during grab.
        foreach (var entry in _lines)
        {
            if (entry.line == null) continue;
            if (!entry.line.enabled) continue;

            if (_objectTransforms.TryGetValue(entry.fromId, out Transform from)
             && _objectTransforms.TryGetValue(entry.toId,   out Transform to))
            {
                entry.line.SetPosition(0, from.position);
                entry.line.SetPosition(1, to.position);
            }
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleConfigLoaded(ConceptConfig config)
    {
        _config = config;
        // Lines are built lazily as their endpoint objects spawn via HandleObjectSpawned.
    }

    private void HandleObjectSpawned(string id, GameObject go)
    {
        _objectTransforms[id] = go.transform;
        TryBuildLines(); // attempt to build any pending lines whose endpoints are now available
    }

    private void HandleObjectDestroyed(string id)
    {
        _objectTransforms.Remove(id);

        // Destroy lines that referenced this object.
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            if (_lines[i].fromId == id || _lines[i].toId == id)
            {
                if (_lines[i].line != null) Destroy(_lines[i].line.gameObject);
                _lines.RemoveAt(i);
            }
        }
    }

    private void HandleLayerToggled(string layerId, bool visible)
    {
        foreach (var entry in _lines)
        {
            if (entry.layerId == layerId && entry.line != null)
                entry.line.enabled = visible;
        }
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    private void TryBuildLines()
    {
        if (_config?.fieldLines == null) return;

        foreach (var cfg in _config.fieldLines)
        {
            // Skip if already built.
            if (_lines.Exists(e => e.id == cfg.id)) continue;

            // Only build once both endpoints are available.
            if (!_objectTransforms.ContainsKey(cfg.fromObjectId)) continue;
            if (!_objectTransforms.ContainsKey(cfg.toObjectId))   continue;

            BuildLine(cfg);
        }
    }

    private void BuildLine(FieldLineConfig cfg)
    {
        var go = new GameObject("FieldLine_" + cfg.id);
        go.transform.SetParent(transform, worldPositionStays: false);

        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;   // positions are set in world space every frame

        float w = cfg.width > 0f ? cfg.width : defaultWidth;
        lr.startWidth = w;
        lr.endWidth   = w;

        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;
        lr.generateLightingData = false;

        // Material — shared reference, color via MaterialPropertyBlock.
        if (fieldLineMaterial != null)
            lr.sharedMaterial = fieldLineMaterial;

        Color lineColor = cfg.color != null ? cfg.color.ToColor() : Color.cyan;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", lineColor);
        // Emissive tint for Bloom.
        mpb.SetColor("_EmissionColor", lineColor * 2f);
        lr.SetPropertyBlock(mpb);

        // Initial positions.
        if (_objectTransforms.TryGetValue(cfg.fromObjectId, out Transform from))
            lr.SetPosition(0, from.position);
        if (_objectTransforms.TryGetValue(cfg.toObjectId, out Transform to))
            lr.SetPosition(1, to.position);

        // Layer visibility.
        bool visible = true;
        var layerMgr = FindAnyObjectByType<ToggleLayerManager>();
        if (layerMgr != null && !string.IsNullOrEmpty(cfg.layerId))
            visible = layerMgr.IsLayerVisible(cfg.layerId);
        lr.enabled = visible;

        _lines.Add(new LineEntry
        {
            id      = cfg.id,
            layerId = cfg.layerId ?? "",
            fromId  = cfg.fromObjectId,
            toId    = cfg.toObjectId,
            line    = lr
        });
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Destroy all lines and reset state.</summary>
    public void ClearAll()
    {
        foreach (var entry in _lines)
            if (entry.line != null) Destroy(entry.line.gameObject);
        _lines.Clear();
        _objectTransforms.Clear();
    }
}
