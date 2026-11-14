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

    [Header("Nahlad v editore (mimo Play)")]
    [Range(0f, 1f)] [SerializeField] private float preview = 0f;

    void Update()
    {
        float night = Application.isPlaying && cycle != null ? FromCycle(cycle) : preview;
        SetNight(night);
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
