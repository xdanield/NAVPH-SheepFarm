using UnityEngine;

public class ZombieAttack : MonoBehaviour
{
    [SerializeField] private int buildingDamage = 1;
    [SerializeField] private int playerDamage = 1;
    [SerializeField] private float range = 1f;
    [SerializeField] private float cooldown = 1f;

    private Health playerHealth;
    private float cooldownTimer;

    void Start()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            playerHealth = player.GetComponent<Health>();
        }
    }

    void Update()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            return;
        }

        if (playerHealth == null || playerHealth.IsDead) return;

        float distance = Vector2.Distance(transform.position, playerHealth.transform.position);
        if (distance <= range)
        {
            playerHealth.TakeDamage(playerDamage);
            cooldownTimer = cooldown;
        }
    }
}