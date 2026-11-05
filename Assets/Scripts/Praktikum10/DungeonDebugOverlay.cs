using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Bantuan visual di dunia game (tombol V): jalur start-key, jalur key-goal, zona aman, dan penanda start.
/// Scene view juga menampilkan partisi BSP, room, serta posisi spawn melalui Gizmos.
/// </summary>
public class DungeonDebugOverlay : MonoBehaviour
{
    [SerializeField] private DungeonSpawner spawner;
    [SerializeField] private Material lineMaterial;
    [SerializeField, Min(0.01f)] private float lineWidth = 0.28f;
    [SerializeField] private float lineHeight = 0.12f;

    private static readonly Color KeyPathColor = new Color(0.25f, 0.9f, 1f);
    private static readonly Color GoalPathColor = new Color(1f, 0.85f, 0.2f);
    private static readonly Color SafeColor = new Color(0.25f, 1f, 0.4f);
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private LineRenderer keyLine;
    private LineRenderer goalLine;
    private LineRenderer safeRing;

    public bool Visible { get; private set; } = true;

    private void Awake()
    {
        keyLine = CreateLine("Path Start-Key", KeyPathColor);
        goalLine = CreateLine("Path Key-Goal", GoalPathColor);
        safeRing = CreateLine("Safe Zone", SafeColor);
        safeRing.loop = true;
    }

    private void OnEnable()
    {
        if (spawner != null)
            spawner.Generated += OnGenerated;
    }

    private void OnDisable()
    {
        if (spawner != null)
            spawner.Generated -= OnGenerated;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.vKey.wasPressedThisFrame)
            SetVisible(!Visible);
    }

    public void SetVisible(bool visible)
    {
        Visible = visible;
        keyLine.enabled = visible;
        goalLine.enabled = visible;
        safeRing.enabled = visible;
    }

    private void OnGenerated(DungeonResult result)
    {
        DungeonLayout layout = spawner.Layout;
        if (layout == null)
            return;

        SetPath(keyLine, layout.pathToKey, 0f);
        SetPath(goalLine, layout.pathToGoal, 0.04f);

        const int segments = 48;
        float radius = spawner.Config.safeRadius * spawner.CellSize;
        Vector3 center = spawner.CellToWorld(layout.start) + Vector3.up * lineHeight;
        safeRing.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            safeRing.SetPosition(i, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
        }
    }

    private void SetPath(LineRenderer line, List<Vector2Int> path, float extraHeight)
    {
        line.positionCount = path.Count;
        for (int i = 0; i < path.Count; i++)
            line.SetPosition(i, spawner.CellToWorld(path[i]) + Vector3.up * (lineHeight + extraHeight));
    }

    private LineRenderer CreateLine(string lineName, Color color)
    {
        var go = new GameObject(lineName);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.startWidth = line.endWidth = lineWidth;
        line.numCornerVertices = 2;
        line.alignment = LineAlignment.TransformZ;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        Material material = new Material(lineMaterial);
        material.color = color;
        if (material.HasProperty(BaseColorId))
            material.SetColor(BaseColorId, color);
        line.sharedMaterial = material;
        return line;
    }

    private void OnDrawGizmos()
    {
        if (spawner == null || spawner.Layout == null)
            return;

        DungeonLayout layout = spawner.Layout;
        float size = spawner.CellSize;

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
        foreach (RectInt leaf in layout.leaves)
            DrawRect(leaf, size, 0.3f);

        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
        foreach (DungeonRoom room in layout.rooms)
            DrawRect(room.rect, size, 0.35f);

        Gizmos.color = new Color(1f, 0.3f, 0.25f);
        foreach (DungeonEnemyPlan enemy in layout.enemies)
            Gizmos.DrawWireSphere(spawner.CellToWorld(enemy.cell) + Vector3.up, 0.6f);

        Gizmos.color = new Color(0.3f, 0.75f, 1f);
        foreach (DungeonItemPlan item in layout.items)
            Gizmos.DrawWireCube(spawner.CellToWorld(item.cell) + Vector3.up * 0.5f, Vector3.one * 0.8f);
    }

    private void DrawRect(RectInt r, float cell, float y)
    {
        var center = new Vector3(
            (r.x + r.width * 0.5f - spawner.Layout.width * 0.5f) * cell,
            y,
            (r.y + r.height * 0.5f - spawner.Layout.height * 0.5f) * cell);
        Gizmos.DrawWireCube(center, new Vector3(r.width * cell, 0.02f, r.height * cell));
    }
}
