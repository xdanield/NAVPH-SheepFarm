using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Globalne svetlo podla dna a noci: cez den biele, vecer oranzove (zapad), v noci tmavomodre.
/// Night (0 = den, 1 = noc) citaju aj WarmLight lampy, aby sa vecer rozsvietili.
///
/// Mimo Play sa da cez "Nahlad" posuvat cas a hned vidiet, ako scena vyzera (Scene aj Game view).
/// </summary>
[ExecuteAlways]
public class DayNightLighting : MonoBehaviour
{
    /// <summary>0 = den, 0.5 = zapad slnka, 1 = noc.</summary>
    public static float Night { get; private set; }

    [SerializeField] private DayNightCycle cycle;
    [SerializeField] private Light2D globalLight;

    [Header("Prechody (sekundy)")]
    [Tooltip("Kolko sekund pred koncom dna zacne zapad slnka.")]
    [SerializeField] private float duskDuration = 15f;
    [Tooltip("Kolko sekund na zaciatku dna trva svitanie.")]
    [SerializeField] private float dawnDuration = 6f;

    [Header("Globalne svetlo (vlavo den, v strede zapad, vpravo noc)")]
    [SerializeField] private Gradient color = DefaultColor();
    [SerializeField] private AnimationCurve intensity = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.5f, 0.85f), new Keyframe(1f, 0.3f));

    [Header("Slnko - tiene postav (SpriteShadow)")]
    [Tooltip("Slnko ide po oblohe PONAD postavu: rano nizko (dlhy tien), na poludnie hore (len kruzok pod nohami), vecer zase nizko na druhej strane.")]
    [SerializeField] private bool sunMoves = true;
    [Tooltip("Smer na obrazovke, kde slnko vychadza (x = vpravo, y = hore). Zapada na presne opacnej strane. " +
             "(-0.6, -1) = vlavo dole, kusok doprava od rohu -> zapad vpravo hore.")]
    [SerializeField] private Vector2 sunriseDirection = new Vector2(-0.6f, -1f);
    [Tooltip("Najvyssia vyska slnka na poludnie (stupne). 90 = presne nad hlavou, tien je len kruzok pod postavou.")]
    [Range(30f, 90f)] [SerializeField] private float noonElevation = 90f;
    [Tooltip("Vyska slnka na zaciatku a konci dna (stupne). Nizsie = tien dlhsie ostava dlhy.")]
    [Range(5f, 60f)] [SerializeField] private float startElevation = 30f;
    [Tooltip("Najdlhsi tien od slnka v nasobkoch vysky postavy (rano/vecer).")]
    [SerializeField] private float maxSunShadowLength = 1.2f;
    [Tooltip("Ked sunMoves je vypnute: pevny tien (smer aj dlzka, x = do strany, y > 0 = dozadu za postavu).")]
    [SerializeField] private Vector2 sunShadowDirection = new Vector2(0.8f, 0.6f);
    [Tooltip("Pohlad zhora pod uhlom: zem je skratena, tien smerom hore/dole je kratsi ako do strany.")]
    [Range(0.2f, 1f)] [SerializeField] private float groundTilt = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float sunShadowStrength = 0.8f;
    [Tooltip("Testovanie v Play: >= 0 natvrdo nastavi cas slnka (0 = rano, 0.5 = poludnie, 1 = vecer). -1 = podla cyklu.")]
    [Range(-1f, 1f)] [SerializeField] private float debugSunTime = -1f;

    [Header("Tiene - celkovy vzhlad (vsetky SpriteShadow naraz)")]
    [Tooltip("Celkova tmavost vsetkych tienov (nasobi maxAlpha na objektoch). Mensie = jemnejsie tiene.")]
    [Range(0f, 1f)] [SerializeField] private float shadowOpacity = 0.6f;
    [Tooltip("Ako vysoko visia lampy a ohen (v jednotkach). Vyssie = kratsie tiene od lamp.")]
    [SerializeField] private float lampHeight = 4f;
    [Tooltip("Najdlhsi tien od lampy v nasobkoch vysky postavy (dlzka sa k nemu blizi plynulo).")]
    [SerializeField] private float lampShadowMaxLength = 1.1f;

    // Staticke hodnoty pre SpriteShadow (default plati aj bez DayNightLighting v scene).
    public static float ShadowOpacity { get; private set; } = 0.6f;
    public static float LampHeight { get; private set; } = 4f;
    public static float LampShadowMaxLength { get; private set; } = 1.1f;
    public static float GroundTilt { get; private set; } = 0.6f;

    /// <summary>Kam sa posunie hlava tiena na 1 jednotku vysky postavy (smer * dlzka).</summary>
    public static Vector2 SunShadow { get; private set; }
    /// <summary>0 = slnecny tien nie je (noc alebo slnko presne nad hlavou), 1 = naplno.</summary>
    public static float SunShadowStrength { get; private set; }
    /// <summary>0 = slnko nizko, 1 = slnko nad hlavou (kontaktny tien pod nohami je vtedy vyraznejsi).</summary>
    public static float SunOverhead { get; private set; }

    [Header("Nahlad v editore (mimo Play)")]
    [Range(0f, 1f)] [SerializeField] private float preview = 0f;

    void Update()
    {
        bool live = Application.isPlaying && cycle != null;
        SetNight(live ? FromCycle(cycle) : preview);
        float t = live && cycle.Phase == DayPhase.Day ? 1f - cycle.TimeLeft / cycle.PhaseLength : 0.5f;
        if (debugSunTime >= 0f) t = debugSunTime;
        UpdateSun(t);
    }

    /// <param name="t">Cast dna: 0 = rano, 0.5 = poludnie, 1 = vecer.</param>
    void UpdateSun(float t)
    {
        ShadowOpacity = shadowOpacity;
        LampHeight = Mathf.Max(0.5f, lampHeight);
        LampShadowMaxLength = Mathf.Max(0.1f, lampShadowMaxLength);
        GroundTilt = groundTilt;

        float daylight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.8f, Night));
        Vector2 shadow;

        if (sunMoves)
        {
            // Slnko v 3D: vodorovny smer na zemi + vyska nad zemou. Ide po oblohe od vychodu
            // (smer sunriseDirection) cez vrchol (noonElevation) az na opacnu stranu.
            Vector2 rise = sunriseDirection.sqrMagnitude > 0.0001f ? sunriseDirection.normalized : new Vector2(-0.6f, -1f).normalized;
            Vector2 side = new Vector2(-rise.y, rise.x);       // kolmo na drahu slnka
            if (side.y > 0f) side = -side;                      // ked slnko nejde cez vrchol, nakloni sa ku kamere
            float peak = noonElevation * Mathf.Deg2Rad;
            float arc = Mathf.Lerp(startElevation, 180f - startElevation, t) * Mathf.Deg2Rad;

            Vector2 sunGround = rise * Mathf.Cos(arc) + side * (Mathf.Sin(arc) * Mathf.Cos(peak));
            float sunHeight = Mathf.Sin(arc) * Mathf.Sin(peak);

            // Hlava vo vyske 1 vrhne tien o -sunGround / sunHeight (cim nizsie slnko, tym dlhsi)
            shadow = Vector2.ClampMagnitude(-sunGround / Mathf.Max(0.01f, sunHeight), maxSunShadowLength);
        }
        else
        {
            shadow = Vector2.ClampMagnitude(sunShadowDirection, maxSunShadowLength);
        }

        // Slnko nad hlavou: vrhany tien zmizne, ostane len kruzok pod nohami
        float len = shadow.magnitude;
        SunOverhead = daylight * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.6f, len)));
        SunShadow = new Vector2(shadow.x, shadow.y * groundTilt);
        SunShadowStrength = sunShadowStrength * daylight * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.35f, len));
    }

    void OnValidate()
    {
        if (!Application.isPlaying) SetNight(preview);
    }

    /// <summary>Nastavi cas rucne (nahlad, screenshoty). V Play ho Update zase prepise podla cyklu.</summary>
    public void SetNight(float night)
    {
        Night = Mathf.Clamp01(night);
        if (globalLight != null)
        {
            globalLight.color = color.Evaluate(Night);
            globalLight.intensity = intensity.Evaluate(Night);
        }
        WarmLight.RefreshAll();
    }

    float FromCycle(DayNightCycle c)
    {
        if (c.Phase == DayPhase.Night) return 1f;

        float elapsed = c.PhaseLength - c.TimeLeft;
        if (c.DayNumber > 1 && elapsed < dawnDuration)
            return Smooth(1f - elapsed / dawnDuration);
        if (c.TimeLeft < duskDuration)
            return Smooth(1f - c.TimeLeft / duskDuration);
        return 0f;
    }

    static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

    static Gradient DefaultColor()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.98f, 0.94f), 0f),     // den
                new GradientColorKey(new Color(1f, 0.82f, 0.6f), 0.3f),    // neskore popoludnie
                new GradientColorKey(new Color(1f, 0.5f, 0.32f), 0.5f),    // zapad
                new GradientColorKey(new Color(0.55f, 0.4f, 0.75f), 0.7f), // fialovy sumrak
                new GradientColorKey(new Color(0.3f, 0.42f, 0.95f), 1f),   // noc
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
