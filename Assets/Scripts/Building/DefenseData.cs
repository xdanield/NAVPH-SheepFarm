using UnityEngine;

/// <summary>
/// Definicia obrany (plot, bodce...). Nova obrana: v Project okne
/// pravy klik > Create > Baa-ricade > Obrana.
/// </summary>
[CreateAssetMenu(menuName = "Baa-ricade/Obrana", fileName = "NovaObrana")]
public class DefenseData : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField] private GameObject prefab;
    [Tooltip("Kolko buniek mriezky zaberie (1 bunka = 2x2 tiles). Plot = 1x1, kmen cez 2 linky = 1x2.")]
    [SerializeField] private Vector2Int size = Vector2Int.one;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public GameObject Prefab => prefab;
    public Vector2Int Size => new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
}