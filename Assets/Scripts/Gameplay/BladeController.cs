using System.Collections;
using UnityEngine;

/// <summary>
/// Sword-shaped VR blade attached to a hand anchor (LeftHandAnchor / RightHandAnchor).
///
/// Awake() builds a sword visual from primitives (handle, guard, blade, tip),
/// configures the kinematic Rigidbody and Z-axis CapsuleCollider trigger, then
/// applies the blade colour via MaterialPropertyBlock so Bloom picks up the glow.
///
/// Sword dimensions (all local, controller +Z = blade forward):
///   Handle  — cylinder, 14 cm long, 18 mm dia, behind grip (z = –0.07)
///   Guard   — cube, 10 cm wide × 1.3 mm tall × 1.6 cm deep (z = 0.01)
///   Blade   — cube, 1 cm wide × 4 mm thick × 25 cm long (z = 0.175 centre)
///   Tip     — narrower cube, 5 mm × 3 mm × 9 cm (z = 0.325 centre, tapers)
///
/// The main blade Renderer is cached as _bladeRenderer — UpdateGlow() brightens
/// its _BaseColor toward an HDR value proportional to swing speed; with a
/// post-processing Bloom volume, this creates a visible gleam on fast swings.
///
/// On a successful slice AudioManager.Instance?.PlaySlashSound(fruitColor)
/// fires the pitch-mapped percussive tone.
///
/// Material rule: all sword materials are serialized [SerializeField] references.
/// No Shader.Find(), no Standard shader, no AssetDatabase at runtime.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class BladeController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Hand")]
    [Tooltip("True = left hand (cyan-blue, LTouch haptics). False = right (orange-red, RTouch).")]
    [SerializeField] private bool isLeftHand = true;

    [Header("Sword Materials")]
    [Tooltip("URP Lit material for the blade (silver, metallic 0.9). " +
             "Assign Assets/Materials/SwordBladeMat.mat — never loaded with Shader.Find().")]
    [SerializeField] private Material swordBladeMat;

    [Tooltip("URP Lit material for the handle (dark brown). " +
             "Assign Assets/Materials/SwordHandleMat.mat.")]
    [SerializeField] private Material swordHandleMat;

    [Tooltip("URP Lit material for the guard (pewter). " +
             "Assign Assets/Materials/SwordGuardMat.mat.")]
    [SerializeField] private Material swordGuardMat;

    [Header("Blade Colour (Glow)")]
    [Tooltip("Idle colour tint applied to the main blade via MaterialPropertyBlock.")]
    [SerializeField] private Color leftColor  = new Color(0.30f, 0.60f, 1.00f); // cyan-blue
    [SerializeField] private Color rightColor = new Color(1.00f, 0.40f, 0.10f); // orange-red

    [Header("Blade Dimensions")]
    [Tooltip("Total blade length (metres). CapsuleCollider is sized to match.")]
    [SerializeField] private float bladeLength = 0.30f;
    [Tooltip("Blade half-width (metres). Also used as CapsuleCollider radius.")]
    [SerializeField] private float bladeRadius = 0.02f;

    [Header("Slash Detection")]
    [Tooltip("Minimum tip speed (m/s) before a swing counts as a slash.")]
    [SerializeField] private float minSlashSpeed = 1.5f;

    [Header("Haptics — double-tap impact pattern")]
    [SerializeField] [Range(0f, 1f)] private float hapticAmplitude = 0.8f;

    // ── Public ────────────────────────────────────────────────────────────────

    /// <summary>
    /// World-space contact point of the last registered slice.
    /// Available to VFX systems for juice spray / slice-half spawn position.
    /// </summary>
    public Vector3 LastContactPoint { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private Renderer              _bladeRenderer; // main blade cube — target for glow
    private MaterialPropertyBlock _mpb;
    private Color                 _idleColor;
    private Color                 _slashColor;    // 4× HDR: triggers Bloom at full swing
    private bool                  _hapticsActive;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _mpb        = new MaterialPropertyBlock();
        _idleColor  = isLeftHand ? leftColor  : rightColor;
        _slashColor = _idleColor * 4f;

        ConfigurePhysics();
        BuildSwordVisual();

        // Sword starts hidden — revealed when the game begins (A / X / Space).
        SetSwordActive(false);
    }

    private void OnEnable()
    {
        GameEvents.OnGameStart += HandleGameStart;
        GameEvents.OnGameOver  += HandleGameOver;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStart -= HandleGameStart;
        GameEvents.OnGameOver  -= HandleGameOver;
    }

    private void Update() => UpdateGlow();

    // ── Game-state visibility ─────────────────────────────────────────────────

    private void HandleGameStart() => SetSwordActive(true);
    private void HandleGameOver()  => SetSwordActive(false);

    /// <summary>
    /// Shows or hides the sword visual and its slice-detection collider together.
    /// Called from Awake (hide) and in response to GameEvents (show/hide).
    /// </summary>
    private void SetSwordActive(bool active)
    {
        // Toggle the CapsuleCollider trigger so the blade can't slash fruit while hidden.
        var col = GetComponent<CapsuleCollider>();
        if (col != null) col.enabled = active;

        // Toggle every Renderer on this GO and all sword-part children.
        foreach (var r in GetComponentsInChildren<Renderer>(includeInactive: true))
            r.enabled = active;
    }

    // ── Physics ───────────────────────────────────────────────────────────────

    private void ConfigurePhysics()
    {
        // Kinematic: OVR tracking moves us, not physics.
        var rb         = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        // Capsule along Z (controller forward). Center at blade mid-point so the
        // trigger spans from grip to tip in front of the hand.
        var col       = GetComponent<CapsuleCollider>();
        col.isTrigger = true;
        col.direction = 2;                                   // Z axis
        col.radius    = bladeRadius;
        col.height    = bladeLength;
        col.center    = new Vector3(0f, 0f, bladeLength * 0.5f);
    }

    // ── Sword Visual ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a sword from primitive child GameObjects.
    /// All parts have their auto-generated physics colliders removed — only the
    /// parent CapsuleCollider (trigger) participates in slice detection.
    ///
    /// Local +Z is the blade forward direction (controller forward).
    /// </summary>
    private void BuildSwordVisual()
    {
        // ── Handle — Cylinder rotated 90° on X so it extends along Z ──────────
        var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(handle.GetComponent<CapsuleCollider>());
        handle.name = "Handle";
        handle.transform.SetParent(transform, worldPositionStays: false);
        handle.transform.localPosition    = new Vector3(0f, 0f, -0.07f);
        handle.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
        // Unity Cylinder: height = localScale.y × 2, radius = localScale.x / 2.
        // After 90° X-rotation the height axis aligns with world/local Z.
        handle.transform.localScale       = new Vector3(0.018f, 0.07f, 0.018f);
        ApplyMaterial(handle, swordHandleMat);

        // ── Guard — Cube, wide on X, spans left/right of grip ─────────────────
        var guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(guard.GetComponent<BoxCollider>());
        guard.name = "Guard";
        guard.transform.SetParent(transform, worldPositionStays: false);
        guard.transform.localPosition    = new Vector3(0f, 0f, 0.01f);
        guard.transform.localEulerAngles = Vector3.zero;
        guard.transform.localScale       = new Vector3(0.10f, 0.013f, 0.016f);
        ApplyMaterial(guard, swordGuardMat);

        // ── Main blade — Cube, thin cross-section, extends along Z ────────────
        // Length and position scale with bladeLength so the Inspector slider
        // resizes both the visual and the collision capsule together.
        float mainLen = bladeLength * 0.833f;
        float tipLen  = bladeLength * 0.300f;
        float mainZ   = bladeLength * 0.583f;
        float tipZ    = bladeLength * 1.083f;

        var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(blade.GetComponent<BoxCollider>());
        blade.name = "Blade";
        blade.transform.SetParent(transform, worldPositionStays: false);
        blade.transform.localPosition    = new Vector3(0f, 0f, mainZ);
        blade.transform.localEulerAngles = Vector3.zero;
        blade.transform.localScale       = new Vector3(0.010f, 0.004f, mainLen);
        ApplyMaterial(blade, swordBladeMat);

        // Cache for glow — UpdateGlow() writes to this Renderer via MPB.
        _bladeRenderer = blade.GetComponent<Renderer>();
        _mpb.SetColor("_BaseColor", _idleColor);
        _bladeRenderer.SetPropertyBlock(_mpb);

        // ── Blade tip — narrower Cube, tapers the sword point ─────────────────
        var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(tip.GetComponent<BoxCollider>());
        tip.name = "BladeTip";
        tip.transform.SetParent(transform, worldPositionStays: false);
        tip.transform.localPosition    = new Vector3(0f, 0f, tipZ);
        tip.transform.localEulerAngles = Vector3.zero;
        tip.transform.localScale       = new Vector3(0.005f, 0.003f, tipLen);
        ApplyMaterial(tip, swordBladeMat);
    }

    /// <summary>Assigns a material to a renderer. Safe if mat is null (uses default).</summary>
    private static void ApplyMaterial(GameObject go, Material mat)
    {
        if (mat == null) return;
        var r = go.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = mat;
    }

    // ── Glow ─────────────────────────────────────────────────────────────────

    private void UpdateGlow()
    {
        if (_bladeRenderer == null || InputManager.Instance == null) return;

        float speed = isLeftHand ? InputManager.Instance.LeftSpeed
                                 : InputManager.Instance.RightSpeed;

        // 0 m/s → idle colour. 2×minSlashSpeed → 4× HDR (Bloom picks this up if enabled).
        float t    = Mathf.Clamp01(speed / (minSlashSpeed * 2f));
        Color glow = Color.Lerp(_idleColor, _slashColor, t);

        _mpb.SetColor("_BaseColor", glow);
        _bladeRenderer.SetPropertyBlock(_mpb);
    }

    // ── Slash detection ───────────────────────────────────────────────────────

    // Use both Enter AND Stay: fast VR swings can pass through a fruit collider
    // in a single physics step and never generate an Enter event alone.
    private void OnTriggerEnter(Collider other) => TrySlice(other);
    private void OnTriggerStay(Collider other)  => TrySlice(other);

    private void TrySlice(Collider other)
    {
        if (InputManager.Instance == null) return;

        float speed = isLeftHand ? InputManager.Instance.LeftSpeed
                                 : InputManager.Instance.RightSpeed;
        if (speed < minSlashSpeed) return;

        // GetComponentInParent: future-proof in case visual children ever get
        // their own colliders; the Fruit component lives on the root GO.
        var fruit = other.GetComponentInParent<Fruit>();
        if (fruit == null) return;

        // ClosestPoint gives the real surface contact — stored for VFX systems.
        // Never use transform.position here (that's the grip, not the impact point).
        LastContactPoint = other.ClosestPoint(transform.position);

        // Capture colour before Slice() — Destroy() fires immediately and the
        // FruitColor property would be inaccessible on the next line.
        Color fruitColor = fruit.FruitColor;

        // Slice() returns false if fruit was already dead → no double-scoring.
        if (fruit.Slice())
        {
            GameManager.Instance?.RegisterSlice(basePoints: 10);
            SliceEffectSpawner.Instance?.Spawn(LastContactPoint, fruitColor);
            AudioManager.Instance?.PlaySlashSound(fruitColor);
            if (!_hapticsActive)
                StartCoroutine(SliceHapticsCoroutine());
        }
    }

    // ── Haptics ───────────────────────────────────────────────────────────────

    /// <summary>Quick double-tap pattern — feels like a clean physical impact.</summary>
    private IEnumerator SliceHapticsCoroutine()
    {
        _hapticsActive = true;
        var ctrl = isLeftHand ? OVRInput.Controller.LTouch
                              : OVRInput.Controller.RTouch;

        OVRInput.SetControllerVibration(0.8f, hapticAmplitude,        ctrl);
        yield return new WaitForSeconds(0.05f);
        OVRInput.SetControllerVibration(0f,   0f,                     ctrl);
        yield return new WaitForSeconds(0.03f);
        OVRInput.SetControllerVibration(0.5f, hapticAmplitude * 0.6f, ctrl);
        yield return new WaitForSeconds(0.04f);
        OVRInput.SetControllerVibration(0f,   0f,                     ctrl);

        _hapticsActive = false;
    }
}
