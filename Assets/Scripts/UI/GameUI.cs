using UnityEngine;
using TMPro;

/// <summary>
/// World-space floating UI for the VR Slash game.
///
/// Three panels swap based on game state:
///   • Title    — "VR SLASH" + press A/X prompt, shown at launch
///   • HUD      — score, lives (hearts), and combo multiplier during play
///   • GameOver — final score + press A/X to restart
///
/// The canvas is repositioned in front of the player on every panel transition
/// (title / game-over moments when the player is standing still), then stays
/// fixed in world space during gameplay so it does not float around mid-swing.
///
/// VR canvas rules observed:
///   • renderMode = WorldSpace — canvas is NEVER parented to the camera.
///   • Position derived from centerEyeAnchor; forward projected onto the
///     horizontal plane so the canvas is always upright regardless of head tilt.
///   • All text uses TextMeshProUGUI — no legacy UI.Text.
/// </summary>
public class GameUI : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("CenterEyeAnchor from OVRCameraRig/TrackingSpace — drives canvas placement.")]
    [SerializeField] private Transform centerEyeAnchor;

    [Header("Canvas Positioning")]
    [Tooltip("Metres in front of the player when a panel is shown.")]
    [SerializeField] private float canvasDistance = 2.0f;
    [Tooltip("Vertical offset from eye level (metres). Negative = slightly below eye.")]
    [SerializeField] private float heightOffset   = -0.1f;

    [Header("Panels")]
    [SerializeField] private GameObject titlePanel;
    [SerializeField] private GameObject hudPanel;
    [SerializeField] private GameObject gameOverPanel;

    [Header("Title")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text titlePromptText;

    [Header("HUD")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text livesText;
    [SerializeField] private TMP_Text comboText;

    [Header("Game Over")]
    [SerializeField] private TMP_Text gameOverScoreText;
    [SerializeField] private TMP_Text gameOverPromptText;

    // ── Private state ─────────────────────────────────────────────────────────

    private enum UIState { Title, HUD, GameOver }
    private UIState _state = UIState.Title;

    private int _score;
    private int _lives;
    private int _combo;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        GameEvents.OnGameStart    += HandleGameStart;
        GameEvents.OnGameOver     += HandleGameOver;
        GameEvents.OnFruitSliced  += HandleFruitSliced;
        GameEvents.OnFruitMissed  += HandleFruitMissed;
        GameEvents.OnComboChanged += HandleComboChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStart    -= HandleGameStart;
        GameEvents.OnGameOver     -= HandleGameOver;
        GameEvents.OnFruitSliced  -= HandleFruitSliced;
        GameEvents.OnFruitMissed  -= HandleFruitMissed;
        GameEvents.OnComboChanged -= HandleComboChanged;
    }

    private void Start() => ShowTitle();

    private void Update()
    {
        if (_state != UIState.Title && _state != UIState.GameOver) return;

        // Reposition every frame on title/gameover screens so the canvas is always
        // in front of the player. This handles OVR tracking not being ready when
        // Start() fires — the canvas catches up as soon as tracking settles.
        // During gameplay (HUD state) the canvas stays fixed so it won't drift mid-swing.
        PositionCanvas();

        bool pressed =
            OVRInput.GetDown(OVRInput.Button.One)    // A — right Touch controller
         || OVRInput.GetDown(OVRInput.Button.Three)  // X — left Touch controller
#if UNITY_EDITOR
         || Input.GetKeyDown(KeyCode.Space)          // Space — editor testing shortcut
#endif
         ;

        if (pressed)
            GameManager.Instance?.StartGame();
    }

    // ── Panel transitions ─────────────────────────────────────────────────────

    private void ShowTitle()
    {
        _state = UIState.Title;
        PositionCanvas();
        SetPanels(title: true, hud: false, gameOver: false);
    }

    private void ShowHUD()
    {
        _state = UIState.HUD;
        PositionCanvas();
        SetPanels(title: false, hud: true, gameOver: false);
        RefreshHUD();
    }

    private void ShowGameOver()
    {
        _state = UIState.GameOver;
        PositionCanvas();
        SetPanels(title: false, hud: false, gameOver: true);
        if (gameOverScoreText != null)
            gameOverScoreText.text = "Score: " + _score;
    }

    private void SetPanels(bool title, bool hud, bool gameOver)
    {
        if (titlePanel    != null) titlePanel.SetActive(title);
        if (hudPanel      != null) hudPanel.SetActive(hud);
        if (gameOverPanel != null) gameOverPanel.SetActive(gameOver);
    }

    // ── Canvas positioning ────────────────────────────────────────────────────

    private void PositionCanvas()
    {
        if (centerEyeAnchor == null) return;

        // Flatten forward to horizontal plane — canvas stays upright even when
        // the player looks up or down.
        Vector3 forward = Vector3.ProjectOnPlane(centerEyeAnchor.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward; // safety: looking straight up/down

        transform.position = centerEyeAnchor.position
                           + forward    * canvasDistance
                           + Vector3.up * heightOffset;

        transform.rotation = Quaternion.LookRotation(forward);
    }

    // ── Game event handlers ───────────────────────────────────────────────────

    private void HandleGameStart()
    {
        _score = 0;
        _lives = GameManager.Instance != null ? GameManager.Instance.Lives : 3;
        _combo = 0;
        ShowHUD();
    }

    private void HandleGameOver() => ShowGameOver();

    private void HandleFruitSliced(int scoreEarned, int totalScore)
    {
        _score = totalScore;
        RefreshHUD();
    }

    private void HandleFruitMissed(int livesRemaining)
    {
        _lives = livesRemaining;
        RefreshHUD();
    }

    private void HandleComboChanged(int combo)
    {
        _combo = combo;
        RefreshHUD();
    }

    // ── HUD refresh ───────────────────────────────────────────────────────────

    private void RefreshHUD()
    {
        if (scoreText != null)
            scoreText.text = "Score\n" + _score;

        if (livesText != null)
            livesText.text = BuildLivesString(_lives);

        if (comboText != null)
        {
            bool active = _combo > 0;
            comboText.gameObject.SetActive(active);
            if (active)
                comboText.text = "x" + (_combo + 1) + "\nCOMBO";
        }

        // World-space canvases don't rebuild automatically every frame — they wait
        // for a scene-change event (e.g. a new renderer appearing when a fruit
        // spawns). Force an immediate rebuild so HUD changes are visible right away.
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>Renders lives as heart symbols — faster to read at a glance in VR
    /// than a number.</summary>
    private static string BuildLivesString(int lives)
    {
        var sb = new System.Text.StringBuilder("Lives\n");
        for (int i = 0; i < Mathf.Max(0, lives); i++)
            sb.Append("♥ ");
        if (lives <= 0)
            sb.Append("—");
        return sb.ToString().TrimEnd();
    }
}
