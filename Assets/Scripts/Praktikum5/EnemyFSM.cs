using System;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum EnemyFSMState { Patrol, Chase, Attack, Flee, Dead }

/// <summary>
/// Otak Enemy berbasis Finite State Machine.
///
/// Pembagian tugas:
/// - EnemyPerception : Condition "Player terlihat?" (range, FOV, Raycast LoS).
/// - Health          : Condition "HP rendah?" / "HP habis?".
/// - NavMeshAgent    : cara BERGERAK (pathfinding + steering). Bukan FSM, hanya alat yang dipakai state.
/// - Script ini      : memegang State aktif dan memutuskan Transition.
///
/// Urutan evaluasi transition tiap frame (prioritas tertinggi dulu):
/// 1. Any State   -> Dead   : HP &lt;= 0
/// 2. Patrol/Chase/Attack -> Flee : HP &lt;= fleeThreshold
/// 3. Transition biasa milik state aktif (Patrol->Chase, Chase->Attack, dst).
/// </summary>
[RequireComponent(typeof(NavMeshAgent), typeof(Health), typeof(EnemyPerception))]
public class EnemyFSM : MonoBehaviour
{
    [Header("State (read only)")]
    [SerializeField] private EnemyFSMState currentState = EnemyFSMState.Patrol;
    [SerializeField] private EnemyFSMState previousState = EnemyFSMState.Patrol;
    [SerializeField] private string lastTransition = "-";
    [SerializeField] private float timeInState;

    [Header("Patrol")]
    [SerializeField] private Transform[] waypoints;
    [SerializeField, Min(0f)] private float patrolSpeed = 2f;
    [SerializeField, Min(0f)] private float waypointTolerance = 0.6f;
    [Tooltip("Jeda singkat di tiap waypoint (detik).")]
    [SerializeField, Min(0f)] private float waypointWait = 1f;

    [Header("Chase")]
    [SerializeField, Min(0f)] private float chaseSpeed = 4.5f;
    [Tooltip("Player tidak terlihat selama ini (detik) -> kembali Patrol.")]
    [SerializeField, Min(0f)] private float loseSightTime = 3f;

    [Header("Attack")]
    [Tooltip("Masuk Attack jika jarak <= nilai ini.")]
    [SerializeField, Min(0f)] private float attackRange = 2f;
    [Tooltip("Keluar Attack (kembali Chase) jika jarak > nilai ini. Harus > attackRange (hysteresis).")]
    [SerializeField, Min(0f)] private float attackExitRange = 3f;
    [SerializeField, Min(0f)] private float attackDamage = 10f;
    [SerializeField, Min(0.05f)] private float attackCooldown = 1.5f;
    [Tooltip("Jeda sebelum pukulan pertama setelah masuk Attack.")]
    [SerializeField, Min(0f)] private float attackWindup = 0.4f;
    [SerializeField, Min(0f)] private float turnSpeed = 10f;

    [Header("Flee")]
    [Tooltip("Masuk Flee jika HP <= fraksi ini dari Max HP.")]
    [SerializeField, Range(0f, 1f)] private float fleeThreshold = 0.3f;
    [SerializeField, Min(0f)] private float fleeSpeed = 6f;
    [SerializeField] private SafePoint[] safePoints;

    [Header("Debug")]
    [SerializeField] private bool logTransitions = true;

    private NavMeshAgent agent;
    private Health health;
    private EnemyPerception perception;
    private Health targetHealth;

    private int waypointIndex;
    private float waitTimer;
    private float nextAttackTime;
    private SafePoint fleeTarget;

    /// <summary>(dari, ke) setiap kali state berganti.</summary>
    public event Action<EnemyFSMState, EnemyFSMState> StateChanged;
    /// <summary>Dipanggil setiap kali pukulan dilepaskan.</summary>
    public event Action Attacked;

    public EnemyFSMState CurrentState => currentState;
    public float AttackCooldownLeft => Mathf.Max(0f, nextAttackTime - Time.time);
    public float AttackCooldown => attackCooldown;
    public SafePoint FleeTarget => fleeTarget;

    private Transform Target => perception.Target;

    private float DistanceToTarget
    {
        get
        {
            if (Target == null)
                return float.PositiveInfinity;
            Vector3 d = Target.position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }
    }

    /// <summary>Dipakai spawner (Praktikum 10): prefab tidak bisa menyimpan referensi ke objek scene.</summary>
    public void SetRoute(Transform[] route, SafePoint[] safe)
    {
        waypoints = route;
        safePoints = safe;
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
        perception = GetComponent<EnemyPerception>();
        if (Target != null)
            targetHealth = Target.GetComponent<Health>();
    }

    private void OnValidate()
    {
        attackExitRange = Mathf.Max(attackExitRange, attackRange + 0.1f);
    }

    private void Start()
    {
        EnterState(currentState);
        Log($"Start di {currentState}");
    }

    private void Update()
    {
        timeInState += Time.deltaTime;

        // 1. Any State -> Dead (prioritas tertinggi, dicek sebelum logika state lain).
        if (currentState != EnemyFSMState.Dead && health.IsDead)
        {
            ChangeState(EnemyFSMState.Dead, "HP <= 0");
            return;
        }
        if (currentState == EnemyFSMState.Dead)
            return;

        // 2. Patrol / Chase / Attack -> Flee.
        if (currentState != EnemyFSMState.Flee && health.Normalized <= fleeThreshold)
        {
            ChangeState(EnemyFSMState.Flee, $"HP rendah ({health.Current:0}/{health.Max:0})");
            return;
        }

        // 3. Perilaku + transition milik state aktif.
        switch (currentState)
        {
            case EnemyFSMState.Patrol: UpdatePatrol(); break;
            case EnemyFSMState.Chase: UpdateChase(); break;
            case EnemyFSMState.Attack: UpdateAttack(); break;
            case EnemyFSMState.Flee: UpdateFlee(); break;
        }
    }

    // ------------------------------------------------------------------ Mesin state

    private void ChangeState(EnemyFSMState next, string reason)
    {
        if (next == currentState)
            return;

        EnemyFSMState from = currentState;
        ExitState(from);
        previousState = from;
        currentState = next;
        timeInState = 0f;
        lastTransition = $"{from} -> {next} ({reason})";
        EnterState(next);

        Log($"{from} -> <b>{next.ToString().ToUpper()}</b>  karena: {reason}");
        StateChanged?.Invoke(from, next);
    }

    private void EnterState(EnemyFSMState state)
    {
        switch (state)
        {
            case EnemyFSMState.Patrol:
                agent.isStopped = false;
                agent.speed = patrolSpeed;
                agent.stoppingDistance = 0f;
                waitTimer = 0f;
                waypointIndex = NearestWaypointIndex();
                GoToWaypoint();
                break;

            case EnemyFSMState.Chase:
                agent.isStopped = false;
                agent.speed = chaseSpeed;
                agent.stoppingDistance = attackRange * 0.8f;
                break;

            case EnemyFSMState.Attack:
                agent.isStopped = true;
                agent.ResetPath();
                agent.updateRotation = false;
                nextAttackTime = Mathf.Max(nextAttackTime, Time.time + attackWindup);
                break;

            case EnemyFSMState.Flee:
                agent.isStopped = false;
                agent.speed = fleeSpeed;
                agent.stoppingDistance = 0f;
                fleeTarget = PickSafePoint();
                if (fleeTarget != null)
                    agent.SetDestination(fleeTarget.transform.position);
                break;

            case EnemyFSMState.Dead:
                if (agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.ResetPath();
                }
                agent.enabled = false;
                perception.enabled = false;
                break;
        }
    }

    private void ExitState(EnemyFSMState state)
    {
        switch (state)
        {
            case EnemyFSMState.Attack:
                agent.updateRotation = true;
                break;
            case EnemyFSMState.Flee:
                fleeTarget = null;
                break;
        }
    }

    // ------------------------------------------------------------------ Patrol

    private void UpdatePatrol()
    {
        if (perception.CanSeeTarget)
        {
            ChangeState(EnemyFSMState.Chase, "Player terlihat");
            return;
        }

        if (waypoints == null || waypoints.Length == 0 || agent.pathPending)
            return;

        if (agent.remainingDistance > waypointTolerance)
            return;

        // Sampai di waypoint: tunggu sebentar, lalu lanjut ke berikutnya (loop).
        waitTimer += Time.deltaTime;
        if (waitTimer < waypointWait)
            return;

        waitTimer = 0f;
        waypointIndex = (waypointIndex + 1) % waypoints.Length;
        GoToWaypoint();
    }

    private void GoToWaypoint()
    {
        if (waypoints != null && waypoints.Length > 0 && waypoints[waypointIndex] != null)
            agent.SetDestination(waypoints[waypointIndex].position);
    }

    private int NearestWaypointIndex()
    {
        int best = waypointIndex;
        float bestDist = float.PositiveInfinity;
        for (int i = 0; waypoints != null && i < waypoints.Length; i++)
        {
            if (waypoints[i] == null)
                continue;
            float d = (waypoints[i].position - transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ Chase

    private void UpdateChase()
    {
        if (TargetIsDead())
        {
            ChangeState(EnemyFSMState.Patrol, "Player sudah kalah");
            return;
        }

        if (perception.CanSeeTarget && DistanceToTarget <= attackRange)
        {
            ChangeState(EnemyFSMState.Attack, $"Player masuk Attack Range ({DistanceToTarget:0.0} m)");
            return;
        }

        if (perception.TimeSinceLastSeen > loseSightTime)
        {
            ChangeState(EnemyFSMState.Patrol, $"Player hilang > {loseSightTime:0.#} detik");
            return;
        }

        // Terlihat: kejar posisi Player. Tidak terlihat: menuju posisi terakhir yang diketahui.
        Vector3 destination = perception.CanSeeTarget ? Target.position : perception.LastKnownPosition;
        if ((agent.destination - destination).sqrMagnitude > 0.04f)
            agent.SetDestination(destination);
    }

    // ------------------------------------------------------------------ Attack

    private void UpdateAttack()
    {
        if (TargetIsDead())
        {
            ChangeState(EnemyFSMState.Patrol, "Player sudah kalah");
            return;
        }

        if (DistanceToTarget > attackExitRange)
        {
            ChangeState(EnemyFSMState.Chase, $"Player menjauh > Attack Exit Range ({DistanceToTarget:0.0} m)");
            return;
        }

        if (perception.TimeSinceLastSeen > 0.5f)
        {
            ChangeState(EnemyFSMState.Chase, "Line of Sight ke Player terputus");
            return;
        }

        FaceTarget();

        if (Time.time < nextAttackTime)
            return;

        nextAttackTime = Time.time + attackCooldown;
        if (targetHealth != null)
            targetHealth.TakeDamage(attackDamage);
        Attacked?.Invoke();
        Log($"ATTACK! damage {attackDamage:0} ke Player" +
            (targetHealth != null ? $" (HP Player {targetHealth.Current:0}/{targetHealth.Max:0})" : ""));
    }

    private void FaceTarget()
    {
        Vector3 dir = Target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            return;
        Quaternion look = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
    }

    private bool TargetIsDead()
    {
        return Target == null || (targetHealth != null && targetHealth.IsDead);
    }

    // ------------------------------------------------------------------ Flee

    private void UpdateFlee()
    {
        if (fleeTarget == null)
        {
            ChangeState(EnemyFSMState.Patrol, "Tidak ada SafePoint");
            return;
        }

        if (fleeTarget.Contains(transform.position))
        {
            fleeTarget.Rest(health);
            ChangeState(EnemyFSMState.Patrol,
                $"Sampai di {fleeTarget.name}, HP pulih ke {health.Current:0}/{health.Max:0}");
        }
    }

    /// <summary>
    /// (Bonus multiple SafePoint) Pilih SafePoint yang paling menjauhkan Enemy dari Player:
    /// skor = jarak SafePoint ke Player - jarak SafePoint ke Enemy.
    /// Jadi Enemy tidak memilih tempat aman yang jalurnya justru melewati Player.
    /// </summary>
    private SafePoint PickSafePoint()
    {
        SafePoint best = null;
        float bestScore = float.NegativeInfinity;
        foreach (SafePoint sp in safePoints)
        {
            if (sp == null)
                continue;
            Vector3 p = sp.transform.position;
            float fromPlayer = Target != null ? Vector3.Distance(p, Target.position) : 0f;
            float score = fromPlayer - Vector3.Distance(p, transform.position);
            if (score > bestScore)
            {
                bestScore = score;
                best = sp;
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ Debug

    private void Log(string message)
    {
        if (logTransitions)
            Debug.Log($"<color={StateColorHex(currentState)}>[EnemyFSM:{currentState}]</color> {message}", this);
    }

    public static Color StateColor(EnemyFSMState state)
    {
        switch (state)
        {
            case EnemyFSMState.Patrol: return new Color(0.3f, 0.9f, 0.35f);
            case EnemyFSMState.Chase: return new Color(1f, 0.8f, 0.15f);
            case EnemyFSMState.Attack: return new Color(1f, 0.2f, 0.2f);
            case EnemyFSMState.Flee: return new Color(0.3f, 0.6f, 1f);
            default: return new Color(0.55f, 0.55f, 0.55f);
        }
    }

    private static string StateColorHex(EnemyFSMState state)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(StateColor(state));
    }

    private void OnDrawGizmos()
    {
        Vector3 origin = transform.position + Vector3.up * 0.05f;

#if UNITY_EDITOR
        // Attack Range (masuk) dan Attack Exit Range (keluar).
        Handles.color = new Color(1f, 0.15f, 0.15f);
        Handles.DrawWireDisc(origin, Vector3.up, attackRange, 3f);
        Handles.color = new Color(1f, 0.55f, 0.1f);
        Handles.DrawWireDisc(origin, Vector3.up, attackExitRange, 1.5f);

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        style.normal.textColor = StateColor(currentState);
        Handles.Label(transform.position + Vector3.up * 2.4f, currentState.ToString().ToUpper(), style);
#endif

        // Rute patroli.
        if (waypoints != null && waypoints.Length > 1)
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.8f);
            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform a = waypoints[i];
                Transform b = waypoints[(i + 1) % waypoints.Length];
                if (a == null || b == null)
                    continue;
                Gizmos.DrawLine(a.position + Vector3.up * 0.1f, b.position + Vector3.up * 0.1f);
                Gizmos.DrawSphere(a.position + Vector3.up * 0.1f, 0.25f);
            }
        }

        // Tujuan agent saat ini.
        if (Application.isPlaying && agent != null && agent.enabled && agent.hasPath)
        {
            Gizmos.color = StateColor(currentState);
            Gizmos.DrawLine(origin, agent.destination + Vector3.up * 0.05f);
        }
    }
}
