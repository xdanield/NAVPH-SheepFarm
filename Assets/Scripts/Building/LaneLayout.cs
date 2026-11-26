using UnityEngine;

public class LaneLayout : MonoBehaviour
{
    [SerializeField] private int lanes = 9;
    [SerializeField] private int columns = 5;
    [SerializeField] private int spawnColumn = -4;
    [SerializeField] private float walkHeight = 0.5f;

    public int Lanes => lanes;
    public int Columns => columns;
    public Grid Grid => GetComponent<Grid>();

    public float GetLaneY(int lane)
    {
        Vector3 laneBottom = Grid.CellToWorld(new Vector3Int(0, lane, 0));
        return laneBottom.y + walkHeight;
    }

    public float NearestLaneY(float y)
    {
        float nearest = GetLaneY(0);
        for (int lane = 1; lane < lanes; lane++)
        {
            float laneY = GetLaneY(lane);
            if (Mathf.Abs(laneY - y) < Mathf.Abs(nearest - y))
            {
                nearest = laneY;
            }
        }
        return nearest;
    }

    public Vector3 GetSpawnPosition(int lane)
    {
        Vector3 spawnCell = Grid.GetCellCenterWorld(new Vector3Int(spawnColumn, lane, 0));
        return new Vector3(spawnCell.x, GetLaneY(lane), 0f);
    }

    [ContextMenu("Place zombies at spawn points")]
    private void PlaceZombiesAtSpawnPoints()
    {
        ZombieAI[] zombies = FindObjectsByType<ZombieAI>(FindObjectsSortMode.None);
        for (int i = 0; i < zombies.Length; i++)
        {
            int lane = i % lanes;
            zombies[i].transform.position = GetSpawnPosition(lane);
        }
    }

    void OnDrawGizmos()
    {
        Vector3 cellSize = new Vector3(Grid.cellSize.x, Grid.cellSize.y, 0f);

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < lanes; y++)
            {
                Vector3 cellCenter = Grid.GetCellCenterWorld(new Vector3Int(x, y, 0));
                Gizmos.DrawWireCube(cellCenter, cellSize);
            }
        }

        for (int lane = 0; lane < lanes; lane++)
        {
            Vector3 spawn = GetSpawnPosition(lane);
            float endX = Grid.CellToWorld(new Vector3Int(columns, lane, 0)).x;
            Vector3 end = new Vector3(endX, spawn.y, 0f);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(spawn, end);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(spawn, 0.3f);
        }
    }
}