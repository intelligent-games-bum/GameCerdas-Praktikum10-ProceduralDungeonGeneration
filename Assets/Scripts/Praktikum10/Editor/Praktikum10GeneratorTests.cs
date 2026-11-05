using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Uji generator tanpa perlu menekan Play: determinisme, validitas, variasi, aturan penempatan, dan repair.
/// Menu "Praktikum 10/Run Generator Tests". Hasilnya tampil di Console.
/// </summary>
public static class Praktikum10GeneratorTests
{
    private const int SeedCount = 200;

    [MenuItem("Praktikum 10/Run Generator Tests")]
    private static void RunFromMenu()
    {
        RunAll();
    }

    /// <summary>Entry point command line: Unity -batchmode -executeMethod Praktikum10GeneratorTests.RunBatch</summary>
    public static void RunBatch()
    {
        EditorApplication.Exit(RunAll() ? 0 : 1);
    }

    private static bool RunAll()
    {
        var presets = new (string name, DungeonConfig config)[]
        {
            ("Default", new DungeonConfig()),
            ("Peta kecil", new DungeonConfig { mapWidth = 24, mapHeight = 24, enemyCount = 3, itemCount = 3, propCount = 4 }),
            ("Peta besar", new DungeonConfig { mapWidth = 64, mapHeight = 64, maxSplitDepth = 6 }),
            ("Corridor sempit", new DungeonConfig { corridorWidth = 1, extraConnections = 0 }),
            ("Corridor lebar", new DungeonConfig { corridorWidth = 4, extraConnections = 3 }),
            ("Room besar", new DungeonConfig { roomMinSize = 7, roomMaxSize = 14, minLeafSize = 14 }),
            ("Padat", new DungeonConfig { enemyCount = 20, itemCount = 20, propCount = 30, minSpacing = 2 }),
        };

        bool passed = true;
        foreach ((string name, DungeonConfig config) in presets)
            passed &= RunPreset(name, config);

        Debug.Log(passed ? "[Praktikum 10] SEMUA UJI LULUS" : "[Praktikum 10] ADA UJI YANG GAGAL");
        return passed;
    }

    private static bool RunPreset(string name, DungeonConfig baseConfig)
    {
        var hashes = new HashSet<string>();
        int invalid = 0;
        int nonDeterministic = 0;
        int ruleViolations = 0;
        int detected = 0;
        int repaired = 0;
        int sabotageUnrepaired = 0;
        int totalRooms = 0;
        int totalAttempts = 0;
        int enemySpawned = 0;
        int enemyRequested = 0;

        for (int seed = 1; seed <= SeedCount; seed++)
        {
            DungeonConfig config = baseConfig.Clone();
            config.seed = seed;

            DungeonResult first = BspDungeonGenerator.Generate(config);
            DungeonResult second = BspDungeonGenerator.Generate(config);
            totalAttempts += first.attemptsUsed;

            if (!first.isValid)
            {
                invalid++;
                continue;
            }

            if (!second.isValid || first.layout.Hash != second.layout.Hash)
                nonDeterministic++;

            DungeonLayout layout = first.layout;
            hashes.Add(layout.Hash);
            totalRooms += layout.rooms.Count;
            enemySpawned += layout.enemies.Count;
            enemyRequested += layout.requestedEnemies;
            ruleViolations += CountRuleViolations(layout, config);

            DungeonResult broken = BspDungeonGenerator.Generate(config, sabotage: true);
            if (broken.sabotaged)
            {
                detected++;
                if (broken.isValid && broken.repaired)
                    repaired++;
                else
                    sabotageUnrepaired++;
            }
        }

        bool ok = invalid == 0 && nonDeterministic == 0 && ruleViolations == 0 && sabotageUnrepaired == 0
                  && hashes.Count > SeedCount * 0.9f;
        int validRuns = SeedCount - invalid;
        string line =
            $"[Praktikum 10] {(ok ? "LULUS" : "GAGAL")} {name}: valid {validRuns}/{SeedCount}, " +
            $"layout unik {hashes.Count}, tidak deterministik {nonDeterministic}, pelanggaran aturan {ruleViolations}, " +
            $"rata-rata room {(validRuns > 0 ? totalRooms / (float)validRuns : 0f):0.0}, " +
            $"rata-rata percobaan {totalAttempts / (float)SeedCount:0.00}, " +
            $"enemy {enemySpawned}/{enemyRequested}, corridor putus {detected}, repair berhasil {repaired}";

        if (ok)
            Debug.Log(line);
        else
            Debug.LogError(line);
        return ok;
    }

    /// <summary>Pemeriksaan ulang dari luar generator, agar kesalahan validator ikut tertangkap.</summary>
    private static int CountRuleViolations(DungeonLayout layout, DungeonConfig config)
    {
        int violations = 0;
        var mandatory = new HashSet<Vector2Int>(layout.pathToKey);
        mandatory.UnionWith(layout.pathToGoal);

        if (layout.pathToKey.Count == 0 || layout.pathToGoal.Count == 0)
            violations++;
        if (layout.startRoom == layout.goalRoom || layout.keyRoom == layout.startRoom || layout.keyRoom == layout.goalRoom)
            violations++;

        foreach (DungeonEnemyPlan enemy in layout.enemies)
        {
            if (!layout.IsWalkable(enemy.cell) || mandatory.Contains(enemy.cell))
                violations++;
            if (Vector2Int.Distance(enemy.cell, layout.start) < config.safeRadius || layout.startRoom.Contains(enemy.cell))
                violations++;
            if (enemy.route.Length == 0)
                violations++;
            foreach (Vector2Int point in enemy.route)
                if (!layout.IsWalkable(point) || layout.startRoom.Contains(point))
                    violations++;
        }

        int keys = 0;
        foreach (DungeonItemPlan item in layout.items)
        {
            if (!layout.IsWalkable(item.cell))
                violations++;
            if (item.kind == DungeonItemKind.Key)
                keys++;
            else if (mandatory.Contains(item.cell))
                violations++;
        }
        if (keys != 1)
            violations++;

        foreach (DungeonPropPlan prop in layout.props)
        {
            if (layout.cells[prop.cell.x, prop.cell.y] != DungeonCell.Room || mandatory.Contains(prop.cell))
                violations++;
            if (Vector2Int.Distance(prop.cell, layout.start) < config.safeRadius)
                violations++;
        }

        // Baris tepi peta harus tetap dinding, supaya Player tidak bisa keluar dari dungeon.
        for (int x = 0; x < layout.width; x++)
            if (layout.cells[x, 0] != DungeonCell.Wall || layout.cells[x, layout.height - 1] != DungeonCell.Wall)
                violations++;
        for (int y = 0; y < layout.height; y++)
            if (layout.cells[0, y] != DungeonCell.Wall || layout.cells[layout.width - 1, y] != DungeonCell.Wall)
                violations++;

        return violations;
    }
}
