using System;
using UnityEngine;

/// <summary>
/// State enum for the MR Concept Visualization scene state machine.
/// Defined here (rather than in MRSceneController) so ConceptVizEvents can
/// reference it without creating a circular dependency.
/// </summary>
public enum MRVizState
{
    /// <summary>Awaiting JSON load and passthrough initialisation.</summary>
    Loading,
    /// <summary>Player is pointing at a surface to anchor the content.</summary>
    Placing,
    /// <summary>Content is anchored and visible — normal interaction mode.</summary>
    Exploring
}

/// <summary>
/// Static event bus for the MR Concept Visualization scene.
///
/// Usage pattern (identical to GameEvents in the VR Slash scene):
///   Subscribe in OnEnable, unsubscribe in OnDisable.
///   Raise via the static Raise* helper methods — never invoke the delegates directly.
///
/// All events are fired on the Unity main thread.
/// </summary>
public static class ConceptVizEvents
{
    // ── Config lifecycle ──────────────────────────────────────────────────────

    /// <summary>Fired once after the JSON config is successfully parsed.</summary>
    public static event Action<ConceptConfig> OnConfigLoaded;

    // ── Scene state ───────────────────────────────────────────────────────────

    /// <summary>Fired when the scene state machine transitions to a new state.</summary>
    public static event Action<MRVizState> OnStateChanged;

    // ── Surface / anchor ──────────────────────────────────────────────────────

    /// <summary>
    /// Fired every frame while SurfaceDetector has a live hit.
    /// (point, normal) — world space.
    /// </summary>
    public static event Action<Vector3, Vector3> OnSurfaceDetected;

    /// <summary>Fired once when the player confirms an anchor placement.</summary>
    public static event Action<Vector3, Quaternion> OnAnchorPlaced;

    // ── Objects ───────────────────────────────────────────────────────────────

    /// <summary>Fired after a concept object is fully spawned. (id, GameObject)</summary>
    public static event Action<string, GameObject> OnObjectSpawned;

    /// <summary>Fired just before a concept object is destroyed.</summary>
    public static event Action<string> OnObjectDestroyed;

    // ── Layers ────────────────────────────────────────────────────────────────

    /// <summary>Fired when a layer's visibility changes. (layerId, isVisible)</summary>
    public static event Action<string, bool> OnLayerToggled;

    // ── UI requests ───────────────────────────────────────────────────────────

    /// <summary>Fired when the player presses the Recenter button in the UI.</summary>
    public static event Action OnRecenterRequested;

    /// <summary>Fired when the player toggles Surface Placement mode. (isActive)</summary>
    public static event Action<bool> OnSurfaceModeToggled;

    // ── Raise helpers ─────────────────────────────────────────────────────────
    // Always raise via these methods — they null-check the delegate,
    // preventing NullReferenceException when no listeners are subscribed.

    public static void RaiseConfigLoaded(ConceptConfig config)
        => OnConfigLoaded?.Invoke(config);

    public static void RaiseStateChanged(MRVizState state)
        => OnStateChanged?.Invoke(state);

    public static void RaiseSurfaceDetected(Vector3 point, Vector3 normal)
        => OnSurfaceDetected?.Invoke(point, normal);

    public static void RaiseAnchorPlaced(Vector3 position, Quaternion rotation)
        => OnAnchorPlaced?.Invoke(position, rotation);

    public static void RaiseObjectSpawned(string id, GameObject go)
        => OnObjectSpawned?.Invoke(id, go);

    public static void RaiseObjectDestroyed(string id)
        => OnObjectDestroyed?.Invoke(id);

    public static void RaiseLayerToggled(string layerId, bool visible)
        => OnLayerToggled?.Invoke(layerId, visible);

    public static void RaiseRecenterRequested()
        => OnRecenterRequested?.Invoke();

    public static void RaiseSurfaceModeToggled(bool active)
        => OnSurfaceModeToggled?.Invoke(active);
}
