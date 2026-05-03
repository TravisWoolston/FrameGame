using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Clean rewrite of FreezeReplay. Self-contained turn-based fighter controller.
/// 
/// TURN LOOP:
///   POSE (physics paused) → COMMIT (Space) → SIMULATE (turnDuration seconds) → CAPTURE → POSE
///
/// SETUP:
///   1. Create a GameObject, add this script
///   2. Assign playerPrefab (the fighter prefab with Rigidbody2D children)
///   3. Assign frozenCopyPrefab (visual duplicate for posing)
///   4. Each instance needs its own GameObject (for multiplayer, make two)
///
/// FEATURES:
///   - Fixed-rate turns (every commit = exactly turnDuration seconds of physics)
///   - Self-contained state recording (no GameStateSnapshot dependency)
///   - Frozen copy updates to END position after simulation
///   - Health snapshot integration
///   - Full sequence replay
///   - Multiple instance support (each tracks only its own bodies)
/// </summary>
public class FreezeReplayV2 : MonoBehaviour
{
    // ===== INSPECTOR =====

    [Header("=== Prefabs ===")]
    [Tooltip("The fighter prefab to instantiate (must have Rigidbody2D children)")]
    public GameObject playerPrefab;

    [Header("=== Frozen Copy ===")]
    [Tooltip("Color tint for the frozen copy so it's visually distinct from the live fighter")]
    public Color frozenCopyColor = new Color(0.3f, 1f, 0.6f, 0.5f);

    [Header("=== Turn System ===")]
    [Tooltip("Seconds of physics simulation per turn")]
    public float turnDuration = 0.5f;

    [Tooltip("Key to commit pose and start simulation")]
    public KeyCode commitKey = KeyCode.Space;

    [Tooltip("Key to replay the full sequence")]
    public KeyCode replayKey = KeyCode.R;

    [Tooltip("Key to reload the scene")]
    public KeyCode resetKey = KeyCode.Q;

    [Header("=== Options ===")]
    [Tooltip("Save/restore health with each turn")]
    public bool enableHealthSnapshots = true;

    [Tooltip("Play full sequence replay after each commit")]
    public bool replayOnCapture = false;

    [Header("=== Spawn ===")]
    [Tooltip("Offset from this transform to spawn the player")]
    public Vector2 spawnOffset = new Vector2(0, -18f);

    [Header("=== Pose Driving ===")]
    [Tooltip("Proportional gain for PoseDrivers. Higher = snappier.")]
    public float poseKp = 40f;

    [Tooltip("Derivative gain for PoseDrivers. Higher = less overshoot.")]
    public float poseKd = 8f;

    [Tooltip("Max motor speed for PoseDrivers.")]
    public float poseMaxSpeed = 800f;

    [Header("=== Debug (Read Only) ===")]
    public TurnPhase currentPhase = TurnPhase.Posing;
    public int turnCount = 0;

    // ===== PUBLIC REFERENCES =====

    [HideInInspector] public GameObject playerObj;
    [HideInInspector] public GameObject frozenCopy;
    [HideInInspector] public List<Rigidbody2D> trackedBodies = new List<Rigidbody2D>();
    private List<PoseDriver> poseDrivers = new List<PoseDriver>();

    // ===== ENUMS =====

    public enum TurnPhase
    {
        Posing,       // Physics paused, player drags frozen copy
        Simulating,   // Physics running for turnDuration seconds
        Capturing     // Snapshot result, return to Posing
    }

    // ===== PRIVATE STATE =====

    // Per-body snapshot for capture/restore
    private struct BodySnapshot
    {
        public Vector2 position;
        public float rotation;
        public Vector2 velocity;
        public float angularVelocity;
        public float jointAngle; // HingeJoint2D angle (if any)
    }

    // The current snapshot (what the frozen copy represents)
    private BodySnapshot[] currentSnapshot;

    // Frame-by-frame recording for replay
    private List<BodySnapshot[]> replayFrames = new List<BodySnapshot[]>();

    // Frozen copy children (matched 1:1 with trackedBodies by index)
    private Transform[] frozenChildren;

    // Simulation timer
    private float simTimeRemaining = 0f;

    // Health reference cache
    private FighterHealth fighterHealth;

    // Saved health for snapshot
    private float savedHP;

    // Store original body types for freeze/unfreeze
    private Dictionary<Rigidbody2D, RigidbodyType2D> savedBodyTypes = new Dictionary<Rigidbody2D, RigidbodyType2D>();

    // ===== LIFECYCLE =====

    void Start()
    {
        // Spawn the player
        Vector3 spawnPos = (Vector2)transform.position + spawnOffset;
        playerObj = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
        playerObj.name = playerPrefab.name + "_Live";

        // Collect all rigidbodies from the player (depth-first, matching child order)
        CollectBodies(playerObj);

        // Auto-generate the frozen copy by cloning the live fighter
        frozenCopy = Instantiate(playerObj);
        frozenCopy.name = playerPrefab.name + "_FrozenCopy";
        PrepareCloneAsFrozenCopy(frozenCopy);

        // Cache frozen copy children
        CacheFrozenChildren();

        // Wire PoseDrivers (live joints → frozen copy joints)
        WirePoseDrivers();

        // Take initial snapshot (frozen copy matches live figure at spawn)
        CopyLiveToFrozen();
        currentSnapshot = CaptureSnapshot();

        // Cache health
        fighterHealth = playerObj.GetComponentInChildren<FighterHealth>();
        if (fighterHealth != null)
            savedHP = fighterHealth.CurrentHP;

        // Start in posing phase — freeze only the live fighter, not the whole sim
        currentPhase = TurnPhase.Posing;
        FreezeLiveBodies();

        Debug.Log($"[FreezeReplayV2] Spawned '{playerObj.name}' with {trackedBodies.Count} bodies. Ready to pose.");
    }

    void Update()
    {
        // Scene reset (always available)
        if (Input.GetKeyDown(resetKey))
        {
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            return;
        }

        switch (currentPhase)
        {
            case TurnPhase.Posing:
                UpdatePosing();
                break;

            case TurnPhase.Simulating:
                UpdateSimulating();
                break;

            case TurnPhase.Capturing:
                UpdateCapturing();
                break;
        }
    }

    // ===== TURN PHASES =====

    /// <summary>
    /// POSING: Physics paused. Player drags the frozen copy to set the target pose.
    /// Press commitKey to lock in the pose and start simulation.
    /// </summary>
    private void UpdatePosing()
    {
        // Live fighter is frozen (Kinematic). Frozen copy is interactive.
        ShowFrozenCopy();

        if (Input.GetKeyDown(commitKey))
        {
            CommitPose();
        }

        if (Input.GetKeyDown(replayKey))
        {
            StartCoroutine(PlayReplayThenResume());
        }
    }

    /// <summary>
    /// SIMULATING: Physics runs for exactly turnDuration seconds.
    /// The live figure chases the committed pose via PoseDriver/motors.
    /// </summary>
    private void UpdateSimulating()
    {
        simTimeRemaining -= Time.deltaTime;

        // Record this frame for replay
        RecordFrame();

        if (simTimeRemaining <= 0f)
        {
            currentPhase = TurnPhase.Capturing;
        }
    }

    /// <summary>
    /// CAPTURING: Simulation done. Snapshot the result.
    /// Copy live figure → frozen copy. Return to posing.
    /// </summary>
    private void UpdateCapturing()
    {
        // Copy the live figure's END position onto the frozen copy
        CopyLiveToFrozen();

        // Store this as the new snapshot
        currentSnapshot = CaptureSnapshot();

        // Save health (this is the "real" HP after this turn's damage)
        if (enableHealthSnapshots && fighterHealth != null)
            savedHP = fighterHealth.CurrentHP;

        // Freeze the live fighter, show frozen copy, return to posing
        FreezeLiveBodies();
        currentPhase = TurnPhase.Posing;
        ShowFrozenCopy();

        Debug.Log($"[FreezeReplayV2] Turn {turnCount} complete. Pose next move.");
    }

    // ===== CORE METHODS =====

    /// <summary>
    /// Lock in the current frozen copy pose and start simulation.
    /// </summary>
    private void CommitPose()
    {
        // Snapshot the frozen copy's current pose (this is what the player chose)
        currentSnapshot = CaptureSnapshot();

        // Save health before simulation (replay damage doesn't count)
        if (enableHealthSnapshots && fighterHealth != null)
            savedHP = fighterHealth.CurrentHP;

        // Start simulation — unfreeze live bodies
        simTimeRemaining = turnDuration;
        currentPhase = TurnPhase.Simulating;
        UnfreezeLiveBodies();
        HideFrozenCopy();
        turnCount++;

        Debug.Log($"[FreezeReplayV2] Turn {turnCount}: Simulating {turnDuration}s...");

        // Optionally replay first, then simulate
        if (replayOnCapture && replayFrames.Count > 0)
        {
            // TODO: Could insert full replay before simulation here
        }
    }

    /// <summary>
    /// Copy the live fighter's current transforms onto the frozen copy.
    /// </summary>
    private void CopyLiveToFrozen()
    {
        for (int i = 0; i < trackedBodies.Count && i < frozenChildren.Length; i++)
        {
            Rigidbody2D liveRb = trackedBodies[i];
            Transform frozenChild = frozenChildren[i];

            if (liveRb == null || frozenChild == null) continue;

            frozenChild.position = liveRb.transform.position;
            frozenChild.rotation = liveRb.transform.rotation;

            // Zero out velocity on the frozen copy's rb (if it has one)
            Rigidbody2D frozenRb = frozenChild.GetComponent<Rigidbody2D>();
            if (frozenRb != null)
            {
                frozenRb.position = liveRb.position;
                frozenRb.rotation = liveRb.rotation;
                frozenRb.linearVelocity = Vector2.zero;
                frozenRb.angularVelocity = 0f;
            }
        }
    }

    /// <summary>
    /// Restore the live fighter to a snapshot (used for replay reset).
    /// </summary>
    private void RestoreSnapshot(BodySnapshot[] snap)
    {
        for (int i = 0; i < trackedBodies.Count && i < snap.Length; i++)
        {
            Rigidbody2D rb = trackedBodies[i];
            if (rb == null) continue;

            rb.position = snap[i].position;
            rb.rotation = snap[i].rotation;
            rb.linearVelocity = snap[i].velocity;
            rb.angularVelocity = snap[i].angularVelocity;
            rb.transform.position = snap[i].position;
            rb.transform.rotation = Quaternion.Euler(0, 0, snap[i].rotation);
        }

        // Restore health
        if (enableHealthSnapshots && fighterHealth != null)
        {
            fighterHealth.RestoreHP();
        }
    }

    // ===== SNAPSHOT =====

    /// <summary>
    /// Capture a snapshot of all tracked bodies' current state.
    /// </summary>
    private BodySnapshot[] CaptureSnapshot()
    {
        BodySnapshot[] snap = new BodySnapshot[trackedBodies.Count];
        for (int i = 0; i < trackedBodies.Count; i++)
        {
            Rigidbody2D rb = trackedBodies[i];
            if (rb == null) continue;

            snap[i] = new BodySnapshot
            {
                position = rb.position,
                rotation = rb.rotation,
                velocity = rb.linearVelocity,
                angularVelocity = rb.angularVelocity,
                jointAngle = rb.GetComponent<HingeJoint2D>()?.jointAngle ?? 0f
            };
        }
        return snap;
    }

    // ===== RECORDING =====

    /// <summary>
    /// Record one frame of state for replay playback.
    /// Called every frame during Simulating phase.
    /// </summary>
    private void RecordFrame()
    {
        BodySnapshot[] frame = new BodySnapshot[trackedBodies.Count];
        for (int i = 0; i < trackedBodies.Count; i++)
        {
            Rigidbody2D rb = trackedBodies[i];
            if (rb == null) continue;

            frame[i] = new BodySnapshot
            {
                position = rb.position,
                rotation = rb.rotation,
                velocity = rb.linearVelocity,
                angularVelocity = rb.angularVelocity
            };
        }
        replayFrames.Add(frame);
    }

    // ===== REPLAY =====

    /// <summary>
    /// Play the full recorded sequence, then resume posing.
    /// </summary>
    private IEnumerator PlayReplayThenResume()
    {
        if (replayFrames.Count == 0)
        {
            Debug.Log("[FreezeReplayV2] No frames recorded yet.");
            yield break;
        }

        // Pause normal update
        HideFrozenCopy();
        Time.timeScale = 1f;

        // Make all tracked bodies kinematic during replay
        var originalTypes = new Dictionary<Rigidbody2D, RigidbodyType2D>();
        foreach (var rb in trackedBodies)
        {
            if (rb == null) continue;
            originalTypes[rb] = rb.bodyType;
            rb.bodyType = RigidbodyType2D.Static;
        }

        Debug.Log($"[FreezeReplayV2] Playing {replayFrames.Count} frames...");

        // Play each frame
        foreach (var frame in replayFrames)
        {
            for (int i = 0; i < trackedBodies.Count && i < frame.Length; i++)
            {
                Rigidbody2D rb = trackedBodies[i];
                if (rb == null) continue;

                rb.transform.position = frame[i].position;
                rb.transform.rotation = Quaternion.Euler(0, 0, frame[i].rotation);
            }
            yield return null; // One frame per physics frame
        }

        // Restore body types
        foreach (var kvp in originalTypes)
        {
            if (kvp.Key != null)
                kvp.Key.bodyType = kvp.Value;
        }

        // Restore to latest snapshot
        CopyLiveToFrozen();

        // Resume posing — freeze live bodies again
        FreezeLiveBodies();
        currentPhase = TurnPhase.Posing;
        frozenCopy.SetActive(true);

        Debug.Log("[FreezeReplayV2] Replay complete. Pose next move.");
    }

    // ===== SETUP HELPERS =====

    /// <summary>
    /// Recursively collect all Rigidbody2D from the player.
    /// Uses GetComponentsInChildren to ensure nested limbs (Calf, Lower Arm) are found.
    /// </summary>
    private void CollectBodies(GameObject root)
    {
        trackedBodies.Clear();
        Rigidbody2D[] bodies = root.GetComponentsInChildren<Rigidbody2D>(true);
        foreach (var rb in bodies)
        {
            trackedBodies.Add(rb);
        }
        Debug.Log($"[FreezeReplayV2] Collected {trackedBodies.Count} bodies recursively from '{root.name}'");
    }

    /// <summary>
    /// Cache transforms of the frozen copy that match the trackedBodies.
    /// This ensures we have a 1:1 mapping even with nested hierarchies.
    /// </summary>
    private void CacheFrozenChildren()
    {
        // Since frozenCopy was cloned from playerObj, its Rigidbody2D components
        // will be in the exact same order when using GetComponentsInChildren.
        Rigidbody2D[] frozenRbs = frozenCopy.GetComponentsInChildren<Rigidbody2D>(true);
        frozenChildren = new Transform[frozenRbs.Length];
        for (int i = 0; i < frozenRbs.Length; i++)
        {
            frozenChildren[i] = frozenRbs[i].transform;
        }
    }

    // ===== FROZEN COPY PREPARATION =====

    /// <summary>
    /// Prepare a clone of the live fighter to be used as the frozen copy.
    /// Strips scripts that shouldn't run on the frozen copy, disables gravity,
    /// and tints renderers so the player can tell them apart.
    /// </summary>
    private void PrepareCloneAsFrozenCopy(GameObject clone)
    {
        // --- Strip scripts that shouldn't run on the frozen copy ---
        // These would interfere with posing or cause errors
        System.Type[] typesToRemove = new System.Type[]
        {
            typeof(PoseDriver),
            typeof(FighterHealth),
            typeof(FreezeReplayV2),
            typeof(SmartPoser),
            typeof(TargetJoint2D),
        };

        // Also try to remove by name for scripts that may/may not exist
        string[] scriptNamesToRemove = new string[]
        {
            "GroundCheck", "RotateHingeJoint", "Stand", "LegsAgent",
            "FreezeReplay", "GameStateReplay", "CombatTestSetup",
            "ImpactDamage", "FighterTuner", "StickFigureAgentv2",
            "StickFigureAgent", "JointAngleSensor", "MuscleDriver"
        };

        // Disable all MonoBehaviours first (immediate effect)
        foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            
            // Don't disable our own script if we accidentally hit it (though unlikely)
            if (mb is FreezeReplayV2) continue;

            // Check if it's in our removal lists
            bool shouldRemove = false;
            foreach (var type in typesToRemove) if (type.IsInstanceOfType(mb)) shouldRemove = true;
            foreach (var name in scriptNamesToRemove) if (mb.GetType().Name == name) shouldRemove = true;

            if (shouldRemove)
            {
                mb.enabled = false;
                Destroy(mb);
            }
        }

        // --- Joint setup: disable motors and fix limits/anchors on the frozen copy ---
        Rigidbody2D[] liveRbs = playerObj.GetComponentsInChildren<Rigidbody2D>(true);
        Rigidbody2D[] frozenRbs = clone.GetComponentsInChildren<Rigidbody2D>(true);
        HingeJoint2D[] liveJoints = playerObj.GetComponentsInChildren<HingeJoint2D>(true);
        HingeJoint2D[] frozenJoints = clone.GetComponentsInChildren<HingeJoint2D>(true);

        for (int i = 0; i < frozenJoints.Length && i < liveJoints.Length; i++)
        {
            HingeJoint2D fJoint = frozenJoints[i];
            HingeJoint2D lJoint = liveJoints[i];

            fJoint.useMotor = false;
            fJoint.useLimits = lJoint.useLimits;
            fJoint.limits = lJoint.limits;
            
            // Crucial: Ensure anchors don't move during clone initialization
            fJoint.autoConfigureConnectedAnchor = false;
            fJoint.anchor = lJoint.anchor;
            fJoint.connectedAnchor = lJoint.connectedAnchor;

            // Remap connectedBody to the clone's own Rigidbody2D components
            if (lJoint.connectedBody != null)
            {
                // Find the index of the connected body in the live fighter
                int connectedIndex = System.Array.IndexOf(liveRbs, lJoint.connectedBody);
                if (connectedIndex >= 0 && connectedIndex < frozenRbs.Length)
                {
                    fJoint.connectedBody = frozenRbs[connectedIndex];
                }
            }
        }

        // --- Physics setup: keep joints active but disable gravity ---
        foreach (var rb in clone.GetComponentsInChildren<Rigidbody2D>(true))
        {
            rb.gravityScale = 0f;           // Don't fall during posing
            rb.linearDamping = 5f;          // Resist unwanted drift
            rb.angularDamping = 5f;         // Resist unwanted spin
            rb.bodyType = RigidbodyType2D.Dynamic;  // Must be Dynamic for TargetJoint2D dragging
        }

        // --- Disable collisions between frozen copy and live fighter ---
        // Instead of changing layers (which breaks SmartPoser raycast detection),
        // use Physics2D.IgnoreCollision between each pair of colliders.
        Collider2D[] liveColliders = playerObj.GetComponentsInChildren<Collider2D>(true);
        Collider2D[] frozenColliders = clone.GetComponentsInChildren<Collider2D>(true);
        foreach (var liveCol in liveColliders)
        {
            foreach (var frozenCol in frozenColliders)
            {
                if (liveCol != null && frozenCol != null)
                    Physics2D.IgnoreCollision(liveCol, frozenCol);
            }
        }

        // --- Tint renderers so it's visually distinct ---
        foreach (var sr in clone.GetComponentsInChildren<SpriteRenderer>(true))
        {
            sr.color = frozenCopyColor;
            sr.sortingOrder += 10; // Draw in front
        }
        foreach (var ssr in clone.GetComponentsInChildren<UnityEngine.U2D.SpriteShapeRenderer>(true))
        {
            ssr.color = frozenCopyColor;
            ssr.sortingOrder += 10;
        }

        Debug.Log($"[FreezeReplayV2] Prepared frozen copy from '{playerObj.name}' clone.");
    }

    /// <summary>
    /// Recursively set the layer of a GameObject and all children.
    /// </summary>
    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    // ===== POSE DRIVER WIRING =====

    /// <summary>
    /// Wire PoseDrivers between each live body's HingeJoint2D and the
    /// corresponding frozen copy's HingeJoint2D. This makes the live fighter
    /// chase the frozen copy's pose during simulation.
    /// </summary>
    private void WirePoseDrivers()
    {
        int wired = 0;
        poseDrivers.Clear();
        for (int i = 0; i < trackedBodies.Count && i < frozenChildren.Length; i++)
        {
            Rigidbody2D liveRb = trackedBodies[i];
            Transform frozenChild = frozenChildren[i];

            if (liveRb == null || frozenChild == null) continue;

            HingeJoint2D liveJoint = liveRb.GetComponent<HingeJoint2D>();
            HingeJoint2D frozenJoint = frozenChild.GetComponent<HingeJoint2D>();

            // Skip bodies without joints (e.g., Hips root)
            if (liveJoint == null || frozenJoint == null) continue;

            // Add PoseDriver to the live body
            PoseDriver driver = liveRb.gameObject.GetComponent<PoseDriver>();
            if (driver == null)
                driver = liveRb.gameObject.AddComponent<PoseDriver>();

            poseDrivers.Add(driver);
            driver.drivenJoint = liveJoint;
            driver.targetJoint = frozenJoint;
            driver.enablePoseDriver = true;
            driver.Kp = poseKp;
            driver.Kd = poseKd;
            driver.maxMotorSpeed = poseMaxSpeed;

            wired++;
            Debug.Log($"[FreezeReplayV2] Wired PoseDriver: '{liveRb.name}' → '{frozenChild.name}'");
        }

        Debug.Log($"[FreezeReplayV2] Wired {wired} PoseDrivers total.");
    }

    // ===== FROZEN COPY VISIBILITY =====

    /// <summary>
    /// Hide the frozen copy visually (disable renderers) but keep it ACTIVE
    /// so HingeJoint2D components still provide valid jointAngle data
    /// for PoseDriver to read during simulation.
    /// </summary>
    private void HideFrozenCopy()
    {
        foreach (var r in frozenCopy.GetComponentsInChildren<Renderer>())
            r.enabled = false;
    }

    /// <summary>
    /// Show the frozen copy (re-enable renderers) for posing.
    /// </summary>
    private void ShowFrozenCopy()
    {
        frozenCopy.SetActive(true); // Ensure active
        foreach (var r in frozenCopy.GetComponentsInChildren<Renderer>())
            r.enabled = true;
    }

    // ===== FREEZE/UNFREEZE =====

    /// <summary>
    /// Freeze the live fighter's bodies by setting them to Kinematic.
    /// The frozen copy remains fully interactive for posing.
    /// </summary>
    private void FreezeLiveBodies()
    {
        foreach (var driver in poseDrivers) if (driver != null) driver.enablePoseDriver = false;

        foreach (var rb in trackedBodies)
        {
            if (rb == null) continue;
            if (!savedBodyTypes.ContainsKey(rb))
                savedBodyTypes[rb] = rb.bodyType;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    /// <summary>
    /// Unfreeze the live fighter's bodies by restoring their original body types.
    /// </summary>
    private void UnfreezeLiveBodies()
    {
        foreach (var driver in poseDrivers) if (driver != null) driver.enablePoseDriver = true;

        foreach (var rb in trackedBodies)
        {
            if (rb == null) continue;
            if (savedBodyTypes.TryGetValue(rb, out RigidbodyType2D savedType))
                rb.bodyType = savedType;
            else
                rb.bodyType = RigidbodyType2D.Dynamic;
        }
    }

    // ===== PUBLIC API =====

    /// <summary>
    /// Get the frozen copy's child that corresponds to trackedBodies[index].
    /// Useful for PoseDriverSetup and other systems that need the mapping.
    /// </summary>
    public Transform GetFrozenChild(int index)
    {
        if (index >= 0 && index < frozenChildren.Length)
            return frozenChildren[index];
        return null;
    }

    /// <summary>
    /// Force a restore to the last snapshot (useful for ML agents).
    /// </summary>
    public void ForceRestore()
    {
        if (currentSnapshot != null)
            RestoreSnapshot(currentSnapshot);
    }
}
