using UnityEngine;

public class DestroyOnDeath : MonoBehaviour
{
    private Health health;

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
        Destroy(gameObject);
    }
}