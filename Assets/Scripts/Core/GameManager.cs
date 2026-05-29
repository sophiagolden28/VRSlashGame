using UnityEngine;

/// <summary>
/// Singleton that owns all authoritative game state: score, lives, and combo.
/// All gameplay systems should call RegisterSlice() / RegisterMiss() rather
/// than mutating state directly. This class fires GameEvents so the rest of
/// the codebase can react without coupling to GameManager.
///
/// Combo tiers (consecutive slices without a miss):
///   1–4   → 1× multiplier (no badge shown)
///   5–9   → 2× multiplier
///   10+   → 3× multiplier
/// A miss resets Combo to 0.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Game Settings")]
    [Tooltip("Lives the player starts with each session.")]
    [SerializeField] private int startingLives = 3;

    // ── Public read-only state ────────────────────────────────────────────────

    /// <summary>Consecutive successful slices without a miss.</summary>
    public int  Score        { get; private set; }
    public int  Lives        { get; private set; }
    public int  Combo        { get; private set; }
    public bool IsGameActive { get; private set; }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets all state and starts a new game session.
    /// Call this from a start screen or restart button.
    /// </summary>
    public void StartGame()
    {
        Score        = 0;
        Lives        = startingLives;
        Combo        = 0;
        IsGameActive = true;
        GameEvents.RaiseGameStart();
        Debug.Log("[GameManager] Game started.");
    }

    /// <summary>
    /// Call this when a fruit is successfully sliced.
    /// Increments Combo, applies the tier multiplier, and fires events.
    /// </summary>
    /// <param name="basePoints">Raw point value of the fruit before multiplier.</param>
    public void RegisterSlice(int basePoints)
    {
        if (!IsGameActive) return;

        Combo++;

        // 1x by default, 2x at 5 consecutive, 3x at 10+.
        int multiplier = Combo >= 10 ? 3 : Combo >= 5 ? 2 : 1;
        int earned     = basePoints * multiplier;
        Score         += earned;

        GameEvents.RaiseFruitSliced(earned, Score);
        GameEvents.RaiseComboChanged(Combo);
        Debug.Log($"[GameManager] Slice +{earned} pts (x{multiplier}). Total: {Score}. Combo: {Combo}");
    }

    /// <summary>
    /// Call this when a fruit exits the play area without being sliced.
    /// Breaks the combo, decrements lives, and fires OnFruitMissed.
    /// Triggers OnGameOver if lives reach zero.
    /// </summary>
    public void RegisterMiss()
    {
        if (!IsGameActive) return;

        Combo = 0;
        GameEvents.RaiseComboChanged(0);

        Lives--;
        GameEvents.RaiseFruitMissed(Lives);
        Debug.Log($"[GameManager] Miss registered. Lives remaining: {Lives}");

        if (Lives <= 0)
        {
            IsGameActive = false;
            GameEvents.RaiseGameOver();
            Debug.Log("[GameManager] Game over.");
        }
    }
}
