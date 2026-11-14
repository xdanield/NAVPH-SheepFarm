using UnityEngine;

/// <summary>
/// Surovina, ktoru hrac zbiera drzanim E: strom (drevo), kamen, trava (vlakno), ker (bobule).
///
/// Po vyzbierani sa zmeni na "depleted" sprite (pnik, prazdny ker). Ak ho nema, zmizne aj s colliderom.
/// Rano po <regrowDays> dnoch doraste, ak na tom mieste prave nikto nestoji.
///
/// Grafika: plny sprite je ten v SpriteRenderer, depleted sprite sa nastavi sam z druheho snimku
/// v .aseprite (tree: "stump", berry_bush: "empty") cez Baa-ricade > Aktualizovat sprity z Aseprite.
/// </summary>
public class Gatherable : Interactable
{
    [Header("Co dava")]
    [SerializeField] private ItemData item;
    [Tooltip("Kolko kusov dava jeden zber (min a max vratane).")]
    [SerializeField] private Vector2Int amount = new Vector2Int(1, 1);

    [Header("Zber")]
    [Tooltip("Text v napovede nad objektom.")]
    [SerializeField] private string actionName = "Gather";
    [Tooltip("Ako dlho treba drzat E (sekundy).")]
    [SerializeField, Min(0)] private float gatherTime = 1f;
    [Tooltip("Kolkokrat sa da zbierat, kym sa minie.")]
    [SerializeField, Min(1)] private int harvests = 1;

    [Header("Po vyzbierani")]
    [Tooltip("Pnik, prazdny ker... Ak je prazdne, objekt zmizne.")]
    [SerializeField] private Sprite depletedSprite;
    [Tooltip("Blokuje aj vyzbierany objekt cestu? (pnik ano, len ak ma depleted sprite)")]
    [SerializeField] private bool blockWhenDepleted = true;
    [Tooltip("Po kolkych dnoch rano doraste. 0 = nikdy.")]
    [SerializeField, Min(0)] private int regrowDays = 2;

    [Header("Efekty")]
    [Tooltip("Ako velmi sa objekt pri zbere kyve (stupne).")]
    [SerializeField] private float wobbleAngle = 4f;
    [Tooltip("Zvuk pocas zberu (sekera, kopanie...), opakuje sa.")]
    [SerializeField] private AudioClip gatherSound;
    [SerializeField, Min(0.05f)] private float gatherSoundInterval = 0.45f;
    [Tooltip("Zvuk, ked zber skonci.")]
    [SerializeField] private AudioClip completeSound;

    private Sprite fullSprite;
    private bool[] colliderEnabled;
    private Quaternion baseRotation;
    private DayNightCycle dayNight;

    private int harvestsLeft;
    private int pendingAmount;   // co sa nezmestilo do inventara, ostava v objekte
    private int daysUntilRegrow;
    private float soundTimer;

    public ItemData Item => item;
    public bool IsDepleted { get; private set; }

    public override string ActionName => actionName;
    public override float HoldDuration => gatherTime;
    public override bool IsAvailable => base.IsAvailable && !IsDepleted && item != null;

    protected override void Awake()
    {
        base.Awake();
        fullSprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        colliderEnabled = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++) colliderEnabled[i] = colliders[i].enabled;
        baseRotation = transform.localRotation;
        harvestsLeft = harvests;
        if (item == null) Debug.LogWarning($"{name}: Gatherable nema nastaveny predmet.", this);
    }

    void Start()
    {
        dayNight = FindFirstObjectByType<DayNightCycle>();
        if (dayNight != null) dayNight.PhaseChanged += OnPhaseChanged;
    }

    void OnDestroy()
    {
        if (dayNight != null) dayNight.PhaseChanged -= OnPhaseChanged;
    }

    void OnValidate()
    {
        amount.x = Mathf.Max(1, amount.x);
        amount.y = Mathf.Max(amount.x, amount.y);
    }

    // ------------------------------------------------------------ interakcia

    public override string BlockedReason(PlayerInteractor who)
    {
        return who.Inventory.SpaceFor(item) <= 0 ? "Inventory full" : null;
    }

    public override void OnHoldProgress(PlayerInteractor who, float progress)
    {
        float t = progress * gatherTime;
        float angle = Mathf.Sin(t * 30f) * wobbleAngle;
        transform.localRotation = baseRotation * Quaternion.Euler(0f, 0f, angle);

        soundTimer -= Time.deltaTime;
        if (soundTimer <= 0f)
        {
            who.PlaySound(gatherSound, 0.8f);
            soundTimer = gatherSoundInterval;
        }
    }

    public override void OnHoldCancel(PlayerInteractor who)
    {
        ResetEffects();
    }

    public override void Interact(PlayerInteractor who)
    {
        ResetEffects();

        // Pocet sa hodi len raz; ked sa nezmesti, zvysok ostane tu a neda sa "prehadzovat".
        if (pendingAmount <= 0) pendingAmount = Random.Range(amount.x, amount.y + 1);

        int left = who.Inventory.Add(item, pendingAmount);
        int given = pendingAmount - left;
        pendingAmount = left;

        Vector3 popupAt = PromptPosition;
        if (given > 0)
        {
            who.ShowPopup(popupAt, $"+{given} {item.DisplayName}", item.Icon, Color.white);
            who.PlaySound(completeSound);
        }
        if (left > 0)
        {
            who.ShowPopup(popupAt, "Inventory full", null, PixelUI.WarningColor);
            return;
        }

        harvestsLeft--;
        if (harvestsLeft <= 0) Deplete();
    }

    private void ResetEffects()
    {
        transform.localRotation = baseRotation;
        soundTimer = 0f;
    }

    // ------------------------------------------------------------ minutie a dorastanie

    private void Deplete()
    {
        IsDepleted = true;
        daysUntilRegrow = regrowDays;

        bool hasDepletedSprite = depletedSprite != null;
        if (spriteRenderer != null)
        {
            if (hasDepletedSprite) spriteRenderer.sprite = depletedSprite;
            else spriteRenderer.enabled = false;
        }

        if (!hasDepletedSprite || !blockWhenDepleted)
            foreach (var col in colliders) col.enabled = false;
    }

    private void OnPhaseChanged(DayPhase phase)
    {
        if (phase != DayPhase.Day || !IsDepleted || regrowDays <= 0) return;
        daysUntilRegrow--;
        if (daysUntilRegrow <= 0) TryRegrow();
    }

    private void TryRegrow()
    {
        // Ak na mieste niekto stoji (hrac, ovca, zombie), skusime to zajtra, inak by sa zasekol v strome.
        if (IsOccupied()) return;

        IsDepleted = false;
        harvestsLeft = harvests;
        pendingAmount = 0;
        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = fullSprite;
            spriteRenderer.enabled = true;
        }
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = colliderEnabled[i];
    }

    private bool IsOccupied()
    {
        for (int i = 0; i < colliders.Length; i++)
        {
            if (!colliderEnabled[i] || colliders[i].isTrigger || colliders[i].enabled) continue;
            if (!(colliders[i] is BoxCollider2D box)) continue;

            Vector2 center = transform.TransformPoint(box.offset);
            Vector2 size = Vector2.Scale(box.size, transform.lossyScale);
            foreach (var hit in Physics2D.OverlapBoxAll(center, size, transform.eulerAngles.z))
                if (hit.attachedRigidbody != null && hit.attachedRigidbody.bodyType == RigidbodyType2D.Dynamic) return true;
        }
        return false;
    }
}
