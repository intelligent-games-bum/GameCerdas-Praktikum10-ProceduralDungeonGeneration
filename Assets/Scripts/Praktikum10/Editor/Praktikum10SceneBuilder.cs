using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Menyusun scene Praktikum 10 (Procedural Dungeon) lewat menu "Praktikum 10".
/// Dibuat prefab modular Floor dan Wall, prefab Prop, Enemy FSM (Praktikum 5), SafePoint, lalu scene berisi
/// Player, Exit, kamera top-down, HUD, dan DungeonSpawner. Geometri dungeon tidak disimpan di scene,
/// karena dibuat saat Play. Item memakai ulang prefab Praktikum 9.
/// </summary>
public static class Praktikum10SceneBuilder
{
    private const string SceneFolder = "Assets/Scenes/Praktikum10";
    private const string MaterialFolder = SceneFolder + "/Materials";
    private const string PrefabFolder = SceneFolder + "/Prefabs";
    private const string ScenePath = SceneFolder + "/P10_BSP_Dungeon.unity";

    private const string P9Prefabs = "Assets/Scenes/Praktikum9/Prefabs/";
    private const string EnemyAnimatorPath = "Assets/Scenes/Praktikum5/EnemyAnimation.controller";
    private const string SoldierModel = "Assets/Materials/sci-fi-supersoldier-game-ready-animated/source/Idle.fbx";
    private const string PlayerAnimController = "Assets/Animations/PlayerAnimation.controller";

    private const string SyntyChars = "Assets/Synty/PolygonGeneric/Prefabs/Characters/";
    private const string SyntyProps = "Assets/Synty/PolygonGeneric/Prefabs/Props/";

    private const float CellSize = 2f;
    private const float WallHeight = 2f;
    private const float FloorThickness = 0.2f;

    [MenuItem("Praktikum 10/Build Scene - BSP Dungeon")]
    private static void BuildScene()
    {
        int obstacleLayer = LayerMask.NameToLayer("Obstacle");
        if (obstacleLayer < 0)
        {
            EditorUtility.DisplayDialog("Praktikum 10", "Layer 'Obstacle' belum ada. Tambahkan di Project Settings > Tags and Layers.", "OK");
            return;
        }
        if (!PrepareNewScene())
            return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        Material floorMat = GetMaterial("P10_Floor", new Color(0.46f, 0.44f, 0.41f));
        Material wallMat = GetMaterial("P10_Wall", new Color(0.3f, 0.32f, 0.38f));
        Material safeMat = GetMaterial("P10_SafePoint", new Color(0.2f, 1f, 0.45f), unlit: true);
        Material exitMat = GetMaterial("P10_Exit", Color.white, unlit: true);
        Material indicatorMat = GetMaterial("P10_StateIndicator", Color.white, unlit: true);
        Material barMat = GetMaterial("P10_HealthBar", Color.white, unlit: true);
        Material lineMat = GetMaterial("P10_DebugLine", Color.white, unlit: true);

        // ---------------------------------------------------------------- Prefab
        GameObject floorPrefab = CreateTilePrefab("Tile_Floor", floorMat, 0, -FloorThickness, FloorThickness);
        GameObject wallPrefab = CreateTilePrefab("Tile_Wall", wallMat, obstacleLayer, 0f, WallHeight);
        GameObject[] propPrefabs =
        {
            CreatePropPrefab("Prop_Crate", SyntyProps + "SM_Gen_Prop_Crate_01.prefab", 1.2f, obstacleLayer),
            CreatePropPrefab("Prop_Barrel", SyntyProps + "SM_Gen_Prop_Barrel_Wood_01.prefab", 1.3f, obstacleLayer),
            CreatePropPrefab("Prop_Barrel2", SyntyProps + "SM_Gen_Prop_Barrel_Wood_02.prefab", 1.3f, obstacleLayer),
        };
        GameObject enemyPrefab = CreateEnemyPrefab(1 << obstacleLayer, indicatorMat, barMat);
        GameObject safePrefab = CreateSafePointPrefab(safeMat);

        // ---------------------------------------------------------------- Player, Exit, kamera
        Camera cam = Object.FindAnyObjectByType<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
        cam.fieldOfView = 50f;
        cam.farClipPlane = 400f;

        GameObject player = CreatePlayer(cam, barMat);
        ExitGate exit = CreateExit(exitMat);

        // ---------------------------------------------------------------- Sistem
        var systems = new GameObject("Dungeon Systems").transform;

        var goal = new GameObject("Game Goal").AddComponent<P9GameGoal>();
        goal.transform.SetParent(systems, false);
        Set(goal, "player", player.transform);
        Set(goal, "playerHealth", player.GetComponent<Health>());
        Set(goal, "exit", exit);
        Set(goal, "requiredItemType", "Key");
        SetArray(goal, "disableOnEnd", new Object[]
        {
            player.GetComponent<global::PlayerController>(),
            player.GetComponent<PlayerMeleeAttack>(),
        });

        var level = new GameObject("Level (NavMeshSurface)");
        var surface = level.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

        var spawner = new GameObject("Dungeon Spawner").AddComponent<DungeonSpawner>();
        spawner.transform.SetParent(systems, false);
        Set(spawner, "floorPrefab", floorPrefab);
        Set(spawner, "wallPrefab", wallPrefab);
        SetArray(spawner, "propPrefabs", propPrefabs);
        Set(spawner, "cellSize", CellSize);
        Set(spawner, "enemyPrefab", enemyPrefab);
        Set(spawner, "coinPrefab", CreateFittedCoinPrefab());
        Set(spawner, "potionPrefab", LoadPrefab(P9Prefabs + "Item_Potion.prefab"));
        Set(spawner, "keyPrefab", LoadPrefab(P9Prefabs + "Item_Key.prefab"));
        Set(spawner, "safePointPrefab", safePrefab);
        Set(spawner, "player", player.transform);
        Set(spawner, "goal", goal);
        Set(spawner, "exit", exit);
        Set(spawner, "surface", surface);

        var overlay = new GameObject("Debug Overlay").AddComponent<DungeonDebugOverlay>();
        overlay.transform.SetParent(systems, false);
        Set(overlay, "spawner", spawner);
        Set(overlay, "lineMaterial", lineMat);

        var topDown = cam.gameObject.AddComponent<TopDownCamera>();
        Set(topDown, "target", player.transform);
        Set(topDown, "spawner", spawner);

        var hud = new GameObject("HUD (Dungeon Debug)").AddComponent<DungeonHUD>();
        hud.transform.SetParent(systems, false);
        Set(hud, "spawner", spawner);
        Set(hud, "goal", goal);
        Set(hud, "overlay", overlay);
        Set(hud, "topDownCamera", topDown);

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        Selection.activeGameObject = spawner.gameObject;
        Debug.Log($"[Praktikum 10] Scene tersimpan di {ScenePath}. Tekan Play.");
    }

    /// <summary>Entry point command line: Unity -batchmode -executeMethod Praktikum10SceneBuilder.BuildSceneBatch</summary>
    public static void BuildSceneBatch()
    {
        BuildScene();
    }

    // ------------------------------------------------------------------ Prefab modular

    /// <summary>Satu cell grid berbentuk balok. Pivot berada di tengah bawah cell, pada ketinggian lantai.</summary>
    private static GameObject CreateTilePrefab(string name, Material material, int layer, float bottom, float height)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = name;
        tile.layer = layer;
        Object.DestroyImmediate(tile.GetComponent<BoxCollider>());

        var box = tile.AddComponent<BoxCollider>();
        box.size = Vector3.one;
        tile.transform.localScale = new Vector3(CellSize, height, CellSize);
        tile.GetComponent<Renderer>().sharedMaterial = material;

        // Mesh digeser lewat child agar transform root tetap bisa diatur oleh Instantiate.
        var root = new GameObject(name);
        root.layer = layer;
        tile.name = "Mesh";
        tile.transform.SetParent(root.transform, false);
        tile.transform.localPosition = Vector3.up * (bottom + height * 0.5f);
        return SavePrefab(root, name);
    }

    private static GameObject CreatePropPrefab(string name, string modelPath, float height, int layer)
    {
        var prop = new GameObject(name);
        GameObject model = InstantiateAsset(modelPath, prop.transform);
        if (model != null)
        {
            model.name = "Model";
            Bounds bounds = RenderBounds(model);
            if (bounds.size.y > 0.01f)
                model.transform.localScale *= height / bounds.size.y;
            foreach (Collider c in model.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);
        }

        var box = prop.AddComponent<BoxCollider>();
        box.size = new Vector3(CellSize * 0.7f, height, CellSize * 0.7f);
        box.center = Vector3.up * (height * 0.5f);

        foreach (Transform t in prop.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
        return SavePrefab(prop, name);
    }

    private static GameObject CreateSafePointPrefab(Material material)
    {
        var go = new GameObject("SafePoint");
        var safePoint = go.AddComponent<SafePoint>();
        Set(safePoint, "radius", 1.5f);
        CreateMarker(go.transform, "Pad", 1.5f, material);
        return SavePrefab(go, "SafePoint");
    }

    /// <summary>Enemy FSM Praktikum 5. Target, waypoint, dan SafePoint diisi DungeonSpawner saat Instantiate.</summary>
    private static GameObject CreateEnemyPrefab(int obstacleMask, Material indicatorMat, Material barMat)
    {
        const string name = "Enemy_FSM";
        var enemy = new GameObject(name);

        var collider = enemy.AddComponent<CapsuleCollider>();
        collider.height = 1.8f;
        collider.radius = 0.35f;
        collider.center = new Vector3(0f, 0.9f, 0f);

        var agent = enemy.AddComponent<NavMeshAgent>();
        agent.baseOffset = 0f;
        agent.radius = 0.4f;
        agent.height = 1.8f;
        agent.speed = 2f;
        agent.angularSpeed = 540f;
        agent.acceleration = 16f;
        agent.autoBraking = true;

        Health health = enemy.AddComponent<Health>();

        var perception = enemy.AddComponent<EnemyPerception>();
        Set(perception, "viewRange", 10f);
        SetMask(perception, "obstacleMask", obstacleMask);

        var fsm = enemy.AddComponent<EnemyFSM>();
        Set(fsm, "chaseSpeed", 4f);
        Set(fsm, "logTransitions", false);

        GameObject model = InstantiateAsset(SyntyChars + "SM_Gen_Chr_Skeleton_01.prefab", enemy.transform);
        Animator animator = null;
        if (model != null)
        {
            model.name = "Model";
            animator = GetOrAdd<Animator>(model);
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(EnemyAnimatorPath);
            animator.applyRootMotion = false;
            foreach (Collider c in model.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);
        }

        GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        indicator.name = "StateIndicator";
        Object.DestroyImmediate(indicator.GetComponent<Collider>());
        indicator.transform.SetParent(enemy.transform, false);
        indicator.transform.localPosition = new Vector3(0f, 2.25f, 0f);
        indicator.transform.localScale = new Vector3(0.25f, 0.35f, 0.25f);
        indicator.GetComponent<Renderer>().sharedMaterial = indicatorMat;

        var visual = enemy.AddComponent<EnemyVisualFeedback>();
        Set(visual, "fsm", fsm);
        Set(visual, "health", health);
        Set(visual, "agent", agent);
        if (animator != null)
            Set(visual, "animator", animator);
        if (model != null)
            Set(visual, "model", model.transform);
        Set(visual, "stateIndicator", indicator.GetComponent<Renderer>());

        var bar = enemy.AddComponent<WorldHealthBar>();
        Set(bar, "health", health);
        Set(bar, "barMaterial", barMat);
        Set(bar, "height", 2.75f);

        return SavePrefab(enemy, name);
    }

    private static GameObject SavePrefab(GameObject go, string name)
    {
        string path = $"{PrefabFolder}/{name}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>Model koin Praktikum 9 lebih lebar dari satu cell, jadi dibuat variannya yang muat di dalam cell.</summary>
    private static GameObject CreateFittedCoinPrefab()
    {
        const float maxFootprint = 1.2f;
        GameObject source = LoadPrefab(P9Prefabs + "Item_Coin.prefab");
        if (source == null)
            return null;

        var coin = (GameObject)PrefabUtility.InstantiatePrefab(source);
        Transform model = coin.transform.Find("Model");
        if (model != null)
        {
            Bounds bounds = RenderBounds(model.gameObject);
            float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
            if (footprint > maxFootprint)
                model.localScale *= maxFootprint / footprint;
        }
        return SavePrefab(coin, "Item_Coin");
    }

    private static GameObject LoadPrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            Debug.LogError($"[Praktikum 10] Prefab tidak ditemukan: {path}. Jalankan menu Praktikum 9 sekali.");
        return prefab;
    }

    // ------------------------------------------------------------------ Player & Exit

    private static GameObject CreatePlayer(Camera cam, Material barMat)
    {
        var player = new GameObject("Player");

        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);

        var controller = player.AddComponent<global::PlayerController>();
        Set(controller, "cameraTransform", cam.transform);
        // Terbang dimatikan agar Player tidak melewati dinding dungeon.
        Set(controller, "allowFlight", false);

        var health = player.AddComponent<Health>();
        Set(health, "maxHealth", 150f);
        Set(health, "current", 150f);

        var model = InstantiateAsset(SoldierModel, player.transform);
        if (model != null)
        {
            model.name = "Supersoldier";
            // FBX ini ikut membawa Light dan Camera dari file sumbernya; dimatikan supaya tidak silau.
            foreach (Light l in model.GetComponentsInChildren<Light>(true))
                l.gameObject.SetActive(false);
            foreach (Camera c in model.GetComponentsInChildren<Camera>(true))
                c.gameObject.SetActive(false);
            Animator animator = GetOrAdd<Animator>(model);
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimController);
            animator.applyRootMotion = false;
        }

        var driver = player.AddComponent<PlayerAnimatorDriver>();
        if (model != null)
            Set(driver, "animator", model.GetComponent<Animator>());

        var melee = player.AddComponent<PlayerMeleeAttack>();
        if (model != null)
            Set(melee, "model", model.transform);

        var bar = player.AddComponent<WorldHealthBar>();
        Set(bar, "health", health);
        Set(bar, "barMaterial", barMat);
        Set(bar, "height", 2.3f);
        return player;
    }

    private static ExitGate CreateExit(Material mat)
    {
        var root = new GameObject("Exit");

        GameObject pad = CreateMarker(root.transform, "Pad", 1.5f, mat);
        GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beacon.name = "Beacon";
        Object.DestroyImmediate(beacon.GetComponent<Collider>());
        beacon.transform.SetParent(root.transform, false);
        beacon.transform.localPosition = Vector3.up * 1.5f;
        beacon.transform.localScale = new Vector3(0.25f, 1.5f, 0.25f);
        beacon.GetComponent<Renderer>().sharedMaterial = mat;

        var renderers = new List<Object> { pad.GetComponent<Renderer>(), beacon.GetComponent<Renderer>() };
        foreach (float x in new[] { -1.4f, 1.4f })
        {
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.name = "Pillar";
            Object.DestroyImmediate(pillar.GetComponent<Collider>());
            pillar.transform.SetParent(root.transform, false);
            pillar.transform.localPosition = new Vector3(x, 1.2f, 0f);
            pillar.transform.localScale = new Vector3(0.3f, 2.4f, 0.3f);
            pillar.GetComponent<Renderer>().sharedMaterial = mat;
            renderers.Add(pillar.GetComponent<Renderer>());
        }

        var gate = root.AddComponent<ExitGate>();
        Set(gate, "radius", 1.5f);
        SetArray(gate, "indicators", renderers.ToArray());
        return gate;
    }

    private static GameObject CreateMarker(Transform parent, string name, float radius, Material mat)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = name;
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = Vector3.up * 0.02f;
        marker.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
        marker.GetComponent<Renderer>().sharedMaterial = mat;
        return marker;
    }

    // ------------------------------------------------------------------ Utilitas

    private static Bounds RenderBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers)
            b.Encapsulate(r.bounds);
        return b;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    private static GameObject InstantiateAsset(string path, Transform parent)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            Debug.LogWarning($"[Praktikum 10] Aset tidak ditemukan: {path}");
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }

    private static bool PrepareNewScene()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return false;

        if (!Application.isBatchMode && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("Praktikum 10", $"{ScenePath} sudah ada. Timpa dengan scene baru?", "Timpa", "Batal"))
            return false;

        EnsureFolder(SceneFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(PrefabFolder);
        return true;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    private static Material GetMaterial(string name, Color color, bool unlit = false)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null)
            return mat;

        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        mat = new Material(shader) { color = color };
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void AddToBuildSettings(string scenePath)
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == scenePath))
            return;
        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // Field script bersifat private [SerializeField], jadi diisi lewat SerializedObject.
    private static void Set(Component target, string property, object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(property);
        if (prop == null)
        {
            Debug.LogError($"[Praktikum 10] Field '{property}' tidak ditemukan di {target.GetType().Name}.");
            return;
        }

        switch (value)
        {
            case Object obj: prop.objectReferenceValue = obj; break;
            case float f: prop.floatValue = f; break;
            case int i: prop.intValue = i; break;
            case bool bo: prop.boolValue = bo; break;
            case string s: prop.stringValue = s; break;
            default:
                Debug.LogError($"[Praktikum 10] Tipe {value?.GetType().Name} belum didukung untuk '{property}'.");
                return;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArray(Component target, string property, Object[] values)
    {
        var so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(property);
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetMask(Component target, string property, int mask)
    {
        var so = new SerializedObject(target);
        so.FindProperty(property).intValue = mask;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
