using UnityEngine;

public class PlayerDeath : MonoBehaviour
{
    private Health health;
    private bool gameOver;

    void Awake()
    {
        health = GetComponent<Health>();
    }

    void OnEnable()
    {
        health.Died += OnDied;
    }

    void OnDisable()
    {
        health.Died -= OnDied;
    }

    private void OnDied()
    {
        gameOver = true;
        Time.timeScale = 0f;
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 30, 400, 20), $"Hearts: {health.CurrentHealth}/{health.Max}");

        if (gameOver)
        {
            GUI.Label(new Rect(10, 50, 400, 20), "GAME OVER");
        }
    }
}