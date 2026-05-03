using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Drop this on your existing stick figure root to fix "derpy" physics.
/// Automatically discovers all limbs and applies tuned mass, drag, joint limits,
/// and motor settings for tight, responsive, martial-arts-ready movement.
///
/// Toggle individual fixes on/off in the Inspector for debugging.
/// Hit Play and it auto-applies on Awake.
/// </summary>
public class FighterTuner : MonoBehaviour
{
    [Header("=== Feature Toggles ===")]
    public bool fixMassDistribution = true;
    public bool fixJointLimits = true;
    public bool fixMotorSettings = true;
    public bool fixDragAndDamping = true;
    public bool addMissingComponents = true;

    [Header("=== Global Tuning ===")]
    [Tooltip("Overall mass scale. Increase for heavier, more grounded feel.")]
    public float massScale = 1.0f;

    [Tooltip("Overall motor torque scale. Increase for snappier joint control.")]
    public float torqueScale = 1.0f;

    [Tooltip("Angular drag on all limbs. Higher = less floppy spinning.")]
    public float angularDrag = 5.0f;

    [Tooltip("Linear drag on all limbs. Small values reduce floaty drifting.")]
    public float linearDrag = 0.5f;

    [Tooltip("Gravity scale. 1 = normal, higher = more grounded.")]
    public float gravityScale = 1.0f;

    // Per-limb tuning data
    [System.Serializable]
    public struct LimbConfig
    {
        public string nameContains;
        public float mass;
        public float maxMotorTorque;
        public float limitMin;
        public float limitMax;
    }

    // Default configs — matches names from the user's hierarchy
    private LimbConfig[] defaultConfigs = new LimbConfig[]
    {
        // Spine — heavy core, limited tilt
        new LimbConfig { nameContains = "Spine",     mass = 3.0f,  maxMotorTorque = 300f, limitMin = -25f, limitMax = 25f },
        // Head — light, small range
        new LimbConfig { nameContains = "Head",      mass = 0.8f,  maxMotorTorque = 100f, limitMin = -40f, limitMax = 40f },
        // Hips — heaviest anchor point
        new LimbConfig { nameContains = "Hips",      mass = 4.0f,  maxMotorTorque = 400f, limitMin = -15f, limitMax = 15f },
        // Upper arms — medium, wide range for punches
        new LimbConfig { nameContains = "Arm",       mass = 0.6f,  maxMotorTorque = 150f, limitMin = -135f, limitMax = 135f },
        // Lower arms / forearms — light, elbow range
        new LimbConfig { nameContains = "Lower Arm", mass = 0.4f,  maxMotorTorque = 120f, limitMin = -5f, limitMax = 145f },
        // Thighs — strong legs
        new LimbConfig { nameContains = "Thigh",     mass = 1.5f,  maxMotorTorque = 250f, limitMin = -100f, limitMax = 80f },
        // Calves — knee joint
        new LimbConfig { nameContains = "Calf",      mass = 1.0f,  maxMotorTorque = 200f, limitMin = -5f, limitMax = 140f },
    };

    void Awake()
    {
        ApplyTuning();
    }

    [ContextMenu("Apply Tuning Now")]
    public void ApplyTuning()
    {
        Rigidbody2D[] allBodies = GetComponentsInChildren<Rigidbody2D>();

        foreach (Rigidbody2D rb in allBodies)
        {
            LimbConfig? config = FindConfig(rb.gameObject.name);

            if (fixDragAndDamping)
            {
                rb.angularDamping = angularDrag;
                rb.linearDamping = linearDrag;
                rb.gravityScale = gravityScale;
                // Continuous collision detection prevents limbs passing through each other
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                // Interpolate for smoother visual movement
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            }

            if (fixMassDistribution && config.HasValue)
            {
                rb.mass = config.Value.mass * massScale;
            }

            HingeJoint2D hinge = rb.GetComponent<HingeJoint2D>();
            if (hinge != null && config.HasValue)
            {
                if (fixJointLimits)
                {
                    JointAngleLimits2D limits = hinge.limits;
                    limits.min = config.Value.limitMin;
                    limits.max = config.Value.limitMax;
                    hinge.limits = limits;
                    hinge.useLimits = true;
                }

                if (fixMotorSettings)
                {
                    JointMotor2D motor = hinge.motor;
                    motor.maxMotorTorque = config.Value.maxMotorTorque * torqueScale;
                    hinge.motor = motor;
                    hinge.useMotor = true;
                }
            }

            // Add ImpactDamage if missing
            if (addMissingComponents && rb.GetComponent<ImpactDamage>() == null)
            {
                rb.gameObject.AddComponent<ImpactDamage>();
            }
        }

        // Add FighterHealth to root if missing
        if (addMissingComponents && GetComponent<FighterHealth>() == null)
        {
            gameObject.AddComponent<FighterHealth>();
        }

        Debug.Log($"[FighterTuner] Applied tuning to {allBodies.Length} rigidbodies on {gameObject.name}");
    }

    /// <summary>
    /// Finds the best matching config for a limb name.
    /// More specific names (like "Lower Arm") are checked before less specific ("Arm").
    /// </summary>
    LimbConfig? FindConfig(string limbName)
    {
        // Sort by specificity — longer name matches first
        LimbConfig? bestMatch = null;
        int bestLength = 0;

        foreach (var config in defaultConfigs)
        {
            if (limbName.Contains(config.nameContains) && config.nameContains.Length > bestLength)
            {
                bestMatch = config;
                bestLength = config.nameContains.Length;
            }
        }
        return bestMatch;
    }
}
