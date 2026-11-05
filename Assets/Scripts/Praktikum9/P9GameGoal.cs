using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Goal sederhana Praktikum 9: ambil semua item hasil spawn, lalu capai Exit.
/// Kalah jika HP Player habis (diserang enemy Behavior Tree).
/// </summary>
public class P9GameGoal : MonoBehaviour
{
    public enum State { Playing, Won, Lost }

    [SerializeField] private Transform player;
    [SerializeField] private Health playerHealth;
    [SerializeField] private ExitGate exit;
    [Tooltip("Jika diisi, Exit terbuka setelah item jenis ini diambil (misalnya Key), bukan setelah semua item terkumpul.")]
    [SerializeField] private string requiredItemType = "";
    [Tooltip("Dimatikan saat menang/kalah (kontrol Player).")]
    [SerializeField] private MonoBehaviour[] disableOnEnd = new MonoBehaviour[0];

    private readonly Dictionary<string, int> collectedByType = new Dictionary<string, int>();

    public State Current { get; private set; } = State.Playing;
    public bool IsPlaying => Current == State.Playing;
    public bool PlayerDead => playerHealth != null && playerHealth.IsDead;
    public bool HasRequiredItem { get; private set; }
    public int ItemsTotal { get; private set; }
    public int ItemsCollected { get; private set; }
    public int Score { get; private set; }
    public int LegendaryCollected { get; private set; }
    public float RoundTime { get; private set; }
    public bool ExitOpen => exit != null && exit.IsOpen;
    public Health PlayerHealth => playerHealth;
    public IReadOnlyDictionary<string, int> CollectedByType => collectedByType;

    public event Action Changed;

    public void BeginRound(int totalItems)
    {
        ItemsTotal = totalItems;
        ItemsCollected = 0;
        Score = 0;
        LegendaryCollected = 0;
        RoundTime = 0f;
        collectedByType.Clear();
        HasRequiredItem = false;
        Current = State.Playing;
        SetControls(true);
        if (exit != null)
            exit.SetOpen(totalItems == 0 && string.IsNullOrEmpty(requiredItemType));
        Changed?.Invoke();
    }

    public void Collect(Collectible item)
    {
        if (!IsPlaying)
            return;

        ItemsCollected++;
        Score += item.Score;
        if (item.Legendary)
            LegendaryCollected++;
        collectedByType.TryGetValue(item.TypeName, out int n);
        collectedByType[item.TypeName] = n + 1;
        if (item.Heal > 0f && playerHealth != null)
            playerHealth.Heal(item.Heal);

        Debug.Log($"[Goal] Ambil {item.TypeName} (+{item.Score}) - item {ItemsCollected}/{ItemsTotal}, skor {Score}", item);

        bool needsType = !string.IsNullOrEmpty(requiredItemType);
        if (needsType && item.TypeName == requiredItemType)
            HasRequiredItem = true;

        bool unlocked = needsType ? HasRequiredItem : ItemsCollected >= ItemsTotal;
        if (unlocked && exit != null && !exit.IsOpen)
        {
            exit.SetOpen(true);
            Debug.Log(needsType
                ? $"[Goal] {requiredItemType} diambil, EXIT terbuka!"
                : "[Goal] Semua item terkumpul, EXIT terbuka!");
        }
        Changed?.Invoke();
    }

    private void Update()
    {
        if (!IsPlaying)
            return;

        RoundTime += Time.deltaTime;
        if (PlayerDead)
        {
            End(State.Lost);
        }
        else if (exit != null && exit.IsOpen && player != null && exit.Contains(player.position))
        {
            End(State.Won);
        }
    }

    private void End(State result)
    {
        Current = result;
        SetControls(false);
        Debug.Log(result == State.Won
            ? $"[Goal] MENANG! Skor {Score}, waktu {RoundTime:0.0} s"
            : "[Goal] KALAH - tekan R untuk mengulang seed yang sama");
        Changed?.Invoke();
    }

    private void SetControls(bool on)
    {
        foreach (MonoBehaviour b in disableOnEnd)
            if (b != null)
                b.enabled = on;
    }
}
