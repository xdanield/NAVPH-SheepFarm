using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float speed = 5f;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private InputAction moveAction;
    private Vector2 moveInput;
    private Animator animator;
    public Vector2 facingDirection = Vector2.down;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        moveAction = InputSystem.actions.FindAction("Move");
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        moveInput = moveAction.ReadValue<Vector2>();

        if (moveInput != Vector2.zero)
        {
            facingDirection = moveInput.normalized;
        }

        if (moveInput.x != 0f)
        {
            spriteRenderer.flipX = moveInput.x < 0f;
        }

        bool isMoving = moveInput != Vector2.zero;
        animator.SetBool("IsMoving", isMoving);
    }

    void FixedUpdate()
    {
        rb.linearVelocity = moveInput * speed;
    }
}