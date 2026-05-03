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

    [Tooltip("Deadzone in degrees. Below this error, the motor relaxes. Prevents jitter at rest.")]
    public float deadzone = 0.5f;

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
    private int settlingFrames = 30; // ~0.5 sec at 50Hz physics — let figure settle before driving

    void Start()
    {
        if (drivenJoint == null)
            drivenJoint = GetComponent<HingeJoint2D>();

        if (drivenJoint != null)
        {
            // Store original limits so we don't overwrite them
            originalLimits = drivenJoint.limits;
            hadOriginalLimits = drivenJoint.useLimits;
            initialized = true;
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

        // Compare jointAngle (the angle between this body and its connected body).
        // This is the correct reference frame for motor control — motorSpeed
        // operates in this same space, so the sign is always correct.
        // (World-space eulerAngles caused winding because the motor's direction
        // doesn't always correspond to the world rotation direction.)
        float targetAngle = targetJoint.jointAngle;
        float currentAngle = drivenJoint.jointAngle;
        float error = Mathf.DeltaAngle(currentAngle, targetAngle);

        // Deadzone — don't fight tiny errors (prevents jitter at rest)
        if (Mathf.Abs(error) < deadzone)
        {
            JointMotor2D restMotor = drivenJoint.motor;
            restMotor.motorSpeed = 0f;
            drivenJoint.motor = restMotor;
            return;
        }

        // Use the joint's built-in angular speed for damping.
        // This is pre-smoothed by the physics engine — no jitter.
        // Negative sign because we want to resist current motion (dampen).
        float damping = -drivenJoint.jointSpeed * Kd;

        // PD output: proportional drives toward target, damping prevents overshoot
        float motorSpeed = Kp * error + damping;
        motorSpeed = Mathf.Clamp(motorSpeed, -maxMotorSpeed, maxMotorSpeed);

        // Apply to motor — ensure torque is high enough to move the limb
        JointMotor2D motor = drivenJoint.motor;
        motor.motorSpeed = motorSpeed;
        motor.maxMotorTorque = Mathf.Max(motor.maxMotorTorque, 150f);
        drivenJoint.motor = motor;
        drivenJoint.useMotor = true;
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
