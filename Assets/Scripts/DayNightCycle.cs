using System;
using UnityEngine;

public enum DayPhase { Day, Night }

public class DayNightCycle : MonoBehaviour
{
    [SerializeField] private float dayLength = 120f;
    [SerializeField] private float nightLength = 60f;

    public DayPhase Phase { get; private set; }
    public int DayNumber { get; private set; } = 1;
    public float TimeLeft { get; private set; }

    public event Action<DayPhase> PhaseChanged;

    void Start()
    {
        StartDay();
    }

    void Update()
    {
        TimeLeft -= Time.deltaTime;
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