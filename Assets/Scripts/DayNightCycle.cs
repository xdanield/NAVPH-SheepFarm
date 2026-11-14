using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum DayPhase { Day, Night }

public class DayNightCycle : MonoBehaviour
{
    [SerializeField] private float dayLength = 120f;
    [SerializeField] private float nightLength = 60f;

    public DayPhase Phase { get; private set; }
    public int DayNumber { get; private set; } = 1;
    public float TimeLeft { get; private set; }
    /// <summary>Dlzka aktualnej fazy (dna alebo noci) v sekundach.</summary>
    public float PhaseLength => Phase == DayPhase.Day ? dayLength : nightLength;

    [Tooltip("Len na testovanie: ked drzis T, cas bezi takto rychlejsie.")]
    [SerializeField] private float debugSpeed = 10f;

    public event Action<DayPhase> PhaseChanged;

    void Start()
    {
        StartDay();
    }

    void Update()
    {
        float speed = 1f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Keyboard.current != null && Keyboard.current.tKey.isPressed) speed = debugSpeed;
#endif
        TimeLeft -= Time.deltaTime * speed;
        if (TimeLeft > 0f) return;

        if (Phase == DayPhase.Day)
        {
            StartNight();
        }
        else
        {
            DayNumber++;
            StartDay();
        }
    }

    private void StartDay()
    {
        Phase = DayPhase.Day;
        TimeLeft = dayLength;
        PhaseChanged?.Invoke(Phase);
    }

    private void StartNight()
    {
        Phase = DayPhase.Night;
        TimeLeft = nightLength;
        PhaseChanged?.Invoke(Phase);
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 400, 20),
            $"Day {DayNumber}  |  {Phase}  |  {TimeLeft:0}s left");
    }
}