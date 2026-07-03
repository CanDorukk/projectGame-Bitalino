using UnityEngine;

/// <summary>
/// Start Game button handler. Panel visibility is owned by GameManager.
/// </summary>
public class EMGStartMenuUI : MonoBehaviour
{
    public GameManager gameManager;

    public void OnStartButtonPressed()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.instance;
        }

        if (gameManager != null)
        {
            gameManager.BeginGameFromStartMenu();
        }
    }
}
