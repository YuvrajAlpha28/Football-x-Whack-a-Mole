using System.Collections;
using UnityEngine;

public class MoleGameManager : MonoBehaviour
{
    [Header("Six Holes")]
    [SerializeField] private MoleHole[] holes = new MoleHole[6];

    [Header("Difficulty Settings")]
    [SerializeField] private GameSessionManager sessionManager;
    [SerializeField] private float difficultyChangeTime = 30f;

    [Header("First 30 Seconds")]
    [SerializeField] private float minSpawnDelay = 0.5f;
    [SerializeField] private float maxSpawnDelay = 1.2f;
    [SerializeField] private float normalMoleTime = 2f;
    [SerializeField] private float fastMoleTime = 0.8f;
    [SerializeField, Range(0f, 1f)] private float fastMoleChance = 0.3f;

    [Header("After 30 Seconds")]
    [SerializeField] private float hardNormalMoleTime = 1.2f;
    [SerializeField] private float hardFastMoleTime = 0.5f;
    [SerializeField, Range(0f, 1f)] private float hardFastMoleChance = 0.7f;

    [Header("Score UI")]
    [SerializeField] private MoleScoreUI scoreUI;

    private Coroutine spawnLoop;
    private int previousHole = -1;
    private int currentScore = 0;

    private void Start()
    {
        for (int i = 0; i < holes.Length; i++)
        {
            if (holes[i] != null)
            {
                holes[i].Initialize(this);
                Debug.Log("Registered mole hole: " + holes[i].name);
            }
            else
            {
                Debug.LogError("Mole hole missing at index: " + i);
            }
        }

        if (sessionManager == null)
            Debug.LogError("Game Session Manager is not assigned!");

        if (scoreUI == null)
            Debug.LogError("Score UI is not assigned!");

        spawnLoop = StartCoroutine(SpawnLoop());
    }

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(
                Random.Range(minSpawnDelay, maxSpawnDelay));

            if (sessionManager == null || !sessionManager.GameRunning)
                continue;

            bool hardMode = sessionManager.ElapsedTime >= difficultyChangeTime;

            float currentFastChance = hardMode
                ? hardFastMoleChance
                : fastMoleChance;

            float currentNormalTime = hardMode
                ? hardNormalMoleTime
                : normalMoleTime;

            float currentFastTime = hardMode
                ? hardFastMoleTime
                : fastMoleTime;

            int holeIndex = GetNextHoleIndex();

            if (holeIndex == -1)
                continue;

            bool isFast = Random.value < currentFastChance;

            float duration = isFast
                ? currentFastTime
                : currentNormalTime;

            Debug.Log(hardMode
                ? "HARD MODE: " + (isFast ? "FAST MOLE" : "NORMAL MOLE")
                : "NORMAL MODE: " + (isFast ? "FAST MOLE" : "NORMAL MOLE"));

            holes[holeIndex].ShowMole(isFast, duration);

            if (scoreUI != null)
            {
                scoreUI.ShowStatus(
                    isFast ? "FAST MOLE!" : "NORMAL MOLE!");
            }
        }
    }

    private int GetNextHoleIndex()
    {
        int[] candidates = new int[holes.Length];
        int count = 0;

        for (int i = 0; i < holes.Length; i++)
        {
            if (holes[i] != null &&
                !holes[i].IsVisible &&
                i != previousHole)
            {
                candidates[count++] = i;
            }
        }

        if (count == 0)
        {
            for (int i = 0; i < holes.Length; i++)
            {
                if (holes[i] != null && !holes[i].IsVisible)
                    candidates[count++] = i;
            }
        }

        if (count == 0)
            return -1;

        int chosen = candidates[Random.Range(0, count)];

        previousHole = chosen;

        return chosen;
    }

    public void OnMoleHit(int points, bool fast)
    {
        currentScore += points;

        Debug.Log("MOLE HIT REGISTERED!");
        Debug.Log("Points added: " + points);
        Debug.Log("TOTAL SCORE: " + currentScore);

        if (scoreUI != null)
            scoreUI.AddPoints(points, fast);
    }

    public void OnMoleMiss()
    {
        Debug.Log("MOLE MISSED!");

        if (scoreUI != null)
            scoreUI.ShowMiss();
    }

    private void OnDisable()
    {
        if (spawnLoop != null)
        {
            StopCoroutine(spawnLoop);
            spawnLoop = null;
        }

        if (holes != null)
        {
            foreach (MoleHole hole in holes)
            {
                if (hole != null)
                    hole.HideImmediately();
            }
        }
    }
}