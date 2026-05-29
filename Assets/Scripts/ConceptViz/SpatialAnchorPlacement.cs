using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places an OVRSpatialAnchor at a detected surface point, saves the UUID to
/// PlayerPrefs so the anchor can be re-localized across sessions, and parents
/// the content root under the anchor once it is localized.
///
/// Anchor lifecycle:
///   1. PlaceAnchor(pos, rot) — creates a new OVRSpatialAnchor at the surface.
///   2. WaitForLocalize polls anchor.Localized each frame until the Quest runtime
///      has confirmed the anchor position (usually < 1 second).
///   3. UUID is saved to PlayerPrefs.
///   4. SaveAnchorAsync persists the anchor to the OVR cloud/device store.
///
/// On next session, pass the saved UUID to TryRelocalize() to attempt recovery.
///
/// Important OVR SDK note:
///   OVRSpatialAnchor requires Meta XR SDK ≥ v60 and "Spatial Anchors" enabled
///   in Meta Developer Hub. The Awake/Update/Localized API used here is stable
///   since SDK v49. SaveAnchorAsync is SDK v60+.
///
/// Fallback: world position/rotation are saved to PlayerPrefs in parallel so the
/// scene can function without spatial anchor persistence (useful in editor/Quest).
/// </summary>
public class SpatialAnchorPlacement : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Persistence")]
    [Tooltip("PlayerPrefs key prefix for saved anchor data.")]
    [SerializeField] private string prefsKey = "ConceptVizAnchor";

    [Tooltip("Max number of saved anchor UUIDs retained across sessions.")]
    [SerializeField] private int maxSavedAnchors = 3;

    [Tooltip("Seconds to wait for localization before giving up.")]
    [SerializeField] private float localizeTimeout = 10f;

    // ── State ─────────────────────────────────────────────────────────────────

    private OVRSpatialAnchor      _anchor;
    private Transform             _contentRoot;
    private readonly List<string> _savedUuids = new List<string>();

    // ── Public API ────────────────────────────────────────────────────────────

    public bool HasAnchor   => _anchor != null;
    public bool IsLocalized => _anchor != null && _anchor.Localized;

    /// <summary>Assign the content root before calling PlaceAnchor. All concept
    /// objects will be re-parented under the localized anchor.</summary>
    public void SetContentRoot(Transform root) => _contentRoot = root;

    /// <summary>Place a new spatial anchor at the given world transform.</summary>
    public void PlaceAnchor(Vector3 position, Quaternion rotation)
    {
        // Remove previous anchor.
        if (_anchor != null) Destroy(_anchor.gameObject);

        var go = new GameObject("ConceptVizAnchor");
        go.transform.SetPositionAndRotation(position, rotation);
        _anchor = go.AddComponent<OVRSpatialAnchor>();

        // Persist world position as fallback immediately.
        SaveWorldTransform(position, rotation);

        StartCoroutine(WaitForLocalizeAndSave(_anchor));
        ConceptVizEvents.RaiseAnchorPlaced(position, rotation);
    }

    /// <summary>Clear all saved anchor data (position + UUID) from PlayerPrefs.</summary>
    public void ClearSavedData()
    {
        _savedUuids.Clear();
        PlayerPrefs.DeleteKey(prefsKey + "_count");
        PlayerPrefs.DeleteKey(prefsKey + "_pos");
        PlayerPrefs.DeleteKey(prefsKey + "_rot");
        for (int i = 0; i < maxSavedAnchors; i++)
            PlayerPrefs.DeleteKey(prefsKey + "_uuid_" + i);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Try to restore the last anchor from PlayerPrefs world-position fallback.
    /// Returns true and fires OnAnchorPlaced if saved data was found.
    /// </summary>
    public bool TryRestoreFromPrefs()
    {
        string posStr = PlayerPrefs.GetString(prefsKey + "_pos", "");
        string rotStr = PlayerPrefs.GetString(prefsKey + "_rot", "");
        if (string.IsNullOrEmpty(posStr)) return false;

        Vector3    pos = ParseVector3(posStr);
        Quaternion rot = ParseQuaternion(rotStr);

        PlaceAnchor(pos, rot);
        return true;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start() => LoadUuidsFromPrefs();

    // ── Coroutines ────────────────────────────────────────────────────────────

    private IEnumerator WaitForLocalizeAndSave(OVRSpatialAnchor anchor)
    {
        float elapsed = 0f;
        while (!anchor.Localized && elapsed < localizeTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!anchor.Localized)
        {
            Debug.LogWarning("[SpatialAnchorPlacement] Anchor localization timed out. " +
                             "World-position fallback is still active.");
            yield break;
        }

        // Reparent content under the localized anchor.
        if (_contentRoot != null)
            _contentRoot.SetParent(anchor.transform, worldPositionStays: true);

        // Save UUID.
        string uuid = anchor.Uuid.ToString();
        if (!_savedUuids.Contains(uuid))
        {
            _savedUuids.Add(uuid);
            if (_savedUuids.Count > maxSavedAnchors)
                _savedUuids.RemoveAt(0);
            SaveUuidsToPrefs();
        }

        // Persist anchor to OVR device store (SDK v60+ async API).
        yield return SaveAnchorAsync(anchor);
    }

    private IEnumerator SaveAnchorAsync(OVRSpatialAnchor anchor)
    {
        // Poll until the OVRTask finishes — GetResult() type varies by SDK version
        // so we avoid comparing against specific result enums here.
        var task = anchor.SaveAnchorAsync();
        while (!task.IsCompleted)
            yield return null;

        // ToString() gives a meaningful status string on all SDK versions.
        Debug.Log("[SpatialAnchorPlacement] SaveAnchorAsync completed: " + task.GetResult());
    }

    // ── PlayerPrefs helpers ───────────────────────────────────────────────────

    private void SaveWorldTransform(Vector3 pos, Quaternion rot)
    {
        PlayerPrefs.SetString(prefsKey + "_pos",
            $"{pos.x},{pos.y},{pos.z}");
        PlayerPrefs.SetString(prefsKey + "_rot",
            $"{rot.x},{rot.y},{rot.z},{rot.w}");
        PlayerPrefs.Save();
    }

    private void SaveUuidsToPrefs()
    {
        PlayerPrefs.SetInt(prefsKey + "_count", _savedUuids.Count);
        for (int i = 0; i < _savedUuids.Count; i++)
            PlayerPrefs.SetString(prefsKey + "_uuid_" + i, _savedUuids[i]);
        PlayerPrefs.Save();
    }

    private void LoadUuidsFromPrefs()
    {
        int count = PlayerPrefs.GetInt(prefsKey + "_count", 0);
        for (int i = 0; i < count; i++)
        {
            string uuid = PlayerPrefs.GetString(prefsKey + "_uuid_" + i, "");
            if (!string.IsNullOrEmpty(uuid) && !_savedUuids.Contains(uuid))
                _savedUuids.Add(uuid);
        }
    }

    private static Vector3 ParseVector3(string s)
    {
        string[] p = s.Split(',');
        if (p.Length < 3) return Vector3.zero;
        return new Vector3(
            float.TryParse(p[0], out float x) ? x : 0f,
            float.TryParse(p[1], out float y) ? y : 0f,
            float.TryParse(p[2], out float z) ? z : 0f);
    }

    private static Quaternion ParseQuaternion(string s)
    {
        string[] p = s.Split(',');
        if (p.Length < 4) return Quaternion.identity;
        return new Quaternion(
            float.TryParse(p[0], out float x) ? x : 0f,
            float.TryParse(p[1], out float y) ? y : 0f,
            float.TryParse(p[2], out float z) ? z : 0f,
            float.TryParse(p[3], out float w) ? w : 1f);
    }
}
