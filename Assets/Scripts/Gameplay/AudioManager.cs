using UnityEngine;

/// <summary>
/// Procedural audio singleton for the VR Slash game.
/// Generates all sounds at runtime — no audio asset files needed.
///
/// Slash sounds (6 clips, one per fruit hue band):
///   Each clip is ~0.15 s — a short sine burst with percussive noise and
///   exponential decay. Frequencies are drawn from the C major pentatonic
///   scale and mapped to fruit colour by hue:
///     Hue [0.00–0.17) red    → E4 330 Hz
///     Hue [0.17–0.33) orange → G4 392 Hz
///     Hue [0.33–0.50) yellow → A4 440 Hz
///     Hue [0.50–0.67) green  → B4 494 Hz
///     Hue [0.67–0.83) pink   → D5 587 Hz
///     Hue [0.83–1.00) purple → C4 262 Hz
///
/// Background music:
///   A looping 8-note C major pentatonic phrase (C5 E5 G5 A5 G5 E5 D5 C5)
///   at BPM 72, soft sine with ADSR envelope. Starts on game boot.
///
/// Wire-up:
///   BladeController calls PlaySlashSound(fruitColor) after a successful slice.
///   PlayMissSound() is available if you want to call it from Fruit/GameManager.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    // ── Audio sources ─────────────────────────────────────────────────────────

    private AudioSource _musicSource;
    private AudioSource _sfxSource;

    // ── Generated clips ───────────────────────────────────────────────────────

    private AudioClip[] _slashClips; // length 6
    private AudioClip   _missClip;

    // ── Frequency tables ──────────────────────────────────────────────────────

    // One frequency per hue band; index = Mathf.FloorToInt(h * 6f) % 6.
    private static readonly float[] SlashFrequencies = { 330f, 392f, 440f, 494f, 587f, 262f };

    // 8-note C major pentatonic melody — loops continuously as background music.
    private static readonly float[] PentatonicMelody =
    {
        523.25f,  // C5
        659.25f,  // E5
        783.99f,  // G5
        880.00f,  // A5
        783.99f,  // G5
        659.25f,  // E5
        587.33f,  // D5
        523.25f,  // C5
    };

    private const int SampleRate = 44100;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Background music source — full 2D, soft volume.
        _musicSource              = gameObject.AddComponent<AudioSource>();
        _musicSource.loop         = true;
        _musicSource.spatialBlend = 0f;
        _musicSource.volume       = 0.20f;
        _musicSource.playOnAwake  = false;

        // SFX source — slight 3D presence (0.4) for a satisfying positional feel.
        _sfxSource               = gameObject.AddComponent<AudioSource>();
        _sfxSource.loop          = false;
        _sfxSource.spatialBlend  = 0.4f;
        _sfxSource.volume        = 1f;
        _sfxSource.playOnAwake   = false;

        // Pre-generate all clips at startup.
        _slashClips = new AudioClip[SlashFrequencies.Length];
        for (int i = 0; i < _slashClips.Length; i++)
            _slashClips[i] = GenerateSlashClip(i);

        _missClip = GenerateMissClip();
    }

    private void Start()
    {
        // Build the music clip in Start() so it can be garbage-collected if the
        // AudioManager is ever destroyed/reloaded (not the case with DontDestroyOnLoad,
        // but good practice).
        _musicSource.clip = GenerateMusicClip();
        _musicSource.Play();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays a short pentatonic tone whose pitch maps to the fruit's hue.
    /// Call this from BladeController after fruit.Slice() succeeds.
    /// </summary>
    public void PlaySlashSound(Color fruitColor)
    {
        Color.RGBToHSV(fruitColor, out float h, out _, out _);
        int idx = Mathf.FloorToInt(h * 6f) % 6;
        _sfxSource.PlayOneShot(_slashClips[idx]);
    }

    /// <summary>Low thud for a missed fruit. Optional — wire if desired.</summary>
    public void PlayMissSound()
    {
        _sfxSource.PlayOneShot(_missClip);
    }

    // ── Clip generation ───────────────────────────────────────────────────────

    /// <summary>
    /// Short (~0.15 s) percussive sine burst with exponential decay.
    /// A small noise component adds crunch character without harshness.
    /// </summary>
    private static AudioClip GenerateSlashClip(int freqIndex)
    {
        float freq        = SlashFrequencies[freqIndex];
        int   sampleCount = Mathf.RoundToInt(0.15f * SampleRate);
        var   data        = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t      = (float)i / SampleRate;
            float attack = Mathf.Clamp01(t / 0.005f);          // 5 ms fade-in
            float decay  = Mathf.Exp(-t * 30f);                 // fast decay
            float noise  = (Random.value * 2f - 1f) * 0.25f;   // percussive crunch
            data[i] = (Mathf.Sin(2f * Mathf.PI * freq * t) + noise) * decay * attack;
        }

        var clip = AudioClip.Create("Slash_" + freqIndex, sampleCount, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>
    /// 8-note C major pentatonic phrase at BPM 72 (≈ 6.67 s), designed to
    /// loop seamlessly. Each note has a soft ADSR envelope at 40% amplitude.
    /// </summary>
    private static AudioClip GenerateMusicClip()
    {
        float bpm          = 72f;
        float beatDuration = 60f / bpm;                          // ≈ 0.833 s
        int   noteCount    = PentatonicMelody.Length;            // 8
        int   totalSamples = Mathf.RoundToInt(noteCount * beatDuration * SampleRate);
        var   data         = new float[totalSamples];

        for (int n = 0; n < noteCount; n++)
        {
            float noteFreq  = PentatonicMelody[n];
            int   noteStart = Mathf.RoundToInt(n       * beatDuration * SampleRate);
            int   noteEnd   = Mathf.Min(
                                  Mathf.RoundToInt((n + 1) * beatDuration * SampleRate),
                                  totalSamples);
            float noteDur   = beatDuration;

            for (int i = noteStart; i < noteEnd; i++)
            {
                float t = (float)(i - noteStart) / SampleRate;

                // ADSR: 20 ms attack, 50 ms decay to 0.7, sustain, 50 ms release.
                float env;
                if      (t < 0.020f)               env = t / 0.020f;
                else if (t < 0.070f)               env = 1f - (t - 0.020f) / 0.050f * 0.3f;
                else if (t < noteDur - 0.050f)     env = 0.7f;
                else                               env = Mathf.Max(0f, 0.7f * (1f - (t - (noteDur - 0.050f)) / 0.050f));

                data[i] += Mathf.Sin(2f * Mathf.PI * noteFreq * t) * env * 0.40f;
            }
        }

        var clip = AudioClip.Create("BGMusic", totalSamples, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Short 0.3 s low-frequency thud for missed fruits.</summary>
    private static AudioClip GenerateMissClip()
    {
        int sampleCount = Mathf.RoundToInt(0.30f * SampleRate);
        var data        = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t   = (float)i / SampleRate;
            float env = Mathf.Exp(-t * 10f);
            data[i]   = Mathf.Sin(2f * Mathf.PI * 120f * t) * env * 0.7f;
        }

        var clip = AudioClip.Create("Miss", sampleCount, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
