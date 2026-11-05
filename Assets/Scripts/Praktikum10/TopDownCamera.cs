using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Kamera top-down. Mode Follow mengikuti Player, mode Overview memperlihatkan seluruh dungeon (tombol M).
/// Sudut Overview dibuat sedikit di bawah 90 derajat: PlayerController meratakan arah depan kamera,
/// dan arah yang tegak lurus ke bawah akan menjadi vektor nol.
/// </summary>
[RequireComponent(typeof(Camera))]
public class TopDownCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private DungeonSpawner spawner;

    [Header("Follow")]
    [SerializeField, Range(30f, 88f)] private float pitch = 62f;
    [SerializeField, Min(1f)] private float distance = 21f;
    [SerializeField, Min(0f)] private float smoothTime = 0.12f;

    [Header("Overview")]
    [SerializeField, Range(30f, 88f)] private float overviewPitch = 88f;
    [SerializeField, Min(1f)] private float overviewMargin = 1.1f;

    private Camera cam;
    private Vector3 velocity;
    private bool snapNextFrame = true;

    public bool Overview { get; private set; }

    private void Awake()
    {
        cam = GetComponent<Camera>();
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

    private void OnGenerated(DungeonResult result)
    {
        snapNextFrame = true;
    }

    public void ToggleOverview()
    {
        Overview = !Overview;
    }

    private void LateUpdate()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.mKey.wasPressedThisFrame)
            ToggleOverview();

        DungeonLayout layout = spawner != null ? spawner.Layout : null;
        bool showAll = Overview && layout != null;

        float angle = showAll ? overviewPitch : pitch;
        Quaternion rotation = Quaternion.Euler(angle, 0f, 0f);
        Vector3 focus = showAll ? Vector3.zero : (target != null ? target.position : Vector3.zero);
        float arm = showAll ? OverviewDistance(layout) : distance;
        Vector3 wanted = focus - rotation * Vector3.forward * arm;

        if (snapNextFrame)
        {
            transform.SetPositionAndRotation(wanted, rotation);
            velocity = Vector3.zero;
            snapNextFrame = false;
            return;
        }

        transform.position = Vector3.SmoothDamp(transform.position, wanted, ref velocity, smoothTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 1f - Mathf.Exp(-10f * Time.deltaTime));
    }

    private float OverviewDistance(DungeonLayout layout)
    {
        float halfTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float fitHeight = layout.height * spawner.CellSize * 0.5f / halfTan;
        float fitWidth = layout.width * spawner.CellSize * 0.5f / (halfTan * cam.aspect);
        return Mathf.Max(fitHeight, fitWidth) * overviewMargin;
    }
}
