using UnityEngine;

/// <summary>
/// Singleton that spawns a one-shot burst of coloured particles at a world-space
/// contact point whenever a fruit is sliced.
///
/// Design notes (VR arm-length scale):
///   • startSize  0.008 – 0.025 m   → visible pips without overwhelming the view
///   • startSpeed 0.8   – 2.5 m/s   → tight outward burst, gravity pulls back down
///   • gravityModifier 0.5f          → gentle parabolic arc near the hit point
///   • stopAction = Destroy          → the temporary GO is cleaned up automatically
///   • simulationSpace = World       → particles don't move with the controller
///
/// Colour: each burst uses a MinMaxGradient between the fruit's base colour and a
/// lighter tint (Color.Lerp toward white 35%) so there is subtle per-particle
/// variation without losing the fruit's identity.
///
/// Material rule: particleMaterial is a serialized reference (URP/Particles/Unlit).
/// No Shader.Find() — assign in the Inspector via MCP.
/// </summary>
public class SliceEffectSpawner : MonoBehaviour
{
    public static SliceEffectSpawner Instance { get; private set; }

    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Particle Material")]
    [Tooltip("URP/Particles/Unlit material at Assets/Materials/FruitParticleMaterial.mat. " +
             "Assigned via MCP — never loaded with Shader.Find() at runtime.")]
    [SerializeField] private Material particleMaterial;

    [Header("Burst Config")]
    [Tooltip("Number of particles emitted per slice hit.")]
    [SerializeField] private int   burstCount    = 24;

    [Tooltip("Base size of each particle (metres). 0.015 m ≈ a small pip at arm length in VR.")]
    [SerializeField] private float particleSize  = 0.015f;

    [Tooltip("Maximum outward speed of each particle (m/s). The actual range is 0.8 – 1.5× this value.")]
    [SerializeField] private float particleSpeed = 1.5f;

    [Tooltip("Maximum particle lifetime (seconds). Actual range is 0.3 – this value.")]
    [SerializeField] private float lifetime      = 0.7f;

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

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a coloured juice-burst at <paramref name="position"/>.
    /// The temporary GameObject destroys itself when all particles have died.
    /// </summary>
    /// <param name="position">World-space contact point (use Collider.ClosestPoint).</param>
    /// <param name="color">Base colour of the sliced fruit — particles are tinted from
    /// this colour to a slightly lighter variant for visual interest.</param>
    public void Spawn(Vector3 position, Color color)
    {
        var go = new GameObject("SliceBurst");
        go.transform.position = position;

        var ps   = go.AddComponent<ParticleSystem>();
        var main = ps.main;

        main.loop            = false;
        main.playOnAwake     = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction      = ParticleSystemStopAction.Destroy;
        main.gravityModifier = 0.5f;

        // Lifetime: 0.3 – lifetime s. Short enough not to linger in VR.
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, lifetime);

        // Speed: 0.8 – 1.5× particleSpeed. Tight burst that drops back near the hit.
        main.startSpeed    = new ParticleSystem.MinMaxCurve(0.8f, particleSpeed * 1.5f);

        // Size: 0.5 – 1.5× particleSize. Spread gives the burst some volume.
        main.startSize     = new ParticleSystem.MinMaxCurve(particleSize * 0.5f, particleSize * 1.5f);

        // Colour: base fruit colour ↔ lighter tint for per-particle variation.
        Color lighter      = Color.Lerp(color, Color.white, 0.35f);
        main.startColor    = new ParticleSystem.MinMaxGradient(color, lighter);

        // Emission: one burst at t=0 — no continuous rate needed.
        var emission       = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burstCount) });

        // Shape: small sphere — particles spray outward from the contact point.
        var shape          = ps.shape;
        shape.enabled      = true;
        shape.shapeType    = ParticleSystemShapeType.Sphere;
        shape.radius       = 0.03f;

        // Renderer: Billboard mode — always faces the camera.
        var rend           = ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode    = ParticleSystemRenderMode.Billboard;
        if (particleMaterial != null)
            rend.material  = particleMaterial;

        // playOnAwake fires during AddComponent, but call Play() explicitly to be safe.
        ps.Play();
    }
}
