using UnityEngine;

/// <summary>
/// Grab / rotate / scale a concept object using Quest Touch controllers or hand tracking.
///
/// Single-hand grab (grip button): translates and rotates the object to follow
/// the controller with the offset captured at grab start.
///
/// Two-hand scale: when both grip buttons are held and the distance between the
/// two controllers changes, the object is uniformly scaled. Minimum and maximum
/// world scale are clamped to prevent objects being made unusably tiny or huge.
///
/// Controller mapping:
///   Right Grip = OVRInput.Button.SecondaryHandTrigger (RTouch)
///   Left  Grip = OVRInput.Button.PrimaryHandTrigger   (LTouch)
///
/// This component is added to each spawned concept object by MRSceneController
/// after all objects are instantiated.
///
/// Interaction requires that the scene has an OVRCameraRig with TrackingSpace
/// and the hand/controller anchors referenced in the Inspector (or auto-found
/// via GameObject.Find on the standard OVR hierarchy names).
/// </summary>
public class VizObjectManipulator : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Hand Anchors")]
    [Tooltip("Right hand / controller anchor. Auto-found from OVR hierarchy if empty.")]
    [SerializeField] private Transform rightHandAnchor;
    [Tooltip("Left hand / controller anchor. Auto-found from OVR hierarchy if empty.")]
    [SerializeField] private Transform leftHandAnchor;

    [Header("Scale Limits")]
    [Tooltip("Minimum allowed world scale (uniform). Prevents shrinking to nothing.")]
    [SerializeField] private float minScale = 0.05f;
    [Tooltip("Maximum allowed world scale (uniform). Prevents objects becoming room-filling.")]
    [SerializeField] private float maxScale = 3.0f;

    [Header("Interaction")]
    [Tooltip("True = this object can be grabbed. Set to false to lock an object in place.")]
    [SerializeField] public bool allowManipulation = true;

    // ── Private state ─────────────────────────────────────────────────────────

    // Grab state
    private bool       _rightGrabbing;
    private bool       _leftGrabbing;
    private Vector3    _grabOffsetR;
    private Vector3    _grabOffsetL;
    private Quaternion _grabRotOffsetR;
    private Quaternion _grabRotOffsetL;

    // Two-hand scale state
    private float      _grabStartHandDist;   // controller separation at two-hand grab start
    private float      _grabStartObjScale;   // object scale at two-hand grab start

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (rightHandAnchor == null)
        {
            var go = GameObject.Find("RightHandAnchor");
            if (go != null) rightHandAnchor = go.transform;
        }
        if (leftHandAnchor == null)
        {
            var go = GameObject.Find("LeftHandAnchor");
            if (go != null) leftHandAnchor = go.transform;
        }
    }

    private void Update()
    {
        if (!allowManipulation) return;
        HandleInput();
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    private void HandleInput()
    {
        bool rightDown    = OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger);
        bool rightUp      = OVRInput.GetUp(OVRInput.Button.SecondaryHandTrigger);
        bool rightHeld    = OVRInput.Get(OVRInput.Button.SecondaryHandTrigger);

        bool leftDown     = OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger);
        bool leftUp       = OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger);
        bool leftHeld     = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger);

        // ── Grab start ───────────────────────────────────────────────────────

        if (rightDown && IsControllerNearObject(rightHandAnchor))
            BeginGrab(ref _rightGrabbing, ref _grabOffsetR, ref _grabRotOffsetR, rightHandAnchor);

        if (leftDown && IsControllerNearObject(leftHandAnchor))
            BeginGrab(ref _leftGrabbing, ref _grabOffsetL, ref _grabRotOffsetL, leftHandAnchor);

        // ── Two-hand scale start ──────────────────────────────────────────────

        if (_rightGrabbing && _leftGrabbing)
        {
            if (rightDown || leftDown)
                BeginTwoHandScale();
        }

        // ── During grab ──────────────────────────────────────────────────────

        if (_rightGrabbing && _leftGrabbing)
        {
            // Two-hand: scale by ratio of current hand separation to start separation.
            TwoHandScaleUpdate();
        }
        else if (_rightGrabbing && rightHandAnchor != null)
        {
            // Single right-hand: translate + rotate.
            SingleHandDrag(rightHandAnchor, _grabOffsetR, _grabRotOffsetR);
        }
        else if (_leftGrabbing && leftHandAnchor != null)
        {
            // Single left-hand: translate + rotate.
            SingleHandDrag(leftHandAnchor, _grabOffsetL, _grabRotOffsetL);
        }

        // ── Grab end ─────────────────────────────────────────────────────────

        if (rightUp) _rightGrabbing = false;
        if (leftUp)  _leftGrabbing  = false;
    }

    // ── Grab helpers ──────────────────────────────────────────────────────────

    private bool IsControllerNearObject(Transform hand)
    {
        if (hand == null) return false;
        // Proximity check: within 15 cm of the object's origin.
        return Vector3.Distance(hand.position, transform.position) < 0.15f;
    }

    private void BeginGrab(ref bool grabbing,
                           ref Vector3 offset, ref Quaternion rotOffset,
                           Transform hand)
    {
        grabbing  = true;
        offset    = Quaternion.Inverse(hand.rotation) * (transform.position - hand.position);
        rotOffset = Quaternion.Inverse(hand.rotation) * transform.rotation;
    }

    private void SingleHandDrag(Transform hand, Vector3 posOffset, Quaternion rotOffset)
    {
        transform.position = hand.position + hand.rotation * posOffset;
        transform.rotation = hand.rotation * rotOffset;
    }

    private void BeginTwoHandScale()
    {
        if (rightHandAnchor == null || leftHandAnchor == null) return;
        _grabStartHandDist  = Vector3.Distance(rightHandAnchor.position,
                                               leftHandAnchor.position);
        _grabStartObjScale  = transform.localScale.x; // uniform scale
    }

    private void TwoHandScaleUpdate()
    {
        if (rightHandAnchor == null || leftHandAnchor == null) return;
        if (_grabStartHandDist < 0.001f) return;

        float currentDist = Vector3.Distance(rightHandAnchor.position,
                                             leftHandAnchor.position);
        float ratio       = currentDist / _grabStartHandDist;
        float newScale    = Mathf.Clamp(_grabStartObjScale * ratio, minScale, maxScale);

        transform.localScale = Vector3.one * newScale;

        // Mid-point translation: keep object centred between both hands.
        Vector3 mid = (rightHandAnchor.position + leftHandAnchor.position) * 0.5f;
        transform.position = mid;
    }
}
