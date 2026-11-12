using UnityEngine;

public class SheepMovement : MonoBehaviour
{
    [SerializeField] private float speed = 1f;
    [SerializeField] private float directionChangeInterval = 3f;
    [SerializeField] private float minIdleTime = 1f;
    [SerializeField] private float maxIdleTime = 3f;
    [SerializeField] private float collisionStunDuration = 0.5f;

    [SerializeField] private float minSoundDelay = 5f;
    [SerializeField] private float maxSoundDelay = 60f;
    private float soundTimer;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private Vector2 currentDirection;
    private AudioSource audioSource;
    private float directionChangeTimer;
    private float idleTimer;
    private bool isIdle;
    private float collisionStunTimer;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        audioSource = GetComponent<AudioSource>();

        float randomVolume = Random.Range(0.1f, 0.4f);
        float randomPitch = Random.Range(0.8f, 1.15f);
        bool mute = Random.value < 0.25f;

        audioSource.volume = mute ? 0f : randomVolume;
        audioSource.pitch = randomPitch;
        
        PickRandomDirection();
        directionChangeTimer = directionChangeInterval;
        soundTimer = Random.Range(minSoundDelay, maxSoundDelay);
    }

    void Update()
    {
        if (collisionStunTimer > 0)
        {
            collisionStunTimer -= Time.deltaTime;
            if (collisionStunTimer <= 0)
            {
                PickRandomDirection();
            }
            return;
        }

        if (isIdle)
        {
            idleTimer -= Time.deltaTime;
            if (idleTimer <= 0)
            {
                isIdle = false;
                PickRandomDirection();
                directionChangeTimer = directionChangeInterval;
            }
        }
        else
        {
            directionChangeTimer -= Time.deltaTime;
            if (directionChangeTimer <= 0)
            {
                if (Random.value < 0.3f)
                {
                    StartIdle();
                }
                else
                {
                    PickRandomDirection();
                    directionChangeTimer = directionChangeInterval;
                }
            }
        }

        UpdateAnimation();
        soundTimer -= Time.deltaTime;
        if (soundTimer <= 0)
        {
            audioSource.Play();
            soundTimer = Random.Range(minSoundDelay, maxSoundDelay);
        }
    }

    void FixedUpdate()
    {
        if (!isIdle && collisionStunTimer <= 0)
        {
            rb.linearVelocity = currentDirection * speed;
        }
        else
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    private void PickRandomDirection()
    {
        float angle = Random.Range(0f, 360f);
        currentDirection = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)).normalized;
        
        if (currentDirection.x != 0f)
        {
            spriteRenderer.flipX = currentDirection.x < 0f;
        }
    }

    private void StartIdle()
    {
        isIdle = true;
        idleTimer = Random.Range(minIdleTime, maxIdleTime);
    }

    private void UpdateAnimation()
    {
        bool isMoving = !isIdle && collisionStunTimer <= 0;
        animator.SetBool("IsMoving", isMoving);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("PenFence") || collision.gameObject.layer == LayerMask.NameToLayer("PenFence"))
        {
            collisionStunTimer = collisionStunDuration;
            rb.linearVelocity = Vector2.zero;
        }
    }
}