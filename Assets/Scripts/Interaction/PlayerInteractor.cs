using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hracova strana interakcie. Kazdy frame vyberie najblizsi Interactable v dosahu
/// (s prednostou toho, ku ktoremu je hrac otoceny) a riesi drzanie klavesu Interact (E).
///
/// Kym hrac drzi E nad surovinou, stoji na mieste a je otoceny k nej (IsBusy).
/// Ak ma Animator bool parameter "IsGathering", nastavuje sa pocas zberu (animacia zberu).
/// </summary>
[RequireComponent(typeof(Inventory))]
public class PlayerInteractor : MonoBehaviour
{
    [Tooltip("Dosah od noh hraca k okraju objektu (units). 1 = jedno policko.")]
    [SerializeField] private float range = 0.9f;
    [Tooltip("O kolko blizsie sa javi objekt, ku ktoremu je hrac otoceny.")]
    [SerializeField] private float facingBonus = 0.35f;
    [Tooltip("Stred noh hraca vzhladom na pivot (pivot je na spodku sprite).")]
    [SerializeField] private Vector2 feetOffset = new Vector2(0f, 0.15f);

    private const string GatherAnimParam = "IsGathering";

    private Inventory inventory;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private AudioSource audioSource;
    private InteractionUI ui;
    private InputAction interactAction;
    private bool hasGatherParam;

    private Interactable target;
    private float holdTime;
    private bool waitForRelease;
    private Vector2 facing = Vector2.down;

    public Inventory Inventory => inventory;
    public Interactable Target => target;
    public string BlockedReason { get; private set; }
    /// <summary>0..1 kym hrac drzi E, inak 0.</summary>
    public float HoldProgress => target != null && target.HoldDuration > 0f ? Mathf.Clamp01(holdTime / target.HoldDuration) : 0f;
    /// <summary>Hrac prave zbiera (PlayerMovement ho vtedy nepusti chodit).</summary>
    public bool IsBusy => holdTime > 0f;
    /// <summary>Nazov klavesu pre napovedu, napr. "E".</summary>
    public string KeyLabel { get; private set; } = "E";

    void Start()
    {
        inventory = GetComponent<Inventory>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        ui = GetComponent<InteractionUI>();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        interactAction = InputSystem.actions != null ? InputSystem.actions.FindAction("Interact") : null;
        if (interactAction != null)
        {
            string label = interactAction.GetBindingDisplayString(InputBinding.MaskByGroup("Keyboard&Mouse"));
            if (!string.IsNullOrEmpty(label)) KeyLabel = label;
        }

        hasGatherParam = animator != null && animator.runtimeAnimatorController != null &&
                         animator.parameters.Any(p => p.name == GatherAnimParam && p.type == AnimatorControllerParameterType.Bool);
    }

    void OnDisable()
    {
        CancelHold();
        target = null;
    }

    void Update()
    {
        // Otvoreny inventar = hra stoji, nic nezbierame.
        if (InventoryUI.IsOpen || Time.timeScale == 0f)
        {
            CancelHold();
            return;
        }

        if (rb != null && rb.linearVelocity.sqrMagnitude > 0.01f) facing = rb.linearVelocity.normalized;

        var best = FindTarget();
        if (best != target)
        {
            CancelHold();
            target = best;
        }

        BlockedReason = target != null ? target.BlockedReason(this) : null;

        bool pressed = IsInteractPressed();
        if (!pressed) waitForRelease = false;

        if (target == null || BlockedReason != null || !pressed || waitForRelease)
        {
            CancelHold();
            return;
        }

        if (target.HoldDuration <= 0f)
        {
            waitForRelease = true;
            target.Interact(this);
            return;
        }

        holdTime += Time.deltaTime;
        FaceTowards(target.transform.position);
        SetGatherAnim(true);
        target.OnHoldProgress(this, HoldProgress);

        if (holdTime >= target.HoldDuration)
        {
            var done = target;
            holdTime = 0f;
            waitForRelease = true; // dalsi zber az po novom stlaceni E
            SetGatherAnim(false);
            done.Interact(this);
        }
    }

    private Interactable FindTarget()
    {
        Vector2 feet = (Vector2)transform.position + feetOffset;
        Interactable best = null;
        float bestScore = float.MaxValue;

        foreach (var candidate in Interactable.Active)
        {
            if (candidate == null || !candidate.IsAvailable) continue;
            float distance = candidate.DistanceFrom(feet);
            if (distance > range) continue;

            float score = distance;
            Vector2 toTarget = (Vector2)candidate.transform.position - feet;
            if (toTarget.sqrMagnitude > 0.0001f && Vector2.Dot(toTarget.normalized, facing) > 0.5f) score -= facingBonus;
            if (candidate == target) score -= 0.1f; // aby napoveda neskakala medzi dvoma objektmi

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }

    private bool IsInteractPressed()
    {
        if (interactAction != null) return interactAction.IsPressed();
        return Keyboard.current != null && Keyboard.current.eKey.isPressed;
    }

    private void CancelHold()
    {
        if (holdTime > 0f && target != null) target.OnHoldCancel(this);
        holdTime = 0f;
        SetGatherAnim(false);
    }

    private void FaceTowards(Vector3 worldPosition)
    {
        float dx = worldPosition.x - transform.position.x;
        if (spriteRenderer != null && Mathf.Abs(dx) > 0.05f) spriteRenderer.flipX = dx < 0f;
    }

    private void SetGatherAnim(bool value)
    {
        if (hasGatherParam) animator.SetBool(GatherAnimParam, value);
    }

    // ------------------------------------------------------------ spatna vazba (volaju to Interactable objekty)

    public void PlaySound(AudioClip clip, float volume = 1f)
    {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip, volume);
    }

    public void ShowPopup(Vector3 worldPosition, string text, Sprite icon, Color color)
    {
        if (ui != null) ui.ShowPopup(worldPosition, text, icon, color);
    }
}
