using UnityEngine;

/// <summary>
/// Singleton that owns all authoritative game state: score, lives, and combo.
/// All gameplay systems should call RegisterSlice() / RegisterMiss() rather
/// than mutating state directly. This class fires GameEvents so the rest of
/// the codebase can react without coupling to GameManager.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Game Settings")]
    [Tooltip("Lives the player starts with each session.")]
    [SerializeField] private int startingLives = 3;

    [Tooltip("Number of consecutive slices required before the combo multiplier activates.")]
    [SerializeField] private int comboStartsAt = 3;

    // ── Public read-only state ────────────────────────────────────────────────

    public int  Score        { get; private set; }
    public int  Lives        { get; private set; }
    public int  Combo        { get; private set; }
    public bool IsGameActive { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private int _consecutiveSlices;

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
        Score              = 0;
        Lives              = startingLives;
        Combo              = 0;
        _consecutiveSlices = 0;
        IsGameActive       = true;
        GameEvents.RaiseGameStart();
        Debug.Log("[GameManager] Game started.");
    }

    /// <summary>
    /// Call this when a fruit is successfully sliced.
    /// Updates score and combo, then fires OnFruitSliced (and OnComboChanged if needed).
    /// </summary>
    /// <param name="basePoints">Raw point value of the fruit before multiplier.</param>
    public void RegisterSlice(int basePoints)
    {
        if (!IsGameActive) return;

        _consecutiveSlices++;

        // Combo activates after comboStartsAt consecutive slices; each extra slice
        // increments the multiplier by 1.
        int newCombo = Mathf.Max(0, _consecutiveSlices - comboStartsAt + 1);
        if (newCombo != Combo)
        {
            Combo = newCombo;
            GameEvents.RaiseComboChanged(Combo);
        }

        int multiplier = Combo > 0 ? Combo + 1 : 1;
        int earned     = basePoints * multiplier;
        Score         += earned;

        GameEvents.RaiseFruitSliced(earned, Score);
        Debug.Log($"[GameManager] Slice registered. +{earned} pts (x{multiplier}). Total: {Score}. Combo: {Combo}");
    }

    /// <summary>
    /// Call this when a fruit exits the play area without being sliced.
    /// Breaks the combo, decrements lives, and fires OnFruitMissed.
    /// Triggers OnGameOver if lives reach zero.
    /// </summary>
    public void RegisterMiss()
    {
        if (!IsGameActive) return;

        // Break combo
        _consecutiveSlices = 0;
        if (Combo != 0)
        {
            Combo = 0;
            GameEvents.RaiseComboChanged(Combo);
        }

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
