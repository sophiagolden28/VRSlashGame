using UnityEngine;

/// <summary>
/// Casts a ray from the active controller toward real-world surfaces and reports
/// the hit point and normal via ConceptVizEvents.OnSurfaceDetected.
///
/// On Quest 3 / 3S with Scene Understanding enabled, OVRSceneManager populates
/// a scene mesh that Unity's physics engine can raycast against. Without scene
/// understanding the raycast still works against any Physics collider in the scene.
///
/// The visual cursor is a flat disc placed at the hit point, oriented to lie flat
/// on the detected surface. Its material should be set in the Inspector
/// (URP/Unlit, semi-transparent recommended). A fallback disc is built at runtime
/// if no material is assigned — colour set via MaterialPropertyBlock.
///
/// Controller selection:
///   The right controller is used by default. If the right hand trigger is not held
///   and the left trigger is held, the left controller takes over automatically.
/// </summary>
public class SurfaceDetector : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Hand Anchors")]
    [Tooltip("Right hand / controller anchor from OVRCameraRig/TrackingSpace.")]
    [SerializeField] private Transform rightHandAnchor;
    [Tooltip("Left hand / controller anchor from OVRCameraRig/TrackingSpace.")]
    [SerializeField] private Transform leftHandAnchor;

    [Header("Raycast")]
    [Tooltip("Maximum ray length (metres).")]
    [SerializeField] private float maxDistance = 5f;
    [Tooltip("Layers to raycast against. Scene-mesh colliders should be on these layers.")]
    [SerializeField] private LayerMask hitLayers = ~0; // Everything by default

    [Header("Cursor Visual")]
    [Tooltip("Optional cursor prefab. Leave empty to build a cylinder disc at runtime.")]
    [SerializeField] private GameObject cursorPrefab;
    [Tooltip("URP/Unlit material for the cursor. Assign in Inspector.")]
    [SerializeField] private Material cursorMaterial;
    [Tooltip("Diameter of the built-in fallback cursor disc (metres).")]
    [SerializeField] private float cursorSize = 0.08f;

    // ── State ─────────────────────────────────────────────────────────────────

    private bool        _isActive;
    private GameObject  _cursor;
    private RaycastHit  _lastHit;

    // ── Public API ────────────────────────────────────────────────────────────

    public bool    IsActive      => _isActive;
    public bool    HasHit        => _lastHit.collider != null;
    public Vector3 HitPoint      => _lastHit.point;
    public Vector3 HitNormal     => _lastHit.normal;
    public Quaternion HitRotation
    {
        get
        {
            if (!HasHit) return Quaternion.identity;
            // Rotation that lays flat on the surface: Y-up aligns with the normal.
            return Quaternion.FromToRotation(Vector3.up, _lastHit.normal);
        }
    }

    /// <summary>Enable or disable surface scanning and cursor visibility.</summary>
    public void SetActive(bool active)
    {
        _isActive = active;
        if (_cursor != null) _cursor.SetActive(active);
        if (!active) _lastHit = default;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        BuildCursor();
        SetActive(false);
    }

    private void Update()
    {
        if (!_isActive) return;

        Transform activeHand = SelectActiveHand();
        if (activeHand == null) return;

        // Debug ray in editor — helpful when iterating in Play mode without a headset.
        Debug.DrawRay(activeHand.position, activeHand.forward * maxDistance, Color.cyan, 0f);

        if (Physics.Raycast(activeHand.position, activeHand.forward,
                             out RaycastHit hit, maxDistance, hitLayers))
        {
            _lastHit = hit;
            PlaceCursor(hit);
            ConceptVizEvents.RaiseSurfaceDetected(hit.point, hit.normal);
        }
        else
        {
            _lastHit = default;
            if (_cursor != null) _cursor.SetActive(false);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Picks right hand by default; falls back to left if the right trigger is
    /// not held but the left is.
    /// </summary>
    private Transform SelectActiveHand()
    {
        bool rightTrigger = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > 0.1f
                          || OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger)  > 0.1f;
        bool leftTrigger  = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger)   > 0.1f
                          || OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger)    > 0.1f;

        if (leftTrigger && !rightTrigger && leftHandAnchor != null)
            return leftHandAnchor;
        return rightHandAnchor ?? leftHandAnchor;
    }

    private void PlaceCursor(RaycastHit hit)
    {
        if (_cursor == null) return;
        _cursor.SetActive(true);

        // Tiny lift above surface to prevent z-fighting.
        _cursor.transform.position = hit.point + hit.normal * 0.002f;

        // Align the disc flat on the surface:
        // LookRotation(normal) points Z along the normal; rotate 90° around X
        // so the flat face (originally XZ plane) lies on the surface.
        _cursor.transform.rotation = Quaternion.LookRotation(hit.normal)
                                   * Quaternion.Euler(90f, 0f, 0f);
    }

    private void BuildCursor()
    {
        if (cursorPrefab != null)
        {
            _cursor = Instantiate(cursorPrefab);
        }
        else
        {
            // Built-in fallback: a very flat cylinder (disc).
            _cursor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_cursor.GetComponent<Collider>());

            // Diameter = cursorSize, height = 4 mm (visually flat).
            _cursor.transform.localScale = new Vector3(cursorSize, 0.002f, cursorSize);

            var rend = _cursor.GetComponent<Renderer>();
            if (cursorMaterial != null)
            {
                rend.sharedMaterial = cursorMaterial;
            }
            else
            {
                // Colour via MaterialPropertyBlock — no unique material instance.
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", new Color(0.2f, 0.85f, 1.0f, 0.75f));
                rend.SetPropertyBlock(mpb);
            }
        }

        _cursor.name = "SurfaceCursor";
    }
}
