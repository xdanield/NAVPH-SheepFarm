using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dynamicke tiene postavy (styl "2D sprite v 3D svete" ako Sea of Stars).
///
/// Tien je kopia spritu, cierna a priesvitna, ktora sa NEOTACA, ale SKOSI (shear):
/// nohy ostavaju na mieste a vodorovne, hlava sa posunie smerom od svetla. Kazde svetlo
/// vrha vlastny tien (max. maxShadows) - medzi dvoma lampami ma postava dva tiene.
///  - lampy/ohen (WarmLight): dlzka podla vzdialenosti a vysky lampy (podobne trojuholniky)
///  - slnko: smer a dlzka z DayNightLighting (SunShadow)
/// Pod nohami je navyse maly kontaktny tien (elipsa), aby postava "stala na zemi" aj vtedy,
/// ked svieti svetlo presne zboku a plochy sprite vrha len tenku ciaru.
///
/// Vsetko vznika az v Play, v prefabe ani scene nic nepribudne.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteShadow : MonoBehaviour
{
    [Header("Vrhane tiene")]
    [Tooltip("Kolko svetiel naraz moze vrhat tien (najsilnejsie vyhravaju).")]
    [Range(1, 4)] [SerializeField] private int maxShadows = 3;
    [Tooltip("Maximalna tmavost tiena (0 = neviditelny, 1 = ciste cierny).")]
    [Range(0f, 1f)] [SerializeField] private float maxAlpha = 0.5f;
    [Tooltip("Posun linie noh od spodku spritu (ak ma sprite pod nohami prazdne pixely).")]
    [SerializeField] private float footOffset = 0.05f;
    [Tooltip("Sorting order tiena: nad zemou (-300..-85), pod postavami (0).")]
    [SerializeField] private int sortingOrder = -50;

    [Header("Lampy a ohen")]
    [Tooltip("Ako vysoko visi svetlo (v jednotkach). Nizsie = dlhsie tiene.")]
    [SerializeField] private float lampHeight = 2.5f;
    [Tooltip("Najdlhsi tien v nasobkoch vysky postavy.")]
    [SerializeField] private float maxLength = 2.2f;

    [Header("Kontaktny tien pod nohami")]
    [SerializeField] private bool contactShadow = true;
    [Range(0f, 1f)] [SerializeField] private float contactAlpha = 0.3f;
    [Tooltip("Sirka elipsy v nasobkoch sirky spritu.")]
    [SerializeField] private float contactWidth = 0.6f;
    [Tooltip("Ked je slnko presne nad hlavou: tmavost a sirka kruzku pod nohami.")]
    [Range(0f, 1f)] [SerializeField] private float noonContactAlpha = 0.5f;
    [SerializeField] private float noonContactWidth = 0.8f;

    private struct Caster
    {
        public Vector2 shear;   // posun hlavy na 1 jednotku vysky
        public float strength;  // 0..1
    }

    private class ShadowView
    {
        public Transform outer, inner;
        public SpriteRenderer renderer;
    }

    private SpriteRenderer body;
    private readonly List<ShadowView> views = new List<ShadowView>();
    private readonly List<Caster> casters = new List<Caster>();
    private SpriteRenderer contact;

    private static Sprite ellipse;

    void Awake()
    {
        body = GetComponent<SpriteRenderer>();
        for (int i = 0; i < maxShadows; i++) views.Add(CreateView(i));

        if (contactShadow)
        {
            var go = new GameObject("ContactShadow");
            go.transform.SetParent(transform, false);
            contact = go.AddComponent<SpriteRenderer>();
            contact.sprite = Ellipse();
            contact.sharedMaterial = body.sharedMaterial;
            contact.sortingLayerID = body.sortingLayerID;
            contact.sortingOrder = sortingOrder;
            contact.color = new Color(0f, 0f, 0f, contactAlpha);
        }
    }

    ShadowView CreateView(int index)
    {
        // outer: pozicia noh + rotacia + nerovnomerna mierka, inner: druha rotacia.
        // Spolu daju lubovolnu 2x2 maticu, teda aj skosenie (Unity Transform skosenie priamo nema).
        var v = new ShadowView();
        v.outer = new GameObject("Shadow" + index).transform;
        v.outer.SetParent(transform, false);
        v.inner = new GameObject("Shear").transform;
        v.inner.SetParent(v.outer, false);

        var sprite = new GameObject("Sprite");
        sprite.transform.SetParent(v.inner, false);
        v.renderer = sprite.AddComponent<SpriteRenderer>();
        v.renderer.sharedMaterial = body.sharedMaterial; // cierna * svetlo = stale cierna
        v.renderer.sortingLayerID = body.sortingLayerID;
        v.renderer.sortingOrder = sortingOrder;
        v.renderer.color = Color.clear;
        return v;
    }

    void LateUpdate()
    {
        bool visible = body.enabled && body.sprite != null;
        if (contact != null) contact.enabled = visible;
        if (!visible)
        {
            foreach (var v in views) v.renderer.enabled = false;
            return;
        }

        Bounds b = body.sprite.bounds; // lokalne, bez flipu
        float centerX = body.flipX ? -b.center.x : b.center.x;
        Vector2 foot = new Vector2(centerX, b.min.y + footOffset);

        if (contact != null)
        {
            contact.transform.localPosition = foot;
            float noon = DayNightLighting.SunOverhead; // slnko nad hlavou -> tien je len kruzok pod nohami
            float width = b.size.x * Mathf.Lerp(contactWidth, noonContactWidth, noon);
            contact.transform.localScale = new Vector3(width, width, 1f);
            contact.color = new Color(0f, 0f, 0f, Mathf.Lerp(contactAlpha, noonContactAlpha, noon) * body.color.a);
        }

        CollectCasters(transform.TransformPoint(foot));

        for (int i = 0; i < views.Count; i++)
        {
            var v = views[i];
            if (i >= casters.Count)
            {
                v.renderer.enabled = false;
                continue;
            }
            v.renderer.enabled = true;
            v.renderer.sprite = body.sprite;
            v.renderer.flipX = body.flipX;
            ApplyShear(v, foot, casters[i].shear);
            v.renderer.color = new Color(0f, 0f, 0f, maxAlpha * casters[i].strength * body.color.a);
        }
    }

    void CollectCasters(Vector2 feetWorld)
    {
        casters.Clear();

        foreach (var w in WarmLight.All)
        {
            if (w.Light == null || w.transform.IsChildOf(transform)) continue; // vlastny lampas nie
            float radius = w.Light.pointLightOuterRadius;
            if (radius <= 0f) continue;

            Vector2 away = feetWorld - (Vector2)w.transform.position;
            float dist01 = away.magnitude / radius;
            if (dist01 >= 1f) continue;

            float strength = Mathf.Clamp01(w.Light.intensity) * (1f - dist01 * dist01);
            if (strength < 0.02f) continue;

            // Podobne trojuholniky: hlava vo vyske h je od lampy dalej o away * h / vyskaLampy.
            Vector2 shear = Vector2.ClampMagnitude(away / Mathf.Max(0.1f, lampHeight), maxLength);
            casters.Add(new Caster { shear = shear, strength = strength });
        }

        float sun = DayNightLighting.SunShadowStrength;
        if (sun >= 0.02f)
            casters.Add(new Caster { shear = DayNightLighting.SunShadow, strength = sun });

        casters.Sort((x, y) => y.strength.CompareTo(x.strength));
    }

    /// <summary>
    /// Bod spritu p sa zobrazi na foot + M * (p - foot), kde M = [[1, s.x], [0, s.y]]:
    /// x ostava (nohy sedia), hlava (vyska h = p.y - foot.y) sa posunie o s * h.
    /// M rozlozime na rotacia * mierka * rotacia (SVD 2x2), co sa da poskladat z dvoch Transformov.
    /// </summary>
    static void ApplyShear(ShadowView v, Vector2 foot, Vector2 s)
    {
        float a = 1f, bb = s.x, c = 0f, d = s.y;
        float e = (a + d) * 0.5f, f = (a - d) * 0.5f, g = (c + bb) * 0.5f, h = (c - bb) * 0.5f;
        float q = Mathf.Sqrt(e * e + h * h), r = Mathf.Sqrt(f * f + g * g);
        float sx = q + r, sy = q - r;
        if (Mathf.Abs(sy) < 0.001f) sy = sy < 0f ? -0.001f : 0.001f; // tien presne zboku = tenka ciara
        float a1 = Mathf.Atan2(g, f), a2 = Mathf.Atan2(h, e);
        float theta = (a2 - a1) * 0.5f, phi = (a2 + a1) * 0.5f;

        v.outer.localPosition = foot;
        v.outer.localRotation = Quaternion.Euler(0f, 0f, phi * Mathf.Rad2Deg);
        v.outer.localScale = new Vector3(sx, sy, 1f);
        v.inner.localRotation = Quaternion.Euler(0f, 0f, theta * Mathf.Rad2Deg);
        v.renderer.transform.localPosition = -foot;
    }

    /// <summary>Pixelova elipsa 16x6 so zubatym okrajom (1 jednotka siroka), vytvori sa raz.</summary>
    static Sprite Ellipse()
    {
        if (ellipse != null) return ellipse;
        const int w = 16, hgt = 6;
        var tex = new Texture2D(w, hgt, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "ContactShadow"
        };
        for (int y = 0; y < hgt; y++)
        for (int x = 0; x < w; x++)
        {
            float nx = (x + 0.5f - w * 0.5f) / (w * 0.5f);
            float ny = (y + 0.5f - hgt * 0.5f) / (hgt * 0.5f);
            float dd = nx * nx + ny * ny;
            float alpha = dd <= 0.45f ? 1f : dd <= 1f ? 0.55f : 0f; // jadro + svetlejsi okraj
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        tex.Apply();
        ellipse = Sprite.Create(tex, new Rect(0, 0, w, hgt), new Vector2(0.5f, 0.5f), w);
        ellipse.name = "ContactShadow";
        return ellipse;
    }
}
