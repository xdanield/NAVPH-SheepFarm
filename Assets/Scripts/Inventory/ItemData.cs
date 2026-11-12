using UnityEngine;

/// <summary>
/// Definicia predmetu (drevo, kamen, bobule...). Novy predmet: v Project okne
/// pravy klik > Create > Baa-ricade > Predmet.
/// </summary>
[CreateAssetMenu(menuName = "Baa-ricade/Predmet", fileName = "NovyPredmet")]
public class ItemData : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private Sprite icon;
    [SerializeField, Min(1)] private int maxStack = 99;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public int MaxStack => Mathf.Max(1, maxStack);
}
