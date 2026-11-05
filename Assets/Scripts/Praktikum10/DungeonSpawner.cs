using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Mewujudkan <see cref="DungeonLayout"/> menjadi scene Unity.
///
/// Urutan: generate data, pasang prefab modular, bake NavMesh, pindahkan Player dan Exit, lalu spawn objek.
/// NavMesh harus selesai lebih dulu karena NavMeshAgent pada Enemy FSM (Praktikum 5) membutuhkannya saat Start.
///
/// Tombol: R = seed sama, N = seed acak, T = uji validasi (corridor diputus lalu di-repair).
/// </summary>
public class DungeonSpawner : MonoBehaviour
{
    [SerializeField] private DungeonConfig config = new DungeonConfig();

    [Header("Prefab modular")]
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject[] propPrefabs = new GameObject[0];
    [SerializeField, Min(0.5f)] private float cellSize = 2f;

    [Header("Prefab objek")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject coinPrefab;
    [SerializeField] private GameObject potionPrefab;
    [SerializeField] private GameObject keyPrefab;
    [SerializeField] private GameObject safePointPrefab;

    [Header("Scene")]
    [SerializeField] private Transform player;
    [SerializeField] private P9GameGoal goal;
    [SerializeField] private ExitGate exit;
    [SerializeField] private NavMeshSurface surface;

    [Header("Debug")]
    [SerializeField] private bool logToConsole = true;

    // Konfigurasi dibawa melewati reload scene (dipakai saat Player sudah kalah).
    private static DungeonConfig pendingConfig;

    private Transform geometryRoot;
    private Transform spawnRoot;
    private Transform stagingRoot;
    private readonly List<int> seedHistory = new List<int>();

    public DungeonConfig Config => config;
    public DungeonResult LastResult { get; private set; }
    public DungeonLayout Layout { get; private set; }
    public int GenerationCount { get; private set; }
    public IReadOnlyList<int> SeedHistory => seedHistory;
    public Transform Player => player;
    public float CellSize => cellSize;

    /// <summary>Dipanggil setiap generation selesai, termasuk yang gagal.</summary>
    public event Action<DungeonResult> Generated;

    private void Awake()
    {
        if (pendingConfig != null)
        {
            config = pendingConfig;
            pendingConfig = null;
        }

        spawnRoot = new GameObject("Spawned (Dungeon)").transform;
        stagingRoot = new GameObject("Spawn Staging (inactive)").transform;
        stagingRoot.gameObject.SetActive(false);
    }

    private void Start()
    {
        Regenerate();
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
            return;
        if (kb.rKey.wasPressedThisFrame)
            Regenerate();
        else if (kb.nKey.wasPressedThisFrame)
            RegenerateWithRandomSeed();
        else if (kb.tKey.wasPressedThisFrame)
            RunValidationTest();
    }

    // ------------------------------------------------------------------ Public API

    public void Regenerate()
    {
        RegenerateWithSeed(config.seed);
    }

    public void RegenerateWithRandomSeed()
    {
        RegenerateWithSeed(UnityEngine.Random.Range(1, 100000));
    }

    public void RunValidationTest()
    {
        RegenerateWithSeed(config.seed, sabotage: true);
    }

    public void RegenerateWithSeed(int seed, bool sabotage = false)
    {
        config.seed = seed;
        config.Clamp();

        // Health yang sudah 0 tidak bisa dipulihkan (Dead adalah state akhir), jadi scene dimuat ulang.
        if (goal != null && goal.PlayerDead)
        {
            pendingConfig = config.Clone();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        DungeonResult result = BspDungeonGenerator.Generate(config, sabotage);
        LastResult = result;
        GenerationCount++;
        RememberSeed(seed);

        if (logToConsole)
            Debug.Log(Describe(result), this);

        // Level lama tetap dipakai jika seluruh percobaan gagal, sehingga Player tidak jatuh.
        if (result.isValid)
        {
            Layout = result.layout;
            ClearLevel();
            BuildGeometry(Layout);
            BakeNavMesh();
            PlacePlayerAndExit(Layout);
            SpawnObjects(Layout);
            if (goal != null)
                goal.BeginRound(Layout.items.Count);
        }

        Generated?.Invoke(result);
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(
            (cell.x + 0.5f - Layout.width * 0.5f) * cellSize,
            0f,
            (cell.y + 0.5f - Layout.height * 0.5f) * cellSize);
    }

    /// <summary>Posisi dunia ke koordinat grid, masih pecahan agar titik minimap bergerak halus.</summary>
    public Vector2 WorldToGrid(Vector3 world)
    {
        return new Vector2(
            world.x / cellSize + Layout.width * 0.5f,
            world.z / cellSize + Layout.height * 0.5f);
    }

    public static string Describe(DungeonResult r)
    {
        string text = $"[Praktikum 10] Seed {r.seed} | percobaan {r.attemptsUsed} | {string.Join(" | ", r.log)}";
        if (r.layout != null)
        {
            DungeonLayout l = r.layout;
            DungeonValidation v = l.validation;
            text += $"\nLayout {l.Hash} | room {l.rooms.Count} | floor {v.reachableFloor}/{v.totalFloor} | " +
                    $"enemy {l.enemies.Count}/{l.requestedEnemies} | item {l.items.Count - 1}/{l.requestedItems} + key | " +
                    $"prop {l.props.Count}/{l.requestedProps} | jalur wajib {v.pathLength} cell";
        }
        return text;
    }

    // ------------------------------------------------------------------ Geometri

    private void ClearLevel()
    {
        if (geometryRoot != null)
        {
            // Destroy baru berjalan di akhir frame, sedangkan bake NavMesh terjadi sekarang.
            geometryRoot.gameObject.SetActive(false);
            geometryRoot.SetParent(null);
            Destroy(geometryRoot.gameObject);
        }

        for (int i = spawnRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = spawnRoot.GetChild(i).gameObject;
            child.SetActive(false);
            child.transform.SetParent(null);
            Destroy(child);
        }
    }

    private void BuildGeometry(DungeonLayout layout)
    {
        geometryRoot = new GameObject("Geometry").transform;
        geometryRoot.SetParent(surface != null ? surface.transform : transform, false);
        var floors = NewGroup("Floor");
        var walls = NewGroup("Walls");
        var props = NewGroup("Props");

        for (int x = 0; x < layout.width; x++)
        {
            for (int y = 0; y < layout.height; y++)
            {
                var cell = new Vector2Int(x, y);
                if (layout.cells[x, y] != DungeonCell.Wall)
                    Instantiate(floorPrefab, CellToWorld(cell), Quaternion.identity, floors);
                else if (TouchesFloor(layout, cell))
                    Instantiate(wallPrefab, CellToWorld(cell), Quaternion.identity, walls);
            }
        }

        if (propPrefabs.Length == 0)
            return;
        foreach (DungeonPropPlan prop in layout.props)
        {
            GameObject prefab = propPrefabs[prop.variant % propPrefabs.Length];
            Instantiate(prefab, CellToWorld(prop.cell), Quaternion.Euler(0f, prop.rotationY, 0f), props);
        }
    }

    private Transform NewGroup(string groupName)
    {
        var group = new GameObject(groupName).transform;
        group.SetParent(geometryRoot, false);
        return group;
    }

    /// <summary>Dinding hanya dibuat di samping floor. Sisa peta yang tidak terlihat dibiarkan kosong.</summary>
    private static bool TouchesFloor(DungeonLayout layout, Vector2Int cell)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (layout.IsFloor(cell + new Vector2Int(dx, dy)))
                    return true;
        return false;
    }

    private void BakeNavMesh()
    {
        if (surface == null)
            return;
        Physics.SyncTransforms();
        surface.BuildNavMesh();
    }

    // ------------------------------------------------------------------ Player dan objek

    private void PlacePlayerAndExit(DungeonLayout layout)
    {
        if (player != null)
        {
            // CharacterController menimpa posisi yang diubah langsung, jadi dimatikan sebentar.
            var cc = player.GetComponent<CharacterController>();
            if (cc != null)
                cc.enabled = false;
            player.SetPositionAndRotation(CellToWorld(layout.start) + Vector3.up * 0.05f, Quaternion.identity);
            if (cc != null)
                cc.enabled = true;

            var health = player.GetComponent<Health>();
            if (health != null)
                health.HealTo(1f);
        }

        if (exit != null)
        {
            exit.transform.position = CellToWorld(layout.goal);
            exit.SetOpen(false);
        }
        Physics.SyncTransforms();
    }

    private void SpawnObjects(DungeonLayout layout)
    {
        var safePoints = new List<SafePoint>();
        foreach (Vector2Int cell in layout.safePoints)
        {
            GameObject go = Instantiate(safePointPrefab, CellToWorld(cell), Quaternion.identity, spawnRoot);
            go.name = $"SafePoint_{safePoints.Count + 1}";
            safePoints.Add(go.GetComponent<SafePoint>());
        }
        SafePoint[] safeArray = safePoints.ToArray();

        var waypointRoot = new GameObject("Waypoints").transform;
        waypointRoot.SetParent(spawnRoot, false);
        var waypoints = new Dictionary<Vector2Int, Transform>();

        int index = 0;
        foreach (DungeonEnemyPlan plan in layout.enemies)
        {
            index++;
            // Parent non-aktif menahan Awake sampai target dan rute terisi.
            GameObject enemy = Instantiate(enemyPrefab, CellToWorld(plan.cell),
                Quaternion.Euler(0f, plan.rotationY, 0f), stagingRoot);
            enemy.name = $"Enemy_{index:00}";
            enemy.GetComponent<EnemyPerception>().SetTarget(player);
            enemy.GetComponent<EnemyFSM>().SetRoute(RouteTransforms(plan.route, waypoints, waypointRoot), safeArray);
            // Tubuh enemy yang mati tidak boleh menutup corridor sempit pada jalur wajib.
            var body = enemy.GetComponent<Collider>();
            enemy.GetComponent<Health>().Died += () => body.enabled = false;
            enemy.transform.SetParent(spawnRoot, true);
        }

        index = 0;
        foreach (DungeonItemPlan plan in layout.items)
        {
            index++;
            GameObject prefab = plan.kind == DungeonItemKind.Key ? keyPrefab
                : plan.kind == DungeonItemKind.Potion ? potionPrefab : coinPrefab;
            GameObject item = Instantiate(prefab, CellToWorld(plan.cell), Quaternion.Euler(0f, plan.rotationY, 0f), spawnRoot);
            item.name = $"{plan.kind}_{index:00}";
            item.GetComponent<Collectible>().Setup(goal, player, index * 0.37f);
        }
    }

    private Transform[] RouteTransforms(Vector2Int[] route, Dictionary<Vector2Int, Transform> cache, Transform parent)
    {
        var result = new Transform[route.Length];
        for (int i = 0; i < route.Length; i++)
        {
            if (!cache.TryGetValue(route[i], out Transform t))
            {
                t = new GameObject($"WP_{route[i].x}_{route[i].y}").transform;
                t.SetParent(parent, false);
                t.position = CellToWorld(route[i]);
                cache[route[i]] = t;
            }
            result[i] = t;
        }
        return result;
    }

    private void RememberSeed(int seed)
    {
        seedHistory.Remove(seed);
        seedHistory.Insert(0, seed);
        if (seedHistory.Count > 6)
            seedHistory.RemoveAt(seedHistory.Count - 1);
    }
}
