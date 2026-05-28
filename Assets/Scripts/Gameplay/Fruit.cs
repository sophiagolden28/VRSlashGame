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
    /// Called by the slash detector when this fruit is successfully hit.
    /// Marks the fruit dead without penalising the player.
    /// TODO: spawn slice halves + juice particles here.
    /// </summary>
    public void Slice()
    {
        if (!_alive) return;
        _alive = false;
        // Slash detection calls GameManager.RegisterSlice() — not this method's job.
        Destroy(gameObject);
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
