using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Teple svetlo (okno, lampas, ohen). Vecer sa rozsvieti podla DayNightLighting.Night a jemne blika ako plamen.
/// Silu a polomer nastavuj tu, nie na Light2D (Light2D.intensity sa prepisuje kazdy frame).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Light2D))]
public class WarmLight : MonoBehaviour
{
    static readonly List<WarmLight> all = new List<WarmLight>();

    /// <summary>Vsetky zapnute teple svetla (cita ich napr. SpriteShadow).</summary>
    public static IReadOnlyList<WarmLight> All => all;
    public Light2D Light => light2D;

    [SerializeField] private float intensity = 1.2f;
    [Tooltip("Ako velmi blika (0 = vobec, 0.3 = ohen).")]
    [Range(0f, 0.5f)] [SerializeField] private float flicker = 0.1f;
    [SerializeField] private float flickerSpeed = 4f;
    [Tooltip("Pri akej tme sa zacne a kedy je naplno rozsvietene (0 = den, 1 = noc).")]
    [SerializeField] private Vector2 turnOn = new Vector2(0.3f, 0.75f);

    private Light2D light2D;
    private float seed;

    void OnEnable()
    {
        light2D = GetComponent<Light2D>();
        seed = Random.value * 100f;
        all.Add(this);
        Refresh();
    }

    void OnDisable() => all.Remove(this);

    void Update() => Refresh();

    public static void RefreshAll()
    {
        foreach (var l in all) l.Refresh();
    }

    void Refresh()
    {
        if (light2D == null) return;
        float on = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(turnOn.x, turnOn.y, DayNightLighting.Night));
        float noise = Application.isPlaying ? Mathf.PerlinNoise(seed, Time.time * flickerSpeed) * 2f - 1f : 0f;
        // Len intenzita, Light2D.enabled nemenime: zapinanie pocas nacitania sceny (Play bez Domain Reload)
        // ho v URP zaregistruje dvakrat a po zniceni ostane v zozname -> MissingReferenceException.
        light2D.intensity = intensity * on * (1f + noise * flicker);
    }
}
