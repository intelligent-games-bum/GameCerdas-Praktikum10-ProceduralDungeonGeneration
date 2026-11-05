using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pemeriksaan konektivitas dengan BFS / flood fill pada grid 4 arah.
/// Prop menghalangi jalan, sedangkan item dan enemy tidak.
/// </summary>
public static class DungeonValidator
{
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left,
    };

    private static bool Passable(DungeonLayout layout, Vector2Int cell, bool ignoreProps)
    {
        return ignoreProps ? layout.IsFloor(cell) : layout.IsWalkable(cell);
    }

    /// <summary>Jarak BFS dari source ke setiap cell. Nilai -1 berarti tidak terjangkau.</summary>
    public static int[,] Distances(DungeonLayout layout, Vector2Int source, bool ignoreProps = false)
    {
        var dist = new int[layout.width, layout.height];
        for (int x = 0; x < layout.width; x++)
            for (int y = 0; y < layout.height; y++)
                dist[x, y] = -1;

        if (!Passable(layout, source, ignoreProps))
            return dist;

        var queue = new Queue<Vector2Int>();
        queue.Enqueue(source);
        dist[source.x, source.y] = 0;
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            foreach (Vector2Int d in Directions)
            {
                Vector2Int n = c + d;
                if (!Passable(layout, n, ignoreProps) || dist[n.x, n.y] >= 0)
                    continue;
                dist[n.x, n.y] = dist[c.x, c.y] + 1;
                queue.Enqueue(n);
            }
        }
        return dist;
    }

    /// <summary>Jalur terpendek dari cell berjarak 0 ke target. Kosong jika tidak terjangkau.</summary>
    public static List<Vector2Int> PathTo(DungeonLayout layout, int[,] dist, Vector2Int target)
    {
        var path = new List<Vector2Int>();
        if (!layout.InBounds(target) || dist[target.x, target.y] < 0)
            return path;

        Vector2Int c = target;
        path.Add(c);
        while (dist[c.x, c.y] > 0)
        {
            foreach (Vector2Int d in Directions)
            {
                Vector2Int n = c + d;
                if (layout.InBounds(n) && dist[n.x, n.y] == dist[c.x, c.y] - 1)
                {
                    c = n;
                    path.Add(c);
                    break;
                }
            }
        }
        path.Reverse();
        return path;
    }

    public static void RecomputePaths(DungeonLayout layout)
    {
        layout.pathToKey = PathTo(layout, Distances(layout, layout.start), layout.key);
        layout.pathToGoal = PathTo(layout, Distances(layout, layout.key), layout.goal);
    }

    public static DungeonValidation Validate(DungeonLayout layout, DungeonConfig config)
    {
        var v = new DungeonValidation();
        int[,] dist = Distances(layout, layout.start);
        bool Reachable(Vector2Int c) => layout.InBounds(c) && dist[c.x, c.y] >= 0;

        if (!layout.IsWalkable(layout.start))
            v.issues.Add("Start tidak berada di floor yang valid");

        v.keyReachable = Reachable(layout.key);
        if (!v.keyReachable)
            v.issues.Add("Key tidak terjangkau dari start");

        v.goalReachable = Reachable(layout.goal);
        if (!v.goalReachable)
            v.issues.Add("Goal tidak terjangkau dari start");

        for (int x = 0; x < layout.width; x++)
        {
            for (int y = 0; y < layout.height; y++)
            {
                if (!layout.IsWalkable(new Vector2Int(x, y)))
                    continue;
                v.totalFloor++;
                if (dist[x, y] >= 0)
                    v.reachableFloor++;
            }
        }
        if (v.reachableFloor < v.totalFloor)
            v.issues.Add($"{v.totalFloor - v.reachableFloor} cell floor terputus");

        v.totalRooms = layout.rooms.Count;
        foreach (DungeonRoom room in layout.rooms)
        {
            if (Reachable(room.Center))
                v.reachableRooms++;
        }
        if (v.reachableRooms < v.totalRooms)
            v.issues.Add($"{v.totalRooms - v.reachableRooms} room wajib terputus");

        foreach (DungeonItemPlan item in layout.items)
            if (!Reachable(item.cell))
                v.invalidObjects++;
        foreach (DungeonEnemyPlan enemy in layout.enemies)
        {
            if (!Reachable(enemy.cell))
                v.invalidObjects++;
            if (Vector2Int.Distance(enemy.cell, layout.start) < config.safeRadius)
                v.enemiesInSafeZone++;
        }
        if (v.invalidObjects > 0)
            v.issues.Add($"{v.invalidObjects} objek berada di luar floor yang terjangkau");
        if (v.enemiesInSafeZone > 0)
            v.issues.Add($"{v.enemiesInSafeZone} enemy berada di zona aman");

        RecomputePaths(layout);
        var mandatory = new HashSet<Vector2Int>(layout.pathToKey);
        mandatory.UnionWith(layout.pathToGoal);
        foreach (DungeonPropPlan prop in layout.props)
            if (mandatory.Contains(prop.cell))
                v.propsOnPath++;
        if (v.propsOnPath > 0)
            v.issues.Add($"{v.propsOnPath} prop menghalangi jalur wajib");

        v.pathLength = layout.pathToKey.Count + layout.pathToGoal.Count;
        v.isValid = v.issues.Count == 0;
        return v;
    }
}
