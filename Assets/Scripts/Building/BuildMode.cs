using UnityEngine;
using UnityEngine.InputSystem;

public class BuildMode : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    [Header("Grid")]
    [SerializeField] private Grid buildGrid;
    [SerializeField] private int columns = 5;
    [SerializeField] private int lanes = 9;
    [Tooltip("Stavat sa da len v tejto lokacii (Location.displayName).")]
    [SerializeField] private string buildLocationName = "Farm";

    [Header("Defense")]
    [SerializeField] private DefenseData selectedDefense;
    [SerializeField] private Color ghostColor = new Color(0.4f, 1f, 0.4f, 0.6f);

    private InputAction buildAction;
    private Camera cam;
    private SpriteRenderer ghost;
    private Vector3Int hoveredCell;

    void Start()
    {
        buildAction = InputSystem.actions.FindAction("Build");
        cam = Camera.main;
        CreateGhost();
        SelectDefense(selectedDefense);
        SetActive(false);
    }

    void OnDisable()
    {
        IsActive = false;
    }

    void Update()
    {
        if (Time.timeScale == 0f || LocationExit.Traveling) return;

        bool canBuild = IsInBuildLocation() && !InventoryUI.IsOpen;
        if (IsActive && !canBuild)
        {
            SetActive(false);
        }

        if (canBuild && buildAction.WasPressedThisFrame())
        {
            SetActive(!IsActive);
        }

        if (!IsActive) return;

        hoveredCell = GetMouseCell();
        ghost.transform.position = GetPlacePosition(hoveredCell, selectedDefense.Size);
    }

    public void SelectDefense(DefenseData defense)
    {
        selectedDefense = defense;

        GameObject prefab = defense.Prefab;
        SpriteRenderer prefabRenderer = prefab.GetComponent<SpriteRenderer>();
        ghost.sprite = prefabRenderer.sprite;
        ghost.sortingLayerID = prefabRenderer.sortingLayerID;
        ghost.transform.localScale = prefab.transform.localScale;
    }

    private bool IsInBuildLocation()
    {
        return Location.Current != null && Location.Current.DisplayName == buildLocationName;
    }

    private void SetActive(bool active)
    {
        IsActive = active;
        ghost.gameObject.SetActive(active);
    }

    private void CreateGhost()
    {
        GameObject ghostObject = new GameObject("BuildGhost");
        ghost = ghostObject.AddComponent<SpriteRenderer>();
        ghost.sortingOrder = 100;
        ghost.color = ghostColor;
    }

    private Vector3Int GetMouseCell()
    {
        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        Vector3 mouseWorld = cam.ScreenToWorldPoint(mouseScreen);
        mouseWorld.z = 0f;
        return buildGrid.WorldToCell(mouseWorld);
    }

    private Vector3 GetPlacePosition(Vector3Int cell, Vector2Int size)
    {
        Vector3 corner = buildGrid.CellToWorld(cell);
        float footprintWidth = size.x * buildGrid.cellSize.x;
        return corner + new Vector3(footprintWidth / 2f, 0f, 0f);
    }

    void OnDrawGizmos()
    {
        if (buildGrid == null) return;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Vector3 cellSize = new Vector3(buildGrid.cellSize.x, buildGrid.cellSize.y, 0f);
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < lanes; y++)
            {
                Vector3 center = buildGrid.GetCellCenterWorld(new Vector3Int(x, y, 0));
                Gizmos.DrawWireCube(center, cellSize);
            }
        }
    }
}