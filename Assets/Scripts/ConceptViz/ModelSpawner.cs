using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Instantiates concept objects from ConceptConfig and registers them with
/// ToggleLayerManager. Fires ConceptVizEvents.OnObjectSpawned for each object.
///
/// Asset loading strategy:
///   Resources.Load is used because it is available in all Unity 6 builds
///   including Quest IL2CPP. Addressables can replace it by swapping LoadModel()
///   with an async Addressables.LoadAssetAsync call.
///
/// Resources folder layout (create manually):
///   Assets/Resources/ConceptViz/Sphere.prefab   ← or any model prefab
///   Assets/Resources/ConceptViz/Cube.prefab
///
/// If a modelPath is not found in Resources the spawner falls back to a coloured
/// primitive so the scene is always usable during development.
///
/// Anchor parenting:
///   SpawnAll(parent) parents every spawned object under the given transform.
///   Call this from MRSceneController after anchor placement — all content then
///   tracks with the spatial anchor in world space.
/// </summary>
public class ModelSpawner : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Fallback Colors")]
    [Tooltip("Colors cycled for primitive fallback objects when the model is not found.")]
    [SerializeField] private Color[] fallbackColors =
    {
        new Color(0.30f, 0.60f, 1.00f),  // blue
        new Color(1.00f, 0.45f, 0.10f),  // orange
        new Color(0.20f, 0.85f, 0.40f),  // green
        new Color(0.90f, 0.20f, 0.90f),  // magenta
        new Color(1.00f, 0.85f, 0.10f),  // yellow
    };

    // ── State ─────────────────────────────────────────────────────────────────

    private ConceptConfig                     _config;
    private readonly Dictionary<string, GameObject> _spawnedObjects = new Dictionary<string, GameObject>();
    private Transform                         _contentParent;
    private int                               _colorIndex;

    // ── Public API ────────────────────────────────────────────────────────────

    public IReadOnlyDictionary<string, GameObject> SpawnedObjects => _spawnedObjects;

    /// <summary>
    /// Spawn all objects from the loaded config under an optional parent transform.
    /// Call this from MRSceneController after the anchor is placed.
    /// </summary>
    public void SpawnAll(Transform parent = null)
    {
        if (_config == null)
        {
            Debug.LogWarning("[ModelSpawner] No config loaded yet. Subscribe to OnConfigLoaded first.");
            return;
        }

        _contentParent = parent;
        _colorIndex    = 0;

        if (_config.objects == null) return;

        foreach (var cfg in _config.objects)
            SpawnObject(cfg);
    }

    /// <summary>Destroy all spawned objects and reset state.</summary>
    public void DespawnAll()
    {
        foreach (var kv in _spawnedObjects)
        {
            if (kv.Value != null)
            {
                ConceptVizEvents.RaiseObjectDestroyed(kv.Key);
                Destroy(kv.Value);
            }
        }
        _spawnedObjects.Clear();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        ConceptVizEvents.OnConfigLoaded += HandleConfigLoaded;
        ConceptVizEvents.OnLayerToggled += HandleLayerToggled;
    }

    private void OnDisable()
    {
        ConceptVizEvents.OnConfigLoaded -= HandleConfigLoaded;
        ConceptVizEvents.OnLayerToggled -= HandleLayerToggled;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleConfigLoaded(ConceptConfig config)
    {
        _config = config;
        // Objects are spawned explicitly via SpawnAll() after anchor placement,
        // not automatically here, so the player can position the content first.
    }

    private void HandleLayerToggled(string layerId, bool visible)
    {
        if (_config?.objects == null) return;

        foreach (var cfg in _config.objects)
        {
            if (cfg.layerIds == null) continue;
            foreach (string id in cfg.layerIds)
            {
                if (id != layerId) continue;
                if (_spawnedObjects.TryGetValue(cfg.id, out GameObject go) && go != null)
                    go.SetActive(visible);
            }
        }
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    private void SpawnObject(SpawnedObjectConfig cfg)
    {
        if (string.IsNullOrEmpty(cfg.id))
        {
            Debug.LogWarning("[ModelSpawner] Skipping object with empty id.");
            return;
        }

        // Load the prefab from Resources or fall back to a primitive.
        GameObject source = null;
        if (!string.IsNullOrEmpty(cfg.modelPath))
            source = Resources.Load<GameObject>(cfg.modelPath);

        GameObject go = source != null
            ? Instantiate(source)
            : BuildFallbackPrimitive(cfg.id);

        go.name = "Concept_" + cfg.id;

        // Apply transform from config.
        go.transform.localPosition = cfg.position?.ToVector3() ?? Vector3.zero;
        go.transform.localEulerAngles = cfg.rotation?.ToVector3() ?? Vector3.zero;
        go.transform.localScale    = cfg.scale?.ToVector3() ?? Vector3.one;

        // Parent under content root (spatial anchor transforms here).
        if (_contentParent != null)
            go.transform.SetParent(_contentParent, worldPositionStays: false);

        // Apply default layer visibility.
        bool visible = ShouldBeVisible(cfg);
        go.SetActive(visible);

        _spawnedObjects[cfg.id] = go;
        ConceptVizEvents.RaiseObjectSpawned(cfg.id, go);
    }

    private bool ShouldBeVisible(SpawnedObjectConfig cfg)
    {
        if (cfg.layerIds == null || cfg.layerIds.Length == 0) return true;

        var layerMgr = FindAnyObjectByType<ToggleLayerManager>();
        if (layerMgr == null) return true;

        // Object is visible if ANY of its layers is visible.
        foreach (string layerId in cfg.layerIds)
            if (layerMgr.IsLayerVisible(layerId)) return true;

        return false;
    }

    // ── Fallback primitive ────────────────────────────────────────────────────

    private GameObject BuildFallbackPrimitive(string id)
    {
        Debug.Log($"[ModelSpawner] Model not found for '{id}', using fallback sphere.");

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>()); // remove physics collider

        // Colour cycle via MaterialPropertyBlock — no unique material instances.
        Color c = fallbackColors != null && fallbackColors.Length > 0
            ? fallbackColors[_colorIndex++ % fallbackColors.Length]
            : Color.white;

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", c);
        go.GetComponent<Renderer>().SetPropertyBlock(mpb);

        return go;
    }
}
