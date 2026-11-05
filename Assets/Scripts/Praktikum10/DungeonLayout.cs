using System.Collections.Generic;
using UnityEngine;

public enum DungeonCell : byte { Wall, Room, Corridor }

public enum DungeonItemKind { Coin, Potion, Key }

public class DungeonRoom
{
    public int id;
    public RectInt rect;
    public Vector2Int Center => new Vector2Int(rect.x + rect.width / 2, rect.y + rect.height / 2);
    public bool Contains(Vector2Int c) => rect.Contains(c);
}

public struct DungeonEnemyPlan
{
    public Vector2Int cell;
    public float rotationY;
    public Vector2Int[] route;
}

public struct DungeonItemPlan
{
    public DungeonItemKind kind;
    public Vector2Int cell;
    public float rotationY;
}

public struct DungeonPropPlan
{
    public Vector2Int cell;
    public int variant;
    public float rotationY;
}

/// <summary>Hasil BFS / flood fill terhadap satu layout.</summary>
public class DungeonValidation
{
    public bool isValid;
    public bool keyReachable;
    public bool goalReachable;
    public int reachableFloor;
    public int totalFloor;
    public int reachableRooms;
    public int totalRooms;
    public int invalidObjects;
    public int propsOnPath;
    public int enemiesInSafeZone;
    public int pathLength;
    public readonly List<string> issues = new List<string>();
}

/// <summary>Layout dungeon sebagai data murni, tanpa GameObject.</summary>
public class DungeonLayout
{
    public int width;
    public int height;
    public DungeonCell[,] cells;
    public bool[,] blocked;

    public readonly List<RectInt> leaves = new List<RectInt>();
    public readonly List<DungeonRoom> rooms = new List<DungeonRoom>();
    public readonly List<Vector2Int> connections = new List<Vector2Int>();

    public DungeonRoom startRoom;
    public DungeonRoom keyRoom;
    public DungeonRoom goalRoom;
    public Vector2Int start;
    public Vector2Int key;
    public Vector2Int goal;

    public List<Vector2Int> pathToKey = new List<Vector2Int>();
    public List<Vector2Int> pathToGoal = new List<Vector2Int>();

    public readonly List<DungeonPropPlan> props = new List<DungeonPropPlan>();
    public readonly List<DungeonEnemyPlan> enemies = new List<DungeonEnemyPlan>();
    public readonly List<DungeonItemPlan> items = new List<DungeonItemPlan>();
    public readonly List<Vector2Int> safePoints = new List<Vector2Int>();

    public int requestedEnemies;
    public int requestedItems;
    public int requestedProps;
    public int rejectedSafeZone;
    public int rejectedPath;
    public int rejectedSpacing;
    public int rejectedBlocking;

    public DungeonValidation validation;

    public int FloorCount
    {
        get
        {
            int n = 0;
            foreach (DungeonCell c in cells)
                if (c != DungeonCell.Wall)
                    n++;
            return n;
        }
    }

    public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;
    public bool IsFloor(Vector2Int c) => InBounds(c) && cells[c.x, c.y] != DungeonCell.Wall;
    public bool IsWalkable(Vector2Int c) => IsFloor(c) && !blocked[c.x, c.y];

    /// <summary>Sidik jari layout. Nilai yang sama berarti dungeon identik, untuk uji seed yang sama.</summary>
    public string Hash
    {
        get
        {
            unchecked
            {
                uint h = 2166136261;
                void Add(int v) { h = (h ^ (uint)v) * 16777619; }
                void AddCell(Vector2Int c) { Add(c.x); Add(c.y); }

                Add(width);
                Add(height);
                foreach (DungeonCell c in cells)
                    Add((int)c);
                AddCell(start);
                AddCell(key);
                AddCell(goal);
                foreach (DungeonPropPlan p in props)
                    AddCell(p.cell);
                foreach (DungeonEnemyPlan e in enemies)
                    AddCell(e.cell);
                foreach (DungeonItemPlan i in items)
                {
                    Add((int)i.kind);
                    AddCell(i.cell);
                }
                return h.ToString("X8");
            }
        }
    }
}

/// <summary>Layout terpilih beserta catatan percobaan generation-nya.</summary>
public class DungeonResult
{
    public DungeonLayout layout;
    public int seed;
    public bool isValid;
    public int attemptsUsed;
    public bool repaired;
    public int carvedCells;
    public bool sabotaged;
    public readonly List<string> log = new List<string>();
}
