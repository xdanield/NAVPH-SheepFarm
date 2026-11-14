using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zaklad pre vsetko, s cim hrac interaguje klavesom E: suroviny (Gatherable),
/// neskor truhla, hrniec na varenie, crafting stol...
///
/// Hrac (PlayerInteractor) si vyberie najblizsi dostupny objekt v dosahu, nad nim sa ukaze
/// napoveda "E  <ActionName>". Ak HoldDuration > 0, treba E drzat (progress bar), inak staci stlacit.
/// </summary>
public abstract class Interactable : MonoBehaviour
{
    private static readonly List<Interactable> active = new List<Interactable>();
    public static IReadOnlyList<Interactable> Active => active;

    [Tooltip("Vyska napovedy nad patou objektu v units. 0 = automaticky podla sprite (max 1,4).")]
    [SerializeField, Min(0)] private float promptHeight = 0f;

    protected SpriteRenderer spriteRenderer;
    protected Collider2D[] colliders;

    /// <summary>Text v napovede, napr. "Chop tree".</summary>
    public abstract string ActionName { get; }

    /// <summary>Ako dlho treba drzat E (sekundy). 0 = staci stlacit.</summary>
    public virtual float HoldDuration => 0f;

    /// <summary>Da sa objekt vobec vybrat? (napr. vyzbierany ker nie)</summary>
    public virtual bool IsAvailable => isActiveAndEnabled;

    /// <summary>Preco sa to teraz neda (zobrazi sa cervenou, napr. "Inventory full"). null = da sa.</summary>
    public virtual string BlockedReason(PlayerInteractor who) => null;

    /// <summary>Interakcia prebehla (E stlacene, alebo dodrzane do konca).</summary>
    public abstract void Interact(PlayerInteractor who);

    /// <summary>Kym hrac drzi E. progress je 0..1.</summary>
    public virtual void OnHoldProgress(PlayerInteractor who, float progress) { }

    /// <summary>Hrac pustil E alebo odisiel skor, nez sa to dokoncilo.</summary>
    public virtual void OnHoldCancel(PlayerInteractor who) { }

    protected virtual void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        colliders = GetComponents<Collider2D>();
    }

    protected virtual void OnEnable() => active.Add(this);
    protected virtual void OnDisable() => active.Remove(this);

    /// <summary>Vzdialenost od bodu k okraju objektu (k colliderom), aby sa velke objekty dali pouzit z kazdej strany.</summary>
    public float DistanceFrom(Vector2 point)
    {
        float best = float.MaxValue;
        foreach (var col in colliders)
        {
            if (col == null || !col.enabled) continue;
            best = Mathf.Min(best, Vector2.Distance(point, col.ClosestPoint(point)));
        }
        return best < float.MaxValue ? best : Vector2.Distance(point, transform.position);
    }

    /// <summary>Kde sa ma ukazat napoveda (vo svete).</summary>
    public Vector3 PromptPosition
    {
        get
        {
            Vector3 pos = transform.position;
            if (promptHeight > 0f) return pos + Vector3.up * promptHeight;
            float top = spriteRenderer != null && spriteRenderer.enabled && spriteRenderer.sprite != null
                ? spriteRenderer.bounds.max.y
                : pos.y + 1f;
            return new Vector3(pos.x, Mathf.Min(top, pos.y + 1.4f) + 0.1f, pos.z);
        }
    }
}
