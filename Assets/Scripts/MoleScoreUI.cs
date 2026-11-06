using TMPro;
using UnityEngine;

public class MoleScoreUI : MonoBehaviour
{
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text statusText;

    [Header("Streak Settings")]
    [SerializeField] private int hitsForDoubleScore = 3;
    [SerializeField] private int streakMultiplier = 2;

    private int score = 0;
    private int currentStreak = 0;

    public int Score
    {
        get { return score; }
    }

    private void Awake()
    {
        RefreshScore();

        if (statusText != null)
            statusText.text = "";
    }

    public void AddPoints(int points, bool fast)
    {
        currentStreak++;

        int multiplier = 1;

        if (currentStreak >= hitsForDoubleScore)
            multiplier = streakMultiplier;

        int earnedPoints = points * multiplier;

        score += earnedPoints;

        RefreshScore();

        if (statusText != null)
        {
            if (currentStreak == hitsForDoubleScore)
                statusText.text = "HAT-TRICK! x2\n+" + earnedPoints + " POINTS!";
            else if (currentStreak > hitsForDoubleScore)
                statusText.text = "STREAK x2!\n+" + earnedPoints + " POINTS!";
            else
                statusText.text = "+" + earnedPoints + " HIT!";
        }
    }

    public void ShowMiss()
    {
        currentStreak = 0;

        if (statusText != null)
            statusText.text = "MISS!";
    }

    public void ShowStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    public void ResetScore()
    {
        score = 0;
        currentStreak = 0;

        RefreshScore();

        if (statusText != null)
            statusText.text = "";
    }

    private void RefreshScore()
    {
        if (scoreText != null)
            scoreText.text = "SCORE: " + score;
    }
}