using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameSessionManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private GameObject endPanel;

    [Header("Gameplay References")]
    [SerializeField] private Behaviour swipeInput;
    [SerializeField] private FootballController footballController;
    [SerializeField] private MoleScoreUI scoreUI;

    [Header("Timer")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField, Min(1f)] private float gameDuration = 60f;

    [Header("End Screen")]
    [SerializeField] private TMP_Text finalScoreText;
    [SerializeField] private Button startButton;
    [SerializeField] private Button restartButton;

    private float timeRemaining;
    private bool gameRunning;
    private bool gameEnded;

    public float ElapsedTime
    {
        get { return gameDuration - timeRemaining; }
    }

    public bool GameRunning
    {
        get { return gameRunning && !gameEnded; }
    }

    private void Awake()
    {
        Time.timeScale = 0f;
        timeRemaining = gameDuration;

        if (startPanel != null) startPanel.SetActive(true);
        if (endPanel != null) endPanel.SetActive(false);
        if (swipeInput != null) swipeInput.enabled = false;

        if (startButton != null) startButton.onClick.AddListener(StartGame);
        if (restartButton != null) restartButton.onClick.AddListener(RestartGame);

        RefreshTimerText();
    }

    private void Update()
    {
        if (!gameRunning || gameEnded) return;

        timeRemaining -= Time.unscaledDeltaTime;

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            RefreshTimerText();
            EndGame();
            return;
        }

        RefreshTimerText();
    }

    public void StartGame()
    {
        if (gameRunning || gameEnded) return;

        if (startPanel != null) startPanel.SetActive(false);
        if (endPanel != null) endPanel.SetActive(false);

        timeRemaining = gameDuration;
        RefreshTimerText();

        if (scoreUI != null) scoreUI.ResetScore();
        if (footballController != null) footballController.ResetBall();
        if (swipeInput != null) swipeInput.enabled = true;

        gameRunning = true;
        Time.timeScale = 1f;
    }

    private void EndGame()
    {
        if (gameEnded) return;

        gameRunning = false;
        gameEnded = true;

        if (swipeInput != null) swipeInput.enabled = false;

        int finalScore = scoreUI != null ? scoreUI.Score : 0;

        if (finalScoreText != null)
            finalScoreText.text = "TOTAL SCORE: " + finalScore;

        if (endPanel != null) endPanel.SetActive(true);

        Time.timeScale = 0f;
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void RefreshTimerText()
    {
        if (timerText == null) return;

        timerText.text = "TIME: " +
            Mathf.CeilToInt(Mathf.Max(0f, timeRemaining));
    }

    private void OnDestroy()
    {
        Time.timeScale = 1f;

        if (startButton != null) startButton.onClick.RemoveListener(StartGame);
        if (restartButton != null) restartButton.onClick.RemoveListener(RestartGame);
    }
}