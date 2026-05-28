using System.Collections;
using UnityEngine;

/// <summary>
/// Glowing VR blade attached to a hand anchor (LeftHandAnchor / RightHandAnchor).
///
/// Awake() builds a capsule visual pointing along local Z, configures the
/// kinematic Rigidbody and Z-axis CapsuleCollider trigger, then applies the
/// blade colour via MaterialPropertyBlock (no unique material instances).
///
/// Update() reads InputManager.LeftSpeed / RightSpeed each frame and brightens
/// the blade colour toward an HDR value — if the scene has a post-processing
/// Volume with Bloom enabled, this creates a visible glow at full swing speed.
///
/// OnTriggerEnter + OnTriggerStay both call TrySlice() so fast VR swings that
/// pass through a fruit collider in a single physics frame still register.
///
/// Material rule: bladeMaterial is a serialized reference assigned via MCP.
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

    [Header("Material")]
    [Tooltip("URP/Unlit material at Assets/Materials/BladeMaterial.mat. Assigned via MCP — " +
             "never loaded with Shader.Find() at runtime.")]
    [SerializeField] private Material bladeMaterial;

    [Header("Blade Colour")]
    [SerializeField] private Color leftColor  = new Color(0.30f, 0.60f, 1.00f); // cyan-blue
    [SerializeField] private Color rightColor = new Color(1.00f, 0.40f, 0.10f); // orange-red

    [Header("Blade Dimensions")]
    [Tooltip("Total blade length in metres. Collider and visual are sized to match.")]
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
    /// World-space contact point of the last registered slice, computed via
    /// Collider.ClosestPoint (not transform.position).
    /// Available to future VFX systems for juice spray / slice-half spawn position.
    /// </summary>
    public Vector3 LastContactPoint { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private Renderer              _bladeRenderer;
    private MaterialPropertyBlock _mpb;
    private Color                 _idleColor;
    private Color                 _slashColor;   // 4× HDR: triggers Bloom at full swing
    private bool                  _hapticsActive;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _mpb        = new MaterialPropertyBlock();
        _idleColor  = isLeftHand ? leftColor  : rightColor;
        _slashColor = _idleColor * 4f;

        ConfigurePhysics();
        BuildVisual();
    }

    private void Update() => UpdateGlow();

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
        col.direction = 2;                                    // Z axis
        col.radius    = bladeRadius;
        col.height    = bladeLength;
        col.center    = new Vector3(0f, 0f, bladeLength * 0.5f);
    }

    // ── Visual ────────────────────────────────────────────────────────────────

    private void BuildVisual()
    {
        // CreatePrimitive(Capsule) attaches its own CapsuleCollider — destroy it
        // immediately so only the parent trigger collider participates in queries.
        var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(visual.GetComponent<CapsuleCollider>());

        visual.name = "BladeVisual";
        visual.transform.SetParent(transform, worldPositionStays: false);

        // Default capsule stands along Y (2 units tall, 1 unit wide).
        // Rotate 90° around X → capsule now points along Z (controller forward).
        // Scale: X/Z = diameter; Y = half-length (Unity capsule height = localScale.y × 2).
        visual.transform.localPosition    = new Vector3(0f, 0f, bladeLength * 0.5f);
        visual.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
        visual.transform.localScale       = new Vector3(
            bladeRadius * 2f,
            bladeLength * 0.5f,
            bladeRadius * 2f);

        _bladeRenderer = visual.GetComponent<Renderer>();

        // Serialized material reference — URP/Unlit, no Shader.Find().
        if (bladeMaterial != null)
            _bladeRenderer.sharedMaterial = bladeMaterial;

        // Colour via MaterialPropertyBlock: doesn't create a unique material instance
        // and therefore doesn't break GPU instancing or batching.
        _mpb.SetColor("_BaseColor", _idleColor);
        _bladeRenderer.SetPropertyBlock(_mpb);
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
