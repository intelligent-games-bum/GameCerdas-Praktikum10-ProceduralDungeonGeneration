using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Locomotion states of the Player, driven by the flight ability.
/// </summary>
public enum PlayerLocomotionState
{
    /// <summary>Walking on the ground.</summary>
    Grounded,

    /// <summary>Airborne from a jump. A second tap while here starts a flight.</summary>
    Jumping,

    /// <summary>Airborne under the player's own control, until the flight timer runs out.</summary>
    Flying,

    /// <summary>Airborne without control, falling back down to the ground.</summary>
    Falling
}

/// <summary>
/// Moves the Player with WASD or the arrow keys relative to the Camera view,
/// rotates the character towards the direction it is walking, and provides a
/// timed flight ability.
///
/// Movement goes through a CharacterController, so walls and props stop the
/// Player without this code having to test for them. The vertical motion is
/// still integrated by hand, because the flight needs control that ordinary
/// gravity would not give.
///
/// The ground is still sampled with a downward ray, but only to know how high
/// above it the Player is, which is what the flight ceiling is measured from.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField]
    [Min(0f)]
    [Tooltip("Movement speed on the ground, in units per second.")]
    private float moveSpeed = 5f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Movement speed while flying, in units per second.")]
    private float flightMoveSpeed = 7f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Speed at which the character rotates to face movement direction.")]
    private float rotationSpeed = 15f;

    [Header("References")]
    [SerializeField]
    [Tooltip("Drag the Main Camera here so movement matches the camera view.")]
    private Transform cameraTransform;

    [Header("Jump and Flight - Input")]
    [SerializeField]
    [Tooltip("Turn off to keep the Player on the ground, for levels bounded by walls.")]
    private bool allowFlight = true;

    [SerializeField]
    [Tooltip("Tap once to jump, twice to take off. While flying, one tap gains " +
             "height and two quick taps cancel the flight.")]
    private Key flyKey = Key.Space;

    [SerializeField]
    [Tooltip("Held while flying to sink gently.")]
    private Key slowDescendKey = Key.LeftShift;

    [SerializeField]
    [Min(0.05f)]
    [Tooltip("How close together two taps have to be to count as a double tap.")]
    private float doubleTapWindow = 0.25f;

    [Header("Flight - Timing")]
    [SerializeField]
    [Min(0f)]
    [Tooltip("How long a single flight lasts, in seconds.")]
    private float flightDuration = 10f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Delay after landing before the next flight can be started, in seconds.")]
    private float flightCooldown = 3f;

    [Header("Jump and Flight - Motion")]
    [SerializeField]
    [Min(0f)]
    [Tooltip("Upward speed of a jump. The height reached is this value squared, " +
             "divided by twice the gravity.")]
    private float jumpSpeed = 8f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Upward speed given the instant a flight starts, so take off feels immediate.")]
    private float takeOffSpeed = 6f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Upward kick from each tap while flying. The kick fades out again, " +
             "so every tap raises the Player by one step.")]
    private float ascendImpulse = 6f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Sinking speed while the slow descend key is held.")]
    private float slowDescendSpeed = 2.5f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Ceiling of the flight, measured from the ground below the Player.")]
    private float maxFlightHeight = 12f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("How quickly the vertical speed reaches the speed being asked for. " +
             "Lower is floatier.")]
    private float verticalSmoothing = 8f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Downward acceleration once the flight timer runs out.")]
    private float gravity = 20f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Terminal falling speed, in units per second.")]
    private float maxFallSpeed = 18f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Constant push into the floor while standing. Keeps the controller " +
             "reporting itself grounded and lets it follow slopes downhill.")]
    private float groundStickSpeed = 2f;

    [Header("Ground Probe")]
    [SerializeField]
    [Tooltip("Height above the Player's pivot the ground ray starts from. Keep it " +
             "small so the ray cannot start above a tree canopy.")]
    private float groundProbeOffset = 0.5f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("How far down the ground ray reaches.")]
    private float groundProbeDistance = 100f;

    [Header("Debug (read only)")]
    [SerializeField]
    [Tooltip("Locomotion state on this frame.")]
    private PlayerLocomotionState state = PlayerLocomotionState.Grounded;

    [SerializeField]
    [Tooltip("Seconds of flight remaining.")]
    private float flightTimeLeft;

    [SerializeField]
    [Tooltip("Seconds before flying is available again.")]
    private float cooldownLeft;

    [SerializeField]
    [Tooltip("Current vertical speed, in units per second.")]
    private float verticalVelocity;

    [SerializeField]
    [Tooltip("Distance between the Player and the ground below it.")]
    private float heightAboveGround;

    [SerializeField]
    [Tooltip("Normalized direction produced by the current input.")]
    private Vector3 moveDirection;

    /// <summary>Height of the ground directly below the Player.</summary>
    private float groundY;

    /// <summary>When the fly key was last tapped, used to spot double taps.</summary>
    private float lastFlyTapTime = -999f;

    private CharacterController characterController;

    /// <summary>
    /// Read by the camera so the view can pull back while the Player is airborne.
    /// </summary>
    public bool IsFlying => state == PlayerLocomotionState.Flying;

    public bool IsAirborne => state != PlayerLocomotionState.Grounded;

    /// <summary>
    /// True while the character should hold a flight pose: during the flight
    /// itself and the fall that follows it, but not during a plain jump.
    /// </summary>
    public bool UsesFlightPose =>
        state == PlayerLocomotionState.Flying || state == PlayerLocomotionState.Falling;
    public float FlightTimeLeft => flightTimeLeft;
    public float CooldownLeft => cooldownLeft;
    public PlayerLocomotionState State => state;

    /// <summary>
    /// True while a movement key is held. Read by the animator driver, which
    /// only plays the run and fly clips while the Player is actually steering.
    /// </summary>
    public bool IsMoving => moveDirection != Vector3.zero;

    /// <summary>How hard the Player is being steered, from 0 to 1.</summary>
    public float MoveAmount => Mathf.Clamp01(moveDirection.magnitude);

    /// <summary>True when a new flight can be started right now.</summary>
    public bool CanFly => state == PlayerLocomotionState.Grounded && cooldownLeft <= 0f;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    private void Start()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        // Seed the ground height, so the first frame already has a sane value
        // even if the probe finds nothing.
        groundY = transform.position.y;
        SampleGround();
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        ReadInput();
        SampleGround();
        UpdateFlightState(deltaTime);
        ApplyMotion(deltaTime);
        RotateTowardsMovement(deltaTime);
    }

    /// <summary>
    /// Reads the keyboard and turns it into a direction vector.
    /// The vector is normalized so diagonal movement is not faster than
    /// movement along a single axis.
    /// </summary>
    private void ReadInput()
    {
        Keyboard keyboard = Keyboard.current;

        // Keyboard can be null when no device is connected.
        if (keyboard == null)
        {
            moveDirection = Vector3.zero;
            return;
        }

        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
        {
            horizontal -= 1f;
        }

        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
        {
            horizontal += 1f;
        }

        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
        {
            vertical -= 1f;
        }

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
        {
            vertical += 1f;
        }

        moveDirection = new Vector3(horizontal, 0f, vertical);

        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection = moveDirection.normalized;
        }
    }

    /// <summary>
    /// Finds the ground height directly below the Player.
    ///
    /// Colliders belonging to the Player itself are skipped, so this works no
    /// matter which layer the character sits on. If nothing is found, the last
    /// known ground height is kept rather than dropping the Player into the void.
    /// </summary>
    private void SampleGround()
    {
        Vector3 origin = transform.position + Vector3.up * groundProbeOffset;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            groundProbeDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        float closest = float.MaxValue;
        bool found = false;

        Transform selfRoot = transform.root;

        for (int i = 0; i < hits.Length; i++)
        {
            // Ignore every collider belonging to the character. Comparing roots
            // rather than using IsChildOf matters because this script does not
            // have to sit on the top of the prefab.
            if (hits[i].collider.transform.root == selfRoot)
            {
                continue;
            }

            if (hits[i].distance < closest)
            {
                closest = hits[i].distance;
                found = true;
            }
        }

        if (found)
        {
            groundY = origin.y - closest;
        }

        heightAboveGround = transform.position.y - groundY;
    }

    /// <summary>
    /// Runs the jump and flight state machine and works out the vertical speed
    /// for this frame.
    /// </summary>
    private void UpdateFlightState(float deltaTime)
    {
        Keyboard keyboard = Keyboard.current;

        bool tapped = allowFlight && keyboard != null && keyboard[flyKey].wasPressedThisFrame;
        bool descendHeld = keyboard != null && keyboard[slowDescendKey].isPressed;
        bool doubleTapped = false;

        if (tapped)
        {
            if (Time.time - lastFlyTapTime <= doubleTapWindow)
            {
                doubleTapped = true;

                // Forget the pair, so a third tap opens a fresh one instead of
                // counting as yet another double.
                lastFlyTapTime = -999f;
            }
            else
            {
                lastFlyTapTime = Time.time;
            }
        }

        switch (state)
        {
            case PlayerLocomotionState.Grounded:
                if (cooldownLeft > 0f)
                {
                    cooldownLeft -= deltaTime;
                }

                // A steady push into the floor. Without it the controller stops
                // reporting itself grounded the moment it walks over a bump,
                // and it would step off downhill slopes instead of hugging them.
                verticalVelocity = -groundStickSpeed;

                // The first tap always jumps. Holding it back to find out
                // whether a second one is coming would put a delay on every
                // single jump, which is worse than the occasional jump that
                // turns into a take off.
                if (tapped)
                {
                    state = PlayerLocomotionState.Jumping;
                    verticalVelocity = jumpSpeed;
                }
                break;

            case PlayerLocomotionState.Jumping:
                if (cooldownLeft > 0f)
                {
                    cooldownLeft -= deltaTime;
                }

                // The second tap of the pair lands here, mid jump, and turns
                // the jump into a take off.
                if (doubleTapped && cooldownLeft <= 0f)
                {
                    state = PlayerLocomotionState.Flying;
                    flightTimeLeft = flightDuration;
                    verticalVelocity = takeOffSpeed;
                    break;
                }

                verticalVelocity -= gravity * deltaTime;
                verticalVelocity = Mathf.Max(verticalVelocity, -maxFallSpeed);
                break;

            case PlayerLocomotionState.Flying:
                flightTimeLeft -= deltaTime;

                // Two quick taps drop out of the flight on purpose.
                if (flightTimeLeft <= 0f || doubleTapped)
                {
                    flightTimeLeft = 0f;
                    state = PlayerLocomotionState.Falling;
                    break;
                }

                // Left alone the Player settles to a hover; holding the descend
                // key settles it to a gentle sink instead.
                float wanted = descendHeld ? -slowDescendSpeed : 0f;

                verticalVelocity = Mathf.Lerp(
                    verticalVelocity,
                    wanted,
                    Smoothing(verticalSmoothing, deltaTime));

                // A single tap kicks upwards. Applied after the easing above so
                // the kick is not damped away on the very frame it is given.
                if (tapped && heightAboveGround < maxFlightHeight)
                {
                    verticalVelocity = ascendImpulse;
                }

                // Stop dead at the ceiling instead of pressing against it. The
                // position is no longer clamped, so this is what enforces it.
                if (verticalVelocity > 0f && heightAboveGround >= maxFlightHeight)
                {
                    verticalVelocity = 0f;
                }
                break;

            case PlayerLocomotionState.Falling:
                verticalVelocity -= gravity * deltaTime;
                verticalVelocity = Mathf.Max(verticalVelocity, -maxFallSpeed);
                break;
        }
    }

    /// <summary>
    /// Moves the CharacterController, then reads back what the move actually
    /// achieved in order to update the state.
    ///
    /// Collision is the controller's job, so nothing here writes the position
    /// directly any more. Walls and crates stop the Player because the
    /// controller refuses to push through them, not because this code looked.
    /// </summary>
    private void ApplyMotion(float deltaTime)
    {
        Vector3 motion = Vector3.zero;

        // Horizontal, relative to where the camera is looking.
        if (moveDirection != Vector3.zero)
        {
            float speed = IsFlying ? flightMoveSpeed : moveSpeed;
            motion = ResolveMoveDirection() * speed;
        }

        motion.y = verticalVelocity;

        characterController.Move(motion * deltaTime);

        bool onGround = characterController.isGrounded;

        switch (state)
        {
            case PlayerLocomotionState.Grounded:
                // Walked off a ledge. Counted as a jump, so the flight stays
                // available and touching down again costs no cooldown.
                if (!onGround)
                {
                    state = PlayerLocomotionState.Jumping;
                    verticalVelocity = 0f;
                }
                break;

            case PlayerLocomotionState.Jumping:
                if (onGround && verticalVelocity <= 0f)
                {
                    verticalVelocity = 0f;
                    state = PlayerLocomotionState.Grounded;
                }
                break;

            case PlayerLocomotionState.Flying:
            case PlayerLocomotionState.Falling:
                // Touching down always ends the flight. Testing the direction
                // of travel as well stops a take off, which begins at ground
                // level, from cancelling itself on its very first frame.
                if (onGround && verticalVelocity <= 0f)
                {
                    Land();
                }
                break;
        }
    }

    /// <summary>
    /// Ends a flight on the ground and starts the cooldown.
    /// </summary>
    private void Land()
    {
        verticalVelocity = 0f;
        flightTimeLeft = 0f;
        state = PlayerLocomotionState.Grounded;
        cooldownLeft = flightCooldown;
    }

    /// <summary>
    /// Projects the raw input onto the camera's horizontal plane, so W always
    /// means "away from the camera" no matter where the camera has been turned.
    /// </summary>
    private Vector3 ResolveMoveDirection()
    {
        if (cameraTransform == null)
        {
            return moveDirection;
        }

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;

        // Flatten, otherwise looking down would shrink the movement.
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        return forward * moveDirection.z + right * moveDirection.x;
    }

    /// <summary>
    /// Turns the character to face the way it is going.
    /// </summary>
    private void RotateTowardsMovement(float deltaTime)
    {
        if (moveDirection == Vector3.zero)
        {
            return;
        }

        Vector3 direction = ResolveMoveDirection();
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Smoothing(rotationSpeed, deltaTime));
    }

    /// <summary>
    /// Exponential smoothing factor. Unlike a plain "speed * deltaTime" this
    /// lands on the same result whatever the frame rate is.
    /// </summary>
    private static float Smoothing(float sharpness, float deltaTime)
    {
        return 1f - Mathf.Exp(-sharpness * deltaTime);
    }

    /// <summary>
    /// Draws the flight ceiling and the sampled ground point in the Scene view.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Vector3 ground = new Vector3(
            transform.position.x,
            Application.isPlaying ? groundY : transform.position.y,
            transform.position.z);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(ground, new Vector3(1f, 0.02f, 1f));

        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawLine(ground, ground + Vector3.up * maxFlightHeight);
        Gizmos.DrawWireCube(
            ground + Vector3.up * maxFlightHeight,
            new Vector3(1f, 0.02f, 1f));
    }
}
