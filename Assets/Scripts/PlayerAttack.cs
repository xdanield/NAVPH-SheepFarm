using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    [SerializeField] private int damage = 10;
    [SerializeField] private float cooldown = 2f;

    [Header("Hit area")]
    [SerializeField] private float attackRadius = 0.4f;
    [SerializeField] private float attackDistance = 0.5f;
    [SerializeField] private float attackHeight = 0.6f;
    [SerializeField] private LayerMask enemyLayers;

    private InputAction attackAction;
    private PlayerMovement movement;
    private float cooldownTimer;

    void Start()
    {
        attackAction = InputSystem.actions.FindAction("Attack");
        movement = GetComponent<PlayerMovement>();
    }

    void Update()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            return;
        }

        if (Time.timeScale == 0f) return;

        if (attackAction.WasPressedThisFrame())
        {
            Attack();
            cooldownTimer = cooldown;
        }
    }

    private void Attack()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(GetHitCenter(), attackRadius, enemyLayers);

        HashSet<Health> alreadyHit = new HashSet<Health>();
        foreach (Collider2D hit in hits)
        {
            Health health = hit.GetComponentInParent<Health>();
            if (health != null && alreadyHit.Add(health))
            {
                health.TakeDamage(damage);
            }
        }
    }

    private Vector2 GetHitCenter()
    {
        Vector2 facing = movement != null ? movement.facingDirection : Vector2.right;
        Vector2 body = (Vector2)transform.position + Vector2.up * attackHeight;
        return body + facing * attackDistance;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(GetHitCenter(), attackRadius);
    }
}