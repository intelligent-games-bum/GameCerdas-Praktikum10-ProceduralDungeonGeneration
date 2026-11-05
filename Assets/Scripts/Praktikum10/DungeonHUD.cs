using System;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// HUD Praktikum 10 (uGUI, dibangun lewat kode saat runtime):
/// - Panel info: seed, hash layout, hasil validasi BFS, jumlah objek, status goal.
/// - Minimap: layout grid, jalur wajib, start, key, goal, spawn enemy dan item, serta posisi Player.
/// - Panel parameter: mengubah parameter generator dan langsung generate ulang dengan seed yang sama.
/// </summary>
public class DungeonHUD : MonoBehaviour
{
    [SerializeField] private DungeonSpawner spawner;
    [SerializeField] private P9GameGoal goal;
    [SerializeField] private DungeonDebugOverlay overlay;
    [SerializeField] private TopDownCamera topDownCamera;
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;

    private const float MinimapSize = 250f;
    private const float RowHeight = 24f;

    private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color ButtonColor = new Color(0.16f, 0.2f, 0.28f, 0.95f);

    private static readonly Color WallPixel = new Color(0.08f, 0.09f, 0.12f);
    private static readonly Color LeafPixel = new Color(0.3f, 0.22f, 0.1f);
    private static readonly Color RoomPixel = new Color(0.46f, 0.48f, 0.53f);
    private static readonly Color CorridorPixel = new Color(0.32f, 0.34f, 0.4f);
    private static readonly Color PropPixel = new Color(0.45f, 0.3f, 0.16f);
    private static readonly Color KeyPathPixel = new Color(0.25f, 0.8f, 0.95f);
    private static readonly Color GoalPathPixel = new Color(0.95f, 0.8f, 0.2f);
    private static readonly Color StartPixel = new Color(0.2f, 1f, 0.35f);
    private static readonly Color KeyPixel = new Color(0.35f, 0.95f, 1f);
    private static readonly Color GoalPixel = new Color(1f, 0.25f, 0.9f);
    private static readonly Color EnemyPixel = new Color(1f, 0.2f, 0.15f);
    private static readonly Color CoinPixel = new Color(1f, 0.9f, 0.3f);
    private static readonly Color PotionPixel = new Color(1f, 0.45f, 0.65f);

    private Font font;
    private Text infoText;
    private Text statusText;
    private Text toastText;
    private RectTransform infoPanel;
    private RectTransform buttonGroup;
    private RawImage minimap;
    private RectTransform playerDot;
    private Texture2D minimapTexture;
    private float refreshTimer;
    private float toastTimer;
    private bool lastOverlay;
    private bool hadRequiredItem;

    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildUI();
    }

    private void OnEnable()
    {
        if (spawner != null)
            spawner.Generated += OnGenerated;
        if (goal != null)
            goal.Changed += OnGoalChanged;
    }

    private void OnDisable()
    {
        if (spawner != null)
            spawner.Generated -= OnGenerated;
        if (goal != null)
            goal.Changed -= OnGoalChanged;
    }

    private void Update()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = refreshInterval;
            Refresh();
        }

        if (toastTimer > 0f)
        {
            toastTimer -= Time.unscaledDeltaTime;
            if (toastTimer <= 0f)
                toastText.text = "";
        }

        if (overlay != null && overlay.Visible != lastOverlay)
            RedrawMinimap();
        UpdatePlayerDot();
    }

    private void OnGenerated(DungeonResult result)
    {
        Toast(result.isValid
            ? $"Generation #{spawner.GenerationCount} - seed {result.seed} - layout {result.layout.Hash}"
            : "Generation gagal, level sebelumnya dipertahankan. Ubah parameter.");
        hadRequiredItem = false;
        RedrawMinimap();
        Refresh();
    }

    private void OnGoalChanged()
    {
        if (goal.HasRequiredItem && !hadRequiredItem)
            Toast("Key diambil, Exit terbuka!");
        hadRequiredItem = goal.HasRequiredItem;
        Refresh();
    }

    // ------------------------------------------------------------------ Isi teks

    private void Refresh()
    {
        DungeonResult r = spawner != null ? spawner.LastResult : null;
        if (r == null)
            return;

        // Mode peta penuh: panel diringkas agar tidak menutupi dungeon, seed tetap terlihat.
        bool compact = topDownCamera != null && topDownCamera.Overview;
        infoPanel.sizeDelta = compact ? new Vector2(370f, 62f) : new Vector2(470f, 372f);
        buttonGroup.anchoredPosition = compact ? new Vector2(0f, 310f) : Vector2.zero;

        DungeonLayout l = spawner.Layout;
        var sb = new StringBuilder();
        if (compact)
        {
            string hash = r.layout != null ? r.layout.Hash : "-";
            bool valid = l != null && l.validation != null && l.validation.isValid;
            sb.AppendLine($"<b>Seed</b> <color=#ffd84a><size=15>{r.seed}</size></color>   layout <color=#9ad>{hash}</color>   {Flag(valid, "VALID", "GAGAL")}");
            if (l != null)
                sb.Append($"Room {l.rooms.Count}   Enemy {l.enemies.Count}   Item {l.items.Count - 1} + Key   Prop {l.props.Count}   Jalur {(l.validation != null ? l.validation.pathLength : 0)} cell");
            infoText.text = sb.ToString();
            statusText.text = "";
            return;
        }

        sb.AppendLine("<b><size=16>PRAKTIKUM 10 - BSP DUNGEON</size></b>");

        string shownHash = r.layout != null ? r.layout.Hash : "-";
        sb.AppendLine($"<b>Seed</b> <color=#ffd84a><size=15>{r.seed}</size></color>   layout <color=#9ad>{shownHash}</color>   gen #{spawner.GenerationCount}");
        sb.AppendLine($"Percobaan {r.attemptsUsed}/{spawner.Config.maxGenerationAttempts}   Repair: {(r.repaired ? $"<color=#fc6>ya, {r.carvedCells} cell</color>" : "tidak")}");

        if (l != null && l.validation != null)
        {
            DungeonValidation v = l.validation;
            sb.AppendLine();
            sb.AppendLine($"<b>Validasi BFS</b> {Flag(v.isValid, "VALID", "GAGAL")}");
            sb.AppendLine($"  Start ke Key {Flag(v.keyReachable, "OK", "PUTUS")}   Key ke Goal {Flag(v.goalReachable, "OK", "PUTUS")}");
            sb.AppendLine($"  Room {v.reachableRooms}/{v.totalRooms}   Floor {v.reachableFloor}/{v.totalFloor}   Jalur wajib {v.pathLength} cell");
            sb.AppendLine($"  Prop di jalur {v.propsOnPath}   Enemy di zona aman {v.enemiesInSafeZone}   Objek invalid {v.invalidObjects}");
            sb.AppendLine();
            sb.AppendLine($"<b>Objek</b> Enemy {l.enemies.Count}/{l.requestedEnemies}   Item {l.items.Count - 1}/{l.requestedItems} + Key   Prop {l.props.Count}/{l.requestedProps}");
            sb.AppendLine($"<b>Ditolak</b> zona aman {l.rejectedSafeZone} | jalur {l.rejectedPath} | jarak {l.rejectedSpacing} | blokir {l.rejectedBlocking}");
        }

        foreach (string line in r.log)
            sb.AppendLine($"<color=#bbb>{line}</color>");

        if (goal != null)
        {
            sb.AppendLine();
            Health hp = goal.PlayerHealth;
            string hpText = hp != null ? $"{hp.Current:0}/{hp.Max:0}" : "-";
            sb.AppendLine($"<b>Goal</b>  item {goal.ItemsCollected}/{goal.ItemsTotal}   skor {goal.Score}   HP {hpText}   {goal.RoundTime:0}s");
            sb.AppendLine(goal.ExitOpen
                ? "  <color=#5f5>Key didapat - menuju Exit!</color>"
                : "  <color=#f66>Exit terkunci - cari Key</color>");
        }

        if (spawner.SeedHistory.Count > 1)
            sb.AppendLine($"<color=#aaa>Riwayat seed: {string.Join(", ", spawner.SeedHistory)}</color>");

        sb.AppendLine();
        sb.Append("<color=#ccc>WASD gerak | Klik serang | R seed sama | N seed acak | T uji validasi | V jalur | M peta penuh</color>");
        infoText.text = sb.ToString();

        statusText.text = goal == null ? "" :
            goal.Current == P9GameGoal.State.Won ? $"<color=#5f5>GOAL TERCAPAI!</color>\n<size=20>skor {goal.Score}, waktu {goal.RoundTime:0.0} s - tekan R atau N</size>" :
            goal.Current == P9GameGoal.State.Lost ? "<color=#ff5050>KALAH</color>\n<size=20>tekan R untuk mengulang seed yang sama</size>" : "";
    }

    private static string Flag(bool ok, string good, string bad)
    {
        return ok ? $"<color=#5f5>{good}</color>" : $"<color=#f55>{bad}</color>";
    }

    private void Toast(string message)
    {
        toastText.text = message;
        toastTimer = 3f;
    }

    // ------------------------------------------------------------------ Minimap

    private void RedrawMinimap()
    {
        DungeonLayout l = spawner != null ? spawner.Layout : null;
        if (l == null)
            return;

        bool showDebug = overlay == null || overlay.Visible;
        lastOverlay = overlay != null && overlay.Visible;

        if (minimapTexture == null || minimapTexture.width != l.width || minimapTexture.height != l.height)
        {
            if (minimapTexture != null)
                Destroy(minimapTexture);
            minimapTexture = new Texture2D(l.width, l.height, TextureFormat.RGB24, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            minimap.texture = minimapTexture;
        }

        for (int x = 0; x < l.width; x++)
        {
            for (int y = 0; y < l.height; y++)
            {
                DungeonCell cell = l.cells[x, y];
                minimapTexture.SetPixel(x, y, cell == DungeonCell.Room ? RoomPixel
                    : cell == DungeonCell.Corridor ? CorridorPixel : WallPixel);
            }
        }

        if (showDebug)
        {
            foreach (RectInt leaf in l.leaves)
                DrawLeafBorder(l, leaf);
        }
        foreach (DungeonPropPlan p in l.props)
            minimapTexture.SetPixel(p.cell.x, p.cell.y, PropPixel);
        if (showDebug)
        {
            foreach (Vector2Int c in l.pathToKey)
                minimapTexture.SetPixel(c.x, c.y, KeyPathPixel);
            foreach (Vector2Int c in l.pathToGoal)
                minimapTexture.SetPixel(c.x, c.y, GoalPathPixel);
        }
        foreach (DungeonItemPlan i in l.items)
            minimapTexture.SetPixel(i.cell.x, i.cell.y, i.kind == DungeonItemKind.Key ? KeyPixel
                : i.kind == DungeonItemKind.Potion ? PotionPixel : CoinPixel);
        foreach (DungeonEnemyPlan e in l.enemies)
            minimapTexture.SetPixel(e.cell.x, e.cell.y, EnemyPixel);
        minimapTexture.SetPixel(l.start.x, l.start.y, StartPixel);
        minimapTexture.SetPixel(l.goal.x, l.goal.y, GoalPixel);
        minimapTexture.Apply();

        // Peta persegi panjang tetap proporsional di dalam kotak minimap.
        float scale = MinimapSize / Mathf.Max(l.width, l.height);
        ((RectTransform)minimap.transform).sizeDelta = new Vector2(l.width * scale, l.height * scale);
    }

    private void DrawLeafBorder(DungeonLayout l, RectInt leaf)
    {
        for (int x = leaf.xMin; x < leaf.xMax; x++)
        {
            for (int y = leaf.yMin; y < leaf.yMax; y++)
            {
                bool border = x == leaf.xMin || y == leaf.yMin || x == leaf.xMax - 1 || y == leaf.yMax - 1;
                if (border && l.cells[x, y] == DungeonCell.Wall)
                    minimapTexture.SetPixel(x, y, LeafPixel);
            }
        }
    }

    private void UpdatePlayerDot()
    {
        DungeonLayout l = spawner != null ? spawner.Layout : null;
        if (l == null || spawner.Player == null)
            return;

        var rt = (RectTransform)minimap.transform;
        Vector2 grid = spawner.WorldToGrid(spawner.Player.position);
        playerDot.anchoredPosition = new Vector2(
            grid.x / l.width * rt.sizeDelta.x,
            grid.y / l.height * rt.sizeDelta.y);
    }

    // ------------------------------------------------------------------ Parameter

    private void BuildParameterPanel(Transform root, float top)
    {
        var rows = new (string label, int step, Func<DungeonConfig, int> get, Action<DungeonConfig, int> set)[]
        {
            ("Ukuran peta", 4, c => c.mapWidth, (c, v) => { c.mapWidth = v; c.mapHeight = v; }),
            ("Kedalaman BSP", 1, c => c.maxSplitDepth, (c, v) => c.maxSplitDepth = v),
            ("Min leaf", 1, c => c.minLeafSize, (c, v) => c.minLeafSize = v),
            ("Room min", 1, c => c.roomMinSize, (c, v) => c.roomMinSize = v),
            ("Room max", 1, c => c.roomMaxSize, (c, v) => c.roomMaxSize = v),
            ("Padding room", 1, c => c.roomPadding, (c, v) => c.roomPadding = v),
            ("Lebar corridor", 1, c => c.corridorWidth, (c, v) => c.corridorWidth = v),
            ("Corridor ekstra", 1, c => c.extraConnections, (c, v) => c.extraConnections = v),
            ("Enemy", 1, c => c.enemyCount, (c, v) => c.enemyCount = v),
            ("Item", 1, c => c.itemCount, (c, v) => c.itemCount = v),
            ("Prop", 2, c => c.propCount, (c, v) => c.propCount = v),
            ("Zona aman", 1, c => c.safeRadius, (c, v) => c.safeRadius = v),
        };

        RectTransform panel = CreatePanel(root, "ParameterPanel", new Vector2(1f, 1f), new Vector2(-10f, top),
            new Vector2(MinimapSize, rows.Length * RowHeight + 10f));

        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            float y = -5f - i * RowHeight;

            Text label = CreateText(panel, "Label_" + row.label, 12, TextAnchor.MiddleLeft);
            AnchorTopLeft(label.rectTransform, new Vector2(8f, y), new Vector2(120f, RowHeight));
            Text value = CreateText(panel, "Value_" + row.label, 13, TextAnchor.MiddleCenter);
            AnchorTopLeft(value.rectTransform, new Vector2(156f, y), new Vector2(40f, RowHeight));
            label.text = row.label;
            value.text = row.get(spawner.Config).ToString();

            void Change(int delta)
            {
                row.set(spawner.Config, row.get(spawner.Config) + delta);
                spawner.Config.Clamp();
                spawner.Regenerate();
            }

            CreateButton(panel, "-", new Vector2(130f, y - 2f), 24f, 20f, () => Change(-row.step), 13);
            CreateButton(panel, "+", new Vector2(198f, y - 2f), 24f, 20f, () => Change(row.step), 13);

            spawner.Generated += _ => value.text = row.get(spawner.Config).ToString();
        }
    }

    // ------------------------------------------------------------------ Membangun UI

    private void BuildUI()
    {
        var canvasGo = new GameObject("P10 HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasGo.transform;

        infoPanel = CreatePanel(root, "InfoPanel", new Vector2(0f, 1f), new Vector2(10f, -10f), new Vector2(470f, 372f));
        infoText = CreateText(infoPanel, "Info", 12, TextAnchor.UpperLeft);
        Stretch(infoText.rectTransform, 9f);

        var groupGo = new GameObject("Buttons", typeof(RectTransform));
        groupGo.transform.SetParent(root, false);
        buttonGroup = (RectTransform)groupGo.transform;
        buttonGroup.anchorMin = buttonGroup.anchorMax = buttonGroup.pivot = new Vector2(0f, 1f);
        buttonGroup.sizeDelta = Vector2.zero;

        float y = -390f;
        CreateButton(buttonGroup, "Regenerate (R)", new Vector2(10f, y), 112f, 32f, () => spawner.Regenerate());
        CreateButton(buttonGroup, "Seed Acak (N)", new Vector2(127f, y), 106f, 32f, () => spawner.RegenerateWithRandomSeed());
        CreateButton(buttonGroup, "Seed -1", new Vector2(238f, y), 62f, 32f, () => spawner.RegenerateWithSeed(spawner.Config.seed - 1));
        CreateButton(buttonGroup, "Seed +1", new Vector2(305f, y), 62f, 32f, () => spawner.RegenerateWithSeed(spawner.Config.seed + 1));
        y -= 37f;
        CreateButton(buttonGroup, "Uji Validasi (T)", new Vector2(10f, y), 112f, 32f, () => spawner.RunValidationTest());
        CreateButton(buttonGroup, "Jalur (V)", new Vector2(127f, y), 106f, 32f, () =>
        {
            if (overlay != null)
                overlay.SetVisible(!overlay.Visible);
        });
        CreateButton(buttonGroup, "Peta Penuh (M)", new Vector2(238f, y), 129f, 32f, () =>
        {
            if (topDownCamera != null)
                topDownCamera.ToggleOverview();
        });

        // Minimap kanan atas: anak panel berjangkar kiri bawah agar koordinatnya sama dengan grid.
        RectTransform mapFrame = CreatePanel(root, "MinimapFrame", new Vector2(1f, 1f), new Vector2(-10f, -10f),
            new Vector2(MinimapSize, MinimapSize));
        var mapGo = new GameObject("Minimap", typeof(RectTransform), typeof(RawImage));
        mapGo.transform.SetParent(mapFrame, false);
        minimap = mapGo.GetComponent<RawImage>();
        minimap.raycastTarget = false;
        var mapRect = (RectTransform)mapGo.transform;
        mapRect.anchorMin = mapRect.anchorMax = mapRect.pivot = new Vector2(0.5f, 0.5f);
        mapRect.sizeDelta = new Vector2(MinimapSize, MinimapSize);

        var dotGo = new GameObject("PlayerDot", typeof(RectTransform), typeof(Image));
        dotGo.transform.SetParent(mapGo.transform, false);
        dotGo.GetComponent<Image>().color = Color.white;
        dotGo.GetComponent<Image>().raycastTarget = false;
        playerDot = (RectTransform)dotGo.transform;
        playerDot.anchorMin = playerDot.anchorMax = Vector2.zero;
        playerDot.pivot = new Vector2(0.5f, 0.5f);
        playerDot.sizeDelta = new Vector2(8f, 8f);

        BuildParameterPanel(root, -10f - MinimapSize - 6f);

        statusText = CreateText(root, "Status", 44, TextAnchor.MiddleCenter);
        statusText.fontStyle = FontStyle.Bold;
        RectTransform st = statusText.rectTransform;
        st.anchorMin = new Vector2(0f, 0.5f);
        st.anchorMax = new Vector2(1f, 0.5f);
        st.sizeDelta = new Vector2(0f, 140f);
        st.anchoredPosition = new Vector2(0f, 60f);
        statusText.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2f, -2f);

        toastText = CreateText(root, "Toast", 20, TextAnchor.MiddleCenter);
        RectTransform tt = toastText.rectTransform;
        tt.anchorMin = new Vector2(0f, 0f);
        tt.anchorMax = new Vector2(1f, 0f);
        tt.pivot = new Vector2(0.5f, 0f);
        tt.sizeDelta = new Vector2(0f, 40f);
        tt.anchoredPosition = new Vector2(0f, 14f);
        toastText.gameObject.AddComponent<Outline>();
    }

    private RectTransform CreatePanel(Transform parent, string panelName, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var go = new GameObject(panelName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = PanelColor;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        return rt;
    }

    private Text CreateText(Transform parent, string textName, int size, TextAnchor alignment)
    {
        var go = new GameObject(textName, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.supportRichText = true;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private void CreateButton(Transform parent, string label, Vector2 position, float width, float height,
        UnityAction onClick, int fontSize = 12)
    {
        RectTransform rt = CreatePanel(parent, "Button_" + label, new Vector2(0f, 1f), position, new Vector2(width, height));
        Image image = rt.GetComponent<Image>();
        image.color = ButtonColor;
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.8f, 0.9f, 1f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        button.colors = colors;
        rt.gameObject.AddComponent<HudPressButton>().onPress.AddListener(onClick);

        Text text = CreateText(rt, "Label", fontSize, TextAnchor.MiddleCenter);
        text.text = label;
        Stretch(text.rectTransform, 1f);
    }

    private static void AnchorTopLeft(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt, float padding)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }

    private static void EnsureEventSystem()
    {
        EventSystem es = FindAnyObjectByType<EventSystem>();
        if (es == null)
            es = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();

        var module = es.GetComponent<InputSystemUIInputModule>();
        if (module == null)
            module = es.gameObject.AddComponent<InputSystemUIInputModule>();
        // Modul yang dibuat lewat kode belum punya action map untuk pointer/klik.
        if (module.actionsAsset == null)
            module.AssignDefaultActions();
    }

    private void OnDestroy()
    {
        if (minimapTexture != null)
            Destroy(minimapTexture);
    }
}
