using UnityEngine;

/// <summary>
/// Wraps OVRPassthroughLayer for the MR Concept Visualization scene.
///
/// Passthrough compositing rules (Quest 3 / 3S):
///   • The camera's background color MUST be (0, 0, 0, 0) — fully transparent
///     alpha — for passthrough to show through. Opaque black blocks it completely.
///   • OVRPassthroughLayer must sit on (or be a child of) OVRCameraRig with
///     projectionSurfaceType set to Reconstructed for accurate surface blending.
///   • textureOpacity: 0 = passthrough invisible, 1 = full passthrough (AR).
///     This is the "how much real world shows through" knob.
///   • The component is always enabled; we control visibility via textureOpacity
///     rather than toggling enabled, to keep the compositor layer registered.
///
/// OVRManager requirements (set via Inspector / Project Settings):
///   • Insights SDK: Hand Tracking Support = Supported (or Required)
///   • Passthrough Support = Required
///   • Color Gamut = Quest3 (or Native)
/// </summary>
public class PassthroughController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("OVRPassthroughLayer on OVRCameraRig. Auto-found if left empty.")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;

    [Header("Default State")]
    [Tooltip("Enable passthrough immediately on Start (MR mode).")]
    [SerializeField] private bool enableOnStart = true;

    [Header("Fade")]
    [Tooltip("Seconds to cross-fade passthrough on/off. Set to 0 for instant.")]
    [SerializeField] [Range(0f, 2f)] private float fadeDuration = 0.4f;

    // ── State ─────────────────────────────────────────────────────────────────

    private float _currentOpacity;
    private float _targetOpacity;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>True when passthrough is enabled (target opacity > 0).</summary>
    public bool IsActive => _targetOpacity > 0f;

    /// <summary>Fade passthrough in to full opacity (MR mode).</summary>
    public void EnablePassthrough()
    {
        _targetOpacity = 1f;
        if (passthroughLayer != null) passthroughLayer.enabled = true;
    }

    /// <summary>Fade passthrough out to zero (VR / fully opaque mode).</summary>
    public void DisablePassthrough() => _targetOpacity = 0f;

    /// <summary>
    /// Set passthrough opacity immediately without fading.
    /// 0 = passthrough invisible, 1 = full passthrough.
    /// </summary>
    public void SetOpacityImmediate(float opacity)
    {
        _targetOpacity  = Mathf.Clamp01(opacity);
        _currentOpacity = _targetOpacity;
        ApplyOpacity();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (passthroughLayer == null)
            passthroughLayer = GetComponentInParent<OVRPassthroughLayer>();
        if (passthroughLayer == null)
            passthroughLayer = FindAnyObjectByType<OVRPassthroughLayer>();
    }

    private void Start()
    {
        _currentOpacity = enableOnStart ? 0f : 1f;   // start from the other end for a clean fade
        _targetOpacity  = enableOnStart ? 1f : 0f;

        if (passthroughLayer != null) passthroughLayer.enabled = true;
    }

    private void Update()
    {
        if (passthroughLayer == null) return;

        float fadeSpeed = fadeDuration > 0f ? Time.deltaTime / fadeDuration : 1f;
        _currentOpacity = Mathf.MoveTowards(_currentOpacity, _targetOpacity, fadeSpeed);
        ApplyOpacity();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplyOpacity()
    {
        if (passthroughLayer != null)
            passthroughLayer.textureOpacity = _currentOpacity;
    }
}
