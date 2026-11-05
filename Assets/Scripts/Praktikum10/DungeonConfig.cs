using System;
using UnityEngine;

/// <summary>
/// Seluruh parameter generator Praktikum 10. Satuan ukuran adalah cell grid, bukan meter.
/// Seed dan konfigurasi yang sama selalu menghasilkan dungeon yang sama.
/// </summary>
[Serializable]
public class DungeonConfig
{
    public int seed = 100;

    [Header("Peta")]
    [Min(16)] public int mapWidth = 36;
    [Min(16)] public int mapHeight = 36;

    [Header("BSP")]
    [Tooltip("Kedalaman maksimum pembelahan. Makin besar, makin banyak room.")]
    [Min(1)] public int maxSplitDepth = 4;
    [Tooltip("Ukuran minimum partisi BSP. Makin kecil, pembelahan makin sering terjadi.")]
    [Min(5)] public int minLeafSize = 9;

    [Header("Room")]
    [Min(3)] public int roomMinSize = 4;
    [Min(3)] public int roomMaxSize = 9;
    [Tooltip("Jarak minimum room ke tepi partisinya. Makin besar, room makin renggang.")]
    [Min(1)] public int roomPadding = 1;

    [Header("Corridor")]
    [Range(1, 4)] public int corridorWidth = 2;
    [Tooltip("Corridor tambahan di luar pohon BSP, sehingga dungeon memiliki jalur memutar.")]
    [Min(0)] public int extraConnections = 1;

    [Header("Objek")]
    [Min(0)] public int enemyCount = 6;
    [Min(0)] public int itemCount = 8;
    [Min(0)] public int propCount = 10;
    [Range(0f, 1f)] public float potionChance = 0.3f;
    [Tooltip("Radius zona aman di sekitar start. Enemy dan prop tidak boleh muncul di dalamnya.")]
    [Min(0)] public int safeRadius = 5;
    [Tooltip("Jarak minimum antar objek.")]
    [Min(1)] public int minSpacing = 3;
    [Tooltip("Batas pengambilan posisi acak per objek.")]
    [Min(1)] public int maxPlacementAttempts = 40;

    [Header("Validasi")]
    [Tooltip("Batas generate ulang jika validasi dan repair tetap gagal.")]
    [Min(1)] public int maxGenerationAttempts = 5;

    /// <summary>Menjaga parameter tetap konsisten satu sama lain.</summary>
    public void Clamp()
    {
        mapWidth = Mathf.Clamp(mapWidth, 16, 80);
        mapHeight = Mathf.Clamp(mapHeight, 16, 80);
        maxSplitDepth = Mathf.Clamp(maxSplitDepth, 1, 7);
        roomPadding = Mathf.Clamp(roomPadding, 1, 4);
        roomMinSize = Mathf.Clamp(roomMinSize, 3, 12);
        roomMaxSize = Mathf.Clamp(roomMaxSize, roomMinSize, 20);
        minLeafSize = Mathf.Clamp(minLeafSize, roomMinSize + 2 * roomPadding, 30);
        corridorWidth = Mathf.Clamp(corridorWidth, 1, 4);
        extraConnections = Mathf.Clamp(extraConnections, 0, 8);
        enemyCount = Mathf.Clamp(enemyCount, 0, 40);
        itemCount = Mathf.Clamp(itemCount, 0, 40);
        propCount = Mathf.Clamp(propCount, 0, 60);
        safeRadius = Mathf.Clamp(safeRadius, 0, 12);
        minSpacing = Mathf.Clamp(minSpacing, 1, 8);
        maxPlacementAttempts = Mathf.Max(1, maxPlacementAttempts);
        maxGenerationAttempts = Mathf.Max(1, maxGenerationAttempts);
    }

    public DungeonConfig Clone()
    {
        return JsonUtility.FromJson<DungeonConfig>(JsonUtility.ToJson(this));
    }
}
