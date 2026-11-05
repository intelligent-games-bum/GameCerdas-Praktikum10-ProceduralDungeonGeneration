using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

/// <summary>
/// Generator dungeon room-corridor berbasis Binary Space Partitioning. Script ini tidak membuat GameObject.
///
/// Urutan kerja:
/// 1. BSP membelah peta menjadi partisi, lalu setiap partisi daun diberi satu room.
/// 2. Room dari dua anak setiap node BSP dihubungkan dengan corridor berbentuk L.
/// 3. Start dan goal dipilih dari dua room yang berjauhan menurut BFS, key dipilih sebagai jalur memutar.
/// 4. Prop, enemy, dan item ditempatkan dengan aturan zona aman, jalur wajib, dan jarak antar objek.
/// 5. Flood fill memvalidasi hasilnya. Jika gagal, layout di-repair atau di-generate ulang.
///
/// Setiap tahap memakai System.Random sendiri yang diturunkan dari seed. UnityEngine.Random tidak dipakai
/// karena digunakan bersama script lain. Mengubah jumlah enemy pun tidak menggeser bentuk layout.
/// </summary>
public static class BspDungeonGenerator
{
    private const int MinimumRooms = 3;

    private class Node
    {
        public RectInt area;
        public Node left;
        public Node right;
        public DungeonRoom room;
        public bool IsLeaf => left == null;
    }

    // ------------------------------------------------------------------ Entry point

    /// <param name="sabotage">Memutus corridor pada percobaan pertama, untuk mendemonstrasikan validasi dan repair.</param>
    public static DungeonResult Generate(DungeonConfig config, bool sabotage = false)
    {
        config.Clamp();
        var result = new DungeonResult { seed = config.seed };

        for (int attempt = 1; attempt <= config.maxGenerationAttempts; attempt++)
        {
            result.attemptsUsed = attempt;
            int attemptSeed = Mix(config.seed, attempt - 1);

            DungeonLayout layout = BuildLayout(config, attemptSeed);
            if (layout.rooms.Count < MinimumRooms)
            {
                result.log.Add($"Percobaan {attempt}: hanya {layout.rooms.Count} room (minimal {MinimumRooms}), generate ulang");
                continue;
            }

            ChooseEndpoints(layout, new Random(Mix(attemptSeed, 2)));
            PlaceProps(layout, config, new Random(Mix(attemptSeed, 3)));
            PlaceEnemies(layout, config, new Random(Mix(attemptSeed, 4)));
            PlaceItems(layout, config, new Random(Mix(attemptSeed, 5)));
            PlaceSafePoints(layout, new Random(Mix(attemptSeed, 6)));

            if (sabotage && attempt == 1)
            {
                result.sabotaged = CutMandatoryCorridor(layout, config, new Random(Mix(attemptSeed, 7)));
                result.log.Add(result.sabotaged
                    ? "Uji validasi: corridor pada jalur wajib diputus"
                    : "Uji validasi: jalur memutar menjaga goal tetap terjangkau");
            }

            layout.validation = DungeonValidator.Validate(layout, config);
            if (!layout.validation.isValid)
            {
                result.log.Add($"Percobaan {attempt}: GAGAL - {string.Join("; ", layout.validation.issues)}");
                int carved = Repair(layout, config);
                layout.validation = DungeonValidator.Validate(layout, config);
                result.repaired = true;
                result.carvedCells += carved;
                result.log.Add(layout.validation.isValid
                    ? $"Repair berhasil: {carved} cell corridor baru"
                    : $"Repair gagal: {string.Join("; ", layout.validation.issues)}");
            }

            if (!layout.validation.isValid)
                continue;

            result.layout = layout;
            result.isValid = true;
            result.log.Add($"Percobaan {attempt}: VALID");
            return result;
        }

        result.log.Add($"Gagal setelah {config.maxGenerationAttempts} percobaan");
        return result;
    }

    private static int Mix(int value, int salt)
    {
        unchecked
        {
            return (value * 397) ^ (salt * 486187739 + 12345);
        }
    }

    // ------------------------------------------------------------------ BSP, room, corridor

    private static DungeonLayout BuildLayout(DungeonConfig config, int attemptSeed)
    {
        var layout = new DungeonLayout
        {
            width = config.mapWidth,
            height = config.mapHeight,
            cells = new DungeonCell[config.mapWidth, config.mapHeight],
            blocked = new bool[config.mapWidth, config.mapHeight],
            requestedEnemies = config.enemyCount,
            requestedItems = config.itemCount,
            requestedProps = config.propCount,
        };

        var rng = new Random(Mix(attemptSeed, 1));
        var root = new Node { area = new RectInt(0, 0, config.mapWidth, config.mapHeight) };
        Split(root, 0, config, rng);
        CreateRooms(root, layout, config, rng);
        Connect(root, layout, config, rng);
        AddExtraConnections(layout, config, rng);
        return layout;
    }

    private static void Split(Node node, int depth, DungeonConfig config, Random rng)
    {
        if (depth >= config.maxSplitDepth)
            return;

        int w = node.area.width;
        int h = node.area.height;
        bool canCutWidth = w >= config.minLeafSize * 2;
        bool canCutHeight = h >= config.minLeafSize * 2;
        if (!canCutWidth && !canCutHeight)
            return;

        // Partisi yang memanjang dibelah pada sisi panjangnya, agar room tidak terlalu pipih.
        bool cutWidth;
        if (canCutWidth && canCutHeight)
        {
            if (w > h * 1.25f)
                cutWidth = true;
            else if (h > w * 1.25f)
                cutWidth = false;
            else
                cutWidth = rng.Next(2) == 0;
        }
        else
        {
            cutWidth = canCutWidth;
        }

        RectInt a = node.area;
        if (cutWidth)
        {
            int cut = rng.Next(config.minLeafSize, w - config.minLeafSize + 1);
            node.left = new Node { area = new RectInt(a.x, a.y, cut, h) };
            node.right = new Node { area = new RectInt(a.x + cut, a.y, w - cut, h) };
        }
        else
        {
            int cut = rng.Next(config.minLeafSize, h - config.minLeafSize + 1);
            node.left = new Node { area = new RectInt(a.x, a.y, w, cut) };
            node.right = new Node { area = new RectInt(a.x, a.y + cut, w, h - cut) };
        }

        Split(node.left, depth + 1, config, rng);
        Split(node.right, depth + 1, config, rng);
    }

    private static void CreateRooms(Node node, DungeonLayout layout, DungeonConfig config, Random rng)
    {
        if (!node.IsLeaf)
        {
            CreateRooms(node.left, layout, config, rng);
            CreateRooms(node.right, layout, config, rng);
            return;
        }

        layout.leaves.Add(node.area);

        int freeW = node.area.width - config.roomPadding * 2;
        int freeH = node.area.height - config.roomPadding * 2;
        int maxW = Mathf.Min(config.roomMaxSize, freeW);
        int maxH = Mathf.Min(config.roomMaxSize, freeH);
        if (maxW < config.roomMinSize || maxH < config.roomMinSize)
            return;

        int w = rng.Next(config.roomMinSize, maxW + 1);
        int h = rng.Next(config.roomMinSize, maxH + 1);
        int x = node.area.x + config.roomPadding + rng.Next(freeW - w + 1);
        int y = node.area.y + config.roomPadding + rng.Next(freeH - h + 1);

        var room = new DungeonRoom { id = layout.rooms.Count, rect = new RectInt(x, y, w, h) };
        node.room = room;
        layout.rooms.Add(room);

        for (int cx = x; cx < x + w; cx++)
            for (int cy = y; cy < y + h; cy++)
                layout.cells[cx, cy] = DungeonCell.Room;
    }

    /// <summary>Menghubungkan pasangan room terdekat dari dua anak, sehingga seluruh pohon BSP tersambung.</summary>
    private static void Connect(Node node, DungeonLayout layout, DungeonConfig config, Random rng)
    {
        if (node.IsLeaf)
            return;

        Connect(node.left, layout, config, rng);
        Connect(node.right, layout, config, rng);

        var roomsA = new List<DungeonRoom>();
        var roomsB = new List<DungeonRoom>();
        Collect(node.left, roomsA);
        Collect(node.right, roomsB);

        DungeonRoom bestA = null;
        DungeonRoom bestB = null;
        int best = int.MaxValue;
        foreach (DungeonRoom a in roomsA)
        {
            foreach (DungeonRoom b in roomsB)
            {
                int d = Manhattan(a.Center, b.Center);
                if (d < best)
                {
                    best = d;
                    bestA = a;
                    bestB = b;
                }
            }
        }

        if (bestA == null)
            return;
        CarveCorridor(layout, bestA.Center, bestB.Center, config.corridorWidth, rng);
        layout.connections.Add(new Vector2Int(bestA.id, bestB.id));
    }

    private static void AddExtraConnections(DungeonLayout layout, DungeonConfig config, Random rng)
    {
        for (int i = 0; i < config.extraConnections && layout.rooms.Count > 2; i++)
        {
            DungeonRoom a = layout.rooms[rng.Next(layout.rooms.Count)];
            DungeonRoom nearest = null;
            int best = int.MaxValue;
            foreach (DungeonRoom b in layout.rooms)
            {
                if (b == a || AreConnected(layout, a, b))
                    continue;
                int d = Manhattan(a.Center, b.Center);
                if (d < best)
                {
                    best = d;
                    nearest = b;
                }
            }
            if (nearest == null)
                continue;
            CarveCorridor(layout, a.Center, nearest.Center, config.corridorWidth, rng);
            layout.connections.Add(new Vector2Int(a.id, nearest.id));
        }
    }

    private static bool AreConnected(DungeonLayout layout, DungeonRoom a, DungeonRoom b)
    {
        foreach (Vector2Int c in layout.connections)
            if ((c.x == a.id && c.y == b.id) || (c.x == b.id && c.y == a.id))
                return true;
        return false;
    }

    private static void Collect(Node node, List<DungeonRoom> into)
    {
        if (node.IsLeaf)
        {
            if (node.room != null)
                into.Add(node.room);
            return;
        }
        Collect(node.left, into);
        Collect(node.right, into);
    }

    private static int CarveCorridor(DungeonLayout layout, Vector2Int from, Vector2Int to, int width, Random rng)
    {
        Vector2Int corner = rng.Next(2) == 0
            ? new Vector2Int(to.x, from.y)
            : new Vector2Int(from.x, to.y);
        return CarveLine(layout, from, corner, width) + CarveLine(layout, corner, to, width);
    }

    private static int CarveLine(DungeonLayout layout, Vector2Int from, Vector2Int to, int width)
    {
        int carved = 0;
        Vector2Int step = new Vector2Int(System.Math.Sign(to.x - from.x), System.Math.Sign(to.y - from.y));
        Vector2Int c = from;
        while (true)
        {
            for (int dx = -(width - 1) / 2; dx <= width / 2; dx++)
            {
                for (int dy = -(width - 1) / 2; dy <= width / 2; dy++)
                {
                    int x = c.x + dx;
                    int y = c.y + dy;
                    // Baris tepi peta tetap menjadi dinding.
                    if (x < 1 || y < 1 || x >= layout.width - 1 || y >= layout.height - 1)
                        continue;
                    if (layout.cells[x, y] != DungeonCell.Wall)
                        continue;
                    layout.cells[x, y] = DungeonCell.Corridor;
                    carved++;
                }
            }
            if (c == to)
                return carved;
            c += step;
        }
    }

    private static int Manhattan(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    // ------------------------------------------------------------------ Start, key, goal

    private static void ChooseEndpoints(DungeonLayout layout, Random rng)
    {
        // Dua kali sapuan BFS memberi dua room yang saling berjauhan.
        DungeonRoom pivot = layout.rooms[rng.Next(layout.rooms.Count)];
        layout.startRoom = FarthestRoom(layout, pivot, null);
        layout.goalRoom = FarthestRoom(layout, layout.startRoom, null);

        // Key berada di room yang paling memaksa jalur memutar, dipilih acak dari dua terbaik.
        int[,] fromStart = DungeonValidator.Distances(layout, layout.startRoom.Center);
        int[,] fromGoal = DungeonValidator.Distances(layout, layout.goalRoom.Center);
        var candidates = new List<DungeonRoom>();
        foreach (DungeonRoom room in layout.rooms)
            if (room != layout.startRoom && room != layout.goalRoom)
                candidates.Add(room);

        int Score(DungeonRoom r) => fromStart[r.Center.x, r.Center.y] + fromGoal[r.Center.x, r.Center.y];
        candidates.Sort((a, b) =>
        {
            int order = Score(b).CompareTo(Score(a));
            return order != 0 ? order : a.id.CompareTo(b.id);
        });
        layout.keyRoom = candidates[rng.Next(Mathf.Min(2, candidates.Count))];

        layout.start = layout.startRoom.Center;
        layout.goal = layout.goalRoom.Center;
        layout.key = RandomRoomCell(layout.keyRoom, rng, inset: 1);
        DungeonValidator.RecomputePaths(layout);
    }

    private static DungeonRoom FarthestRoom(DungeonLayout layout, DungeonRoom from, DungeonRoom exclude)
    {
        int[,] dist = DungeonValidator.Distances(layout, from.Center);
        DungeonRoom best = from;
        int bestDist = -1;
        foreach (DungeonRoom room in layout.rooms)
        {
            if (room == exclude)
                continue;
            int d = dist[room.Center.x, room.Center.y];
            if (d > bestDist)
            {
                bestDist = d;
                best = room;
            }
        }
        return best;
    }

    private static Vector2Int RandomRoomCell(DungeonRoom room, Random rng, int inset = 0)
    {
        RectInt r = room.rect;
        int ix = Mathf.Min(inset, (r.width - 1) / 2);
        int iy = Mathf.Min(inset, (r.height - 1) / 2);
        return new Vector2Int(
            rng.Next(r.x + ix, r.x + r.width - ix),
            rng.Next(r.y + iy, r.y + r.height - iy));
    }

    // ------------------------------------------------------------------ Prop, enemy, item

    private static HashSet<Vector2Int> MandatoryCells(DungeonLayout layout)
    {
        var cells = new HashSet<Vector2Int>(layout.pathToKey);
        cells.UnionWith(layout.pathToGoal);
        return cells;
    }

    private static List<Vector2Int> OccupiedCells(DungeonLayout layout)
    {
        var occupied = new List<Vector2Int> { layout.start, layout.key, layout.goal };
        foreach (DungeonPropPlan p in layout.props)
            occupied.Add(p.cell);
        foreach (DungeonEnemyPlan e in layout.enemies)
            occupied.Add(e.cell);
        foreach (DungeonItemPlan i in layout.items)
            occupied.Add(i.cell);
        return occupied;
    }

    private static bool TooClose(Vector2Int cell, List<Vector2Int> occupied, int spacing)
    {
        foreach (Vector2Int c in occupied)
            if (Vector2Int.Distance(cell, c) < spacing)
                return true;
        return false;
    }

    private static void PlaceProps(DungeonLayout layout, DungeonConfig config, Random rng)
    {
        HashSet<Vector2Int> mandatory = MandatoryCells(layout);
        int walkable = layout.FloorCount;

        for (int i = 0; i < config.propCount; i++)
        {
            for (int attempt = 0; attempt < config.maxPlacementAttempts; attempt++)
            {
                DungeonRoom room = layout.rooms[rng.Next(layout.rooms.Count)];
                Vector2Int cell = RandomRoomCell(room, rng);
                if (cell == room.Center)
                    continue;
                if (Vector2Int.Distance(cell, layout.start) < config.safeRadius)
                {
                    layout.rejectedSafeZone++;
                    continue;
                }
                if (mandatory.Contains(cell))
                {
                    layout.rejectedPath++;
                    continue;
                }
                if (TouchesCorridor(layout, cell) || TooClose(cell, OccupiedCells(layout), 2))
                {
                    layout.rejectedSpacing++;
                    continue;
                }

                // Prop yang memutus area mana pun ditolak.
                layout.blocked[cell.x, cell.y] = true;
                if (CountReachable(layout) != walkable - 1)
                {
                    layout.blocked[cell.x, cell.y] = false;
                    layout.rejectedBlocking++;
                    continue;
                }

                walkable--;
                layout.props.Add(new DungeonPropPlan
                {
                    cell = cell,
                    variant = rng.Next(1000),
                    rotationY = rng.Next(4) * 90f,
                });
                break;
            }
        }
    }

    private static bool TouchesCorridor(DungeonLayout layout, Vector2Int cell)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                Vector2Int n = cell + new Vector2Int(dx, dy);
                if (layout.InBounds(n) && layout.cells[n.x, n.y] == DungeonCell.Corridor)
                    return true;
            }
        }
        return false;
    }

    private static int CountReachable(DungeonLayout layout)
    {
        int count = 0;
        foreach (int d in DungeonValidator.Distances(layout, layout.start))
            if (d >= 0)
                count++;
        return count;
    }

    private static void PlaceEnemies(DungeonLayout layout, DungeonConfig config, Random rng)
    {
        HashSet<Vector2Int> mandatory = MandatoryCells(layout);
        for (int i = 0; i < config.enemyCount; i++)
        {
            for (int attempt = 0; attempt < config.maxPlacementAttempts; attempt++)
            {
                DungeonRoom room = layout.rooms[rng.Next(layout.rooms.Count)];
                Vector2Int cell = RandomRoomCell(room, rng, inset: 1);
                if (room == layout.startRoom || Vector2Int.Distance(cell, layout.start) < config.safeRadius)
                {
                    layout.rejectedSafeZone++;
                    continue;
                }
                if (!CanPlace(layout, cell, mandatory, config.minSpacing))
                    continue;

                layout.enemies.Add(new DungeonEnemyPlan
                {
                    cell = cell,
                    rotationY = rng.Next(360),
                    route = BuildPatrolRoute(layout, room),
                });
                break;
            }
        }
    }

    /// <summary>Rute patroli: room sendiri dan room tetangganya, tanpa masuk ke room start.</summary>
    private static Vector2Int[] BuildPatrolRoute(DungeonLayout layout, DungeonRoom room)
    {
        var route = new List<Vector2Int> { room.Center };
        foreach (Vector2Int link in layout.connections)
        {
            int other = link.x == room.id ? link.y : link.y == room.id ? link.x : -1;
            if (other < 0 || layout.rooms[other] == layout.startRoom)
                continue;
            route.Add(layout.rooms[other].Center);
        }

        // Tanpa tetangga, enemy berpatroli pada sudut room-nya sendiri.
        if (route.Count == 1)
        {
            RectInt r = room.rect;
            route.Add(new Vector2Int(r.x, r.y));
            route.Add(new Vector2Int(r.xMax - 1, r.yMax - 1));
            route.RemoveAll(c => !layout.IsWalkable(c));
        }
        return route.ToArray();
    }

    private static void PlaceItems(DungeonLayout layout, DungeonConfig config, Random rng)
    {
        layout.items.Add(new DungeonItemPlan { kind = DungeonItemKind.Key, cell = layout.key, rotationY = rng.Next(360) });

        HashSet<Vector2Int> mandatory = MandatoryCells(layout);
        for (int i = 0; i < config.itemCount; i++)
        {
            for (int attempt = 0; attempt < config.maxPlacementAttempts; attempt++)
            {
                DungeonRoom room = layout.rooms[rng.Next(layout.rooms.Count)];
                Vector2Int cell = RandomRoomCell(room, rng);
                if (!CanPlace(layout, cell, mandatory, config.minSpacing))
                    continue;

                layout.items.Add(new DungeonItemPlan
                {
                    kind = rng.NextDouble() < config.potionChance ? DungeonItemKind.Potion : DungeonItemKind.Coin,
                    cell = cell,
                    rotationY = rng.Next(360),
                });
                break;
            }
        }
    }

    private static bool CanPlace(DungeonLayout layout, Vector2Int cell, HashSet<Vector2Int> mandatory, int spacing)
    {
        if (!layout.IsWalkable(cell))
        {
            layout.rejectedBlocking++;
            return false;
        }
        if (mandatory.Contains(cell))
        {
            layout.rejectedPath++;
            return false;
        }
        if (TooClose(cell, OccupiedCells(layout), spacing))
        {
            layout.rejectedSpacing++;
            return false;
        }
        return true;
    }

    private static void PlaceSafePoints(DungeonLayout layout, Random rng)
    {
        var candidates = new List<DungeonRoom>();
        foreach (DungeonRoom room in layout.rooms)
            if (room != layout.startRoom && room != layout.goalRoom)
                candidates.Add(room);

        // Fisher-Yates agar pilihan room mengikuti seed.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
        for (int i = 0; i < Mathf.Min(2, candidates.Count); i++)
            layout.safePoints.Add(candidates[i].Center);
    }

    // ------------------------------------------------------------------ Uji validasi dan repair

    /// <summary>Memutus corridor pada jalur wajib. Dicoba di beberapa titik karena jalur memutar bisa tetap terhubung.</summary>
    private static bool CutMandatoryCorridor(DungeonLayout layout, DungeonConfig config, Random rng)
    {
        var path = new List<Vector2Int>();
        foreach (Vector2Int c in MandatoryCells(layout))
            if (layout.cells[c.x, c.y] == DungeonCell.Corridor)
                path.Add(c);
        path.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        int radius = config.corridorWidth;
        for (int attempt = 0; attempt < 40 && path.Count > 0; attempt++)
        {
            Vector2Int center = path[rng.Next(path.Count)];
            var removed = new List<Vector2Int>();
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    Vector2Int c = center + new Vector2Int(dx, dy);
                    if (layout.InBounds(c) && layout.cells[c.x, c.y] == DungeonCell.Corridor)
                    {
                        layout.cells[c.x, c.y] = DungeonCell.Wall;
                        removed.Add(c);
                    }
                }
            }

            int[,] dist = DungeonValidator.Distances(layout, layout.start);
            if (dist[layout.key.x, layout.key.y] < 0 || dist[layout.goal.x, layout.goal.y] < 0)
                return true;

            foreach (Vector2Int c in removed)
                layout.cells[c.x, c.y] = DungeonCell.Corridor;
        }
        return false;
    }

    /// <summary>Menyambungkan setiap area yang terputus ke area utama dengan corridor baru.</summary>
    private static int Repair(DungeonLayout layout, DungeonConfig config)
    {
        int carved = 0;
        var rng = new Random(Mix(config.seed, 8));

        for (int guard = 0; guard < 32; guard++)
        {
            int[,] dist = DungeonValidator.Distances(layout, layout.start, ignoreProps: true);
            var reachable = new List<Vector2Int>();
            var orphan = new List<Vector2Int>();
            for (int x = 0; x < layout.width; x++)
            {
                for (int y = 0; y < layout.height; y++)
                {
                    if (layout.cells[x, y] == DungeonCell.Wall)
                        continue;
                    (dist[x, y] >= 0 ? reachable : orphan).Add(new Vector2Int(x, y));
                }
            }
            if (orphan.Count == 0 || reachable.Count == 0)
                break;

            Vector2Int target = orphan[0];
            Vector2Int nearest = reachable[0];
            int best = int.MaxValue;
            foreach (Vector2Int c in reachable)
            {
                int d = Manhattan(c, target);
                if (d < best)
                {
                    best = d;
                    nearest = c;
                }
            }
            carved += CarveCorridor(layout, nearest, target, config.corridorWidth, rng);
        }

        // Corridor baru bisa melewati prop, jadi prop yang masih memutus area dilepas.
        DungeonValidation check = DungeonValidator.Validate(layout, config);
        if (check.reachableFloor < check.totalFloor || check.propsOnPath > 0
            || !check.keyReachable || !check.goalReachable)
        {
            foreach (DungeonPropPlan prop in layout.props)
                layout.blocked[prop.cell.x, prop.cell.y] = false;
            layout.props.Clear();
        }
        return carved;
    }
}
