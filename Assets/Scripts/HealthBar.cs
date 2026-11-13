using UnityEngine;

public class HealthBar : MonoBehaviour
{
    [SerializeField] private Transform fillPivot;
    [SerializeField] private bool hideWhenFull = true;

    private Health health;
    private SpriteRenderer[] renderers;

    void Awake()
    {
        health = GetComponentInParent<Health>();
        renderers = GetComponentsInChildren<SpriteRenderer>();
    }

    void OnEnable()
    {
        health.Damaged += UpdateBar;
    }

    void OnDisable()
    {
        health.Damaged -= UpdateBar;
    }

    void Start()
    {
        UpdateBar(health.CurrentHealth);
    }

    private void UpdateBar(int current)
    {
        float fraction = (float)current / health.Max;
        fillPivot.localScale = new Vector3(fraction, 1f, 1f);

        bool visible = !(hideWhenFull && current >= health.Max);
        foreach (SpriteRenderer r in renderers)
        {
            r.enabled = visible;
        }
    }
}