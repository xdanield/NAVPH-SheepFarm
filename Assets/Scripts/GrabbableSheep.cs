using System.Collections.Generic;
using UnityEngine;

public class GrabbableSheep : MonoBehaviour
{
    [SerializeField] private int holdersNeeded = 2;

    private readonly List<ZombieAI> holders = new List<ZombieAI>();
    private SheepMovement movement;
    private Rigidbody2D rb;
    private Animator animator;

    public int HolderCount => holders.Count;
    public bool IsHeld => holders.Count > 0;
    public bool IsFull => holders.Count >= holdersNeeded;

    void Awake()
    {
        movement = GetComponent<SheepMovement>();
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
    }

    public bool TryGrab(ZombieAI zombie)
    {
        if (IsFull || holders.Contains(zombie)) return false;

        holders.Add(zombie);
        UpdateHeldState();
        return true;
    }

    public void Release(ZombieAI zombie)
    {
        if (holders.Remove(zombie))
        {
            UpdateHeldState();
        }
    }

    private void UpdateHeldState()
    {
        movement.enabled = !IsHeld;

        if (IsHeld)
        {
            rb.linearVelocity = Vector2.zero;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
            animator.SetBool("IsMoving", false);
        }
        else
        {
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
    }
}