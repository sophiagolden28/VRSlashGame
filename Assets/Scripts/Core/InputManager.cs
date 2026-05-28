using UnityEngine;

/// <summary>
/// Tracks VR controller velocity every frame so slash-detection systems can
/// read it without doing their own physics.
///
/// Hand anchor references (LeftHandAnchor / RightHandAnchor) come from
/// OVRCameraRig/TrackingSpace. Assign them in the Inspector via MCP.
///
/// Velocity is computed at the blade tip, not the grip. tipOffset pushes the
/// sample point forward along the controller's local Z axis.
/// </summary>
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Hand Anchors — assign from OVRCameraRig/TrackingSpace")]
    [SerializeField] private Transform leftHandAnchor;
    [SerializeField] private Transform rightHandAnchor;

    [Header("Tip Offset")]
    [Tooltip("Metres along controller local-Z from grip to blade tip.")]
    [SerializeField] private float tipOffset = 0.15f;

    // ── Public read-only data ─────────────────────────────────────────────────

    /// <summary>World-space velocity of the left blade tip (m/s).</summary>
    public Vector3 LeftVelocity   { get; private set; }

    /// <summary>World-space velocity of the right blade tip (m/s).</summary>
    public Vector3 RightVelocity  { get; private set; }

    /// <summary>Speed (magnitude) of the left blade tip (m/s).</summary>
    public float   LeftSpeed      { get; private set; }

    /// <summary>Speed (magnitude) of the right blade tip (m/s).</summary>
    public float   RightSpeed     { get; private set; }

    /// <summary>Current world-space position of the left blade tip.</summary>
    public Vector3 LeftTipPosition  { get; private set; }

    /// <summary>Current world-space position of the right blade tip.</summary>
    public Vector3 RightTipPosition { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private Vector3 _prevLeftTip;
    private Vector3 _prevRightTip;
    private bool    _initialized;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        if (leftHandAnchor == null || rightHandAnchor == null) return;

        // Tip = grip position + tipOffset along the controller's local forward (Z)
        Vector3 leftTip  = leftHandAnchor.position  + leftHandAnchor.forward  * tipOffset;
        Vector3 rightTip = rightHandAnchor.position + rightHandAnchor.forward * tipOffset;

        if (_initialized)
        {
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                LeftVelocity  = (leftTip  - _prevLeftTip)  / dt;
                RightVelocity = (rightTip - _prevRightTip) / dt;
                LeftSpeed     = LeftVelocity.magnitude;
                RightSpeed    = RightVelocity.magnitude;
            }
        }

        LeftTipPosition  = leftTip;
        RightTipPosition = rightTip;

        _prevLeftTip  = leftTip;
        _prevRightTip = rightTip;
        _initialized  = true;
    }

    // ── Convenience ───────────────────────────────────────────────────────────

    /// <summary>Returns true if either hand is moving faster than speedThreshold m/s.</summary>
    public bool IsSlashing(float speedThreshold = 1.5f)
        => LeftSpeed > speedThreshold || RightSpeed > speedThreshold;

    /// <summary>Returns the faster of the two hands' velocities.</summary>
    public Vector3 DominantVelocity
        => LeftSpeed >= RightSpeed ? LeftVelocity : RightVelocity;
}
