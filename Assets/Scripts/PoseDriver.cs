using UnityEngine;

/// <summary>
/// PD (Proportional-Derivative) controller that smoothly drives a live joint
/// to match a target joint on the frozen copy.
/// 
/// Instead of teleporting or locking limits, this uses motor speed calculated as:
///   motorSpeed = Kp × error + Kd × derivative
/// 
/// This gives smooth, critically-damped convergence:
/// - Accelerates toward the target pose
/// - Decelerates as it approaches (no overshoot)
/// - Reacts naturally to hits (gets knocked off-pose, then recovers)
/// - Can be overwhelmed by strong forces (not infinitely stiff)
///
/// Attach one of these to each limb of the LIVE fighter.
/// </summary>
public class PoseDriver : MonoBehaviour
{
    [Header("=== Feature Toggles ===")]
    [Tooltip("Master toggle for pose driving on this joint")]
    public bool enablePoseDriver = true;

    [Tooltip("Show debug gizmo of target vs current angle")]
    public bool showDebugGizmo = false;

    [Header("=== PD Controller Tuning ===")]
    [Tooltip("Proportional gain — how aggressively the joint chases the target. Higher = snappier response.")]
    public float Kp = 40f;

    [Tooltip("Derivative gain — how much the joint resists overshoot. Higher = smoother settling.")]
    public float Kd = 8f;

    [Tooltip("Maximum motor speed. Caps how fast a joint can spin.")]
    public float maxMotorSpeed = 800f;

    [Tooltip("Deadzone in degrees. Below this error, the motor fully disengages.")]
    public float deadzone = 0.5f;

    [Tooltip("Relaxation range in degrees above the deadzone. Motor output smoothly ramps from 0 to full across this zone. Prevents vibration from constant micro-corrections.")]
    public float motorRelaxationRange = 8f;

    [Tooltip("Allow this driver to chase target angles outside the driven joint's normal limits.")]
    public bool ignoreTargetJointLimits = false;

    [Header("=== Anatomical Multipliers ===")]
    [Tooltip("Anatomical multiplier for Kp. Set automatically based on bone name at Start, but can be overridden.")]
    public float KpMultiplier = 1.0f;

    [Tooltip("Anatomical multiplier for Kd. Set automatically based on bone name at Start, but can be overridden.")]
    public float KdMultiplier = 1.0f;

    [Header("=== Hit Reaction State ===")]
    [Tooltip("Current hit reaction multiplier. Weakens joint motor on impact.")]
    public float hitReactionMultiplier = 1.0f;
    private float hitReactionTimer = 0f;
    private float hitReactionDuration = 0f;

    [Header("=== Runtime Multipliers ===")]
    [Tooltip("Temporary speed multiplier used by turn intents such as strikes.")]
    public float runtimeSpeedMultiplier = 1f;

    [Tooltip("Temporary torque multiplier used by turn intents such as strikes.")]
    public float runtimeTorqueMultiplier = 1f;

    [Header("=== Strike Impulse ===")]
    [Tooltip("Angular velocity (deg/s) injected directly when a strike begins. PD is bypassed for strikeImpulseFrames fixed steps.")]
    public float strikeImpulseAngularSpeed = 1200f;

    [Tooltip("Fixed steps the PD motor stays disabled while the limb flies under strike impulse.")]
    public int strikeImpulseFrames = 6;

    // Runtime state for strike impulse
    private int strikeImpulseFramesRemaining = 0;

    [Header("=== Airborne Compensation ===")]
    [Tooltip("Rotation delta (live root - frozen root) in degrees. Set by FRV2 during airborne sim so joints connected to the root maintain their world-space orientation.")]
    [HideInInspector]
    public float airborneRootRotationOffset = 0f;

    [Header("=== References ===")]
    [Tooltip("The HingeJoint2D on this live limb that gets driven")]
    public HingeJoint2D drivenJoint;

    [Tooltip("The HingeJoint2D on the frozen copy limb to match")]
    public HingeJoint2D targetJoint;

    // Runtime state
    private float lastError = 0f;
    private bool initialized = false;
    private JointAngleLimits2D originalLimits;
    private bool hadOriginalLimits;
    private float originalMotorTorque = 150f;
    private int settlingFrames = 30; // ~0.5 sec at 50Hz physics — let figure settle before driving
    private Rigidbody2D rb;

    void Start()
    {
        if (drivenJoint == null)
            drivenJoint = GetComponent<HingeJoint2D>();

        rb = GetComponent<Rigidbody2D>();

        if (drivenJoint != null)
        {
            // Store original limits so we don't overwrite them
            originalLimits = drivenJoint.limits;
            hadOriginalLimits = drivenJoint.useLimits;
            originalMotorTorque = Mathf.Max(1f, drivenJoint.motor.maxMotorTorque);
            initialized = true;

            // Auto-configure anatomical multipliers based on limb name
            string n = gameObject.name.ToLowerInvariant();
            if (n.Contains("spine") || n.Contains("torso") || n.Contains("body"))
            {
                KpMultiplier = 2.0f;
                KdMultiplier = 1.5f;
            }
            else if (n.Contains("head"))
            {
                KpMultiplier = 0.6f;
                KdMultiplier = 0.8f; // Head wobbles slightly on hit
            }
            else if (n.Contains("thigh") || n.Contains("knee"))
            {
                KpMultiplier = 1.2f;
                KdMultiplier = 1.0f;
            }
            else if (n.Contains("calf") || n.Contains("shin") || n.Contains("lower leg"))
            {
                KpMultiplier = 1.0f;
                KdMultiplier = 1.0f;
            }
            else if (n.Contains("foot") || n.Contains("toe"))
            {
                KpMultiplier = 0.5f;
                KdMultiplier = 0.8f; // Floppy feet
            }
            else if (n.Contains("upper arm") || n.Contains("shoulder"))
            {
                KpMultiplier = 0.8f;
                KdMultiplier = 0.8f;
            }
            else if (n.Contains("lower arm") || n.Contains("elbow"))
            {
                KpMultiplier = 0.7f;
                KdMultiplier = 0.7f;
            }
            else if (n.Contains("hand") || n.Contains("fist"))
            {
                KpMultiplier = 0.4f;
                KdMultiplier = 0.6f; // Floppy hands/fists trail behind
            }
        }
    }

    void FixedUpdate()
    {
        if (!enablePoseDriver || !initialized) return;
        if (drivenJoint == null || targetJoint == null) return;

        // Brief settling period — let physics stabilize before driving.
        // Prevents initial winding when the figure is still being set up.
        if (settlingFrames > 0)
        {
            settlingFrames--;
            return;
        }

        // Strike impulse phase: bypass PD entirely and inject angular velocity
        // directly onto the Rigidbody2D. This produces an immediate, visible snap
        // that doesn't depend on motor torque overcoming inertia.
        if (strikeImpulseFramesRemaining > 0)
        {
            strikeImpulseFramesRemaining--;

            float targetAngleImpulse = NormalizeAngle(targetJoint.jointAngle);
            if (Mathf.Abs(airborneRootRotationOffset) > 0.01f)
                targetAngleImpulse = NormalizeAngle(targetAngleImpulse - airborneRootRotationOffset);

            float currentAngleImpulse = NormalizeAngle(drivenJoint.jointAngle);
            float impulseError = Mathf.DeltaAngle(currentAngleImpulse, targetAngleImpulse);

            // Drive both via motor AND direct angular velocity for maximum snap.
            float direction = Mathf.Sign(impulseError);
            JointMotor2D strikeMotor = drivenJoint.motor;
            strikeMotor.motorSpeed = direction * strikeImpulseAngularSpeed;
            strikeMotor.maxMotorTorque = Mathf.Max(originalMotorTorque, 150f) * runtimeTorqueMultiplier * 3f;
            drivenJoint.motor = strikeMotor;
            drivenJoint.useMotor = true;

            // Direct angular velocity injection — bypasses torque limitations entirely.
            if (rb != null)
            {
                rb.angularVelocity = direction * strikeImpulseAngularSpeed;
            }
            return;
        }

        // Process hit reaction fadeout
        if (hitReactionTimer > 0f)
        {
            hitReactionTimer -= Time.fixedDeltaTime;
            if (hitReactionTimer <= 0f)
            {
                hitReactionMultiplier = 1.0f;
            }
            else
            {
                // Smoothly fade the reaction back to 1.0
                float t = 1f - (hitReactionTimer / hitReactionDuration);
                hitReactionMultiplier = Mathf.Lerp(hitReactionMultiplier, 1.0f, t);
            }
        }

        // Compare jointAngle (the angle between this body and its connected body).
        // This is the correct reference frame for motor control — motorSpeed
        // operates in this same space, so the sign is always correct.
        // (World-space eulerAngles caused winding because the motor's direction
        // doesn't always correspond to the world rotation direction.)
        float targetAngle = NormalizeAngle(targetJoint.jointAngle);

        // Airborne compensation: when the live root (hips) rotates under gravity
        // but the frozen copy's root stays fixed, jointAngle is measured in a
        // rotated frame. Subtract the root rotation delta so this joint chases
        // the world-space pose, not the drifting local-space pose.
        if (Mathf.Abs(airborneRootRotationOffset) > 0.01f)
            targetAngle = NormalizeAngle(targetAngle - airborneRootRotationOffset);

        float currentAngle = NormalizeAngle(drivenJoint.jointAngle);
        if (drivenJoint.useLimits && !ignoreTargetJointLimits)
            targetAngle = Mathf.Clamp(targetAngle, drivenJoint.limits.min, drivenJoint.limits.max);
        float error = Mathf.DeltaAngle(currentAngle, targetAngle);
        float absError = Mathf.Abs(error);

        // Hard deadzone — fully disengage motor at tiny errors
        if (absError < deadzone)
        {
            JointMotor2D restMotor = drivenJoint.motor;
            restMotor.motorSpeed = 0f;
            restMotor.maxMotorTorque = originalMotorTorque * 0.1f;
            drivenJoint.motor = restMotor;
            return;
        }

        // Smooth relaxation ramp: motor output scales from 0 at the deadzone edge
        // to full strength at the relaxation range boundary. This prevents the
        // constant micro-corrections that cause visible vibration.
        float relaxationFactor = 1f;
        float relaxUpper = deadzone + Mathf.Max(0.1f, motorRelaxationRange);
        if (absError < relaxUpper)
        {
            float t = (absError - deadzone) / (relaxUpper - deadzone);
            // Smoothstep: 3t^2 - 2t^3 for smooth acceleration
            relaxationFactor = t * t * (3f - 2f * t);
        }

        // Use the joint's built-in angular speed for damping.
        // This is pre-smoothed by the physics engine — no jitter.
        // Negative sign because we want to resist current motion (dampen).
        float damping = -drivenJoint.jointSpeed * (Kd * KdMultiplier);

        float speedMultiplier = Mathf.Max(0.01f, runtimeSpeedMultiplier);
        float torqueMultiplier = Mathf.Max(0.01f, runtimeTorqueMultiplier);

        // PD output: proportional drives toward target, damping prevents overshoot.
        // Runtime multipliers let strikes move faster without changing base tuning.
        // Kp and Kd multipliers apply anatomical tuning.
        // hitReactionMultiplier reduces gains and torque on collision.
        // relaxationFactor smoothly attenuates near the target.
        float motorSpeed = (Kp * KpMultiplier * hitReactionMultiplier * error + damping) * speedMultiplier * relaxationFactor;
        float speedLimit = Mathf.Max(1f, maxMotorSpeed * speedMultiplier);
        motorSpeed = Mathf.Clamp(motorSpeed, -speedLimit, speedLimit);

        // Apply to motor — torque also ramps with relaxation to prevent sudden snaps
        JointMotor2D motor = drivenJoint.motor;
        motor.motorSpeed = motorSpeed;
        motor.maxMotorTorque = Mathf.Max(originalMotorTorque, 150f) * torqueMultiplier * hitReactionMultiplier * relaxationFactor;
        drivenJoint.motor = motor;
        drivenJoint.useMotor = true;
    }

    public void SetRuntimeMultipliers(float speedMultiplier, float torqueMultiplier)
    {
        runtimeSpeedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        runtimeTorqueMultiplier = Mathf.Max(0.01f, torqueMultiplier);
    }

    public void ResetRuntimeMultipliers()
    {
        runtimeSpeedMultiplier = 1f;
        runtimeTorqueMultiplier = 1f;
        airborneRootRotationOffset = 0f;
        hitReactionMultiplier = 1f;
        hitReactionTimer = 0f;
        strikeImpulseFramesRemaining = 0;
    }

    /// <summary>
    /// Begins a strike impulse: disables PD for <paramref name="frames"/> fixed steps
    /// and directly drives the joint at <paramref name="angularSpeed"/> deg/s toward
    /// the target angle, producing a visible snap regardless of initial error magnitude.
    /// </summary>
    public void BeginStrikeImpulse(float angularSpeed, int frames)
    {
        strikeImpulseAngularSpeed = Mathf.Max(1f, angularSpeed);
        strikeImpulseFrames = Mathf.Max(1, frames);
        strikeImpulseFramesRemaining = strikeImpulseFrames;
    }

    /// <summary>
    /// Soften the motor of this joint temporarily when struck.
    /// </summary>
    public void ApplyHitReaction(float duration, float targetMultiplier)
    {
        hitReactionMultiplier = Mathf.Clamp01(targetMultiplier);
        hitReactionTimer = Mathf.Max(0f, duration);
        hitReactionDuration = Mathf.Max(0.01f, duration);
    }

    private float NormalizeAngle(float angle)
    {
        return Mathf.Repeat(angle + 180f, 360f) - 180f;
    }

    /// <summary>
    /// Call this to re-read the original joint limits after they've been changed externally.
    /// </summary>
    public void RefreshLimits()
    {
        if (drivenJoint != null)
        {
            originalLimits = drivenJoint.limits;
            hadOriginalLimits = drivenJoint.useLimits;
            originalMotorTorque = Mathf.Max(1f, drivenJoint.motor.maxMotorTorque);
        }
    }

    /// <summary>
    /// Links this driver to a frozen copy joint. Call from FreezeReplay after setup.
    /// </summary>
    public void SetTarget(HingeJoint2D target)
    {
        targetJoint = target;
        lastError = 0f;
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmo || drivenJoint == null || targetJoint == null) return;

        // Draw current angle as green line, target angle as yellow line
        Vector3 pos = drivenJoint.transform.position;
        float current = drivenJoint.jointAngle * Mathf.Deg2Rad;
        float target = targetJoint.jointAngle * Mathf.Deg2Rad;

        Gizmos.color = Color.green;
        Gizmos.DrawLine(pos, pos + new Vector3(Mathf.Cos(current), Mathf.Sin(current), 0) * 0.5f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pos, pos + new Vector3(Mathf.Cos(target), Mathf.Sin(target), 0) * 0.5f);
    }
}
