using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;
    private InGameRanking ig;
    public EMGInputBridge inputBridge;
    public GameObject pausePanel;
    public GameObject startGamePanel;

    private GameObject[] runners;
    List<RankingSystem> sortArray = new();

    public bool isGameOver = false;

    private bool isGameStarted = false;
    private bool isPaused = false;
    private bool waitingForStartMenu = false;
    private bool useEmgSessionFlow = false;

    public bool IsGameStarted => isGameStarted;
    public bool IsWaitingToStart => waitingForStartMenu;

    private void Awake()
    {
        instance = this;
        runners = GameObject.FindGameObjectsWithTag("Runner");
        ig = FindObjectOfType<InGameRanking>();
    }

    void Start()
    {
        isPaused = false;
        isGameOver = false;
        isGameStarted = false;

        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }

        if (startGamePanel != null)
        {
            startGamePanel.SetActive(false);
        }

        for (int i = 0; i < runners.Length; i++)
        {
            sortArray.Add(runners[i].GetComponent<RankingSystem>());
        }

        if (startGamePanel == null)
        {
            GameObject found = GameObject.Find("StartGamePanel");
            if (found != null)
            {
                startGamePanel = found;
            }
        }

        useEmgSessionFlow = inputBridge != null;
        if (useEmgSessionFlow)
        {
            EnterPreGameFreeze();
            waitingForStartMenu = false;
        }
        else
        {
            Time.timeScale = 1f;
        }
    }

    [System.Obsolete]
    void Update()
    {
        if (useEmgSessionFlow)
        {
            UpdateEmgSessionFlow();
            if (!isGameStarted)
            {
                return;
            }
        }

        HandlePauseInput();

        if (isGameStarted)
        {
            CalculateRank();
        }

    }

    private void UpdateEmgSessionFlow()
    {
        if (inputBridge == null)
        {
            return;
        }

        if (!inputBridge.IsReadyForStartMenu)
        {
            if (waitingForStartMenu && startGamePanel != null)
            {
                startGamePanel.SetActive(false);
            }
            waitingForStartMenu = false;
            EnterPreGameFreeze();
            return;
        }

        if (!waitingForStartMenu && !isGameStarted)
        {
            ShowStartMenu();
        }
    }

    private void EnterPreGameFreeze()
    {
        Time.timeScale = 0f;
        FreezeAllRunners();
    }

    private void ShowStartMenu()
    {
        waitingForStartMenu = true;
        Time.timeScale = 0f;
        FreezeAllRunners();

        if (startGamePanel != null)
        {
            startGamePanel.SetActive(true);
            startGamePanel.transform.SetAsLastSibling();
        }
        else
        {
            Debug.LogWarning("GameManager: startGamePanel not assigned. Run EMG > Setup EMG UI.");
        }
    }

    public void BeginGameFromStartMenu()
    {
        if (isGameStarted)
        {
            return;
        }

        if (useEmgSessionFlow && inputBridge != null && !inputBridge.IsGameInputAllowed)
        {
            return;
        }

        waitingForStartMenu = false;

        if (startGamePanel != null)
        {
            startGamePanel.SetActive(false);
        }

        Time.timeScale = 1f;
        isPaused = false;

        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }

        StartGame();
    }

    private void FreezeAllRunners()
    {
        foreach (GameObject runner in runners)
        {
            PlayerController player = runner.GetComponent<PlayerController>();
            if (player != null)
            {
                player.StopRunning();
                continue;
            }

            Opponent opponent = runner.GetComponent<Opponent>();
            if (opponent != null)
            {
                opponent.StopRunning();
            }
        }
    }

    private void HandlePauseInput()
    {
        if (isGameOver || !isGameStarted || waitingForStartMenu)
        {
            return;
        }

        bool pauseToggleRequested = Input.GetKeyDown(KeyCode.Escape);
        if (inputBridge != null && inputBridge.ConsumePauseToggleSignal())
        {
            pauseToggleRequested = true;
        }

        if (pauseToggleRequested)
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        if (!isGameStarted || waitingForStartMenu)
        {
            return;
        }

        isPaused = !isPaused;
        Time.timeScale = isPaused ? 0f : 1f;

        if (pausePanel != null)
        {
            pausePanel.SetActive(isPaused);
        }
    }

    public void ResumeGame()
    {
        if (!isGameStarted || !isPaused)
        {
            return;
        }

        isPaused = false;
        Time.timeScale = 1f;

        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }
    }

    void CalculateRank()
    {
        sortArray = sortArray.OrderBy(x => x.distance).ToList();
        sortArray[0].rank = 1;
        sortArray[1].rank = 2;
        sortArray[2].rank = 3;
        sortArray[3].rank = 4;
        sortArray[4].rank = 5;
        sortArray[5].rank = 6;
        sortArray[6].rank = 7;

        ig.a = sortArray[6].name;
        ig.b = sortArray[5].name;
        ig.c = sortArray[4].name;
        ig.d = sortArray[3].name;
        ig.e = sortArray[2].name;
        ig.f = sortArray[1].name;
        ig.g = sortArray[0].name;
    }

    [System.Obsolete]
    public void StartGame()
    {
        if (!isGameStarted)
        {
            isGameStarted = true;

            foreach (GameObject runner in runners)
            {
                PlayerController playerController = runner.GetComponent<PlayerController>();
                if (playerController != null)
                {
                    playerController.StartRunning();
                }
                else
                {
                    Opponent opponent = runner.GetComponent<Opponent>();
                    if (opponent != null)
                    {
                        opponent.StartRunning();
                    }
                }
            }
        }
    }
}
