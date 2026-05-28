using UnityEngine;

/// <summary>
/// Handles per-fruit physics (manual gravity integration on a kinematic Rigidbody),
/// lifetime expiry, and the public Slice() entry point for slash detection.
///
/// Kinematic + useGravity=false lets us own the trajectory entirely while still
/// receiving OnTriggerEnter/Stay callbacks for slash detection.
/// Movement goes through transform.position in Update (not MovePosition, which
/// only works in FixedUpdate).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class Fruit : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Lifetime")]
    [Tooltip("Seconds before an unsliced fruit disappears and counts as a miss.")]
    [SerializeField] private float lifetime = 5f;

    [Header("Physics")]
    [Tooltip("Gravity magnitude applied manually each frame (m/s²).")]
    [SerializeField] private float gravity = 9.81f;

    // ── State ─────────────────────────────────────────────────────────────────

    private Vector3 _velocity;
    private Vector3 _rotationAxis;
    private float   _rotationSpeed;   // deg/s
    private float   _elapsed;
    private bool    _launched;
    private bool    _alive = true;

    // ── Colour ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The display colour set by FruitSpawner at spawn time.
    /// BladeController reads this before calling Slice() so the juice-burst
    /// particles can match the fruit.
    /// </summary>
    public Color FruitColor { get; private set; }

    /// <summary>True once Slice() has run successfully (fruit was alive).</summary>
    public bool IsSliced { get; private set; }

    /// <summary>
    /// Called by FruitSpawner immediately after BuildFruit to record the
    /// representative display colour (used for particle tinting on slice).
    /// </summary>
    public void SetColor(Color color) => FruitColor = color;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by FruitSpawner immediately after the fruit is positioned.
    /// Sets the initial velocity and randomises the tumble spin.
    /// </summary>
    public void Launch(Vector3 velocity)
    {
        _velocity      = velocity;
        _launched      = true;
        _rotationAxis  = Random.onUnitSphere;
        _rotationSpeed = Random.Range(80f, 220f); // deg/s — feels natural in VR
    }

    /// <summary>
    /// Called by BladeController when this fruit is hit at slash speed.
    /// Returns true if the fruit was alive and is now sliced (caller must then call
    /// GameManager.RegisterSlice exactly once). Returns false if already dead —
    /// guards against double-scoring when OnTriggerEnter and OnTriggerStay both
    /// fire for the same overlap in the same frame.
    /// TODO: spawn slice halves + juice particles here.
    /// </summary>
    public bool Slice()
    {
        if (!_alive) return false;
        _alive   = false;
        IsSliced = true;
        Destroy(gameObject);
        return true;
    }

    /// <summary>
    /// Called by FruitSpawner.HandleGameOver() to remove the fruit silently
    /// (no miss penalty — the game is already over).
    /// </summary>
    public void Kill()
    {
        _alive = false;
        Destroy(gameObject);
    }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Update()
    {
        if (!_alive || !_launched) return;

        // ── Gravity integration ──────────────────────────────────────────────
        // Kinematic Rigidbody with useGravity=false: we own the simulation.
        // Move via transform.position in Update (MovePosition only works in FixedUpdate).
        _velocity          += Vector3.down * gravity * Time.deltaTime;
        transform.position += _velocity * Time.deltaTime;

        // ── Tumble rotation (visual only — collider doesn't rotate) ──────────
        transform.Rotate(_rotationAxis, _rotationSpeed * Time.deltaTime, Space.World);

        // ── Lifetime expiry ──────────────────────────────────────────────────
        _elapsed += Time.deltaTime;
        if (_elapsed >= lifetime)
            OnMissed();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void OnMissed()
    {
        _alive = false;

        // Only penalise if a game session is actually running.
        // Guards against misses firing in the test-spawn phase before StartGame().
        if (GameManager.Instance != null && GameManager.Instance.IsGameActive)
            GameManager.Instance.RegisterMiss();

        Destroy(gameObject);
    }
}
