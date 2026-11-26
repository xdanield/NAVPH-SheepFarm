using System.Collections;
using UnityEngine;

/// <summary>
/// Trigger na okraji lokacie (napr. cesta hore cez utes na farme). Ked do neho vojde hrac,
/// obrazovka stmavne, hrac sa presunie na arrival v cielovej lokacii a obrazovka sa znova rozjasni.
///
/// Arrival musi byt mimo triggra vychodu v cielovej lokacii, inak by hraca hned poslalo spat.
/// Nastavuje to generator mapy (ExploreExit na farme, FarmExit v Explore).
///
/// Ciel sa hlada podla mena (targetName + arrivalPath), lebo farma a Explore su samostatne prefaby
/// a prefab nemoze odkazovat na objekt v inom prefabe. Priame odkazy (target, arrival) maju prednost, ak su vyplnene.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class LocationExit : MonoBehaviour
{
    [Tooltip("Nazov cielovej lokacie (Location.displayName), napr. \"The Wilds\".")]
    [SerializeField] private string targetName;
    [Tooltip("Cesta k bodu prichodu pod cielovou lokaciou, napr. \"Points/ArrivalFromFarm\".")]
    [SerializeField] private string arrivalPath;
    [Tooltip("Volitelne: priamy odkaz na cielovu lokaciu (ma prednost pred targetName).")]
    [SerializeField] private Location target;
    [Tooltip("Volitelne: kde sa hrac objavi v cielovej lokacii (ma prednost pred arrivalPath).")]
    [SerializeField] private Transform arrival;
    [Tooltip("Ako dlho trva stmavnutie (a potom rozjasnenie), sekundy.")]
    [SerializeField, Min(0f)] private float fadeTime = 0.35f;

    /// <summary>Prave prebieha presun (hrac sa neda ovladat).</summary>
    public static bool Traveling { get; private set; }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (Traveling || !Resolve()) return;
        var player = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;
        if (!player.CompareTag("Player")) return;
        StartCoroutine(Travel(player));
    }

    /// <summary>Doplni target a arrival podla mena. False, ak ciel v scene nie je (napr. Explore este nie je postavene).</summary>
    private bool Resolve()
    {
        if (target == null && !string.IsNullOrEmpty(targetName)) target = Location.Find(targetName);
        if (arrival == null && target != null && !string.IsNullOrEmpty(arrivalPath)) arrival = target.transform.Find(arrivalPath);
        return target != null && arrival != null;
    }

    private IEnumerator Travel(GameObject player)
    {
        Traveling = true;
        var movement = player.GetComponent<PlayerMovement>();
        var rb = player.GetComponent<Rigidbody2D>();
        var animator = player.GetComponent<Animator>();

        // Hrac pocas presunu stoji.
        if (movement != null) movement.enabled = false;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        if (animator != null && animator.runtimeAnimatorController != null) animator.SetBool("IsMoving", false);

        var fader = ScreenFader.Instance;
        yield return fader.Fade(1f, fadeTime);

        Vector3 pos = arrival.position;
        pos.z = player.transform.position.z;
        if (rb != null) rb.position = pos;
        player.transform.position = pos;
        Physics2D.SyncTransforms();
        target.Enter(); // kamera dostane hranice novej lokacie a v LateUpdate skoci na hraca

        yield return null;
        yield return fader.Fade(0f, fadeTime);

        if (movement != null) movement.enabled = true;
        fader.ShowTitle(target.DisplayName);
        Traveling = false;
    }

    void OnDrawGizmos()
    {
        if (arrival == null) return; // v editore len pri priamom odkaze (meno sa hlada az v hre)
        Gizmos.color = new Color(1f, 0.85f, 0.3f);
        Gizmos.DrawWireSphere(arrival.position, 0.4f);
    }
}
