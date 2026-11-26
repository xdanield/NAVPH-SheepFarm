using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BuildMode : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    [Header("Grid")]
    [SerializeField] private LaneLayout layout;
    [Tooltip("Stavat sa da len v tejto lokacii (Location.displayName).")]
    [SerializeField] private string buildLocationName = "Farm";

    [Header("Defense")]
    [SerializeField] private DefenseData selectedDefense;
    [SerializeField] private Color ghostColor = new Color(0.4f, 1f, 0.4f, 0.6f);
    [SerializeField] private Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.6f);

    [Header("Placement rules")]
    [SerializeField] private LayerMask blockingLayers;
    [SerializeField] private Color zoneColor = new Color(0.4f, 0.8f, 1f, 0.15f);

    private InputAction buildAction;
    private Camera cam;
    private SpriteRenderer ghost;
    private Vector3Int hoveredCell;
    private GameObject zoneOverlay;
    private bool canPlace;
    private readonly Dictionary<Vector3Int, GameObject> placed = new Dictionary<Vector3Int, GameObject>();

    void Start()
    {
        buildAction = InputSystem.actions.FindAction("Build");
        cam = Camera.main;
        CreateGhost();
        CreateZoneOverlay();
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

        canPlace = CanPlace(hoveredCell, selectedDefense.Size);
        ghost.color = canPlace ? ghostColor : invalidColor;

        Mouse mouse = Mouse.current;
        if (mouse == null || InventoryUI.PointerOverUI) return;

        if (mouse.leftButton.wasPressedThisFrame && canPlace)
        {
            Place(hoveredCell, selectedDefense);
        }
        else if (mouse.rightButton.wasPressedThisFrame)
        {
            Remove(hoveredCell);
        }
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
        zoneOverlay.SetActive(active);
    }

    private void CreateGhost()
    {
        GameObject ghostObject = new GameObject("BuildGhost");
        ghost = ghostObject.AddComponent<SpriteRenderer>();
        ghost.sortingOrder = 100;
        ghost.color = ghostColor;
    }

    private void CreateZoneOverlay()
    {
        zoneOverlay = new GameObject("BuildZone");
        Grid grid = layout.Grid;

        Texture2D texture = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), texture.width);

        Vector3 tileScale = new Vector3(grid.cellSize.x - 0.1f, grid.cellSize.y - 0.1f, 1f);
        for (int x = 0; x < layout.Columns; x++)
        {
            for (int y = 0; y < layout.Lanes; y++)
            {
                GameObject tile = new GameObject($"Cell {x},{y}");
                tile.transform.SetParent(zoneOverlay.transform);
                tile.transform.position = grid.GetCellCenterWorld(new Vector3Int(x, y, 0));
                tile.transform.localScale = tileScale;

                SpriteRenderer tileRenderer = tile.AddComponent<SpriteRenderer>();
                tileRenderer.sprite = square;
                tileRenderer.color = zoneColor;
                tileRenderer.sortingOrder = 99;
            }
        }
    }

    private bool CanPlace(Vector3Int cell, Vector2Int size)
    {
        if (!IsInsideZone(cell, size)) return false;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                if (IsTaken(cell + new Vector3Int(x, y, 0))) return false;
            }
        }

        return !IsBlocked(cell, size);
    }

    private bool IsInsideZone(Vector3Int cell, Vector2Int size)
    {
        return cell.x >= 0 && cell.y >= 0
            && cell.x + size.x <= layout.Columns
            && cell.y + size.y <= layout.Lanes;
    }

    private bool IsTaken(Vector3Int cell)
    {
        return placed.TryGetValue(cell, out GameObject defense) && defense != null;
    }

    private bool IsBlocked(Vector3Int cell, Vector2Int size)
    {
        Grid grid = layout.Grid;
        Vector2 footprint = new Vector2(size.x * grid.cellSize.x, size.y * grid.cellSize.y);
        Vector2 center = (Vector2)grid.CellToWorld(cell) + footprint / 2f;
        return Physics2D.OverlapBox(center, footprint * 0.9f, 0f, blockingLayers) != null;
    }

    private void Place(Vector3Int cell, DefenseData defense)
    {
        Vector3 position = GetPlacePosition(cell, defense.Size);
        GameObject placedDefense = Instantiate(defense.Prefab, position, Quaternion.identity, layout.transform);

        for (int x = 0; x < defense.Size.x; x++)
        {
            for (int y = 0; y < defense.Size.y; y++)
            {
                placed[cell + new Vector3Int(x, y, 0)] = placedDefense;
            }
        }
    }

    private void Remove(Vector3Int cell)
    {
        if (!placed.TryGetValue(cell, out GameObject defense) || defense == null) return;

        List<Vector3Int> cellsToFree = new List<Vector3Int>();
        foreach (KeyValuePair<Vector3Int, GameObject> entry in placed)
        {
            if (entry.Value == defense)
            {
                cellsToFree.Add(entry.Key);
            }
        }

        foreach (Vector3Int freeCell in cellsToFree)
        {
            placed.Remove(freeCell);
        }

        Destroy(defense);
    }

    private Vector3Int GetMouseCell()
    {
        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        Vector3 mouseWorld = cam.ScreenToWorldPoint(mouseScreen);
        mouseWorld.z = 0f;
        return layout.Grid.WorldToCell(mouseWorld);
    }

    private Vector3 GetPlacePosition(Vector3Int cell, Vector2Int size)
    {
        Vector3 corner = layout.Grid.CellToWorld(cell);
        float footprintWidth = size.x * layout.Grid.cellSize.x;
        return corner + new Vector3(footprintWidth / 2f, 0f, 0f);
    }
}