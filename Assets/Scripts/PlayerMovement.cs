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
    private PlayerInteractor interactor;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        moveAction = InputSystem.actions.FindAction("Move");
        animator = GetComponent<Animator>();
        interactor = GetComponent<PlayerInteractor>();
    }

    void Update()
    {
        moveInput = moveAction.ReadValue<Vector2>();

        // Pocas zberu (drzanie E) hrac stoji.
        if (interactor != null && interactor.IsBusy)
        {
            moveInput = Vector2.zero;
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