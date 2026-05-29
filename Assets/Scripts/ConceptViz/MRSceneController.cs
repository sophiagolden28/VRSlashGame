using UnityEngine;

/// <summary>
/// Orchestrates the MR Concept Visualization scene through a three-state machine:
///
///   Loading   — passthrough initialises, JSON config loads, assets stream in.
///               Transition → Placing when config is loaded.
///
///   Placing   — SurfaceDetector is active; player aims at a real-world surface.
///               Pressing the right A button (or trigger) confirms the anchor location.
///               Transition → Exploring on anchor confirmation.
///
///   Exploring — Concept content is visible and grabbable. Player can:
///               • Grab/rotate/scale objects (VizObjectManipulator on each object).
///               • Toggle layers via ConceptVizUI buttons.
///               • Press Recenter to snap the UI back in front of them.
///               • Re-enter Placing mode via the "Move" button.
///
/// Button mapping (Placing mode):
///   Right A (Button.One) or Left X (Button.Three) = confirm anchor / start Exploring.
///
/// Dependencies:
///   All manager components are found at Start via FindAnyObjectByType.
///   Assign them via Inspector serialized fields for more robust scene wiring.
/// </summary>
public class MRSceneController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Managers — assign in Inspector or auto-found at Start")]
    [SerializeField] private PassthroughController  passthroughController;
    [SerializeField] private SurfaceDetector        surfaceDetector;
    [SerializeField] private SpatialAnchorPlacement anchorPlacement;
    [SerializeField] private ModelSpawner           modelSpawner;
    [SerializeField] private ToggleLayerManager     layerManager;
    [SerializeField] private AnnotationManager      annotationManager;
    [SerializeField] private FieldLineGenerator     fieldLineGenerator;
    [SerializeField] private ConceptVizUI           vizUI;

    [Header("Content Root")]
    [Tooltip("Empty GameObject that all spawned concept objects are parented under. " +
             "This root is then re-parented under the spatial anchor after placement.")]
    [SerializeField] private Transform contentRoot;

    [Header("Placing Instructions")]
    [Tooltip("Text shown in BillboardUI while the player is aiming at a surface.")]
    [SerializeField] private string placingInstructions = "Point at a flat surface.\nPress A or X to place.";

    // ── State ─────────────────────────────────────────────────────────────────

    private MRVizState _state = MRVizState.Loading;
    private bool       _configLoaded;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        ConceptVizEvents.OnConfigLoaded    += HandleConfigLoaded;
        ConceptVizEvents.OnRecenterRequested += HandleRecenterRequested;
        ConceptVizEvents.OnSurfaceModeToggled += HandleSurfaceModeToggled;
        ConceptVizEvents.OnAnchorPlaced    += HandleAnchorPlaced;
    }

    private void OnDisable()
    {
        ConceptVizEvents.OnConfigLoaded    -= HandleConfigLoaded;
        ConceptVizEvents.OnRecenterRequested -= HandleRecenterRequested;
        ConceptVizEvents.OnSurfaceModeToggled -= HandleSurfaceModeToggled;
        ConceptVizEvents.OnAnchorPlaced    -= HandleAnchorPlaced;
    }

    private void Start()
    {
        AutoFindManagers();
        EnterLoading();
    }

    private void Update()
    {
        HandleStateInput();
    }

    // ── State machine ─────────────────────────────────────────────────────────

    private void EnterLoading()
    {
        SetState(MRVizState.Loading);
        passthroughController?.EnablePassthrough();
        surfaceDetector?.SetActive(false);
    }

    private void EnterPlacing()
    {
        SetState(MRVizState.Placing);
        surfaceDetector?.SetActive(true);
        // Hide any previously spawned content while re-placing.
        if (contentRoot != null) contentRoot.gameObject.SetActive(false);
    }

    private void EnterExploring()
    {
        SetState(MRVizState.Exploring);
        surfaceDetector?.SetActive(false);

        if (contentRoot != null) contentRoot.gameObject.SetActive(true);

        // Spawn all objects under the content root if not already spawned.
        if (modelSpawner != null && modelSpawner.SpawnedObjects.Count == 0)
        {
            modelSpawner.SpawnAll(contentRoot);
        }

        // Attach VizObjectManipulator to every spawned object.
        AddManipulatorsToContent();
    }

    private void SetState(MRVizState state)
    {
        _state = state;
        ConceptVizEvents.RaiseStateChanged(state);
    }

    // ── Per-frame input ───────────────────────────────────────────────────────

    private void HandleStateInput()
    {
        bool confirmPressed =
            OVRInput.GetDown(OVRInput.Button.One)   // A — right Touch
         || OVRInput.GetDown(OVRInput.Button.Three) // X — left Touch
#if UNITY_EDITOR
         || Input.GetKeyDown(KeyCode.Return)
#endif
         ;

        switch (_state)
        {
            case MRVizState.Loading:
                // Loading transitions automatically when config fires OnConfigLoaded.
                break;

            case MRVizState.Placing:
                if (confirmPressed)
                {
                    // Use surface hit if available; otherwise fall back to a
                    // default position 0.8 m in front of and slightly below the camera.
                    Vector3    placementPos;
                    Quaternion placementRot;

                    if (surfaceDetector != null && surfaceDetector.HasHit)
                    {
                        placementPos = surfaceDetector.HitPoint;
                        placementRot = surfaceDetector.HitRotation;
                    }
                    else
                    {
                        Camera cam = Camera.main ?? FindAnyObjectByType<Camera>();
                        Vector3 forward = cam != null
                            ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized
                            : Vector3.forward;
                        Vector3 origin  = cam != null ? cam.transform.position : Vector3.zero;
                        placementPos    = origin + forward * 0.8f + Vector3.down * 0.3f;
                        placementRot    = Quaternion.LookRotation(forward);
                    }

                    anchorPlacement?.SetContentRoot(contentRoot);
                    anchorPlacement?.PlaceAnchor(placementPos, placementRot);
                    EnterExploring();
                }
                break;

            case MRVizState.Exploring:
                // All interaction is handled by VizObjectManipulator and ConceptVizUI.
                break;
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleConfigLoaded(ConceptConfig config)
    {
        _configLoaded = true;
        EnterPlacing();
    }

    private void HandleRecenterRequested()
    {
        var billboard = FindAnyObjectByType<BillboardUI>();
        billboard?.SnapToPlayer();
    }

    private void HandleSurfaceModeToggled(bool active)
    {
        // "Move" mode: re-enter Placing so the player can relocate the content.
        if (active && _state == MRVizState.Exploring)
            EnterPlacing();
        else if (!active && _state == MRVizState.Placing && _configLoaded)
            EnterExploring();
    }

    private void HandleAnchorPlaced(Vector3 position, Quaternion rotation)
    {
        // Position the content root at the anchor location.
        if (contentRoot != null)
            contentRoot.SetPositionAndRotation(position, rotation);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddManipulatorsToContent()
    {
        if (modelSpawner == null) return;

        foreach (var kv in modelSpawner.SpawnedObjects)
        {
            if (kv.Value == null) continue;

            // Add a VizObjectManipulator if not already present.
            if (kv.Value.GetComponent<VizObjectManipulator>() == null)
                kv.Value.AddComponent<VizObjectManipulator>();
        }
    }

    private void AutoFindManagers()
    {
        if (passthroughController == null) passthroughController = FindAnyObjectByType<PassthroughController>();
        if (surfaceDetector        == null) surfaceDetector        = FindAnyObjectByType<SurfaceDetector>();
        if (anchorPlacement        == null) anchorPlacement        = FindAnyObjectByType<SpatialAnchorPlacement>();
        if (modelSpawner           == null) modelSpawner           = FindAnyObjectByType<ModelSpawner>();
        if (layerManager           == null) layerManager           = FindAnyObjectByType<ToggleLayerManager>();
        if (annotationManager      == null) annotationManager      = FindAnyObjectByType<AnnotationManager>();
        if (fieldLineGenerator     == null) fieldLineGenerator     = FindAnyObjectByType<FieldLineGenerator>();
        if (vizUI                  == null) vizUI                  = FindAnyObjectByType<ConceptVizUI>();
    }
}
