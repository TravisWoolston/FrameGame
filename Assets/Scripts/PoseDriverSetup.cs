using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Automatically wires up PoseDriver components between the live fighter's joints
/// and the frozen copy's joints. Attach to the same object as FreezeReplay.
///
/// Call WirePoseDrivers() after the frozen copy and playerObj are both instantiated
/// (i.e., at end of FreezeReplay.Start or shortly after).
/// </summary>
public class PoseDriverSetup : MonoBehaviour
{
    [Header("=== Feature Toggles ===")]
    [Tooltip("Master toggle for the pose driver system")]
    public bool enablePoseDrivers = true;

    [Tooltip("Auto-wire on Start (requires FreezeReplay on same object)")]
    public bool autoWireOnStart = true;

    [Header("=== PD Tuning (applied to all drivers) ===")]
    [Tooltip("Proportional gain. Higher = snappier. Try 30-60.")]
    public float globalKp = 40f;

    [Tooltip("Derivative gain. Higher = less overshoot. Try 5-12.")]
    public float globalKd = 8f;

    [Tooltip("Max motor speed for all joints")]
    public float globalMaxSpeed = 800f;

    [Header("=== Per-Limb Overrides ===")]
    [Tooltip("Lighter limbs (arms) should be snappier. This multiplies Kp for arms.")]
    public float armKpMultiplier = 1.5f;

    [Tooltip("Heavier limbs (legs) can be slightly slower. This multiplies Kp for legs.")]
    public float legKpMultiplier = 0.9f;

    [Header("=== References ===")]
    public FreezeReplay freezeReplay;

    // Track created drivers so we can update them
    private List<PoseDriver> drivers = new List<PoseDriver>();

    void Start()
    {
        if (freezeReplay == null)
            freezeReplay = GetComponent<FreezeReplay>();

        if (autoWireOnStart && freezeReplay != null)
        {
            // Delay one frame so FreezeReplay.Start() finishes creating objects first
            Invoke(nameof(WirePoseDrivers), 0.1f);
        }
    }

    /// <summary>
    /// Uses FreezeReplay's own index-based mapping (rbToCap[i] ↔ frozenCopies[i])
    /// to wire PoseDrivers. This is the same mapping Capture/Restore use.
    /// </summary>
    [ContextMenu("Wire Pose Drivers")]
    public void WirePoseDrivers()
    {
        if (freezeReplay == null || freezeReplay.playerObj == null || freezeReplay.frozenCopy == null)
        {
            Debug.LogWarning("[PoseDriverSetup] FreezeReplay, playerObj, or frozenCopy not ready yet.");
            return;
        }

        // Clear any existing drivers
        foreach (var old in drivers)
        {
            if (old != null) Destroy(old);
        }
        drivers.Clear();

        // Use FreezeReplay's own rbToCap → frozenCopies mapping (index-based)
        int wired = 0;
        int skipped = 0;
        for (int i = 0; i < freezeReplay.rbToCap.Count; i++)
        {
            Rigidbody2D liveRb = freezeReplay.rbToCap[i];
            GameObject frozenObj = (i < freezeReplay.frozenCopies.Length)
                ? freezeReplay.frozenCopies[i]
                : null;

            if (liveRb == null || frozenObj == null)
            {
                skipped++;
                continue;
            }

            HingeJoint2D liveJoint = liveRb.GetComponent<HingeJoint2D>();
            HingeJoint2D frozenJoint = frozenObj.GetComponent<HingeJoint2D>();

            if (liveJoint == null || frozenJoint == null)
            {
                Debug.Log($"[PoseDriverSetup] Skipping '{liveRb.name}' — no HingeJoint2D on live ({liveJoint != null}) or frozen ({frozenJoint != null})");
                skipped++;
                continue;
            }

            // Add or get PoseDriver on the live limb
            PoseDriver driver = liveRb.gameObject.GetComponent<PoseDriver>();
            if (driver == null)
                driver = liveRb.gameObject.AddComponent<PoseDriver>();

            driver.drivenJoint = liveJoint;
            driver.targetJoint = frozenJoint;
            driver.enablePoseDriver = enablePoseDrivers;

            // Apply tuning with per-limb multipliers
            float kpMult = 1f;
            string lower = liveRb.name.ToLower();
            if (lower.Contains("lower arm"))
                kpMult = armKpMultiplier * 1.2f; // Extra boost — end of chain, fights parent
            else if (lower.Contains("arm"))
                kpMult = armKpMultiplier;
            else if (lower.Contains("calf"))
                kpMult = legKpMultiplier * 1.2f;  // Extra boost — end of chain
            else if (lower.Contains("thigh"))
                kpMult = legKpMultiplier;

            driver.Kp = globalKp * kpMult;
            driver.Kd = globalKd;
            driver.maxMotorSpeed = globalMaxSpeed;

            drivers.Add(driver);
            wired++;
            Debug.Log($"[PoseDriverSetup] ✓ Wired '{liveRb.name}' → frozen '{frozenObj.name}' (Kp={driver.Kp:F1})");
        }

        Debug.Log($"[PoseDriverSetup] Done: {wired} wired, {skipped} skipped out of {freezeReplay.rbToCap.Count} total.");
    }

    /// <summary>
    /// Update all drivers' enabled state at runtime.
    /// </summary>
    public void SetAllDriversEnabled(bool enabled)
    {
        foreach (var d in drivers)
        {
            if (d != null) d.enablePoseDriver = enabled;
        }
    }

    /// <summary>
    /// Update tuning on all drivers at runtime.
    /// </summary>
    public void UpdateTuning()
    {
        foreach (var d in drivers)
        {
            if (d == null) continue;
            float kpMult = 1f;
            string lower = d.gameObject.name.ToLower();
            if (lower.Contains("arm")) kpMult = armKpMultiplier;
            else if (lower.Contains("thigh") || lower.Contains("calf")) kpMult = legKpMultiplier;

            d.Kp = globalKp * kpMult;
            d.Kd = globalKd;
            d.maxMotorSpeed = globalMaxSpeed;
        }
    }
}
