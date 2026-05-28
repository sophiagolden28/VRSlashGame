using System;

/// <summary>
/// Static event bus for decoupled communication between all game systems.
/// Subscribe in OnEnable, unsubscribe in OnDisable.
/// </summary>
public static class GameEvents
{
    // ── Game Flow ────────────────────────────────────────────────────────────

    /// <summary>Fired when a new game session begins.</summary>
    public static event Action OnGameStart;

    /// <summary>Fired when the player runs out of lives.</summary>
    public static event Action OnGameOver;

    // ── Scoring ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when a fruit is successfully sliced.
    /// </summary>
    /// <param name="scoreEarned">Points earned for this slice (combo multiplier already applied).</param>
    /// <param name="totalScore">Running total score after this slice.</param>
    public static event Action<int, int> OnFruitSliced;

    /// <summary>
    /// Fired when a fruit passes the player without being sliced.
    /// </summary>
    /// <param name="livesRemaining">Lives remaining after this miss.</param>
    public static event Action<int> OnFruitMissed;

    // ── Combo ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired whenever the combo count changes (increase or reset to 0).
    /// </summary>
    /// <param name="comboCount">New combo count. 0 means the combo was broken.</param>
    public static event Action<int> OnComboChanged;

    // ── Raise helpers ────────────────────────────────────────────────────────

    public static void RaiseGameStart()
        => OnGameStart?.Invoke();

    public static void RaiseGameOver()
        => OnGameOver?.Invoke();

    public static void RaiseFruitSliced(int scoreEarned, int totalScore)
        => OnFruitSliced?.Invoke(scoreEarned, totalScore);

    public static void RaiseFruitMissed(int livesRemaining)
        => OnFruitMissed?.Invoke(livesRemaining);

    public static void RaiseComboChanged(int comboCount)
        => OnComboChanged?.Invoke(comboCount);
}
