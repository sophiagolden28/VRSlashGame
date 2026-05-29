using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Manages world-space text labels anchored to spawned concept objects.
///
/// Labels are TextMeshPro GameObjects parented to their target object so they
/// follow any manipulation (grab/rotate/scale). Each annotation belongs to a
/// layer and shows or hides with that layer.
///
/// Billboard behaviour: labels face the camera via LookRotation in LateUpdate.
/// Text scale is compensated for the parent's world scale so labels stay a
/// consistent visual size regardless of model scale.
///
/// Requirements:
///   TextMeshPro package must be installed (com.unity.textmeshpro).
///   annotationFont must be assigned in the Inspector — world-space TMP objects
///   created while their parent is inactive will not receive TMP's default font,
///   so explicit font assignment is mandatory (same issue seen in GameUI).
/// </summary>
public class AnnotationManager : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("CenterEyeAnchor — labels always face this transform.")]
    [SerializeField] private Transform centerEyeAnchor;

    [Header("Label Appearance")]
    [Tooltip("TMP font asset. MUST be assigned — do not leave null.")]
    [SerializeField] private TMP_FontAsset annotationFont;
    [Tooltip("Font size in TMP units (1 unit = 1 m in world space at scale 1).")]
    [SerializeField] private float fontSize   = 0.18f;
    [Tooltip("Background panel scale relative to text bounds.")]
    [SerializeField] private Vector2 padding  = new Vector2(0.04f, 0.02f);
    [Tooltip("Text color.")]
    [SerializeField] private Color textColor  = Color.white;
    [Tooltip("Background panel color.")]
    [SerializeField] private Color bgColor    = new Color(0f, 0f, 0f, 0.65f);

    [Header("Materials")]
    [Tooltip("URP/Unlit material for the annotation background panel. Assign in Inspector.")]
    [SerializeField] private Material panelMaterial;

    // ── State ─────────────────────────────────────────────────────────────────

    private struct AnnotationEntry
    {
        public string      id;
        public string      layerId;
        public GameObject  root;       // Parented to the target object
        public GameObject  bgPanel;
        public TMP_Text    label;
    }

    private readonly List<AnnotationEntry>            _annotations    = new List<AnnotationEntry>();
    private readonly Dictionary<string, GameObject>   _spawnedObjects = new Dictionary<string, GameObject>();
    private ConceptConfig                              _config;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (centerEyeAnchor == null)
        {
            var go = GameObject.Find("CenterEyeAnchor");
            if (go != null) centerEyeAnchor = go.transform;
        }
    }

    private void OnEnable()
    {
        ConceptVizEvents.OnConfigLoaded  += HandleConfigLoaded;
        ConceptVizEvents.OnObjectSpawned += HandleObjectSpawned;
        ConceptVizEvents.OnObjectDestroyed += HandleObjectDestroyed;
        ConceptVizEvents.OnLayerToggled  += HandleLayerToggled;
    }

    private void OnDisable()
    {
        ConceptVizEvents.OnConfigLoaded  -= HandleConfigLoaded;
        ConceptVizEvents.OnObjectSpawned -= HandleObjectSpawned;
        ConceptVizEvents.OnObjectDestroyed -= HandleObjectDestroyed;
        ConceptVizEvents.OnLayerToggled  -= HandleLayerToggled;
    }

    private void LateUpdate()
    {
        if (centerEyeAnchor == null) return;

        // Billboard each annotation to face the camera (project to horizontal plane
        // so labels stay upright regardless of head tilt).
        Vector3 forward = Vector3.ProjectOnPlane(
            centerEyeAnchor.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        foreach (var entry in _annotations)
        {
            if (entry.root == null || !entry.root.activeSelf) continue;
            entry.root.transform.rotation = Quaternion.LookRotation(forward);
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleConfigLoaded(ConceptConfig config)
    {
        _config = config;
        // Annotations are built lazily in HandleObjectSpawned once their target
        // objects have been instantiated by ModelSpawner.
    }

    private void HandleObjectSpawned(string id, GameObject go)
    {
        _spawnedObjects[id] = go;
        TryBuildAnnotationsForObject(id, go);
    }

    private void HandleObjectDestroyed(string id)
    {
        _spawnedObjects.Remove(id);
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            if (_annotations[i].id.StartsWith(id + "_"))
            {
                if (_annotations[i].root != null) Destroy(_annotations[i].root);
                _annotations.RemoveAt(i);
            }
        }
    }

    private void HandleLayerToggled(string layerId, bool visible)
    {
        foreach (var entry in _annotations)
        {
            if (entry.layerId == layerId && entry.root != null)
                entry.root.SetActive(visible);
        }
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    private void TryBuildAnnotationsForObject(string objectId, GameObject go)
    {
        if (_config?.annotations == null) return;

        foreach (var ann in _config.annotations)
        {
            if (ann.targetObjectId != objectId) continue;
            BuildAnnotation(ann, go);
        }
    }

    private void BuildAnnotation(AnnotationConfig ann, GameObject target)
    {
        // Root parented to target so it follows manipulation.
        var root = new GameObject("Annotation_" + ann.id);
        root.transform.SetParent(target.transform, worldPositionStays: false);
        root.transform.localPosition = ann.offset != null
            ? ann.offset.ToVector3()
            : Vector3.up * 0.15f;

        // Background quad.
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(bg.GetComponent<Collider>());
        bg.name = "AnnotationBG";
        bg.transform.SetParent(root.transform, worldPositionStays: false);
        bg.transform.localPosition = Vector3.zero;
        bg.transform.localScale    = new Vector3(0.20f, 0.08f, 1f); // adjusted later

        if (panelMaterial != null)
        {
            bg.GetComponent<Renderer>().sharedMaterial = panelMaterial;
        }
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", bgColor);
        bg.GetComponent<Renderer>().SetPropertyBlock(mpb);

        // TMP label as a child of root (not canvas — world-space TMP).
        var labelGO = new GameObject("AnnotationText");
        labelGO.transform.SetParent(root.transform, worldPositionStays: false);
        labelGO.transform.localPosition = new Vector3(0f, 0f, -0.001f); // tiny Z in front of BG

        var tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.text          = ann.text;
        tmp.fontSize      = fontSize;
        tmp.color         = textColor;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;

        // Font MUST be assigned explicitly — TMP doesn't init its default font on
        // GameObjects created while parents are inactive.
        if (annotationFont != null)
            tmp.font = annotationFont;

        // Layer visibility.
        bool visible = true;
        var manager = FindAnyObjectByType<ToggleLayerManager>();
        if (manager != null && !string.IsNullOrEmpty(ann.layerId))
            visible = manager.IsLayerVisible(ann.layerId);
        root.SetActive(visible);

        _annotations.Add(new AnnotationEntry
        {
            id      = ann.id,
            layerId = ann.layerId ?? "",
            root    = root,
            bgPanel = bg,
            label   = tmp
        });
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Destroy all annotations and reset state.</summary>
    public void ClearAll()
    {
        foreach (var entry in _annotations)
            if (entry.root != null) Destroy(entry.root);
        _annotations.Clear();
        _spawnedObjects.Clear();
    }
}
