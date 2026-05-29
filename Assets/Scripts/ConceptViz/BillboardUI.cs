using UnityEngine;

/// <summary>
/// Keeps a world-space canvas panel always facing the player's camera.
///
/// VR billboard rules (same reasoning as GameUI):
///   • Never parent the panel to the camera — that causes VR sickness.
///   • Forward is projected onto the horizontal plane so the panel stays upright
///     regardless of head tilt.
///   • SmoothDamp for position and Slerp for rotation make repositioning feel
///     natural in MR rather than snapping around.
///
/// If fixedDistance > 0 the panel is repositioned in front of the player at
/// that distance on every frame. Set to 0 to billboard in-place (panel stays
/// where it was placed but rotates to face you).
///
/// Attach this to the root of any world-space canvas GameObject.
/// </summary>
public class BillboardUI : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("CenterEyeAnchor from OVRCameraRig/TrackingSpace. Auto-found if empty.")]
    [SerializeField] private Transform centerEyeAnchor;

    [Header("Position")]
    [Tooltip("Distance in front of the player (metres). 0 = billboard-only, no position lock.")]
    [SerializeField] private float fixedDistance  = 1.2f;
    [Tooltip("Vertical offset from eye level (metres). Negative = slightly below eye.")]
    [SerializeField] private float heightOffset   = -0.10f;

    [Header("Smoothing")]
    [Tooltip("Position smooth-damp time (seconds). 0 = instant snap.")]
    [SerializeField] [Range(0f, 1f)] private float positionSmoothing = 0.20f;
    [Tooltip("Rotation lerp factor per frame. Higher = snappier.")]
    [SerializeField] [Range(0f, 1f)] private float rotationSpeed     = 0.18f;

    // ── Private state ─────────────────────────────────────────────────────────

    private Vector3    _posVelocity;
    private Quaternion _targetRot;
    private Vector3    _targetPos;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (centerEyeAnchor == null)
        {
            var go = GameObject.Find("CenterEyeAnchor");
            if (go != null) centerEyeAnchor = go.transform;
        }
    }

    private void Start()
    {
        // Snap into place immediately so there's no lerp-lag on first frame.
        CalculateTargets();
        if (fixedDistance > 0f) transform.position = _targetPos;
        transform.rotation = _targetRot;
    }

    private void LateUpdate()
    {
        if (centerEyeAnchor == null) return;

        CalculateTargets();

        // Position — only move if we have a fixed distance requirement.
        if (fixedDistance > 0f)
        {
            transform.position = positionSmoothing > 0f
                ? Vector3.SmoothDamp(transform.position, _targetPos,
                                     ref _posVelocity, positionSmoothing)
                : _targetPos;
        }

        // Rotation — always billboard toward player.
        transform.rotation = Quaternion.Slerp(
            transform.rotation, _targetRot,
            rotationSpeed > 0f ? rotationSpeed : 1f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void CalculateTargets()
    {
        // Project forward onto horizontal plane — panel is always upright.
        Vector3 forward = Vector3.ProjectOnPlane(
            centerEyeAnchor.forward, Vector3.up).normalized;

        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward; // safety: player looking straight up/down

        if (fixedDistance > 0f)
        {
            _targetPos = centerEyeAnchor.position
                       + forward    * fixedDistance
                       + Vector3.up * heightOffset;
        }

        _targetRot = Quaternion.LookRotation(forward);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Snap the panel to the player's current facing direction immediately.</summary>
    public void SnapToPlayer()
    {
        if (centerEyeAnchor == null) return;
        CalculateTargets();
        if (fixedDistance > 0f) transform.position = _targetPos;
        transform.rotation = _targetRot;
        _posVelocity = Vector3.zero;
    }
}
