using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float speed = 5f;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private InputAction moveAction;
    private Vector2 moveInput;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        moveAction = InputSystem.actions.FindAction("Move");
    }

    void Update()
    {
        moveInput = moveAction.ReadValue<Vector2>();

        if (moveInput.x != 0f)
        {
            spriteRenderer.flipX = moveInput.x < 0f;
        }
    }

    void FixedUpdate()
    {
        rb.linearVelocity = moveInput * speed;
    }
}