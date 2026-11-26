using UnityEngine;

public class ZombieAI : MonoBehaviour
{
    private enum State { WalkRight, ChasePlayer, AttackObstacle, ChaseSheep, HoldSheep }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float laneSteering = 0.5f;

    [Header("Player")]
    [SerializeField] private float aggroRange = 2f;
    [SerializeField] private float giveUpRange = 3.5f;
    [SerializeField] private float stopDistance = 0.6f;

    [Header("Sheep")]
    [SerializeField] private float sheepSightRange = 5f;
    [SerializeField] private float grabRange = 1f;
    [SerializeField] private LayerMask sheepLayers;

    [Header("Obstacles")]
    [SerializeField] private float blockCheckDistance = 0.6f;
    [SerializeField] private float blockCheckRadius = 0.25f;
    [SerializeField] private float feetHeight = 0.15f;
    [SerializeField] private LayerMask obstacleLayers;

    [Header("Crowd")]
    [SerializeField] private float separationRadius = 0.8f;
    [SerializeField] private float separationStrength = 1f;
    [SerializeField] private LayerMask zombieLayers;
    [SerializeField] private float speedVariation = 0.1f;
    [SerializeField] private float laneOffsetRange = 0.3f;

    private State state = State.WalkRight;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private ZombieAttack attack;
    private Transform player;
    private LaneLayout laneLayout;
    private Vector2 moveDirection;
    private Health currentObstacle;
    private GrabbableSheep heldSheep;
    private Vector2 separation;
    private float laneOffset;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        attack = GetComponent<ZombieAttack>();

        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject != null) player = playerObject.transform;

        laneLayout = FindFirstObjectByType<LaneLayout>();

        moveSpeed *= Random.Range(1f - speedVariation, 1f + speedVariation);
        laneOffset = Random.Range(-laneOffsetRange, laneOffsetRange);
    }

    void Update()
    {
        Think();
        separation = GetSeparation();

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
        Vector2 velocity = standStill ? Vector2.zero : moveDirection * moveSpeed;
        velocity += separation * separationStrength;
        rb.linearVelocity = velocity;
    }

    void OnDisable()
    {
        if (heldSheep != null)
        {
            heldSheep.Release(this);
            heldSheep = null;
        }
    }

    private void Think()
    {
        // 1. Holding a sheep? Stay put.
        if (heldSheep != null)
        {
            state = State.HoldSheep;
            moveDirection = Vector2.zero;
            return;
        }

        // 2. Player close?
        if (PlayerIsClose())
        {
            state = State.ChasePlayer;
            moveDirection = GetChasePlayerDirection();
        }
        else
        {
            // 3. Sheep in sight?
            GrabbableSheep sheep = FindNearestSheep();
            if (sheep != null)
            {
                if (TryGrabSheep(sheep))
                {
                    state = State.HoldSheep;
                    moveDirection = Vector2.zero;
                    return;
                }

                state = State.ChaseSheep;
                moveDirection = ((Vector2)sheep.transform.position - (Vector2)transform.position).normalized;
            }
            else
            {
                // 4. Otherwise walk right.
                state = State.WalkRight;
                moveDirection = GetWalkRightDirection();
            }
        }

        // Something with HP in the way? Attack it instead of moving.
        if (moveDirection == Vector2.zero) return;

        Health blocker = FindBlocker(moveDirection);
        if (blocker != null)
        {
            state = State.AttackObstacle;
            currentObstacle = blocker;
        }
    }

    private GrabbableSheep FindNearestSheep()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, sheepSightRange, sheepLayers);

        GrabbableSheep nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (Collider2D hit in hits)
        {
            GrabbableSheep sheep = hit.GetComponentInParent<GrabbableSheep>();
            if (sheep == null || sheep.IsFull) continue;

            float distance = Vector2.Distance(transform.position, sheep.transform.position);
            if (distance < nearestDistance)
            {
                nearest = sheep;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    private bool TryGrabSheep(GrabbableSheep sheep)
    {
        float distance = Vector2.Distance(transform.position, sheep.transform.position);
        if (distance > grabRange) return false;
        if (!sheep.TryGrab(this)) return false;

        heldSheep = sheep;
        return true;
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
        if (laneLayout == null) return Vector2.right;

        float y = transform.position.y;
        float targetY = laneLayout.NearestLaneY(y) + laneOffset;
        float vertical = Mathf.Clamp(targetY - y, -1f, 1f) * laneSteering;
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

    private Vector2 GetSeparation()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, separationRadius, zombieLayers);

        Vector2 push = Vector2.zero;
        foreach (Collider2D hit in hits)
        {
            if (hit.attachedRigidbody == rb) continue;

            Vector2 away = rb.position - hit.attachedRigidbody.position;
            float distance = away.magnitude;
            if (distance < 0.01f)
            {
                away = Random.insideUnitCircle;
                distance = 0.01f;
            }

            push += away.normalized * (1f - distance / separationRadius);
        }

        return Vector2.ClampMagnitude(push, 1f);
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
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, sheepSightRange);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, separationRadius);
    }
}