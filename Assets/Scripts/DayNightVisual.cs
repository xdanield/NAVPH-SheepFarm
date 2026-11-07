using UnityEngine;

public class DayNightVisual : MonoBehaviour
{
    [SerializeField] private DayNightCycle dayNight;
    [SerializeField] private Color dayColor = new Color(0.55f, 0.8f, 0.45f);
    [SerializeField] private Color nightColor = new Color(0.08f, 0.1f, 0.2f);

    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void OnEnable()
    {
        dayNight.PhaseChanged += OnPhaseChanged;
    }

    void OnDisable()
    {
        dayNight.PhaseChanged -= OnPhaseChanged;
    }

    private void OnPhaseChanged(DayPhase phase)
    {
        cam.backgroundColor = phase == DayPhase.Day ? dayColor : nightColor;
    }
}