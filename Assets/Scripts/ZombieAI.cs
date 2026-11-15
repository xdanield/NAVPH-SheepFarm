using UnityEngine;

public class ZombieAI : MonoBehaviour
{
    private enum State { WalkRight, ChasePlayer, AttackObstacle }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float laneSteering = 0.5f;

    [Header("Player")]
    [SerializeField] private float aggroRange = 2f;
    [SerializeField] private float giveUpRange = 3.5f;
    [SerializeField] private float stopDistance = 0.6f;

    [Header("Obstacles")]
    [SerializeField] private float blockCheckDistance = 0.6f;
    [SerializeField] private float blockCheckRadius = 0.25f;
    [SerializeField] private float feetHeight = 0.15f;
    [SerializeField] private LayerMask obstacleLayers;

    private State state = State.WalkRight;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private ZombieAttack attack;
    private Transform player;
    private float[] laneYs;
    private Vector2 moveDirection;
    private Health currentObstacle;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        attack = GetComponent<ZombieAttack>();

        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject != null) player = playerObject.transform;

        GameObject[] lanes = GameObject.FindGameObjectsWithTag("ZombieLane");
        laneYs = new float[lanes.Length];
        for (int i = 0; i < lanes.Length; i++)
        {
            laneYs[i] = lanes[i].transform.position.y;
        }
    }

    void Update()
    {
        Think();

        if (state == State.AttackObstacle)
        {
            attack.Attack(currentObstacle);
        }

        if (moveDirection.x != 0f)
        {
            spriteRenderer.flipX = moveDirection.x < 0f;
        }
    }

    void FixedUpdate()
    {
        bool standStill = state == State.AttackObstacle || moveDirection == Vector2.zero;
        rb.linearVelocity = standStill ? Vector2.zero : moveDirection * moveSpeed;
    }

    private void Think()
    {
        // 1. Player close? Otherwise walk right.
        if (PlayerIsClose())
        {
            state = State.ChasePlayer;
            moveDirection = GetChasePlayerDirection();
        }
        else
        {
            state = State.WalkRight;
            moveDirection = GetWalkRightDirection();
        }

        // 2. Something with HP in the way? Attack it instead of moving.
        if (moveDirection == Vector2.zero) return;

        Health blocker = FindBlocker(moveDirection);
        if (blocker != null)
        {
            state = State.AttackObstacle;
            currentObstacle = blocker;
        }
    }

    private bool PlayerIsClose()
    {
        if (player == null) return false;

        float distance = Vector2.Distance(transform.position, player.position);
        float limit = state == State.ChasePlayer ? giveUpRange : aggroRange;
        return distance <= limit;
    }

    private Vector2 GetChasePlayerDirection()
    {
        Vector2 toPlayer = (Vector2)player.position - (Vector2)transform.position;
        if (toPlayer.magnitude <= stopDistance) return Vector2.zero;
        return toPlayer.normalized;
    }

    private Vector2 GetWalkRightDirection()
    {
        if (laneYs.Length == 0) return Vector2.right;

        float y = transform.position.y;
        float nearestLane = laneYs[0];
        foreach (float laneY in laneYs)
        {
            if (Mathf.Abs(laneY - y) < Mathf.Abs(nearestLane - y))
            {
                nearestLane = laneY;
            }
        }

        float vertical = Mathf.Clamp(nearestLane - y, -1f, 1f) * laneSteering;
        return new Vector2(1f, vertical).normalized;
    }

    private Health FindBlocker(Vector2 direction)
    {
        Collider2D hit = Physics2D.OverlapCircle(GetBlockCheckPoint(direction), blockCheckRadius, obstacleLayers);
        if (hit == null) return null;

        Health health = hit.GetComponentInParent<Health>();
        if (health == null || health.IsDead) return null;
        return health;
    }

    private Vector2 GetBlockCheckPoint(Vector2 direction)
    {
        Vector2 feet = (Vector2)transform.position + Vector2.up * feetHeight;
        return feet + direction * blockCheckDistance;
    }

    void OnDrawGizmosSelected()
    {
        Vector2 direction = Application.isPlaying ? moveDirection : Vector2.right;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(GetBlockCheckPoint(direction), blockCheckRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, aggroRange);
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, giveUpRange);
    }
}