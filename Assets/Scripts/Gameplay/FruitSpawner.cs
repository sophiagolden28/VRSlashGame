using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns fruits in a wide arc in front of the player every spawnInterval seconds.
///
/// Spawn position: ground level (centerEyeAnchor.y - 1.5 m) in a horizontal arc.
/// Launch: mostly upward so fruits arc to arm height, with small random drift for
/// varied paths. Gravity is handled by the Fruit component.
///
/// Model priority:
///   1. fruitModels[] — populate with Kenney Food Kit FBX references via Inspector.
///   2. Fallback: coloured sphere primitives (no external assets needed).
///
/// Lifecycle:
///   • Spawning starts immediately in Play mode for testing.
///   • OnGameStart clears test fruits and restarts clean.
///   • OnGameOver kills all live fruits silently (no miss penalty).
/// </summary>
public class FruitSpawner : MonoBehaviour
{
    // ── References ────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("CenterEyeAnchor from OVRCameraRig/TrackingSpace — drives spawn position.")]
    [SerializeField] private Transform centerEyeAnchor;

    // ── Fruit Models ──────────────────────────────────────────────────────────

    [Header("Fruit Models")]
    [Tooltip("Assign Kenney Food Kit FBX models here. Leave empty to use coloured spheres.")]
    [SerializeField] private GameObject[] fruitModels;

    [Tooltip("Uniform scale applied to Kenney models. Tune until fruits look apple-sized in VR.")]
    [SerializeField] private float fruitModelScale = 0.12f;

    // ── Spawn ─────────────────────────────────────────────────────────────────

    [Header("Spawn")]
    [Tooltip("Seconds between each fruit.")]
    [SerializeField] private float spawnInterval = 1.5f;

    [Tooltip("Total horizontal arc in front of the player (degrees). 120° = 60° each side.")]
    [SerializeField] private float arcDegrees = 120f;

    [Tooltip("Minimum distance from the player's centre line (metres).")]
    [SerializeField] private float spawnRadiusMin = 1.5f;

    [Tooltip("Maximum distance from the player's centre line (metres).")]
    [SerializeField] private float spawnRadiusMax = 2.5f;

    [Tooltip("Spawn height relative to eye level (metres). EyeLevel tracking: true ground ≈ -1.5. " +
             "Raise toward 0 to lift the whole arc higher.")]
    [SerializeField] private float groundOffset = -1.0f;

    [Tooltip("SphereCollider radius on spawned fruits (metres). ~0.08 = 16 cm diameter.")]
    [SerializeField] private float colliderRadius = 0.08f;

    [Tooltip("Cap on simultaneous live fruits.")]
    [SerializeField] private int maxActiveFruits = 10;

    // ── Launch Physics ────────────────────────────────────────────────────────

    [Header("Launch Physics")]
    [Tooltip("Minimum upward launch speed (m/s). At 4.5 m/s a fruit peaks at ~arm height.")]
    [SerializeField] private float launchSpeedMin = 4.5f;

    [Tooltip("Maximum upward launch speed (m/s). At 5.5 m/s a fruit peaks near eye level.")]
    [SerializeField] private float launchSpeedMax = 5.5f;

    [Tooltip("Max random horizontal speed added per axis (m/s). Varies each fruit's path.")]
    [SerializeField] private float driftRange = 0.35f;

    // ── Fallback colours ──────────────────────────────────────────────────────

    private static readonly Color[] FallbackColors =
    {
        new Color(0.95f, 0.18f, 0.18f),  // red    (apple)
        new Color(1.00f, 0.82f, 0.08f),  // yellow (banana / lemon)
        new Color(1.00f, 0.52f, 0.05f),  // orange
        new Color(0.18f, 0.72f, 0.22f),  // green  (lime / kiwi)
        new Color(0.92f, 0.18f, 0.48f),  // pink   (watermelon flesh)
        new Color(0.55f, 0.12f, 0.72f),  // purple (grapes)
    };

    // Kenney models share a single colormap texture — material.color returns white.
    // Map fruit type (identified by model name substring) to a particle tint colour.
    private static readonly Dictionary<string, Color> KenneyFruitColors = new Dictionary<string, Color>
    {
        { "apple",      new Color(0.95f, 0.18f, 0.18f) },  // red
        { "banana",     new Color(1.00f, 0.85f, 0.10f) },  // yellow
        { "coconut",    new Color(0.60f, 0.40f, 0.20f) },  // brown
        { "lemon",      new Color(1.00f, 0.95f, 0.20f) },  // bright yellow
        { "lime",       new Color(0.25f, 0.80f, 0.20f) },  // bright green
        { "mango",      new Color(1.00f, 0.60f, 0.05f) },  // deep orange
        { "orange",     new Color(1.00f, 0.52f, 0.05f) },  // orange
        { "pineapple",  new Color(1.00f, 0.85f, 0.15f) },  // golden yellow
        { "strawberry", new Color(0.95f, 0.15f, 0.25f) },  // deep red
        { "watermelon", new Color(0.92f, 0.18f, 0.35f) },  // pink-red flesh
    };

    /// <summary>
    /// Returns the representative particle colour for a Kenney model name by
    /// scanning for a known fruit-type substring. Falls back to orange if unknown.
    /// </summary>
    private static Color GetKenneyFruitColor(string modelName)
    {
        string lower = modelName.ToLowerInvariant();
        foreach (var pair in KenneyFruitColors)
            if (lower.Contains(pair.Key))
                return pair.Value;
        return new Color(1.00f, 0.52f, 0.05f); // fallback: orange
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private bool             _isSpawning;
    private float            _timer;

    // List<Fruit> so we can call Kill() directly without GetComponent in the hot path.
    private readonly List<Fruit> _activeFruits = new List<Fruit>(16);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

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

    private void Start()
    {
        // Start spawning immediately so the scene is testable before the full
        // game loop (GameManager.StartGame) is wired up.
        _isSpawning = true;
        _timer      = spawnInterval; // spawn on the very first Update
    }

    private void Update()
    {
        if (!_isSpawning) return;

        // Clean null entries (fruits destroyed by Fruit.OnMissed / Fruit.Slice)
        // Reverse loop — no allocation, safe for removal.
        for (int i = _activeFruits.Count - 1; i >= 0; i--)
            if (_activeFruits[i] == null) _activeFruits.RemoveAt(i);

        if (_activeFruits.Count >= maxActiveFruits) return;

        _timer += Time.deltaTime;
        if (_timer >= spawnInterval)
        {
            _timer = 0f;
            SpawnFruit();
        }
    }

    // ── Event Handlers ────────────────────────────────────────────────────────

    private void HandleGameStart()
    {
        // Purge any fruits from the pre-game test phase silently.
        KillAllFruits();

        _isSpawning = true;
        _timer      = spawnInterval; // spawn immediately at game start
    }

    private void HandleGameOver()
    {
        _isSpawning = false;
        KillAllFruits();
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    private void SpawnFruit()
    {
        if (centerEyeAnchor == null)
        {
            Debug.LogWarning("[FruitSpawner] centerEyeAnchor not assigned — cannot spawn.");
            return;
        }

        // ── Spawn position ───────────────────────────────────────────────────
        Vector3 eyePos  = centerEyeAnchor.position;

        // Flatten the head's forward to the horizontal plane so arc is always upright.
        Vector3 forward = Vector3.ProjectOnPlane(centerEyeAnchor.forward, Vector3.up).normalized;
        Vector3 right   = Vector3.Cross(Vector3.up, forward); // right relative to head facing

        float   angle   = Random.Range(-arcDegrees * 0.5f, arcDegrees * 0.5f) * Mathf.Deg2Rad;
        float   radius  = Random.Range(spawnRadiusMin, spawnRadiusMax);

        // Direction in the horizontal arc, then down to ground level.
        // EyeLevel tracking: y=0 is eye height, ground at eye.y - 1.5 m.
        Vector3 spawnPos   = eyePos
                           + (Mathf.Cos(angle) * forward + Mathf.Sin(angle) * right) * radius;
        spawnPos.y         = eyePos.y + groundOffset;

        // ── Launch velocity ──────────────────────────────────────────────────
        // peak_height = v² / (2g). At 4.5 m/s: h ≈ 1.03 m above ground ≈ arm height.
        // Small world-space drift makes every fruit take a different path.
        float   vy             = Random.Range(launchSpeedMin, launchSpeedMax);
        float   driftRight     = Random.Range(-driftRange, driftRange);
        float   driftForward   = Random.Range(-driftRange, driftRange);
        Vector3 launchVelocity = Vector3.up * vy
                               + right   * driftRight
                               + forward * driftForward;

        // ── Build and launch ─────────────────────────────────────────────────
        Fruit fruit = BuildFruit(spawnPos);
        fruit.Launch(launchVelocity);
        _activeFruits.Add(fruit);
    }

    // ── Build Fruit GameObject ────────────────────────────────────────────────

    private Fruit BuildFruit(Vector3 position)
    {
        var go             = new GameObject("Fruit");
        go.transform.position = position;

        // ── Rigidbody — kinematic so we simulate gravity manually,
        //    but still receive trigger callbacks for slash detection.
        var rb         = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        // ── Trigger collider — SphereCollider instead of MeshCollider
        //    (FBX MeshColliders don't work as triggers).
        var col        = go.AddComponent<SphereCollider>();
        col.isTrigger  = true;
        col.radius     = colliderRadius;

        // ── Visual ────────────────────────────────────────────────────────────
        // Both build methods return the fruit's representative colour so we can
        // store it on the Fruit component for particle tinting on slice.
        Color fruitColor = (fruitModels != null && fruitModels.Length > 0)
            ? BuildKenneyVisual(go)
            : BuildSphereVisual(go);

        // ── Fruit component — add last so Awake() sees the Rigidbody already.
        var fruit = go.AddComponent<Fruit>();
        fruit.SetColor(fruitColor);
        return fruit;
    }

    private Color BuildKenneyVisual(GameObject parent)
    {
        int idx    = Random.Range(0, fruitModels.Length);
        var source = fruitModels[idx];
        var model  = Instantiate(source, parent.transform);

        // Preserve the source name so name-based fruit-type lookups work.
        model.name = source.name;
        model.transform.localPosition = Vector3.zero;
        model.transform.localScale    = Vector3.one * fruitModelScale;

        // Remove any colliders the FBX may have baked in — our parent SphereCollider
        // is the sole trigger.
        foreach (var c in model.GetComponentsInChildren<Collider>())
            Destroy(c);

        // Kenney models share a colormap atlas — identify the fruit type by name.
        return GetKenneyFruitColor(source.name);
    }

    private Color BuildSphereVisual(GameObject parent)
    {
        // CreatePrimitive adds its own SphereCollider — remove it immediately so only
        // the parent trigger collider participates in physics queries.
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(sphere.GetComponent<SphereCollider>());

        sphere.name = "FruitVisual";
        sphere.transform.SetParent(parent.transform);
        sphere.transform.localPosition = Vector3.zero;
        // 0.16 m diameter ≈ a large orange — feels right at arm's length in VR.
        sphere.transform.localScale    = Vector3.one * (colliderRadius * 2f);

        // Colour via MaterialPropertyBlock — avoids unique material instances
        // that would break GPU instancing / batching.
        Color c   = FallbackColors[Random.Range(0, FallbackColors.Length)];
        var   mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", c);
        sphere.GetComponent<Renderer>().SetPropertyBlock(mpb);
        return c;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void KillAllFruits()
    {
        for (int i = _activeFruits.Count - 1; i >= 0; i--)
        {
            if (_activeFruits[i] != null)
                _activeFruits[i].Kill();
        }
        _activeFruits.Clear();
    }
}
