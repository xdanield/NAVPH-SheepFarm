using System;
using UnityEngine;

public class Health : MonoBehaviour
{

    [SerializeField] private int maxHealth = 100;
    
    public int CurrentHealth { get; private set; }
    public int Max => maxHealth;
    public bool IsDead => CurrentHealth <= 0;

    public event Action<int> Damaged;
    public event Action Died;

    void Awake()
    {
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (IsDead || damage <= 0) return;

        CurrentHealth -= damage;
        if (CurrentHealth < 0)
            CurrentHealth = 0;

        Damaged?.Invoke(CurrentHealth);
        if (IsDead) Died?.Invoke();
    }
}
