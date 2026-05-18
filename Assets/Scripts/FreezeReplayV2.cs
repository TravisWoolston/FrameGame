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

    [Tooltip("Prevent the frozen target root from being dragged unrealistically far from the current fighter.")]
    public bool limitFrozenRootMovement = true;

    [Tooltip("Maximum root movement allowed between snapshots.")]
    public float maxRootMovePerSnapshot = 2f;

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

    [Header("=== Replay Timing ===")]
    [Tooltip("Record committed motion on FixedUpdate and play it back at Time.fixedDeltaTime so replay speed matches capture speed.")]
    public bool syncReplayToFixedStep = true;

    [Tooltip("Do not store identical consecutive replay frames. This avoids tiny pauses at snapshot boundaries.")]
    public bool skipDuplicateReplayFrames = true;

    public float duplicateReplayPositionTolerance = 0.0005f;
    public float duplicateReplayRotationTolerance = 0.05f;

    [Header("=== Spawn ===")]
    [Tooltip("Offset from this transform to spawn the player")]
    public Vector2 spawnOffset = new Vector2(0, -18f);

    [Tooltip("For marker-built fighters, move the generated rig so its lowest collider starts on the ground.")]
    public bool snapRuntimeRigFeetToGround = true;

    public float spawnGroundProbeHeight = 12f;
    public float spawnGroundProbeDistance = 40f;
    public float spawnGroundClearance = 0.03f;

    [Header("=== Camera Framing ===")]
    [Tooltip("Center the main camera on the root position of each newly captured snapshot, including the initial spawn snapshot.")]
    public bool centerMainCameraOnSnapshotStart = true;

    [Header("=== Pose Driving ===")]
    [Tooltip("Proportional gain for PoseDrivers. Higher = snappier.")]
    public float poseKp = 40f;

    [Tooltip("Derivative gain for PoseDrivers. Higher = less overshoot.")]
    public float poseKd = 8f;

    [Tooltip("Max motor speed for PoseDrivers.")]
    public float poseMaxSpeed = 800f;

    [Tooltip("Temporarily disables HingeJoint2D limits on live and frozen bodies and skips pose target clamping.")]
    public bool ignoreJointLimits = false;

    [Header("=== Physics / IK Assist ===")]
    [Tooltip("Adds soft Rigidbody2D forces toward the frozen copy so motors are not the only driver.")]
    public bool enablePhysicsPoseAssist = true;

    [Tooltip("Only use pose-assist forces on root/un-jointed bodies. Hinge-driven limbs rely on joint angles instead.")]
    public bool poseAssistOnlyRootAndUnjointedBodies = true;

    [Header("=== Pose Tethers ===")]
    [Tooltip("Attach DistanceJoint2D tethers from live limbs to their frozen targets to physically leash them.")]
    public bool enablePoseTethers = false;
    [Tooltip("The minimum distance the tether restricts the limb to at the very end of the turn.")]
    public float poseTetherMaxDistance = 0.05f;
    [Tooltip("If true, the tether distance starts at startingTetherDistance and shrinks to poseTetherMaxDistance over the turn.")]
    public bool tetherShrinksOverTurn = true;
    public float startingTetherDistance = 1.0f;
    [Tooltip("Tethers break if they receive an impact force exceeding this amount.")]
    public float tetherBreakForce = 4000f;

    [Tooltip("Linear spring force used to pull live bodies toward their frozen-copy targets.")]
    public float poseAssistSpring = 35f;

    [Tooltip("Linear damping used by the force assist.")]
    public float poseAssistDamping = 8f;

    [Tooltip("Maximum force applied by the body pose assist.")]
    public float maxPoseAssistForce = 500f;

    [Tooltip("Angular spring torque used to rotate live bodies toward their frozen-copy targets.")]
    public float poseAssistAngularSpring = 8f;

    [Tooltip("Angular damping used by the torque assist.")]
    public float poseAssistAngularDamping = 1.2f;

    [Tooltip("Maximum torque applied by the body pose assist.")]
    public float maxPoseAssistTorque = 350f;

    [Header("=== Airborne Physics ===")]
    [Tooltip("Multiplier for angular spring strength when airborne to provide 'rigid' control while falling.")]
    public float airborneAngularSpringMultiplier = 10f;

    [Header("=== Adaptive Leg Assist ===")]
    public bool enableAdaptiveLegAssist = true;
    public LayerMask groundMask = ~0;
    public float minStepDistance = 0.35f;
    public float groundProbeHeight = 3f;
    public float groundProbeDistance = 6f;
    public float footGroundOffset = 0.15f;
    public bool fallbackGroundProbeToAllLayers = true;
    public bool preventFrozenCopyGroundPenetration = true;
    public float frozenCopyGroundClearance = 0.01f;
    public float legStepSpring = 55f;
    public float legPlantForce = 45f;
    public float legLiftForce = 70f;
    public float maxLegAssistForce = 400f;
    public bool useForceLegAssistWhenNotWalking = false;

    [Header("=== Planted Foot Stability ===")]
    public bool enablePlantedFootStability = true;
    public bool usePlantedFootJoints = true;
    public bool usePlantedFootSpringFallback = false;
    public float plantedFootGroundTolerance = 0.35f;
    public float plantedFootSpring = 120f;
    public float plantedFootDamping = 18f;
    public float maxPlantedFootForce = 900f;
    public float plantedFootDeadzone = 0.04f;
    public float plantedFootVelocityDeadzone = 0.08f;
    public float plantedFootJointFrequency = 5f;
    public float plantedFootJointDampingRatio = 1f;
    public float plantedFootJointBreakForce = 2500f;
    public float plantedFootJointBreakTorque = 1200f;

    [Tooltip("If the frozen copy's foot target has moved further than this from the planted position, pre-release the foot joint before simulation begins. Set to 0 to disable intent-based detachment.")]
    public float plantedFootDetachThreshold = 0.4f;

    [Tooltip("When a planted foot is pre-released because its frozen target moved, create a TargetJoint2D that pulls the foot toward the new ground position.")]
    public bool enableFootReplanting = true;

    [Tooltip("TargetJoint2D frequency for the replanting pull joint. Higher = snappier pull.")]
    public float replantFootFrequency = 3f;

    [Tooltip("TargetJoint2D damping ratio for the replanting pull joint.")]
    public float replantFootDamping = 0.9f;

    [Tooltip("Maximum force the replanting pull joint can apply.")]
    public float replantFootMaxForce = 800f;

    [Tooltip("Once the replanting foot arrives within this distance of its target, convert to a planted FixedJoint2D.")]
    public float replantFootArrivalDistance = 0.15f;

    [Tooltip("Log planted-foot release and replant arrival details. Useful while tuning, noisy during preview loops.")]
    public bool logFootReplantingDetails = false;

    [Header("=== Jumping & Airborne ===")]
    [Tooltip("Velocity continuation factor at jump trigger. 1.0 = maintain exact velocity, 2.0 = double it. The fighter builds upward speed during leg extension; this multiplies that speed at launch.")]
    public float jumpForceMultiplier = 1.5f;
    [Tooltip("Fraction of max leg reach needed to trigger jump impulse.")]
    public float jumpExtensionThreshold = 0.9f;
    [HideInInspector] public bool forceAirborneMode = false;
    [HideInInspector] public bool isJumpNextSimulation = false;
    [HideInInspector] public bool jumpTriggerArmed = false;
    private bool hasJumpTriggered = false;

    [Header("=== Ragdoll & Floor Tumble ===")]
    [Tooltip("Damage threshold to trigger full ragdoll for the rest of the turn.")]
    public float ragdollDamageThreshold = 25f;
    [HideInInspector] public bool isRagdolled = false;

    private Dictionary<Collider2D, PhysicsMaterial2D> originalMaterials = new Dictionary<Collider2D, PhysicsMaterial2D>();
    private PhysicsMaterial2D ragdollMaterial;

    [Header("=== Posing Preview Loop ===")]
    public bool enablePosePreviewLoop = true;
    public bool disableDamageDuringPreview = true;
    public float previewLoopDuration = 0f;

    [Header("=== Auto Walk From Hips ===")]
    public bool enableAutoWalkFromHipTarget = true;
    public float walkMinHipTravel = 0.35f;
    public float walkMaxHipGroundDistance = 3.5f;
    public float walkMaxStrideDistance = 3.5f;
    public float walkStepLead = 0.85f;
    public float walkFootSpacing = 0.45f;
    public float walkStepLift = 0.8f;
    public float walkFootSpring = 95f;
    public float walkFootDamping = 12f;
    public float maxWalkFootForce = 750f;
    public float minAutoFootSeparation = 0.85f;
    public float autoKneeOutwardOffset = 0.55f;
    public float autoCrouchKneeLift = 0.35f;

    [Tooltip("Rotate frozen Auto leg segments toward generated hip/knee/foot targets while the hips are dragged.")]
    public bool rotateFrozenAutoLegsToGeneratedTargets = true;

    [Header("=== Auto Leg Live Driver ===")]
    [Tooltip("Drive Auto legs on the live fighter toward the generated frozen-copy leg targets.")]
    public bool driveAutoLegsToGeneratedTargets = true;

    [Tooltip("During Auto walking, use hinge motors to rotate thighs/calves toward the generated step pose.")]
    public bool useJointMotorsForAutoWalk = true;

    [Tooltip("When Auto-walk joint motors are active, leave thigh/calf translation mostly to joints and only force-drive feet/contact bodies.")]
    public bool reduceAutoWalkLimbForcesWhenUsingMotors = true;

    [Tooltip("When a foot is planted, let Planted Foot Stability own that body so two springs do not fight over it.")]
    public bool skipAutoLegTargetForPlantedFeet = true;

    [Tooltip("Only use generated Auto-leg target forces while the hip target is actually walking.")]
    public bool driveAutoLegTargetsOnlyDuringWalk = true;

    [Tooltip("Also allow PoseDriver motors to chase generated Auto leg joint angles. Keep off if motors wind up.")]
    public bool usePoseDriversForAutoLegs = false;

    [Tooltip("Linear spring used by live Auto legs to chase generated frozen-copy positions.")]
    public float autoLegTargetSpring = 160f;

    [Tooltip("Linear damping used by live Auto leg target tracking.")]
    public float autoLegTargetDamping = 18f;

    [Tooltip("Maximum force applied by live Auto leg target tracking.")]
    public float maxAutoLegTargetForce = 1400f;

    [Tooltip("Rotate live Auto legs toward generated frozen-copy rotations. Useful only if generated rotations are sane.")]
    public bool rotateAutoLegsTowardGeneratedTargets = false;

    public float autoLegTargetAngularSpring = 10f;
    public float autoLegTargetAngularDamping = 1.5f;
    public float maxAutoLegTargetTorque = 450f;

    [Header("=== Move Planning UI ===")]
    public bool showMovePlanningUI = true;
    public bool tintFrozenLimbsByIntent = true;
    public bool defaultLegsToAuto = true;
    public Vector2 moveUIPanelOffset = new Vector2(12f, 12f);
    public float moveUIPanelWidth = 460f;
    public bool moveUIStartMinimized = false;
    public float moveUIMinimizedWidth = 230f;
    public float moveUIMinimizedHeight = 42f;
    public int strikeRecoverySnapshots = 2;
    public float strikeVelocityMultiplier = 2f;
    public float strikeDamageMultiplier = 1.5f;
    public float strikeImpactImpulse = 8f;
    public float blockIncomingDamageMultiplier = 0.75f;

    [Header("=== Rig Validation / Conflicts ===")]
    public bool validateRigOnStart = true;
    public bool logRigValidationDetails = true;

    [Tooltip("Disable older scripts that write HingeJoint2D motors while FreezeReplayV2 owns the fighter.")]
    public bool disableLegacyMotorControllers = true;

    [Header("=== Debug (Read Only) ===")]
    public TurnPhase currentPhase = TurnPhase.Posing;
    public int turnCount = 0;

    [HideInInspector]
    public KeyCode resetCopyKey = KeyCode.C;
    private string resetCopyKeyString = "C";

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

    private struct FrozenRootEditPose
    {
        public Vector2 position;
        public float rotation;
    }

    private class LimbPlan
    {
        public int bodyIndex;
        public Rigidbody2D liveBody;
        public Transform frozenBody;
        public HingeJoint2D liveJoint;
        public HingeJoint2D frozenJoint;
        public PoseDriver poseDriver;
        public LimbMoveIntent intent;
    }

    // The current snapshot (what the frozen copy represents)
    private BodySnapshot[] currentSnapshot;

    // Frame-by-frame recording for replay
    private List<BodySnapshot[]> replayFrames = new List<BodySnapshot[]>();

    // Frozen copy children (matched 1:1 with trackedBodies by index)
    private Transform[] frozenChildren;

    // Simulation timer
    private float simTimeRemaining = 0f;

    // === Replay State ===
    private bool isPlayingReplay = false;

    // === Posing Preview State ===
    private FighterHealth fighterHealth;

    // Saved health for snapshot
    private float savedHP;

    // Store original body types for freeze/unfreeze
    private Dictionary<Rigidbody2D, RigidbodyType2D> savedBodyTypes = new Dictionary<Rigidbody2D, RigidbodyType2D>();
    private Dictionary<HingeJoint2D, bool> originalJointLimitStates = new Dictionary<HingeJoint2D, bool>();

    private List<LimbPlan> limbPlans = new List<LimbPlan>();
    private int rootBodyIndex = -1;
    private Dictionary<Rigidbody2D, Vector2> plantedFootTargets = new Dictionary<Rigidbody2D, Vector2>();
    private Dictionary<Rigidbody2D, Vector2> frozenRootEditPlantedLegTargets = new Dictionary<Rigidbody2D, Vector2>();
    private Dictionary<Rigidbody2D, FixedJoint2D> plantedFootJoints = new Dictionary<Rigidbody2D, FixedJoint2D>();
    private Dictionary<Rigidbody2D, TargetJoint2D> replantingFootJoints = new Dictionary<Rigidbody2D, TargetJoint2D>();
    private Dictionary<Rigidbody2D, Vector2> replantingFootTargets = new Dictionary<Rigidbody2D, Vector2>();
    private bool appliedIgnoreJointLimits = false;
    private bool posePreviewLoopRunning = false;
    private float posePreviewTimeRemaining = 0f;
    private int posePreviewFixedStepsRemaining = 0;
    private bool posePreviewRestartRequested = false;
    private Dictionary<ImpactDamage, bool> previewDamageStates = new Dictionary<ImpactDamage, bool>();
    private bool autoWalkActive = false;
    private Rigidbody2D autoWalkSwingFoot;
    private float autoWalkDirection = 1f;
    private Vector2 autoWalkTargetRoot;
    private bool frozenRootEditActive = false;
    private bool frozenPhysicsPoseEditActive = false;
    private Vector2 frozenRootEditStartRoot;
    private Dictionary<Rigidbody2D, FrozenRootEditPose> frozenRootEditStartPoses = new Dictionary<Rigidbody2D, FrozenRootEditPose>();
    private List<MonoBehaviour> disabledLegacyMotorControllers = new List<MonoBehaviour>();
    private bool playerBuiltFromRuntimeRig = false;
    private bool movePlanningUIStateInitialized = false;
    private bool movePlanningUIMinimized = false;

    // DistanceJoint2D tethers for all limbs
    private Dictionary<Rigidbody2D, DistanceJoint2D> poseTetherJoints = new Dictionary<Rigidbody2D, DistanceJoint2D>();
    private Dictionary<DistanceJoint2D, float> poseTetherStartDistances = new Dictionary<DistanceJoint2D, float>();

    // Tracks if the fighter has landed during the current simulation turn (preview or committed).
    // Used to protect landing foot joints from being cleared, and to switch legs back to Auto on the next turn.
    private bool hasLandedThisTurn = false;

    // ===== LIFECYCLE =====

    void Start()
    {
        // Spawn the player
        Vector3 spawnPos = (Vector2)transform.position + spawnOffset;
        playerObj = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
        playerObj.name = playerPrefab.name + "_Live";

        playerBuiltFromRuntimeRig = BuildRuntimeRigIfPresent(playerObj);

        if (disableLegacyMotorControllers)
            DisableLegacyMotorControllers();

        // Collect all rigidbodies from the player (depth-first, matching child order)
        CollectBodies(playerObj);
        CacheRootBody();
        SnapRuntimeRigToGroundIfNeeded();

        // Auto-generate the frozen copy by cloning the live fighter
        frozenCopy = Instantiate(playerObj);
        frozenCopy.name = playerPrefab.name + "_FrozenCopy";
        PrepareCloneAsFrozenCopy(frozenCopy);

        // Cache frozen copy children
        CacheFrozenChildren();
        CacheRootBody();

        // Wire PoseDrivers (live joints → frozen copy joints)
        WirePoseDrivers();
        ApplyJointLimitMode();
        if (validateRigOnStart)
            ValidateRigSetup();

        // Take initial snapshot (frozen copy matches live figure at spawn)
        CopyLiveToFrozen();
        currentSnapshot = CaptureSnapshot();
        CenterMainCameraOnSnapshot(currentSnapshot);

        // Cache health
        fighterHealth = playerObj.GetComponentInChildren<FighterHealth>();
        if (fighterHealth != null)
        {
            savedHP = fighterHealth.CurrentHP;
            fighterHealth.SaveHP();
        }

        // Start in posing phase — freeze only the live fighter, not the whole sim
        currentPhase = TurnPhase.Posing;
        FreezeLiveBodies();

        // Load preset if configured (overrides inspector values before first turn)
        TryAutoLoadPreset();

        if (walkMaxHipGroundDistance <= 0.01f || walkMaxStrideDistance <= 0.01f)
        {
            float autoLength = CalculateAnatomicalLegLength();
            if (walkMaxHipGroundDistance <= 0.01f) walkMaxHipGroundDistance = autoLength;
            if (walkMaxStrideDistance <= 0.01f) walkMaxStrideDistance = autoLength * 0.95f;
            Debug.Log($"[FreezeReplayV2] Auto-calculated leg length: {autoLength:F2}");
        }

        // Create high-friction physics material for floor tumbling
        ragdollMaterial = new PhysicsMaterial2D("RagdollMaterial")
        {
            friction = 0.8f,
            bounciness = 0.2f
        };

        Debug.Log($"[FreezeReplayV2] Spawned '{playerObj.name}' with {trackedBodies.Count} bodies. Ready to pose.");

        // Re-evaluate cross-fighter ghost collision ignores to accommodate initialization order
        ReapplyFighterGhostCollisionIgnores();

        // Explicitly force live fighter limbs to collide (bypassing any accidental layer overrides)
        EnableCrossFighterCollisions();
    }

    void Update()
    {
        if (appliedIgnoreJointLimits != ignoreJointLimits)
            ApplyJointLimitMode();
        ApplyPoseDriverSettings();

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

    void FixedUpdate()
    {
        bool isCommittedSimulation = currentPhase == TurnPhase.Simulating;
        bool shouldDrivePhysics = isCommittedSimulation
            || (currentPhase == TurnPhase.Posing && posePreviewLoopRunning);
        if (!shouldDrivePhysics) return;

        if (isRagdolled)
        {
            // Full ragdoll: disable all PoseDrivers, release all tethers/plants, and let raw physics take over
            foreach (var plan in limbPlans)
            {
                if (plan != null && plan.poseDriver != null)
                    plan.poseDriver.enablePoseDriver = false;
            }
            ClearPlantedFootJoints();
            ClearReplantingFootJoints();
            ClearPoseTethers();
            return;
        }

        // Jump has two phases:
        // Phase 1 (pre-trigger): legs extend upward, all assist systems active.
        //   The fighter naturally builds velocity as motors/tethers drive hips up.
        // Phase 2 (post-trigger): foot joints released, fighter is airborne.
        //   Only PoseDrivers (with root rotation compensation) keep body form.
        bool isPostJumpAirborne = forceAirborneMode || (jumpTriggerArmed && hasJumpTriggered);
        bool isPurePhysicsMode = isPostJumpAirborne;

        // Airborne root rotation compensation:
        // When the live root (hips) rotates under gravity but the frozen copy's root
        // stays fixed, PoseDriver jointAngle targets become stale because jointAngle
        // is measured relative to the connectedBody. We compute the rotation delta
        // and inject it into each PoseDriver so their targets compensate.
        UpdateAirborneRootRotationOffset(isPostJumpAirborne);

        if (!isPurePhysicsMode)
        {
            ApplyPlantedFootStability();
            ApplyPhysicsPoseAssist();
            ApplyPoseTethers();
            ApplyAdaptiveLegAssist();
            ApplyAutoLegGeneratedTargetAssist();

            // During jump pre-trigger phase, always drive the root upward toward
            // the frozen copy regardless of the global enablePhysicsPoseAssist toggle.
            // This builds the upward velocity that CheckJumpTrigger() will continue.
            if (jumpTriggerArmed && !hasJumpTriggered)
                ApplyJumpExtensionAssist();
        }
        else
        {
            if (!hasLandedThisTurn)
                ClearPlantedFootJoints();
            ClearReplantingFootJoints();
            ClearPoseTethers();
        }

        if (jumpTriggerArmed && !hasJumpTriggered)
        {
            CheckJumpTrigger();
        }

        // When airborne, continuously check if feet have touched ground.
        // On landing, switch legs to Auto so they can replant naturally
        // without requiring the frozen copy to be near.
        if (isPostJumpAirborne)
        {
            CheckAirborneLanding();
        }
        
        CheckReplantingFootArrival();

        if (!isCommittedSimulation)
        {
            AdvancePosePreviewFixedStep();
            return;
        }

        RecordFrame();
        simTimeRemaining -= Time.fixedDeltaTime;
        if (simTimeRemaining <= 0f)
        {
            currentPhase = TurnPhase.Capturing;
            jumpTriggerArmed = false;
        }
    }

    // ===== TURN PHASES =====

    /// <summary>
    /// POSING: Physics paused. Player drags the frozen copy to set the target pose.
    /// Press commitKey to lock in the pose and start simulation.
    /// </summary>
    private void UpdatePosing()
    {
        if (isPlayingReplay) return;

        // Frozen copy is interactive; live fighter either previews the turn or stays frozen.
        ShowFrozenCopy();
        RefreshFrozenIntentTints();

        if (Input.GetKeyDown(commitKey))
        {
            CommitPose();
            return;
        }

        if (Input.GetKeyDown(replayKey))
        {
            StopPosePreviewLoop(true, true);
            StartCoroutine(PlayReplayThenResume());
            return;
        }

        if (Input.GetKeyDown(resetCopyKey))
        {
            ResetFrozenCopy();
            return;
        }

        UpdatePosePreviewLoop();
    }

    /// <summary>
    /// SIMULATING: Physics runs for exactly turnDuration seconds.
    /// The live figure chases the committed pose via PoseDriver/motors.
    /// </summary>
    private void UpdateSimulating()
    {
        // Turn simulation timing is advanced in FixedUpdate so recording and
        // playback use the same cadence.
    }

    /// <summary>
    /// CAPTURING: Simulation done. Snapshot the result.
    /// Copy live figure → frozen copy. Return to posing.
    /// </summary>
    private void UpdateCapturing()
    {
        // Copy the live figure's END position onto the frozen copy
        CopyLiveToFrozen();
        StabilizeFrozenCopyPose();

        RecordFrame();

        // Store this as the new snapshot
        currentSnapshot = CaptureSnapshot();
        CenterMainCameraOnSnapshot(currentSnapshot);

        // Save health (this is the "real" HP after this turn's damage)
        if (enableHealthSnapshots && fighterHealth != null)
        {
            savedHP = fighterHealth.CurrentHP;
            fighterHealth.SaveHP();
        }

        CompleteLimbPlansAfterSimulation();

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
        StopPosePreviewLoop(true, true);
        StabilizeFrozenCopyPose();

        if (currentSnapshot != null)
            RestoreSnapshot(currentSnapshot);

        if (replayFrames.Count == 0)
            RecordFrame(false);

        // Save health before simulation (replay damage doesn't count)
        if (enableHealthSnapshots && fighterHealth != null)
        {
            savedHP = fighterHealth.CurrentHP;
            fighterHealth.SaveHP();
        }

        hasJumpTriggered = false;
        hasLandedThisTurn = false; // Clear landing flag for fresh committed turn simulation

        if (isJumpNextSimulation)
        {
            isJumpNextSimulation = false;
            jumpTriggerArmed = true;
            // Do NOT set forceAirborneMode here! The fighter needs pose assist
            // and planted feet active during the leg extension phase (pre-trigger).
            // forceAirborneMode is set by CheckJumpTrigger() when legs reach
            // full extension and the fighter actually leaves the ground.
        }

        // Start simulation — unfreeze live bodies
        simTimeRemaining = turnDuration;
        currentPhase = TurnPhase.Simulating;

        // Hide frozen copy BEFORE BeginLimbPlans so collider state matches
        // what the live fighter will experience during simulation.
        // (Preview also does not have frozen colliders active during its physics.)
        frozenPhysicsPoseEditActive = false;
        SetFrozenCopyBodyType(RigidbodyType2D.Kinematic);
        HideFrozenCopy();

        BeginLimbPlansForSimulation();
        UnfreezeLiveBodies();
        turnCount++;

        Debug.Log($"[FreezeReplayV2] Turn {turnCount}: Simulating {turnDuration}s...");

        // Optionally replay first, then simulate
        if (replayOnCapture && replayFrames.Count > 0)
        {
            // TODO: Could insert full replay before simulation here
        }
    }

    // ===== PREVIEW LOOP =====

    private void UpdatePosePreviewLoop()
    {
        if (!enablePosePreviewLoop || currentSnapshot == null)
        {
            StopPosePreviewLoop(true, true);
            return;
        }

        if (!posePreviewLoopRunning)
            StartPosePreviewLoop();

        if (posePreviewRestartRequested)
            RestartPosePreviewLoop();
    }

    private void StartPosePreviewLoop()
    {
        if (currentSnapshot == null) return;

        Dictionary<Rigidbody2D, FrozenRootEditPose> authoredFrozenPose = CaptureFrozenCopyPose();

        // Preview simulation is read-only with respect to the authored frozen copy.
        // The copy is stabilized when the player edits or commits it, not on preview restarts.
        RestoreSnapshot(currentSnapshot);
        SetPreviewDamageEnabled(false);
        posePreviewTimeRemaining = GetPreviewLoopDuration();
        posePreviewFixedStepsRemaining = GetPosePreviewFixedStepCount();
        simTimeRemaining = posePreviewTimeRemaining;
        BeginLimbPlansForSimulation();
        UnfreezeLiveBodies();
        RestoreFrozenCopyPose(authoredFrozenPose);

        // During LAUNCH READY preview, arm the jump trigger so the preview
        // shows the actual launch trajectory (legs extend → trigger → arc).
        // Also reset forceAirborneMode so the leg extension phase starts fresh
        // with all ground assists active (it gets set back to true by CheckJumpTrigger).
        if (isJumpNextSimulation)
        {
            jumpTriggerArmed = true;
            hasJumpTriggered = false;
            forceAirborneMode = false;
        }

        posePreviewLoopRunning = true;
        posePreviewRestartRequested = false;
        hasLandedThisTurn = false;
    }

    private void RestartPosePreviewLoop()
    {
        ResetLimbRuntimeAfterPreview();
        plantedFootTargets.Clear();
        autoWalkActive = false;
        autoWalkSwingFoot = null;
        posePreviewFixedStepsRemaining = 0;
        StartPosePreviewLoop();

        // Only show the frozen copy on preview restart if we are NOT in LAUNCH READY
        // mode. In LAUNCH READY, the frozen copy is already positioned at the authored
        // launch target — reshowing it here could cause a flicker or drift.
        if (currentPhase == TurnPhase.Posing && !isJumpNextSimulation)
            ShowFrozenCopy();
    }

    private void StopPosePreviewLoop(bool restoreSnapshot, bool freezeAfterRestore)
    {
        if (!posePreviewLoopRunning && !posePreviewRestartRequested)
            return;

        ResetLimbRuntimeAfterPreview();
        ClearPoseTethers();
        plantedFootTargets.Clear();
        autoWalkActive = false;
        autoWalkSwingFoot = null;
        SetPreviewDamageEnabled(true);
        posePreviewLoopRunning = false;
        posePreviewFixedStepsRemaining = 0;
        posePreviewRestartRequested = false;

        // If we were previewing a LAUNCH READY jump, reset forceAirborneMode.
        // The preview's CheckJumpTrigger may have set it to true; we must clear it
        // so the committed simulation starts with the leg extension phase active.
        if (isJumpNextSimulation)
        {
            forceAirborneMode = false;
            jumpTriggerArmed = false;
            hasJumpTriggered = false;
        }
        hasLandedThisTurn = false; // Reset preview landing flag

        if (restoreSnapshot && currentSnapshot != null)
            RestoreSnapshot(currentSnapshot);

        if (freezeAfterRestore)
            FreezeLiveBodies();
    }

    private void ResetLimbRuntimeAfterPreview()
    {
        ClearPlantedFootJoints();
        ClearReplantingFootJoints();

        foreach (var plan in limbPlans)
        {
            if (plan == null) continue;

            if (plan.poseDriver != null)
                plan.poseDriver.ResetRuntimeMultipliers();

            if (plan.intent != null)
                plan.intent.ClearRuntimePreview();
        }

        ResetRagdollState();
    }

    private float GetPreviewLoopDuration()
    {
        return previewLoopDuration > 0.001f ? previewLoopDuration : turnDuration;
    }

    private int GetPosePreviewFixedStepCount()
    {
        float fixedDelta = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        return Mathf.Max(1, Mathf.CeilToInt(GetPreviewLoopDuration() / fixedDelta));
    }

    private void AdvancePosePreviewFixedStep()
    {
        if (!posePreviewLoopRunning || posePreviewRestartRequested)
            return;

        posePreviewFixedStepsRemaining = Mathf.Max(0, posePreviewFixedStepsRemaining - 1);
        posePreviewTimeRemaining = Mathf.Max(0f, posePreviewTimeRemaining - Time.fixedDeltaTime);
        simTimeRemaining = posePreviewTimeRemaining;

        if (posePreviewFixedStepsRemaining <= 0)
            posePreviewRestartRequested = true;
    }

    private float GetActiveSimulationDuration()
    {
        return currentPhase == TurnPhase.Posing && posePreviewLoopRunning
            ? GetPreviewLoopDuration()
            : turnDuration;
    }

    public void RequestPosePreviewRestart()
    {
        posePreviewRestartRequested = true;

        if (currentPhase == TurnPhase.Posing)
            ShowFrozenCopy();
    }

    private void SetPreviewDamageEnabled(bool enabled)
    {
        if (playerObj == null) return;
        if (!disableDamageDuringPreview) return;

        ImpactDamage[] damageComponents = playerObj.GetComponentsInChildren<ImpactDamage>(true);
        foreach (var damage in damageComponents)
        {
            if (damage == null) continue;

            if (!previewDamageStates.ContainsKey(damage))
                previewDamageStates[damage] = damage.enableImpactDamage;

            damage.enableImpactDamage = previewDamageStates[damage];
            damage.SetPreviewSimulationMode(!enabled);
        }

        if (enabled)
            previewDamageStates.Clear();
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

            HingeJoint2D joint = rb.GetComponent<HingeJoint2D>();

            snap[i] = new BodySnapshot
            {
                position = rb.position,
                rotation = rb.rotation,
                velocity = rb.linearVelocity,
                angularVelocity = rb.angularVelocity,
                jointAngle = joint != null ? joint.jointAngle : 0f
            };
        }
        return snap;
    }

    private void CenterMainCameraOnSnapshot(BodySnapshot[] snapshot)
    {
        if (!centerMainCameraOnSnapshotStart) return;
        if (snapshot == null || snapshot.Length == 0) return;

        int cameraBodyIndex = rootBodyIndex >= 0 && rootBodyIndex < snapshot.Length
            ? rootBodyIndex
            : 0;
        Vector2 center = snapshot[cameraBodyIndex].position;

        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        CameraFollow cameraFollow = mainCamera.GetComponent<CameraFollow>();
        if (cameraFollow != null)
        {
            cameraFollow.CenterOnSnapshot(center);
            return;
        }

        Transform cameraTransform = mainCamera.transform;
        cameraTransform.position = new Vector3(center.x, center.y, cameraTransform.position.z);
    }

    // ===== RECORDING =====

    /// <summary>
    /// Record one frame of state for replay playback.
    /// Called from FixedUpdate during committed simulation so capture cadence
    /// matches replay playback cadence.
    /// </summary>
    private void RecordFrame(bool allowDuplicateSkip = true)
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

        if (allowDuplicateSkip && skipDuplicateReplayFrames && IsDuplicateReplayFrame(frame))
            return;

        replayFrames.Add(frame);
    }

    private bool IsDuplicateReplayFrame(BodySnapshot[] frame)
    {
        if (frame == null || replayFrames.Count == 0) return false;

        BodySnapshot[] previous = replayFrames[replayFrames.Count - 1];
        if (previous == null || previous.Length != frame.Length) return false;

        float positionToleranceSqr = duplicateReplayPositionTolerance * duplicateReplayPositionTolerance;
        for (int i = 0; i < frame.Length; i++)
        {
            if ((previous[i].position - frame[i].position).sqrMagnitude > positionToleranceSqr)
                return false;

            if (Mathf.Abs(Mathf.DeltaAngle(previous[i].rotation, frame[i].rotation)) > duplicateReplayRotationTolerance)
                return false;
        }

        return true;
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
        isPlayingReplay = true;
        HideFrozenCopy();
        Time.timeScale = 1f;

        ClearPlantedFootJoints();
        ClearReplantingFootJoints();
        ClearPoseTethers();

        foreach (var driver in poseDrivers)
        {
            if (driver != null) driver.enablePoseDriver = false;
        }

        // Make all tracked bodies kinematic during replay
        var originalTypes = new Dictionary<Rigidbody2D, RigidbodyType2D>();
        foreach (var rb in trackedBodies)
        {
            if (rb == null) continue;
            originalTypes[rb] = rb.bodyType;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        Debug.Log($"[FreezeReplayV2] Playing {replayFrames.Count} frames...");

        YieldInstruction frameWait = syncReplayToFixedStep
            ? new WaitForSeconds(Time.fixedDeltaTime)
            : null;

        // Play each frame
        for (int frameIndex = 0; frameIndex < replayFrames.Count; frameIndex++)
        {
            BodySnapshot[] frame = replayFrames[frameIndex];
            for (int i = 0; i < trackedBodies.Count && i < frame.Length; i++)
            {
                Rigidbody2D rb = trackedBodies[i];
                if (rb == null) continue;

                rb.position = frame[i].position;
                rb.rotation = frame[i].rotation;
                rb.transform.position = frame[i].position;
                rb.transform.rotation = Quaternion.Euler(0, 0, frame[i].rotation);
            }

            if (frameIndex >= replayFrames.Count - 1)
                continue;

            if (frameWait != null)
                yield return frameWait;
            else
                yield return null;
        }

        // Restore body types
        foreach (var kvp in originalTypes)
        {
            if (kvp.Key != null)
                kvp.Key.bodyType = kvp.Value;
        }

        isPlayingReplay = false;
        
        // Ensure we freeze again if we return to posing phase
        if (currentPhase == TurnPhase.Posing)
            FreezeLiveBodies();

        // Restore the planning ghost from the authoritative captured snapshot,
        // never from replay-driven live transforms.
        ResetFrozenCopy();

        // Resume posing — freeze live bodies again
        FreezeLiveBodies();
        currentPhase = TurnPhase.Posing;
        ShowFrozenCopy();

        Debug.Log("[FreezeReplayV2] Replay complete. Pose next move.");
    }

    // ===== SETUP HELPERS =====

    private bool BuildRuntimeRigIfPresent(GameObject fighter)
    {
        if (fighter == null) return false;

        FighterRuntimeRigBuilder controllerBuilder = GetComponent<FighterRuntimeRigBuilder>();
        FighterRuntimeRigBuilder builder = fighter.GetComponent<FighterRuntimeRigBuilder>();
        if (builder == null && ShouldAutoAddRuntimeRigBuilder(fighter))
            builder = fighter.AddComponent<FighterRuntimeRigBuilder>();

        if (builder == null) return false;

        if (controllerBuilder != null)
            builder.CopySettingsFrom(controllerBuilder);

        builder.BuildIfNeeded();
        return fighter.GetComponentInChildren<FighterRuntimeRigInstance>(true) != null;
    }

    private void SnapRuntimeRigToGroundIfNeeded()
    {
        if (!snapRuntimeRigFeetToGround || !playerBuiltFromRuntimeRig) return;
        if (trackedBodies.Count == 0) return;
        if (!TryGetLiveColliderBottom(out float lowestY)) return;

        Vector2 probeOrigin = GetSpawnGroundProbeOrigin();
        if (!TryGetGroundPointFromProbe(probeOrigin, spawnGroundProbeHeight, spawnGroundProbeDistance, out Vector2 groundPoint))
        {
            Debug.LogWarning("[FreezeReplayV2] Runtime rig ground snap skipped: no ground was found below the generated fighter.");
            return;
        }

        float targetBottom = groundPoint.y + Mathf.Max(0f, spawnGroundClearance);
        float deltaY = targetBottom - lowestY;
        if (Mathf.Abs(deltaY) <= 0.001f) return;

        Vector2 delta = Vector2.up * deltaY;
        foreach (var rb in trackedBodies)
        {
            if (rb == null) continue;
            rb.position += delta;
            rb.transform.position = rb.position;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    private bool TryGetLiveColliderBottom(out float lowestY)
    {
        lowestY = float.PositiveInfinity;
        bool found = false;

        foreach (var rb in trackedBodies)
        {
            if (rb == null) continue;
            foreach (var col in rb.GetComponents<Collider2D>())
            {
                if (col == null || !col.enabled || col.isTrigger) continue;
                lowestY = Mathf.Min(lowestY, col.bounds.min.y);
                found = true;
            }
        }

        return found;
    }

    private Vector2 GetSpawnGroundProbeOrigin()
    {
        Rigidbody2D root = rootBodyIndex >= 0 && rootBodyIndex < trackedBodies.Count
            ? trackedBodies[rootBodyIndex]
            : null;

        Vector2 origin = root != null ? root.position : (Vector2)playerObj.transform.position;
        origin.y += Mathf.Max(0f, spawnGroundProbeHeight);
        return origin;
    }

    private bool ShouldAutoAddRuntimeRigBuilder(GameObject fighter)
    {
        if (fighter == null) return false;
        if (fighter.GetComponentInChildren<Rigidbody2D>(true) != null) return false;

        bool hasHips = HasMarkerNamed(fighter.transform, "Hips", "Hip", "Pelvis", "Root");
        bool hasUpperBody = HasMarkerNamed(fighter.transform, "Shoulders", "Shoulder", "Chest")
            || HasMarkerNamed(fighter.transform, "Head");
        bool hasLimbs = HasMarkerNamed(
            fighter.transform,
            "Knee L", "Knee R", "KneeL", "KneeR", "Left Knee", "Right Knee",
            "Foot L", "Foot R", "FootL", "FootR", "Left Foot", "Right Foot",
            "Elbow L", "Elbow R", "ElbowL", "ElbowR", "Left Elbow", "Right Elbow",
            "Hand L", "Hand R", "HandL", "HandR", "Left Hand", "Right Hand");

        return hasHips && (hasUpperBody || hasLimbs);
    }

    private bool HasMarkerNamed(Transform root, params string[] aliases)
    {
        if (root == null) return false;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || child == root) continue;
            string childName = NormalizeRigMarkerName(child.name);

            foreach (string alias in aliases)
            {
                if (childName == NormalizeRigMarkerName(alias))
                    return true;
            }
        }

        return false;
    }

    private string NormalizeRigMarkerName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return string.Empty;

        string n = rawName.ToLowerInvariant();
        n = n.Replace("_", string.Empty);
        n = n.Replace("-", string.Empty);
        n = n.Replace(" ", string.Empty);
        return n;
    }

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

    private void CacheRootBody()
    {
        rootBodyIndex = -1;

        for (int i = 0; i < trackedBodies.Count; i++)
        {
            if (trackedBodies[i] == null) continue;
            string bodyName = trackedBodies[i].name.ToLowerInvariant();
            if (bodyName.Contains("hip") || bodyName.Contains("pelvis") || bodyName.Contains("root"))
            {
                rootBodyIndex = i;
                return;
            }
        }

        for (int i = 0; i < trackedBodies.Count; i++)
        {
            if (trackedBodies[i] == null) continue;
            if (trackedBodies[i].GetComponent<HingeJoint2D>() == null)
            {
                rootBodyIndex = i;
                return;
            }
        }

        for (int i = 0; i < trackedBodies.Count; i++)
        {
            if (trackedBodies[i] == null) continue;
            string bodyName = trackedBodies[i].name.ToLowerInvariant();
            if (bodyName.Contains("spine") || bodyName.Contains("torso"))
            {
                rootBodyIndex = i;
                return;
            }
        }

        rootBodyIndex = trackedBodies.Count > 0 ? 0 : -1;
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
            typeof(LimbMoveIntent),
            typeof(FighterRuntimeRigBuilder),
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

        // --- Physics setup: keep joints active but do not let the planning ghost drift ---
        foreach (var rb in clone.GetComponentsInChildren<Rigidbody2D>(true))
        {
            rb.gravityScale = 0f;           // Don't fall during posing
            rb.linearDamping = 5f;          // Resist unwanted drift
            rb.angularDamping = 5f;         // Resist unwanted spin
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // --- Disable collisions between this frozen copy and ALL live fighters in the scene ---
        // This prevents physical interference from opponent ghosts, enabling clean, direct local multi-fighter collisions.
        ReapplyFighterGhostCollisionIgnores();

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
        limbPlans.Clear();
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
            driver.ignoreTargetJointLimits = ignoreJointLimits;

            LimbMoveIntent intent = liveRb.gameObject.GetComponent<LimbMoveIntent>();
            if (intent == null)
                intent = liveRb.gameObject.AddComponent<LimbMoveIntent>();
            ConfigureLimbIntent(intent);
            if (defaultLegsToAuto && IsLegLimb(liveRb.name) && intent.mode == LimbMoveIntent.MoveMode.BasicBlock)
                intent.QueueAuto();

            limbPlans.Add(new LimbPlan
            {
                bodyIndex = i,
                liveBody = liveRb,
                frozenBody = frozenChild,
                liveJoint = liveJoint,
                frozenJoint = frozenJoint,
                poseDriver = driver,
                intent = intent
            });

            wired++;
            Debug.Log($"[FreezeReplayV2] Wired PoseDriver: '{liveRb.name}' → '{frozenChild.name}'");
        }

        Debug.Log($"[FreezeReplayV2] Wired {wired} PoseDrivers total.");
        ApplyPoseDriverSettings();
        RefreshFrozenIntentTints();
    }

    private void ApplyPoseDriverSettings()
    {
        foreach (var driver in poseDrivers)
        {
            if (driver == null) continue;
            driver.Kp = poseKp;
            driver.Kd = poseKd;
            driver.maxMotorSpeed = poseMaxSpeed;
            driver.ignoreTargetJointLimits = ignoreJointLimits;
        }
    }

    private void ApplyJointLimitMode()
    {
        RegisterAndApplyJointLimitMode(playerObj);
        RegisterAndApplyJointLimitMode(frozenCopy);

        foreach (var driver in poseDrivers)
        {
            if (driver == null) continue;
            driver.ignoreTargetJointLimits = ignoreJointLimits;
            driver.RefreshLimits();
        }

        appliedIgnoreJointLimits = ignoreJointLimits;
    }

    private void RegisterAndApplyJointLimitMode(GameObject root)
    {
        if (root == null) return;

        HingeJoint2D[] joints = root.GetComponentsInChildren<HingeJoint2D>(true);
        foreach (var joint in joints)
        {
            if (joint == null) continue;

            if (!originalJointLimitStates.ContainsKey(joint))
                originalJointLimitStates[joint] = joint.useLimits;

            joint.useLimits = ignoreJointLimits ? false : originalJointLimitStates[joint];
        }
    }

    // ===== RIG VALIDATION / CONFLICTS =====

    private void DisableLegacyMotorControllers()
    {
        disabledLegacyMotorControllers.Clear();
        if (playerObj == null) return;

        foreach (var script in playerObj.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (script == null || !script.enabled) continue;
            if (!IsLegacyMotorController(script)) continue;

            script.enabled = false;
            disabledLegacyMotorControllers.Add(script);
        }

        if (disabledLegacyMotorControllers.Count > 0)
            Debug.LogWarning($"[FreezeReplayV2] Disabled {disabledLegacyMotorControllers.Count} legacy motor/controller script(s) on '{playerObj.name}' so FreezeReplayV2 can own the pose simulation.");
    }

    private bool IsLegacyMotorController(MonoBehaviour script)
    {
        string typeName = script.GetType().Name;
        return typeName == "RotateHingeJoint"
            || typeName == "Stand"
            || typeName == "AIBalance"
            || typeName == "LegZBalance"
            || typeName == "LeftShoulderScript"
            || typeName == "RightShoulderScript"
            || typeName == "LeftThighScript"
            || typeName == "RightThighScript"
            || typeName == "MotorEnabler"
            || typeName == "StepHelper"
            || typeName == "StickFigureAgent"
            || typeName == "StickFigureAgentv2"
            || typeName == "LegsAgent"
            || typeName == "PoseDriverSetup";
    }

    [ContextMenu("Validate FreezeReplay Rig")]
    public void ValidateRigSetupNow()
    {
        ValidateRigSetup();
    }

    private void ValidateRigSetup()
    {
        if (playerObj == null || frozenCopy == null)
        {
            Debug.LogWarning("[FreezeReplayV2] Rig validation skipped: live or frozen fighter is missing.");
            return;
        }

        int frozenCount = frozenChildren != null ? frozenChildren.Length : 0;
        if (trackedBodies.Count != frozenCount)
            Debug.LogWarning($"[FreezeReplayV2] Rig mismatch: live has {trackedBodies.Count} Rigidbody2D bodies, frozen copy has {frozenCount}.");

        if (rootBodyIndex < 0 || rootBodyIndex >= trackedBodies.Count)
            Debug.LogWarning("[FreezeReplayV2] No root body found. Hips/root dragging and auto walking need a root Rigidbody2D.");
        else if (logRigValidationDetails)
            Debug.Log($"[FreezeReplayV2] Root body: index {rootBodyIndex}, '{trackedBodies[rootBodyIndex].name}'.");

        for (int i = 0; i < trackedBodies.Count; i++)
        {
            Rigidbody2D liveRb = trackedBodies[i];
            Transform frozenBody = frozenChildren != null && i < frozenChildren.Length ? frozenChildren[i] : null;
            if (liveRb == null) continue;

            bool isRoot = i == rootBodyIndex;
            HingeJoint2D liveJoint = liveRb.GetComponent<HingeJoint2D>();
            HingeJoint2D frozenJoint = frozenBody != null ? frozenBody.GetComponent<HingeJoint2D>() : null;

            if (!isRoot && liveJoint == null)
                Debug.LogWarning($"[FreezeReplayV2] '{liveRb.name}' has no HingeJoint2D. It cannot be joint-driven.");

            if (liveJoint != null)
            {
                if (liveJoint.connectedBody == null)
                    Debug.LogWarning($"[FreezeReplayV2] '{liveRb.name}' HingeJoint2D has no connectedBody.");

                if (liveJoint.motor.maxMotorTorque <= 0.01f)
                    Debug.LogWarning($"[FreezeReplayV2] '{liveRb.name}' HingeJoint2D motor torque is near zero. PoseDriver motors will not move it.");
            }

            if (frozenBody == null)
            {
                Debug.LogWarning($"[FreezeReplayV2] No frozen-copy body mapped for live body index {i} ('{liveRb.name}').");
                continue;
            }

            if (!isRoot && liveJoint != null && frozenJoint == null)
                Debug.LogWarning($"[FreezeReplayV2] Frozen body '{frozenBody.name}' is missing HingeJoint2D for live '{liveRb.name}'.");

            if (IsLegLimb(liveRb.name))
            {
                LimbPlan plan = GetPlanForLiveBody(liveRb);
                if (plan == null)
                    Debug.LogWarning($"[FreezeReplayV2] Leg '{liveRb.name}' has no LimbPlan. Auto/Strike UI will not control it.");
                else if (logRigValidationDetails)
                {
                    string modeName = plan.intent != null ? plan.intent.mode.ToString() : "None";
                    Debug.Log($"[FreezeReplayV2] Leg plan: '{liveRb.name}' mode={modeName}, poseDriver={(plan.poseDriver != null)}.");
                }
            }
        }

        if (!disableLegacyMotorControllers)
        {
            foreach (var script in playerObj.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script == null || !script.enabled || !IsLegacyMotorController(script)) continue;
                Debug.LogWarning($"[FreezeReplayV2] '{script.GetType().Name}' is enabled on '{script.gameObject.name}' and may overwrite FreezeReplayV2 joint motors.");
            }
        }
    }

    // ===== LIMB PLANNING / INTENT =====

    private void ConfigureLimbIntent(LimbMoveIntent intent)
    {
        if (intent == null) return;

        intent.Configure(
            strikeVelocityMultiplier,
            strikeDamageMultiplier,
            strikeImpactImpulse,
            blockIncomingDamageMultiplier);
    }

    private void BeginLimbPlansForSimulation()
    {
        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.intent == null) continue;

            ConfigureLimbIntent(plan.intent);
            plan.intent.BeginSimulation();

            float velocityMultiplier = plan.intent.CurrentVelocityMultiplier;
            if (plan.poseDriver != null)
            {
                // Give strikes an extra torque boost to overcome inertia and accelerate instantly
                float torqueMultiplier = plan.intent.IsActiveStrike ? velocityMultiplier * 1.5f : velocityMultiplier;
                plan.poseDriver.SetRuntimeMultipliers(velocityMultiplier, torqueMultiplier);
            }
        }

        CachePlantedFootTargets();
        PreReleasePlantedFeetByFrozenTarget();
        CreatePlantedFootJoints();
        RefreshFrozenIntentTints();
    }

    private void CompleteLimbPlansAfterSimulation()
    {
        foreach (var plan in limbPlans)
        {
            if (plan == null) continue;

            if (plan.poseDriver != null)
                plan.poseDriver.ResetRuntimeMultipliers();

            if (plan.intent != null)
                plan.intent.FinishSnapshot(strikeRecoverySnapshots);
        }

        // If the fighter landed during the simulation, switch leg intents to Auto mode
        // so that in the next turn's posing phase, the preview copy starts in Auto mode!
        if (hasLandedThisTurn)
        {
            foreach (var plan in limbPlans)
            {
                if (plan != null && plan.intent != null && IsLegLimb(plan.liveBody.name))
                {
                    plan.intent.mode = LimbMoveIntent.MoveMode.Auto;
                }
            }
            // Also reset forceAirborneMode since we have landed on the ground!
            forceAirborneMode = false;
            
            Debug.Log("[FreezeReplayV2] Snapshot captured on ground: switching legs to Auto and setting forceAirborneMode = false.");
        }

        plantedFootTargets.Clear();
        ClearPlantedFootJoints();
        ClearReplantingFootJoints();
        RefreshFrozenIntentTints();

        ResetRagdollState();
    }

    private float GetIntentVelocityMultiplier(int bodyIndex)
    {
        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.bodyIndex != bodyIndex || plan.intent == null) continue;
            return plan.intent.CurrentVelocityMultiplier;
        }

        return 1f;
    }

    private LimbPlan GetPlanForLiveBody(Rigidbody2D body)
    {
        if (body == null) return null;

        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody != body) continue;
            return plan;
        }

        return null;
    }

    private void RefreshFrozenIntentTints()
    {
        if (!tintFrozenLimbsByIntent) return;

        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.frozenBody == null) continue;
            SetFrozenBodyColor(plan.frozenBody, GetIntentColor(plan.intent));
        }
    }

    private Color GetIntentColor(LimbMoveIntent intent)
    {
        if (intent == null) return frozenCopyColor;

        if (intent.IsRecovering)
            return new Color(0.45f, 0.55f, 0.65f, 0.38f);

        if (intent.mode == LimbMoveIntent.MoveMode.Auto)
            return new Color(0.15f, 0.55f, 1f, 0.62f);

        if (intent.mode == LimbMoveIntent.MoveMode.Strike || intent.IsActiveStrike)
            return new Color(1f, 0.28f, 0.16f, 0.72f);

        return frozenCopyColor;
    }

    private void SetFrozenBodyColor(Transform body, Color color)
    {
        foreach (var sr in body.GetComponentsInChildren<SpriteRenderer>(true))
            sr.color = color;

        foreach (var ssr in body.GetComponentsInChildren<UnityEngine.U2D.SpriteShapeRenderer>(true))
            ssr.color = color;
    }

    // ===== PHYSICS / IK ASSIST =====

    private void ApplyPhysicsPoseAssist()
    {
        if (!enablePhysicsPoseAssist || frozenChildren == null) return;

        int count = Mathf.Min(trackedBodies.Count, frozenChildren.Length);
        for (int i = 0; i < count; i++)
        {
            Rigidbody2D rb = trackedBodies[i];
            Transform target = frozenChildren[i];
            if (rb == null || target == null || rb.bodyType != RigidbodyType2D.Dynamic) continue;
            if (IsAutoLegBody(rb)) continue;
            if (poseAssistOnlyRootAndUnjointedBodies && rb.GetComponent<HingeJoint2D>() != null) continue;

            float multiplier = GetIntentVelocityMultiplier(i);
            Vector2 positionError = (Vector2)target.position - rb.position;
            Vector2 force = positionError * (poseAssistSpring * multiplier) - rb.linearVelocity * poseAssistDamping;
            
            float angularMult = multiplier;
            if (forceAirborneMode || isJumpNextSimulation) angularMult *= airborneAngularSpringMultiplier;

            rb.AddForce(Vector2.ClampMagnitude(force, maxPoseAssistForce * multiplier), ForceMode2D.Force);

            float rotationError = Mathf.DeltaAngle(rb.rotation, target.eulerAngles.z);
            float torque = rotationError * (poseAssistAngularSpring * angularMult)
                - rb.angularVelocity * poseAssistAngularDamping;
            rb.AddTorque(Mathf.Clamp(torque, -maxPoseAssistTorque * angularMult, maxPoseAssistTorque * angularMult), ForceMode2D.Force);
        }
    }

    /// <summary>
    /// Drives the root body toward the frozen copy's position during the jump pre-trigger
    /// phase. This is always active when jumpTriggerArmed, even if enablePhysicsPoseAssist
    /// is off — the upward velocity built here is what CheckJumpTrigger() amplifies.
    /// </summary>
    private void ApplyJumpExtensionAssist()
    {
        if (rootBodyIndex < 0 || rootBodyIndex >= trackedBodies.Count) return;
        if (frozenChildren == null || rootBodyIndex >= frozenChildren.Length) return;

        Rigidbody2D liveRoot = trackedBodies[rootBodyIndex];
        Transform frozenRootTf = frozenChildren[rootBodyIndex];
        if (liveRoot == null || frozenRootTf == null) return;
        if (liveRoot.bodyType != RigidbodyType2D.Dynamic) return;

        // Spring toward frozen root position, with extra upward bias to ensure
        // the fighter is clearly moving upward when the trigger fires.
        Vector2 positionError = (Vector2)frozenRootTf.position - liveRoot.position;

        // Use pose assist spring values if available, otherwise use sensible defaults.
        float spring = enablePhysicsPoseAssist ? poseAssistSpring : 80f;
        float damping = enablePhysicsPoseAssist ? poseAssistDamping : 15f;
        float maxForce = enablePhysicsPoseAssist ? maxPoseAssistForce : 1200f;

        Vector2 force = positionError * spring - liveRoot.linearVelocity * damping;
        liveRoot.AddForce(Vector2.ClampMagnitude(force, maxForce), ForceMode2D.Force);
    }

    private void ApplyPoseTethers()
    {
        if (!enablePoseTethers || frozenChildren == null) return;

        bool isAirborne = forceAirborneMode || isJumpNextSimulation;
        bool isStarting = poseTetherJoints.Count == 0 && GetActiveSimulationDuration() > 0.001f;
        
        Rigidbody2D rootRb = (rootBodyIndex >= 0 && rootBodyIndex < trackedBodies.Count) ? trackedBodies[rootBodyIndex] : null;

        int count = Mathf.Min(trackedBodies.Count, frozenChildren.Length);
        for (int i = 0; i < count; i++)
        {
            Rigidbody2D rb = trackedBodies[i];
            Transform target = frozenChildren[i];
            
            if (rb == null || target == null || rb.bodyType != RigidbodyType2D.Dynamic) continue;

            // Skip striking limbs entirely — tethers would restrict their explosive velocity and range of motion!
            LimbPlan plan = null;
            for (int pIdx = 0; pIdx < limbPlans.Count; pIdx++)
            {
                if (limbPlans[pIdx].bodyIndex == i)
                {
                    plan = limbPlans[pIdx];
                    break;
                }
            }
            if (plan != null && plan.intent != null && plan.intent.IsActiveStrike)
            {
                // If a tether was already created, destroy it
                if (poseTetherJoints.TryGetValue(rb, out DistanceJoint2D existing))
                {
                    existing.enabled = false;
                    Destroy(existing);
                    poseTetherJoints.Remove(rb);
                }
                continue;
            }
            
            // In airborne mode, the root has NO world-space tether (falls via gravity)
            if (isAirborne && i == rootBodyIndex)
            {
                if (poseTetherJoints.TryGetValue(rb, out DistanceJoint2D existing))
                {
                    existing.enabled = false;
                    Destroy(existing);
                    poseTetherJoints.Remove(rb);
                }
                continue;
            }

            // In airborne mode, only tether limb ends. Grounded motion keeps
            // the full tether behavior so editor-tuned grounded poses still apply.
            if (isAirborne)
            {
                string n = rb.name;
                bool isEnd = IsFootLimb(n) || IsHandLimb(n);
                if (!isEnd)
                {
                    if (poseTetherJoints.TryGetValue(rb, out DistanceJoint2D existing))
                    {
                        existing.enabled = false;
                        Destroy(existing);
                        poseTetherJoints.Remove(rb);
                    }

                    continue;
                }
            }

            // Skip planted feet as they have their own rigid joints
            if (plantedFootTargets.ContainsKey(rb)) continue;

            if (isStarting)
            {
                DistanceJoint2D tether = rb.gameObject.AddComponent<DistanceJoint2D>();
                tether.autoConfigureConnectedAnchor = false;
                tether.anchor = Vector2.zero;
                tether.maxDistanceOnly = true;
                tether.breakForce = tetherBreakForce;

                if (isAirborne && rootRb != null)
                {
                    // LOCAL SPACE TETHER: Tether limbs to the root instead of the world
                    tether.connectedBody = rootRb;
                    // Position relative to root
                    Transform frozenRoot = GetFrozenRootTransform();
                    if (frozenRoot != null)
                    {
                        Vector3 localOffset = frozenRoot.InverseTransformPoint(target.position);
                        tether.connectedAnchor = localOffset;
                    }
                    else
                    {
                        tether.connectedAnchor = rootRb.transform.InverseTransformPoint(target.position);
                    }
                }
                else
                {
                    // WORLD SPACE TETHER
                    tether.connectedAnchor = target.position; 
                }
                
                float initialDist = Vector2.Distance(rb.position, target.position);
                tether.distance = tetherShrinksOverTurn ? initialDist : poseTetherMaxDistance;
                
                poseTetherJoints[rb] = tether;
                poseTetherStartDistances[tether] = initialDist;
            }
            else if (poseTetherJoints.TryGetValue(rb, out DistanceJoint2D tether) && tether != null)
            {
                // Update world target if not airborne (airborne local targets stay static relative to root)
                if (!isAirborne)
                {
                    tether.connectedAnchor = target.position;
                }

                if (tetherShrinksOverTurn && poseTetherStartDistances.TryGetValue(tether, out float initialDist))
                {
                    float phase = GetActiveSimulationPhase(); // 0 to 1
                    tether.distance = Mathf.Lerp(initialDist, poseTetherMaxDistance, phase);
                }
            }
        }
    }

    private void ClearPoseTethers()
    {
        foreach (var kvp in poseTetherJoints)
        {
            if (kvp.Value != null)
            {
                kvp.Value.enabled = false;
                Destroy(kvp.Value);
            }
        }
        poseTetherJoints.Clear();
        poseTetherStartDistances.Clear();
    }

    private void ApplyAutoLegGeneratedTargetAssist()
    {
        if (!driveAutoLegsToGeneratedTargets || frozenChildren == null) return;
        if (autoWalkActive) return;
        if (driveAutoLegTargetsOnlyDuringWalk && !autoWalkActive) return;

        float phase = GetActiveSimulationPhase();
        foreach (var plan in limbPlans)
        {
            if (!IsAutoLimbPlan(plan)) continue;
            if (plan.liveBody.bodyType != RigidbodyType2D.Dynamic) continue;
            if (skipAutoLegTargetForPlantedFeet
                && !autoWalkActive
                && IsGroundContactLegPlan(plan)
                && plantedFootTargets.ContainsKey(plan.liveBody))
            {
                continue;
            }

            Vector2 targetPosition = GetAutoLegLiveTarget(plan, phase);
            Vector2 positionError = targetPosition - plan.liveBody.position;
            Vector2 force = positionError * autoLegTargetSpring
                - plan.liveBody.linearVelocity * autoLegTargetDamping;
            plan.liveBody.AddForce(Vector2.ClampMagnitude(force, maxAutoLegTargetForce), ForceMode2D.Force);

            if (!rotateAutoLegsTowardGeneratedTargets) continue;

            float rotationError = Mathf.DeltaAngle(plan.liveBody.rotation, plan.frozenBody.eulerAngles.z);
            float torque = rotationError * autoLegTargetAngularSpring
                - plan.liveBody.angularVelocity * autoLegTargetAngularDamping;
            plan.liveBody.AddTorque(Mathf.Clamp(torque, -maxAutoLegTargetTorque, maxAutoLegTargetTorque), ForceMode2D.Force);
        }
    }

    private float GetActiveSimulationPhase()
    {
        float activeDuration = GetActiveSimulationDuration();
        return activeDuration > 0.001f
            ? Mathf.Clamp01(1f - simTimeRemaining / activeDuration)
            : 1f;
    }

    private Vector2 GetAutoLegLiveTarget(LimbPlan plan, float phase)
    {
        if (autoWalkActive)
        {
            bool isSwingLeg = IsSameLegSide(plan, autoWalkSwingFoot);
            return GetAutoLimbBodyTarget(plan, autoWalkTargetRoot, Vector2.zero, phase, isSwingLeg, plan.liveBody == autoWalkSwingFoot);
        }

        if ((IsGroundContactLegPlan(plan) || IsGroundContactArmPlan(plan)) && plantedFootTargets.TryGetValue(plan.liveBody, out Vector2 plantedTarget))
        {
            return plantedTarget;
        }

        return plan.frozenBody.position;
    }

    private void ApplyAdaptiveLegAssist()
    {
        if (!enableAdaptiveLegAssist || frozenChildren == null) return;
        if (rootBodyIndex < 0 || rootBodyIndex >= trackedBodies.Count || rootBodyIndex >= frozenChildren.Length) return;

        Rigidbody2D rootBody = trackedBodies[rootBodyIndex];
        Transform frozenRoot = frozenChildren[rootBodyIndex];
        if (rootBody == null || frozenRoot == null) return;

        float phase = GetActiveSimulationPhase();

        ConfigureAutoWalkPlan();
        if (autoWalkActive)
        {
            ApplyAutoWalkLegAssist(phase);
            return;
        }

        if (useJointMotorsForAutoWalk)
            RelaxAutoLegJointMotors();

        if (!useForceLegAssistWhenNotWalking) return;

        Vector2 travel = (Vector2)frozenRoot.position - rootBody.position;
        if (travel.magnitude < minStepDistance) return;

        bool leftSwingLeg = (turnCount % 2) == 1;

        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null || plan.frozenBody == null) continue;
            if (plan.liveBody.bodyType != RigidbodyType2D.Dynamic) continue;
            if (!IsLowerLeg(plan.liveBody.name)) continue;
            if (plantedFootTargets.ContainsKey(plan.liveBody)) continue;

            bool isSwingLeg = IsLeftSide(plan) == leftSwingLeg;
            Vector2 target = ProjectFootTargetToGround(plan.frozenBody.position);
            Vector2 error = target - plan.liveBody.position;
            float spring = legStepSpring * (isSwingLeg ? 1f : 0.45f);
            Vector2 force = error * spring;

            if (isSwingLeg)
                force += Vector2.up * (legLiftForce * Mathf.Sin(phase * Mathf.PI));
            else
                force += Vector2.down * legPlantForce;

            plan.liveBody.AddForce(Vector2.ClampMagnitude(force, maxLegAssistForce), ForceMode2D.Force);
        }
    }

    private void ApplyAutoWalkLegAssist(float phase)
    {
        Vector2 phaseRootTarget = GetAutoWalkPhaseRootTarget(phase);

        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null) continue;
            if (plan.liveBody.bodyType != RigidbodyType2D.Dynamic) continue;
            if (!IsLegLimb(plan.liveBody.name)) continue;

            bool isContact = IsGroundContactLegPlan(plan);
            bool isSwingLeg = IsSameLegSide(plan, autoWalkSwingFoot);
            bool isSwingContact = plan.liveBody == autoWalkSwingFoot;

            if (useJointMotorsForAutoWalk)
                DriveAutoWalkJointMotor(plan, phaseRootTarget, phase, isSwingLeg, isSwingContact);

            if (useJointMotorsForAutoWalk
                && reduceAutoWalkLimbForcesWhenUsingMotors
                && !isContact
                && !IsFootLimb(plan.liveBody.name))
            {
                continue;
            }

            if (!isSwingContact && isContact && HasActivePlantedFootJoint(plan.liveBody))
                continue;

            Vector2 target = GetAutoLimbBodyTarget(plan, phaseRootTarget, Vector2.zero, phase, isSwingLeg, isSwingContact);
            Vector2 error = target - plan.liveBody.position;
            float spring = isContact ? walkFootSpring : walkFootSpring * 0.7f;
            Vector2 force = error * spring - plan.liveBody.linearVelocity * walkFootDamping;

            if (isContact && isSwingContact)
                force += Vector2.up * (walkStepLift * walkFootSpring * Mathf.Sin(phase * Mathf.PI));

            plan.liveBody.AddForce(Vector2.ClampMagnitude(force, maxWalkFootForce), ForceMode2D.Force);
        }
    }

    private Vector2 GetAutoWalkPhaseRootTarget(float phase)
    {
        if (currentSnapshot == null || rootBodyIndex < 0 || rootBodyIndex >= currentSnapshot.Length)
            return autoWalkTargetRoot;

        float t = Mathf.Clamp01(phase);
        t = t * t * (3f - 2f * t);
        return Vector2.Lerp(currentSnapshot[rootBodyIndex].position, autoWalkTargetRoot, t);
    }

    private void DriveAutoWalkJointMotor(
        LimbPlan plan,
        Vector2 rootTarget,
        float phase,
        bool isSwingLeg,
        bool isSwingContact)
    {
        if (plan == null || plan.liveBody == null || plan.liveJoint == null) return;
        if (!TryGetAutoLegTargetWorldRotation(plan, rootTarget, phase, isSwingLeg, isSwingContact, out float targetRotation))
            return;

        float rotationError = Mathf.DeltaAngle(plan.liveBody.rotation, targetRotation);

        if (!ignoreJointLimits && plan.liveJoint.useLimits)
        {
            float desiredJointAngle = NormalizeAngle(plan.liveJoint.jointAngle + rotationError);
            float clampedJointAngle = Mathf.Clamp(desiredJointAngle, plan.liveJoint.limits.min, plan.liveJoint.limits.max);
            rotationError = Mathf.DeltaAngle(plan.liveJoint.jointAngle, clampedJointAngle);
        }

        float damping = -plan.liveJoint.jointSpeed * poseKd;
        float motorSpeed = poseKp * rotationError + damping;
        float speedLimit = Mathf.Max(1f, poseMaxSpeed);
        motorSpeed = Mathf.Clamp(motorSpeed, -speedLimit, speedLimit);

        JointMotor2D motor = plan.liveJoint.motor;
        motor.motorSpeed = motorSpeed;
        motor.maxMotorTorque = Mathf.Max(motor.maxMotorTorque, 150f);
        plan.liveJoint.motor = motor;
        plan.liveJoint.useMotor = true;
    }

    private bool TryGetAutoLegTargetWorldRotation(
        LimbPlan plan,
        Vector2 rootTarget,
        float phase,
        bool isSwingLeg,
        bool isSwingContact,
        out float targetRotation)
    {
        targetRotation = 0f;
        if (plan == null || plan.liveBody == null) return false;

        Vector2 footTarget = GetAutoLimbEndTarget(plan, rootTarget, Vector2.zero, isSwingLeg || isSwingContact);
        Vector2 kneeTarget = GetAutoMidJointTarget(plan, rootTarget, footTarget, phase, isSwingLeg);
        Vector2 direction;

        if (IsUpperLeg(plan.liveBody.name))
        {
            direction = kneeTarget - rootTarget;
        }
        else if (IsLowerLeg(plan.liveBody.name))
        {
            direction = footTarget - kneeTarget;
        }
        else if (IsFootLimb(plan.liveBody.name))
        {
            targetRotation = 0f;
            return true;
        }
        else
        {
            return false;
        }

        if (direction.sqrMagnitude < 0.0001f)
            return false;

        targetRotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        return true;
    }

    private void RelaxAutoLegJointMotors()
    {
        foreach (var plan in limbPlans)
        {
            if (!IsAutoLimbPlan(plan) || plan.liveJoint == null) continue;

            JointMotor2D motor = plan.liveJoint.motor;
            if (Mathf.Abs(motor.motorSpeed) <= 0.001f) continue;

            motor.motorSpeed = 0f;
            plan.liveJoint.motor = motor;
        }
    }

    private Vector2 GetAutoWalkFootTarget(LimbPlan plan, bool isSwingFoot)
    {
        return GetAutoLimbEndTarget(plan, autoWalkTargetRoot, Vector2.zero, isSwingFoot);
    }

    private Vector2 GetAutoLimbBodyTarget(
        LimbPlan plan,
        Vector2 rootTarget,
        Vector2 rootDelta,
        float phase,
        bool isSwingLeg,
        bool isSwingContact)
    {
        Vector2 endTarget = GetAutoLimbEndTarget(plan, rootTarget, rootDelta, isSwingLeg || isSwingContact);
        Vector2 midTarget = GetAutoMidJointTarget(plan, rootTarget, endTarget, phase, isSwingLeg);

        if (IsFootLimb(plan.liveBody.name) || IsHandLimb(plan.liveBody.name) || IsGroundContactLegPlan(plan) || IsGroundContactArmPlan(plan))
            return endTarget;

        if (IsLowerLeg(plan.liveBody.name) || IsLowerArm(plan.liveBody.name))
            return Vector2.Lerp(midTarget, endTarget, 0.5f);

        if (IsUpperLeg(plan.liveBody.name) || IsUpperArm(plan.liveBody.name))
            return Vector2.Lerp(rootTarget, midTarget, 0.5f);

        return plan.frozenBody.position;
    }

    private Vector2 GetAutoLimbEndTarget(LimbPlan plan, Vector2 rootTarget, Vector2 rootDelta, bool isSwingContact)
    {
        LimbPlan contactPlan = FindGroundContactPlanForSide(plan);
        
        // If this limb is planted and NOT swinging, it MUST stay exactly at its anchor.
        if (!isSwingContact && contactPlan != null && TryGetAutoLimbPlantedTarget(contactPlan.liveBody, out Vector2 plantedTarget))
            return plantedTarget;

        float direction = Mathf.Abs(rootDelta.x) > 0.001f ? Mathf.Sign(rootDelta.x) : autoWalkDirection;
        if (Mathf.Abs(direction) < 0.001f)
            direction = 1f;

        bool isLeg = IsLegLimb(plan.liveBody.name);
        bool hasTravel = autoWalkActive && isLeg;
        float targetX = rootTarget.x;
        float side = GetLegSideSign(plan);
        
        // Calculate max reachable horizontal distance to ensure limb mathematically stays on the floor
        float maxReach = walkFootSpacing; 
        
        if (TryGetGroundPoint(rootTarget, out Vector2 centerGround))
        {
            float hipHeight = rootTarget.y - centerGround.y;
            // Overestimate by 1% to prevent float precision hovering when fully extended
            float effectiveLegLength = walkMaxHipGroundDistance * 1.01f;
            float maxReachSq = (effectiveLegLength * effectiveLegLength) - (hipHeight * hipHeight);
            maxReach = maxReachSq > 0f ? Mathf.Sqrt(maxReachSq) : 0f;
        }

        if (hasTravel)
        {
            if (isSwingContact)
            {
                // Small hip edits keep the swing foot close to its natural side.
                // Larger committed moves progressively let that foot land farther
                // in the travel direction, up to the current physically reachable arc.
                float neutralSpacing = Mathf.Min(
                    Mathf.Max(walkFootSpacing * 0.5f, minAutoFootSeparation * 0.5f),
                    maxReach);
                float neutralTargetX = rootTarget.x + side * neutralSpacing;

                float strideProgress = GetAutoWalkStrideProgress(rootTarget, rootDelta);
                float leadReach = Mathf.Min(walkFootSpacing + walkStepLead, maxReach);
                float committedReach = Mathf.Lerp(leadReach, maxReach, strideProgress);
                float directionalTargetX = rootTarget.x + direction * Mathf.Max(neutralSpacing, committedReach);

                targetX = Mathf.Lerp(neutralTargetX, directionalTargetX, strideProgress);
            }
            else
            {
                // Unplanted trailing foot
                float desiredStep = walkFootSpacing + walkStepLead * 0.35f;
                desiredStep = Mathf.Min(desiredStep, maxReach);
                targetX -= direction * desiredStep;
            }
        }
        else
        {
            // Idle standing: keep limbs separated anatomically
            float spacing = isLeg ? walkFootSpacing : 1.2f;
            float desiredStep = Mathf.Max(spacing * 0.5f, minAutoFootSeparation * 0.5f);
            desiredStep = Mathf.Min(desiredStep, maxReach);
            targetX += side * desiredStep;
        }

        Vector2 target = new Vector2(targetX, rootTarget.y);

        if (TryGetGroundPoint(target, out Vector2 groundPoint))
        {
            Rigidbody2D gb = contactPlan != null ? contactPlan.liveBody : plan.liveBody;
            target.y = groundPoint.y + GetBodyGroundCenterOffset(gb);
        }
        
        return target;
    }

    private float GetAutoWalkStrideProgress(Vector2 rootTarget, Vector2 rootDelta)
    {
        float horizontalTravel = Mathf.Abs(rootDelta.x);
        if (horizontalTravel <= 0.0001f
            && currentSnapshot != null
            && rootBodyIndex >= 0
            && rootBodyIndex < currentSnapshot.Length)
        {
            horizontalTravel = Mathf.Abs(rootTarget.x - currentSnapshot[rootBodyIndex].position.x);
        }

        float startTravel = Mathf.Max(0f, walkMinHipTravel);
        float strideTravelLimit = walkMaxStrideDistance;
        if (limitFrozenRootMovement && maxRootMovePerSnapshot > 0f)
            strideTravelLimit = Mathf.Min(strideTravelLimit, maxRootMovePerSnapshot);

        float fullTravel = Mathf.Max(startTravel + 0.001f, strideTravelLimit);
        return Mathf.InverseLerp(startTravel, fullTravel, horizontalTravel);
    }

    private bool TryGetAutoLimbPlantedTarget(Rigidbody2D contactBody, out Vector2 plantedTarget)
    {
        plantedTarget = Vector2.zero;

        if (contactBody == null)
            return false;

        if (frozenRootEditActive && IsLegLimb(contactBody.name))
        {
            return frozenRootEditPlantedLegTargets.TryGetValue(contactBody, out plantedTarget);
        }

        return plantedFootTargets.TryGetValue(contactBody, out plantedTarget);
    }


    private Vector2 GetAutoMidJointTarget(LimbPlan plan, Vector2 rootTarget, Vector2 footTarget, float phase, bool isSwingLeg)
    {
        float side = GetLegSideSign(plan);
        float legRange = Mathf.Max(0.01f, walkMaxHipGroundDistance);
        float halfLeg = legRange * 0.5f; // approximate thigh ≈ calf

        float distance = Vector2.Distance(rootTarget, footTarget);
        distance = Mathf.Min(distance, legRange * 0.98f);

        // 2-bone IK triangle: compute how far the knee must be from the hip-foot line
        float halfDist = distance * 0.5f;
        float heightSq = halfLeg * halfLeg - halfDist * halfDist;
        float ikHeight = heightSq > 0f ? Mathf.Sqrt(heightSq) : 0f;

        Vector2 midpoint = Vector2.Lerp(rootTarget, footTarget, 0.5f);

        // Compute the perpendicular to the hip→foot direction.
        // For a 2D side-view: when standing upright (hips above feet),
        // the hip-foot line is mostly vertical, so the perpendicular is
        // mostly horizontal (knees go forward). When crouching (hips near
        // feet height), the line is more horizontal, so the perpendicular
        // is mostly vertical (knees go up). This gives natural bend in
        // all poses instead of always pushing the knee upward.
        Vector2 hipToFoot = footTarget - rootTarget;
        Vector2 perpendicular = Vector2.zero;
        
        if (hipToFoot.sqrMagnitude > 0.0001f)
        {
            Vector2 perp = new Vector2(-hipToFoot.y, hipToFoot.x).normalized;
            
            // Calculate natural bend direction from the live bodies
            float bendSign = GetLimbBendDirection(plan);
            if (bendSign < 0f)
                perp = -perp;
                
            perpendicular = perp;
        }

        // Small lateral offset just for visual separation between left/right legs
        float crouch = Mathf.InverseLerp(legRange * 0.9f, legRange * 0.2f, distance);
        float outward = autoKneeOutwardOffset * 0.3f * (1f - crouch * 0.5f);
        Vector2 kneeOffset = new Vector2(side * outward, 0f);
        Vector2 kneePos = midpoint + perpendicular * ikHeight + kneeOffset;

        // Force a frog squat when crouching so the knees don't go backwards into the floor!
        if (crouch > 0.01f)
        {
            Vector2 frogPerp = new Vector2(side, 1f).normalized;
            perpendicular = Vector2.Lerp(perpendicular, frogPerp, crouch).normalized;
            kneePos = midpoint + perpendicular * ikHeight + kneeOffset;
        }

        bool hasPlantedSupportContact = HasPlantedGroundContactForSide(plan);
        if (IsLegLimb(plan.liveBody.name) && (isSwingLeg || !hasPlantedSupportContact))
            kneePos = ChooseLegKneeTargetAwayFromGround(midpoint, perpendicular, ikHeight, kneeOffset, kneePos);

        // Swing leg lift during walking
        if (isSwingLeg)
            kneePos.y += walkStepLift * 0.25f * Mathf.Sin(phase * Mathf.PI);

        if (TryGetGroundPoint(kneePos, out Vector2 groundPoint))
        {
            if (kneePos.y < groundPoint.y + 0.15f)
                kneePos.y = groundPoint.y + 0.15f;
        }

        return kneePos;
    }

    private Vector2 ChooseLegKneeTargetAwayFromGround(
        Vector2 midpoint,
        Vector2 perpendicular,
        float ikHeight,
        Vector2 kneeOffset,
        Vector2 preferredKnee)
    {
        if (ikHeight <= 0.0001f || perpendicular.sqrMagnitude <= 0.0001f)
            return preferredKnee;

        Vector2 oppositeKnee = midpoint - perpendicular * ikHeight + kneeOffset;
        float preferredClearance = GetGroundClearanceAt(preferredKnee);
        float oppositeClearance = GetGroundClearanceAt(oppositeKnee);

        const float floorClearanceBias = 0.05f;
        return oppositeClearance > preferredClearance + floorClearanceBias
            ? oppositeKnee
            : preferredKnee;
    }

    private float GetGroundClearanceAt(Vector2 position)
    {
        if (!TryGetGroundPoint(position, out Vector2 groundPoint))
            return position.y;

        return position.y - groundPoint.y;
    }

    private bool HasPlantedGroundContactForSide(LimbPlan plan)
    {
        if (plan == null || plan.liveBody == null) return false;

        LimbPlan contactPlan = FindGroundContactPlanForSide(plan);
        Vector2 plantedTarget;
        return contactPlan != null
            && contactPlan.liveBody != null
            && TryGetAutoLimbPlantedTarget(contactPlan.liveBody, out plantedTarget);
    }

    private float GetLimbBendDirection(LimbPlan plan)
    {
        bool isLeg = IsLegLimb(plan.liveBody.name);
        LimbPlan upperPlan = isLeg ? FindUpperLegPlanForSide(plan) : FindUpperArmPlanForSide(plan);
        LimbPlan endPlan = isLeg ? FindFootPlanForSide(plan) : FindHandPlanForSide(plan);

        if (upperPlan != null && endPlan != null)
        {
            // Find the lower limb body to act as the knee/elbow
            foreach (var p in limbPlans)
            {
                if (p == null || p.liveBody == null) continue;
                bool isMid = isLeg ? IsLowerLeg(p.liveBody.name) : IsLowerArm(p.liveBody.name);
                if (isMid && IsSameLegSide(p, plan))
                {
                    Vector2 hip = upperPlan.liveBody.position;
                    Vector2 foot = endPlan.liveBody.position;
                    Vector2 knee = p.liveBody.position;
                    
                    Vector2 hipToFoot = foot - hip;
                    Vector2 hipToKnee = knee - hip;
                    float cross = hipToFoot.x * hipToKnee.y - hipToFoot.y * hipToKnee.x;
                    
                    return cross < 0f ? -1f : 1f;
                }
            }
        }
        return 1f;
    }

    private void CachePlantedFootTargets()
    {
        plantedFootTargets.Clear();
        if (!enablePlantedFootStability) return;

        LimbPlan lowestPlan = null;
        float lowestDist = float.MaxValue;
        Vector2 lowestFootStart = Vector2.zero;
        Vector2 lowestGroundPoint = Vector2.zero;

        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null || plan.frozenBody == null) continue;
            
            bool isLeg = IsGroundContactLegPlan(plan);
            bool isArm = IsGroundContactArmPlan(plan);
            
            if (!isLeg && !isArm) continue;
            if (plan.intent != null && plan.intent.IsActiveStrike) continue;

            // Arms must be in Auto mode to plant
            if (isArm && (plan.intent == null || plan.intent.mode != LimbMoveIntent.MoveMode.Auto)) continue;

            Vector2 footStart = currentSnapshot != null && plan.bodyIndex >= 0 && plan.bodyIndex < currentSnapshot.Length
                ? currentSnapshot[plan.bodyIndex].position
                : (Vector2)plan.frozenBody.position;

            if (TryGetGroundPoint(footStart, out Vector2 groundPoint))
            {
                float groundCenterOffset = GetBodyGroundCenterOffset(plan.liveBody);
                float footDistance = Mathf.Abs(footStart.y - (groundPoint.y + groundCenterOffset));
                
                if (footDistance <= plantedFootGroundTolerance)
                {
                    plantedFootTargets[plan.liveBody] = new Vector2(footStart.x, groundPoint.y + groundCenterOffset);
                }

                if (isLeg && footDistance < lowestDist)
                {
                    lowestDist = footDistance;
                    lowestPlan = plan;
                    lowestFootStart = footStart;
                    lowestGroundPoint = groundPoint;
                }
            }
        }

        // Failsafe: if no leg contact was close enough to the ground, forcefully plant the lowest foot.
        // Auto hands may also be planted, but they should never satisfy the grounded-walk foot support rule.
        if (!HasPlantedGroundContactLegTarget() && lowestPlan != null)
        {
            float groundCenterOffset = GetBodyGroundCenterOffset(lowestPlan.liveBody);
            plantedFootTargets[lowestPlan.liveBody] = new Vector2(lowestFootStart.x, lowestGroundPoint.y + groundCenterOffset);
        }

        ConfigureAutoWalkPlan();
    }

    private void CacheFrozenRootEditPlantedLegTargets()
    {
        frozenRootEditPlantedLegTargets.Clear();
        if (!enablePlantedFootStability) return;

        LimbPlan lowestPlan = null;
        float lowestDist = float.MaxValue;
        Vector2 lowestFootStart = Vector2.zero;
        Vector2 lowestGroundPoint = Vector2.zero;

        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null || plan.frozenBody == null) continue;
            if (!IsGroundContactLegPlan(plan)) continue;
            if (plan.intent != null && plan.intent.IsActiveStrike) continue;

            Vector2 footStart = currentSnapshot != null && plan.bodyIndex >= 0 && plan.bodyIndex < currentSnapshot.Length
                ? currentSnapshot[plan.bodyIndex].position
                : (Vector2)plan.frozenBody.position;

            if (!TryGetGroundPoint(footStart, out Vector2 groundPoint)) continue;

            float groundCenterOffset = GetBodyGroundCenterOffset(plan.liveBody);
            float footDistance = Mathf.Abs(footStart.y - (groundPoint.y + groundCenterOffset));

            if (footDistance <= plantedFootGroundTolerance)
                frozenRootEditPlantedLegTargets[plan.liveBody] = new Vector2(footStart.x, groundPoint.y + groundCenterOffset);

            if (footDistance < lowestDist)
            {
                lowestDist = footDistance;
                lowestPlan = plan;
                lowestFootStart = footStart;
                lowestGroundPoint = groundPoint;
            }
        }

        if (!HasPlantedGroundContactLegTarget(frozenRootEditPlantedLegTargets) && lowestPlan != null)
        {
            float groundCenterOffset = GetBodyGroundCenterOffset(lowestPlan.liveBody);
            frozenRootEditPlantedLegTargets[lowestPlan.liveBody] = new Vector2(lowestFootStart.x, lowestGroundPoint.y + groundCenterOffset);
        }
    }

    /// <summary>
    /// Intent-based foot detachment + replanting: before joints are created,
    /// compare each planted foot's snapshot position to the frozen copy's
    /// current foot position. If the frozen foot has moved beyond the detach
    /// threshold, remove it from plantedFootTargets (so no FixedJoint2D is
    /// created) and optionally create a TargetJoint2D that pulls the foot
    /// toward its new ground-projected frozen target during simulation.
    /// </summary>
    private void PreReleasePlantedFeetByFrozenTarget()
    {
        ClearReplantingFootJoints();
        if (plantedFootDetachThreshold <= 0f) return;
        if (!HasPlantedGroundContactLegTarget()) return;

        // Collect feet to release + their new targets
        List<Rigidbody2D> toRelease = null;
        Dictionary<Rigidbody2D, Vector2> newTargets = null;

        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null || plan.frozenBody == null) continue;
            if (!IsGroundContactLegPlan(plan)) continue;
            if (!plantedFootTargets.TryGetValue(plan.liveBody, out Vector2 plantedPos)) continue;

            // Compare the frozen copy's current foot position to the planted anchor
            Vector2 frozenFootPos = (Vector2)plan.frozenBody.position;
            float distance = Vector2.Distance(frozenFootPos, plantedPos);

            if (distance > plantedFootDetachThreshold)
            {
                if (toRelease == null)
                {
                    toRelease = new List<Rigidbody2D>();
                    newTargets = new Dictionary<Rigidbody2D, Vector2>();
                }
                toRelease.Add(plan.liveBody);

                // Compute the new replant target — project frozen foot to the ground surface
                Vector2 replantTarget = frozenFootPos;
                if (TryGetGroundPoint(frozenFootPos, out Vector2 groundPoint))
                {
                    float groundOffset = GetBodyGroundCenterOffset(plan.liveBody);
                    replantTarget = new Vector2(frozenFootPos.x, groundPoint.y + groundOffset);
                }
                newTargets[plan.liveBody] = replantTarget;
            }
        }

        if (toRelease == null) return;

        foreach (var body in toRelease)
        {
            Vector2 replantTarget = newTargets[body];
            float logDist = Vector2.Distance(replantTarget, plantedFootTargets[body]);
            plantedFootTargets.Remove(body);

            // Create a TargetJoint2D to pull the foot toward its new position
            if (enableFootReplanting)
            {
                CreateReplantingFootJoint(body, replantTarget);
                if (logFootReplantingDetails)
                    Debug.Log($"[FreezeReplayV2] Replanting foot '{body.name}': pulling to new target {logDist:F2} away (threshold={plantedFootDetachThreshold:F2})");
            }
            else
            {
                if (logFootReplantingDetails)
                    Debug.Log($"[FreezeReplayV2] Pre-released planted foot '{body.name}': frozen target moved {logDist:F2} from planted position (threshold={plantedFootDetachThreshold:F2})");
            }
        }
    }

    private void CreateReplantingFootJoint(Rigidbody2D body, Vector2 target)
    {
        if (body == null) return;

        // Remove any existing replanting joint for this body
        if (replantingFootJoints.TryGetValue(body, out TargetJoint2D existing) && existing != null)
        {
            existing.enabled = false;
            Destroy(existing);
        }

        TargetJoint2D joint = body.gameObject.AddComponent<TargetJoint2D>();
        joint.autoConfigureTarget = false;
        joint.target = target;
        joint.anchor = Vector2.zero;
        joint.frequency = Mathf.Max(0.01f, replantFootFrequency);
        joint.dampingRatio = Mathf.Clamp01(replantFootDamping);
        joint.maxForce = Mathf.Max(0f, replantFootMaxForce);

        replantingFootJoints[body] = joint;
        replantingFootTargets[body] = target;
    }

    private void ClearReplantingFootJoints()
    {
        foreach (var kvp in replantingFootJoints)
        {
            TargetJoint2D joint = kvp.Value;
            if (joint == null) continue;
            joint.enabled = false;
            Destroy(joint);
        }
        replantingFootJoints.Clear();
        replantingFootTargets.Clear();
    }

    /// <summary>
    /// During simulation, check if any replanting feet have arrived at their target.
    /// If so, convert them to planted FixedJoint2D anchors and remove the pull joint.
    /// </summary>
    private void CheckReplantingFootArrival()
    {
        if (replantingFootJoints.Count == 0) return;

        List<Rigidbody2D> arrived = null;

        foreach (var kvp in replantingFootJoints)
        {
            Rigidbody2D body = kvp.Key;
            TargetJoint2D joint = kvp.Value;
            if (body == null || joint == null) continue;

            if (!replantingFootTargets.TryGetValue(body, out Vector2 target)) continue;

            float dist = Vector2.Distance(body.position, target);
            if (dist <= replantFootArrivalDistance)
            {
                if (arrived == null)
                    arrived = new List<Rigidbody2D>();
                arrived.Add(body);
            }
        }

        if (arrived == null) return;

        foreach (var body in arrived)
        {
            Vector2 target = replantingFootTargets[body];

            // Destroy the pull joint
            if (replantingFootJoints.TryGetValue(body, out TargetJoint2D pullJoint) && pullJoint != null)
            {
                pullJoint.enabled = false;
                Destroy(pullJoint);
            }
            replantingFootJoints.Remove(body);
            replantingFootTargets.Remove(body);

            // Create a planted FixedJoint2D at the arrival position
            if (usePlantedFootJoints)
            {
                FixedJoint2D planted = body.gameObject.AddComponent<FixedJoint2D>();
                planted.autoConfigureConnectedAnchor = false;
                planted.anchor = Vector2.zero;
                planted.connectedBody = null;
                planted.connectedAnchor = target;
                planted.enableCollision = false;
                planted.frequency = Mathf.Max(0f, plantedFootJointFrequency);
                planted.dampingRatio = Mathf.Clamp01(plantedFootJointDampingRatio);
                planted.breakForce = Mathf.Max(0f, plantedFootJointBreakForce);
                planted.breakTorque = Mathf.Max(0f, plantedFootJointBreakTorque);
                plantedFootJoints[body] = planted;

                if (logFootReplantingDetails)
                    Debug.Log($"[FreezeReplayV2] Foot '{body.name}' arrived at replant target — converted to planted FixedJoint2D.");
            }
        }
    }

    /// <summary>
    /// Find the frozen copy transform that corresponds to a live body.
    /// </summary>
    private Transform GetFrozenTransformForBody(Rigidbody2D liveBody)
    {
        if (liveBody == null) return null;

        foreach (var plan in limbPlans)
        {
            if (plan != null && plan.liveBody == liveBody)
                return plan.frozenBody;
        }

        // Fallback: find by tracked body index
        for (int i = 0; i < trackedBodies.Count && i < frozenChildren.Length; i++)
        {
            if (trackedBodies[i] == liveBody)
                return frozenChildren[i];
        }

        return null;
    }

    private void ApplyPlantedFootStability()
    {
        if (!enablePlantedFootStability || plantedFootTargets.Count == 0) return;

        foreach (var kvp in plantedFootTargets)
        {
            Rigidbody2D rb = kvp.Key;
            if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) continue;
            if (autoWalkActive && rb == autoWalkSwingFoot) continue;
            if (HasActivePlantedFootJoint(rb)) continue;
            if (usePlantedFootJoints && !usePlantedFootSpringFallback) continue;

            Vector2 error = kvp.Value - rb.position;
            Vector2 force;
            if (error.sqrMagnitude <= plantedFootDeadzone * plantedFootDeadzone)
            {
                if (rb.linearVelocity.sqrMagnitude <= plantedFootVelocityDeadzone * plantedFootVelocityDeadzone)
                    continue;

                force = -rb.linearVelocity * plantedFootDamping;
            }
            else
            {
                force = error * plantedFootSpring - rb.linearVelocity * plantedFootDamping;
            }

            rb.AddForce(Vector2.ClampMagnitude(force, maxPlantedFootForce), ForceMode2D.Force);
        }
    }

    private void CreatePlantedFootJoints()
    {
        ClearPlantedFootJoints();
        if (!enablePlantedFootStability || !usePlantedFootJoints) return;

        foreach (var kvp in plantedFootTargets)
        {
            Rigidbody2D rb = kvp.Key;
            if (rb == null) continue;
            if (autoWalkActive && rb == autoWalkSwingFoot) continue;

            FixedJoint2D joint = rb.gameObject.AddComponent<FixedJoint2D>();
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector2.zero;
            joint.connectedBody = null;
            joint.connectedAnchor = kvp.Value;
            joint.enableCollision = false;
            joint.frequency = Mathf.Max(0f, plantedFootJointFrequency);
            joint.dampingRatio = Mathf.Clamp01(plantedFootJointDampingRatio);
            joint.breakForce = Mathf.Max(0f, plantedFootJointBreakForce);
            joint.breakTorque = Mathf.Max(0f, plantedFootJointBreakTorque);

            plantedFootJoints[rb] = joint;
        }
    }

    private bool HasActivePlantedFootJoint(Rigidbody2D body)
    {
        if (body == null) return false;

        if (!plantedFootJoints.TryGetValue(body, out FixedJoint2D joint))
            return false;

        if (joint != null)
            return true;

        plantedFootJoints.Remove(body);
        return false;
    }

    private void ReleasePlantedFootJoint(Rigidbody2D body)
    {
        if (body == null) return;
        if (!plantedFootJoints.TryGetValue(body, out FixedJoint2D joint)) return;

        if (joint != null)
        {
            joint.enabled = false;
            Destroy(joint);
        }

        plantedFootJoints.Remove(body);
    }

    private void ClearPlantedFootJoints()
    {
        foreach (var kvp in plantedFootJoints)
        {
            FixedJoint2D joint = kvp.Value;
            if (joint == null) continue;

            joint.enabled = false;
            Destroy(joint);
        }

        plantedFootJoints.Clear();
    }

    private void ConfigureAutoWalkPlan()
    {
        autoWalkActive = false;
        autoWalkSwingFoot = null;

        if (isJumpNextSimulation) return;

        if (!enableAutoWalkFromHipTarget) return;
        if (!HasPlantedGroundContactLegTarget()) return;
        if (currentSnapshot == null || rootBodyIndex < 0 || rootBodyIndex >= currentSnapshot.Length) return;
        if (rootBodyIndex >= frozenChildren.Length || frozenChildren[rootBodyIndex] == null) return;

        Vector2 startRoot = currentSnapshot[rootBodyIndex].position;
        autoWalkTargetRoot = frozenChildren[rootBodyIndex].position;
        float horizontalTravel = autoWalkTargetRoot.x - startRoot.x;
        if (Mathf.Abs(horizontalTravel) < walkMinHipTravel) return;

        if (!TryGetGroundPoint(autoWalkTargetRoot, out Vector2 groundPoint)) return;

        float hipHeight = autoWalkTargetRoot.y - groundPoint.y;
        float legRange = walkMaxHipGroundDistance * 1.15f;
        if (hipHeight < 0f || hipHeight > legRange) return;

        autoWalkDirection = Mathf.Sign(horizontalTravel);
        autoWalkSwingFoot = ChooseAutoWalkSwingFoot(autoWalkDirection, plantedFootTargets);
        autoWalkActive = autoWalkSwingFoot != null;
        if (autoWalkActive)
            ReleasePlantedFootJoint(autoWalkSwingFoot);
    }

    private Rigidbody2D ChooseAutoWalkSwingFoot(float direction, Dictionary<Rigidbody2D, Vector2> plantedTargets)
    {
        Rigidbody2D firstUnplanted = null;
        Rigidbody2D plantedSwingChoice = null;
        float furthestBackwardsScore = float.MinValue;
        int plantedLegCount = 0;

        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null) continue;
            if (!IsGroundContactLegPlan(plan)) continue;

            Vector2 plantedPos = Vector2.zero;
            bool isPlantedLeg = plantedTargets != null && plantedTargets.TryGetValue(plan.liveBody, out plantedPos);
            if (!isPlantedLeg && firstUnplanted == null)
                firstUnplanted = plan.liveBody;

            if (!isPlantedLeg) continue;

            plantedLegCount++;

            // Score based on how far backwards the planted foot is relative to travel direction.
            // If moving right (+1), we want the leftmost foot (min X) -> score = -X.
            // If moving left (-1), we want the rightmost foot (max X) -> score = X.
            float footX = plantedPos.x;
            float score = -footX * direction;
            if (score > furthestBackwardsScore)
            {
                furthestBackwardsScore = score;
                plantedSwingChoice = plan.liveBody;
            }
        }

        if (firstUnplanted != null)
            return firstUnplanted;

        // Do not consume the last grounded support foot. If only one planted leg exists,
        // the copy should keep that foot down instead of inventing a supportless step.
        return plantedLegCount > 1 ? plantedSwingChoice : null;
    }

    private bool HasPlantedGroundContactLegTarget()
    {
        return HasPlantedGroundContactLegTarget(plantedFootTargets);
    }

    private bool HasPlantedGroundContactLegTarget(Dictionary<Rigidbody2D, Vector2> plantedTargets)
    {
        if (plantedTargets == null || plantedTargets.Count == 0) return false;

        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null) continue;
            if (!IsGroundContactLegPlan(plan)) continue;
            if (plantedTargets.ContainsKey(plan.liveBody))
                return true;
        }

        return false;
    }

    private LimbPlan FindGroundContactPlanForSide(LimbPlan sideSource)
    {
        bool isLeg = IsLegLimb(sideSource.liveBody.name);
        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null) continue;
            if (isLeg)
            {
                if (!IsGroundContactLegPlan(plan)) continue;
            }
            else
            {
                if (!IsGroundContactArmPlan(plan)) continue;
            }

            if (IsSameLegSide(plan, sideSource))
                return plan;
        }
        return null;
    }

    private float CalculateAnatomicalLegLength()
    {
        if (limbPlans == null || rootBodyIndex < 0 || rootBodyIndex >= trackedBodies.Count) return 3.5f;

        Rigidbody2D rootRb = trackedBodies[rootBodyIndex];
        float maxLength = 0f;

        foreach (var plan in limbPlans)
        {
            if (!IsGroundContactLegPlan(plan)) continue;

            LimbPlan footPlan = plan;
            LimbPlan upperPlan = FindUpperLegPlanForSide(plan);
            LimbPlan lowerPlan = null;
            
            foreach (var p in limbPlans)
            {
                if (p != null && p.liveBody != null && IsLowerLeg(p.liveBody.name) && IsSameLegSide(p, plan))
                {
                    lowerPlan = p;
                    break;
                }
            }

            if (upperPlan != null && lowerPlan != null && footPlan != null)
            {
                // Measure exact distance between the joints' rigidbodies at start
                float upperDist = Vector2.Distance(rootRb.position, lowerPlan.liveBody.position);
                float lowerDist = Vector2.Distance(lowerPlan.liveBody.position, footPlan.liveBody.position);
                float totalLength = upperDist + lowerDist;
                if (totalLength > maxLength) maxLength = totalLength;
            }
        }

        return maxLength > 0f ? maxLength : 3.5f;
    }

    private LimbPlan FindFootPlanForSide(LimbPlan sideSource)
    {
        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null) continue;
            if (!IsFootLimb(plan.liveBody.name)) continue;
            if (IsSameLegSide(plan, sideSource))
                return plan;
        }

        return null;
    }

    private LimbPlan FindHandPlanForSide(LimbPlan sideSource)
    {
        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null) continue;
            if (!IsHandLimb(plan.liveBody.name)) continue;
            if (IsSameLegSide(plan, sideSource))
                return plan;
        }

        return null;
    }

    private LimbPlan FindUpperArmPlanForSide(LimbPlan sideSource)
    {
        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null) continue;
            if (!IsUpperArm(plan.liveBody.name)) continue;
            if (IsSameLegSide(plan, sideSource))
                return plan;
        }

        return null;
    }

    private bool IsSameLegSide(LimbPlan a, LimbPlan b)
    {
        if (a == null || b == null || a.liveBody == null || b.liveBody == null) return false;
        return IsLeftSide(a) == IsLeftSide(b);
    }

    private bool IsSameLegSide(LimbPlan plan, Rigidbody2D body)
    {
        if (plan == null || plan.liveBody == null || body == null) return false;
        return IsLeftSide(plan) == IsLeftSide(body);
    }

    private Vector2 ProjectFootTargetToGround(Vector2 target)
    {
        if (TryGetGroundPoint(target, out Vector2 groundPoint))
            target.y = groundPoint.y + footGroundOffset;

        return target;
    }

    private float GetBodyGroundCenterOffset(Rigidbody2D body)
    {
        if (body == null) return footGroundOffset;

        float offset = footGroundOffset;
        Collider2D[] colliders = body.GetComponents<Collider2D>();
        foreach (var col in colliders)
        {
            if (col == null || !col.enabled) continue;
            offset = Mathf.Max(offset, col.bounds.extents.y);
        }

        return offset;
    }

    public bool TryGetGroundPoint(Vector2 target, out Vector2 groundPoint)
    {
        float probeDist = Mathf.Max(groundProbeDistance, 30f);
        Vector2 rayStart = target + Vector2.up * groundProbeHeight;
        return TryGetGroundPointFromProbe(rayStart, groundProbeHeight, probeDist, out groundPoint);
    }

    private bool TryGetGroundPointFromProbe(Vector2 rayStart, float probeHeight, float probeDistance, out Vector2 groundPoint)
    {
        float rayDistance = Mathf.Max(0f, probeHeight) + Mathf.Max(0.01f, probeDistance);
        if (TryGetGroundPointFromHits(Physics2D.RaycastAll(rayStart, Vector2.down, rayDistance, groundMask.value), out groundPoint))
            return true;

        if (fallbackGroundProbeToAllLayers
            && TryGetGroundPointFromHits(Physics2D.RaycastAll(rayStart, Vector2.down, rayDistance), out groundPoint))
        {
            return true;
        }

        groundPoint = rayStart - Vector2.up * Mathf.Max(0f, probeHeight);
        return false;
    }

    private bool TryGetGroundPointFromHits(RaycastHit2D[] hits, out Vector2 groundPoint)
    {
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.isTrigger) continue;
            if (playerObj != null && hit.collider.transform.IsChildOf(playerObj.transform)) continue;
            if (frozenCopy != null && hit.collider.transform.IsChildOf(frozenCopy.transform)) continue;
            if (hit.collider.GetComponentInParent<FighterHealth>() != null) continue;

            groundPoint = hit.point;
            return true;
        }

        groundPoint = Vector2.zero;
        return false;
    }

    /// <summary>
    /// Velocity-continuation jump trigger.
    /// During the pre-trigger phase, pose assist and motors drive the fighter's hips
    /// upward toward the frozen copy, naturally building velocity through leg extension.
    /// When legs reach full extension, this method:
    ///   1. Captures the root's existing velocity (built by the extension)
    ///   2. Releases foot joints so the fighter leaves the ground
    ///   3. Applies a modest directional boost that continues the existing motion
    ///   4. Sets forceAirborneMode so the fighter flies freely
    /// </summary>
    private void CheckJumpTrigger()
    {
        if (trackedBodies == null || rootBodyIndex < 0 || rootBodyIndex >= trackedBodies.Count) return;
        Rigidbody2D liveRoot = trackedBodies[rootBodyIndex];
        
        float maxExtension = 0f;
        int plantedLegs = 0;
        foreach (var plan in limbPlans)
        {
            if (IsGroundContactLegPlan(plan))
            {
                float dist = Vector2.Distance(liveRoot.position, plan.liveBody.position);
                if (dist > maxExtension) maxExtension = dist;
                plantedLegs++;
            }
        }

        bool triggered = false;
        if (plantedLegs > 0)
        {
            if (maxExtension >= walkMaxHipGroundDistance * jumpExtensionThreshold)
                triggered = true;
        }
        else
        {
            triggered = true; // No feet planted, already airborne or fell off ledge
        }

        if (triggered)
        {
            hasJumpTriggered = true;
            forceAirborneMode = true; // Transition to airborne — next FixedUpdate disables ground systems
            
            // Force legs out of Auto mode so they don't try to plant themselves mid-air
            foreach (var plan in limbPlans)
            {
                if (plan != null && plan.intent != null && IsLegLimb(plan.liveBody.name))
                {
                    if (plan.intent.mode == LimbMoveIntent.MoveMode.Auto)
                        plan.intent.mode = LimbMoveIntent.MoveMode.BasicBlock;
                }
            }
            
            // Release planted foot joints so the fighter can actually leave the ground
            ClearPlantedFootJoints();
            ClearReplantingFootJoints();
            plantedFootTargets.Clear();
            
            // === VELOCITY CONTINUATION ===
            // The fighter has been building upward velocity during leg extension.
            // Capture that velocity and add a directional boost to continue the motion.
            Vector2 currentVelocity = liveRoot.linearVelocity;
            
            Transform frozenRoot = GetFrozenRootTransform();
            Vector2 startRootPos = currentSnapshot != null && rootBodyIndex < currentSnapshot.Length 
                ? currentSnapshot[rootBodyIndex].position 
                : liveRoot.position;

            if (frozenRoot != null)
            {
                // Jump direction from frozen copy offset (normalized)
                Vector2 jumpDirection = ((Vector2)frozenRoot.position - startRootPos).normalized;
                
                // The boost magnitude is proportional to the existing velocity.
                // jumpForceMultiplier acts as a velocity boost factor (e.g., 1.5 = 50% boost).
                // This makes stronger leg extension → faster upward velocity → stronger jump.
                float existingSpeed = currentVelocity.magnitude;
                Vector2 velocityBoost = jumpDirection * (existingSpeed * jumpForceMultiplier);
                
                // Apply the velocity boost to ALL bodies for body coherence.
                // This keeps the fighter's form together during launch.
                for (int i = 0; i < trackedBodies.Count; i++)
                {
                    Rigidbody2D rb = trackedBodies[i];
                    if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) continue;
                    
                    // Set each body's velocity to match root direction + boost
                    // This unifies all body velocities for a cohesive launch
                    float fraction = (i == rootBodyIndex) ? 1f : 0.85f;
                    rb.linearVelocity = (currentVelocity + velocityBoost) * fraction;
                }
                
                Debug.Log($"[FreezeReplayV2] Jump triggered! Velocity: {currentVelocity}, Boost: {velocityBoost} (Ext: {maxExtension:F2}, Speed: {existingSpeed:F2})");
            }
            else
            {
                // No frozen root — just unify velocities with the root's current motion
                for (int i = 0; i < trackedBodies.Count; i++)
                {
                    Rigidbody2D rb = trackedBodies[i];
                    if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) continue;
                    rb.linearVelocity = currentVelocity;
                }
                Debug.Log($"[FreezeReplayV2] Jump triggered (no frozen root)! Velocity: {currentVelocity}");
            }
        }
    }

    /// <summary>
    /// During airborne simulation, raycasts from each foot body.
    /// On ground contact, anchors the foot with a FixedJoint2D to the world-space
    /// hit point. forceAirborneMode stays TRUE so no frozen-copy-driven assist runs.
    /// Intent modes and the frozen copy are NOT modified -- the live fighter settles
    /// via physics + PoseDrivers while anchored feet resist the fall.
    /// </summary>
    private void CheckAirborneLanding()
    {
        if (trackedBodies == null || limbPlans == null) return;
        if (rootBodyIndex < 0 || rootBodyIndex >= trackedBodies.Count) return;

        Rigidbody2D liveRoot = trackedBodies[rootBodyIndex];
        if (liveRoot == null) return;

        // Only detect landing while root is moving downward.
        // Prevents immediate re-trigger right after the jump impulse.
        if (liveRoot.linearVelocity.y > 0.5f) return;

        bool anyFootLanded = false;

        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null) continue;
            if (!IsFootLimb(plan.liveBody.name) && !IsLowerLeg(plan.liveBody.name)) continue;

            Rigidbody2D footRb = plan.liveBody;

            // Skip if this foot is already anchored.
            if (plantedFootJoints.ContainsKey(footRb)) continue;

            // Probe slightly above the foot to account for collider radius.
            Vector2 probeOrigin = footRb.position + Vector2.up * 0.15f;
            float probeDistance = footGroundOffset + 0.35f;
            RaycastHit2D hit = Physics2D.Raycast(probeOrigin, Vector2.down, probeDistance, groundMask);
            if (hit.collider == null) continue;

            // Nail the foot to the world at the ground contact point.
            // Use the same FixedJoint2D parameters as the normal planted-foot system.
            Vector2 anchorPoint = hit.point + Vector2.up * footGroundOffset;
            FixedJoint2D joint = footRb.gameObject.AddComponent<FixedJoint2D>();
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector2.zero;
            joint.connectedBody = null;
            joint.connectedAnchor = anchorPoint;
            joint.enableCollision = false;
            joint.frequency = Mathf.Max(0f, plantedFootJointFrequency);
            joint.dampingRatio = Mathf.Clamp01(plantedFootJointDampingRatio);
            joint.breakForce = Mathf.Max(0f, plantedFootJointBreakForce);
            joint.breakTorque = Mathf.Max(0f, plantedFootJointBreakTorque);
            plantedFootJoints[footRb] = joint;
            plantedFootTargets[footRb] = anchorPoint;

            anyFootLanded = true;
            Debug.Log($"[FreezeReplayV2] Landing: anchored '{footRb.name}' at {anchorPoint}");
        }

        if (anyFootLanded)
        {
            hasLandedThisTurn = true;
        }
    }

    /// <summary>
    /// Compute the rotation delta between live and frozen root, and inject it
    /// into PoseDrivers whose drivenJoint is connected to the root body.
    /// This compensates for joint-space target drift when the root rotates
    /// under gravity during airborne/jump simulation.
    /// </summary>
    private void UpdateAirborneRootRotationOffset(bool isAirborne)
    {
        if (!isAirborne || poseDrivers == null || poseDrivers.Count == 0)
        {
            // Clear offsets when not airborne
            foreach (var driver in poseDrivers)
            {
                if (driver != null)
                    driver.airborneRootRotationOffset = 0f;
            }
            return;
        }
 
        if (rootBodyIndex < 0 || rootBodyIndex >= trackedBodies.Count) return;

        Rigidbody2D liveRoot = trackedBodies[rootBodyIndex];
        Transform frozenRoot = GetFrozenRootTransform();
        if (liveRoot == null || frozenRoot == null) return;

        float liveRootRotation = liveRoot.rotation;
        float frozenRootRotation = frozenRoot.eulerAngles.z;
        float rootDelta = Mathf.DeltaAngle(frozenRootRotation, liveRootRotation);

        foreach (var driver in poseDrivers)
        {
            if (driver == null || driver.drivenJoint == null) continue;

            // Only compensate joints directly connected to the root body.
            // Child-of-child joints (e.g. calf connected to thigh) don't need
            // compensation because their parent (thigh) is already compensated,
            // and their jointAngle is relative to the thigh, not the root.
            Rigidbody2D connectedBody = driver.drivenJoint.connectedBody;
            if (connectedBody != null && connectedBody == liveRoot)
            {
                driver.airborneRootRotationOffset = rootDelta;
            }
            else
            {
                driver.airborneRootRotationOffset = 0f;
            }
        }
    }

    private bool IsLowerLeg(string limbName)
    {
        string n = limbName.ToLowerInvariant();
        return n.Contains("calf") || n.Contains("shin");
    }

    private bool IsFootLimb(string limbName)
    {
        return limbName.ToLowerInvariant().Contains("foot");
    }

    private bool IsUpperLeg(string limbName)
    {
        return limbName.ToLowerInvariant().Contains("thigh");
    }

    private bool IsLowerArm(string limbName)
    {
        string n = limbName.ToLowerInvariant();
        return n.Contains("forearm") || n.Contains("arm 2") || n.Contains("arm2") || n.Contains("lower arm");
    }

    private bool IsHandLimb(string limbName)
    {
        return limbName.ToLowerInvariant().Contains("hand");
    }

    private bool IsUpperArm(string limbName)
    {
        string n = limbName.ToLowerInvariant();
        return n.Contains("upper arm") || n.Contains("arm 1") || n.Contains("arm1");
    }

    private bool IsGroundContactLegPlan(LimbPlan plan)
    {
        if (plan == null || plan.liveBody == null) return false;
        if (IsFootLimb(plan.liveBody.name)) return true;
        return IsLowerLeg(plan.liveBody.name) && FindFootPlanForSide(plan) == null;
    }

    private bool IsGroundContactArmPlan(LimbPlan plan)
    {
        if (plan == null || plan.liveBody == null) return false;
        if (IsHandLimb(plan.liveBody.name)) return true;
        return IsLowerArm(plan.liveBody.name) && FindHandPlanForSide(plan) == null;
    }

    private bool IsLegLimb(string limbName)
    {
        string n = limbName.ToLowerInvariant();
        return n.Contains("thigh") || n.Contains("calf") || n.Contains("foot") || n.Contains("shin");
    }

    private bool IsArmLimb(string limbName)
    {
        string n = limbName.ToLowerInvariant();
        return n.Contains("arm") || n.Contains("hand");
    }

    private float GetLegSideSign(LimbPlan plan)
    {
        return IsLeftSide(plan) ? -1f : 1f;
    }

    private bool IsAutoLegBody(Rigidbody2D body)
    {
        if (body == null) return false;

        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody != body || plan.intent == null) continue;
            return IsLegLimb(body.name) && plan.intent.mode == LimbMoveIntent.MoveMode.Auto;
        }

        return false;
    }

    private bool IsLeftSide(LimbPlan plan)
    {
        if (plan == null || plan.liveBody == null) return true;
        return IsLeftSide(plan.liveBody);
    }

    private bool IsLeftSide(Rigidbody2D body)
    {
        if (body == null) return true;
        return IsLeftSide(GetSideSearchName(body.transform));
    }

    private string GetSideSearchName(Transform bodyTransform)
    {
        if (bodyTransform == null) return string.Empty;

        string names = string.Empty;
        Transform t = bodyTransform;
        while (t != null)
        {
            if (playerObj != null && t.gameObject == playerObj) break;
            if (frozenCopy != null && t.gameObject == frozenCopy) break;

            names += " " + t.name;
            t = t.parent;
        }

        return names;
    }

    private bool IsLeftSide(string limbName)
    {
        string n = limbName.ToLowerInvariant();
        return !(n.Contains("right") || n.EndsWith("2") || n.Contains(" 2") || n.Contains("2 "));
    }

    // ===== MOVE PLANNING UI =====

    private void OnGUI()
    {
        if (!showMovePlanningUI || currentPhase != TurnPhase.Posing || limbPlans.Count == 0) return;

        if (!movePlanningUIStateInitialized)
        {
            movePlanningUIMinimized = moveUIStartMinimized;
            
            if (PlayerPrefs.HasKey("FRV2_ResetCopyKey"))
            {
                string savedKey = PlayerPrefs.GetString("FRV2_ResetCopyKey");
                if (System.Enum.TryParse(savedKey, out KeyCode parsedKey))
                {
                    resetCopyKey = parsedKey;
                }
            }
            resetCopyKeyString = resetCopyKey.ToString();
            
            movePlanningUIStateInitialized = true;
        }

        var filteredPlans = new List<LimbPlan>();
        foreach (var p in limbPlans) 
        {
            if (p == null || p.liveBody == null) continue;
            string n = p.liveBody.name.ToLower();
            if (n.Contains("hand") || n.Contains("foot")) 
            {
                filteredPlans.Add(p);
            }
        }

        float panelWidth = movePlanningUIMinimized
            ? Mathf.Min(moveUIPanelWidth, moveUIMinimizedWidth)
            : moveUIPanelWidth;
        float panelHeight = movePlanningUIMinimized
            ? moveUIMinimizedHeight
            : Mathf.Min(Screen.height - 24f, 64f + filteredPlans.Count * 52f);

        GUILayout.BeginArea(new Rect(moveUIPanelOffset.x, moveUIPanelOffset.y, panelWidth, panelHeight), GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Turn {turnCount + 1} Limb Plan", GUILayout.ExpandWidth(true));
        
        if (!movePlanningUIMinimized)
        {
            if (GUILayout.Button("Reset Copy", GUILayout.Width(80f)))
            {
                ResetFrozenCopy();
            }
            
            resetCopyKeyString = GUILayout.TextField(resetCopyKeyString, GUILayout.Width(30f));
            if (GUI.changed)
            {
                if (System.Enum.TryParse(resetCopyKeyString, true, out KeyCode newKey))
                {
                    resetCopyKey = newKey;
                    PlayerPrefs.SetString("FRV2_ResetCopyKey", resetCopyKey.ToString());
                    PlayerPrefs.Save();
                }
            }

            GUI.backgroundColor = isJumpNextSimulation ? Color.yellow : Color.white;
            if (GUILayout.Button(isJumpNextSimulation ? "LAUNCH READY" : "PREP LAUNCH", GUILayout.Width(100f)))
            {
                isJumpNextSimulation = !isJumpNextSimulation;
                RequestPosePreviewRestart();
            }
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = forceAirborneMode ? new Color(0.2f, 0.8f, 1f) : Color.white;
            if (GUILayout.Button(forceAirborneMode ? "AIRBORNE" : "GROUNDED", GUILayout.Width(80f)))
            {
                forceAirborneMode = !forceAirborneMode;
                RequestPosePreviewRestart();
            }
            GUI.backgroundColor = Color.white;
        }

        if (GUILayout.Button(movePlanningUIMinimized ? "Show" : "Hide", GUILayout.Width(54f)))
            movePlanningUIMinimized = !movePlanningUIMinimized;
        GUILayout.EndHorizontal();

        if (movePlanningUIMinimized)
        {
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label("Live angle -> frozen target");

        foreach (var plan in filteredPlans)
            DrawLimbPlanRow(plan);

        GUILayout.EndArea();
    }

    private void DrawLimbPlanRow(LimbPlan plan)
    {
        if (plan == null || plan.liveBody == null || plan.liveJoint == null || plan.frozenJoint == null) return;

        LimbMoveIntent intent = plan.intent;
        bool isLeg = IsLegLimb(plan.liveBody.name);
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label(plan.liveBody.name, GUILayout.Width(84f));

        bool wasEnabled = GUI.enabled;
        GUI.enabled = intent != null && !intent.IsRecovering;

        if (isLeg)
        {
            bool autoSelected = intent != null && intent.mode == LimbMoveIntent.MoveMode.Auto;
            if (GUILayout.Toggle(autoSelected, "Auto", "Button", GUILayout.Width(58f)) && intent != null)
                QueueIntentForEntireLimb(plan, LimbMoveIntent.MoveMode.Auto);
        }

        bool basicSelected = intent == null || intent.mode == LimbMoveIntent.MoveMode.BasicBlock;
        if (GUILayout.Toggle(basicSelected, "Basic/Block", "Button", GUILayout.Width(96f)) && intent != null)
            QueueIntentForEntireLimb(plan, LimbMoveIntent.MoveMode.BasicBlock);

        bool strikeSelected = intent != null && intent.mode == LimbMoveIntent.MoveMode.Strike;
        if (GUILayout.Toggle(strikeSelected, "Strike", "Button", GUILayout.Width(64f)) && intent != null)
            QueueIntentForEntireLimb(plan, LimbMoveIntent.MoveMode.Strike);

        GUI.enabled = wasEnabled;
        GUILayout.Label(intent != null ? intent.GetDisplayName() : "Basic/Block", GUILayout.Width(88f));
        GUILayout.Label(GetJointDeltaLabel(plan), GUILayout.Width(56f));
        GUILayout.EndHorizontal();

        Rect barRect = GUILayoutUtility.GetRect(moveUIPanelWidth - 28f, 10f);
        DrawJointRangeBar(barRect, plan);
        GUILayout.EndVertical();
    }

    private string GetJointDeltaLabel(LimbPlan plan)
    {
        float delta = Mathf.DeltaAngle(plan.liveJoint.jointAngle, plan.frozenJoint.jointAngle);
        return $"{delta:+0;-0;0} deg";
    }

    private void DrawJointRangeBar(Rect rect, LimbPlan plan)
    {
        float min = -180f;
        float max = 180f;
        if (plan.liveJoint.useLimits && !ignoreJointLimits)
        {
            min = plan.liveJoint.limits.min;
            max = plan.liveJoint.limits.max;
        }

        if (Mathf.Abs(max - min) < 0.01f)
            max = min + 1f;

        float liveAngle = Mathf.Clamp(plan.liveJoint.jointAngle, min, max);
        float targetAngle = Mathf.Clamp(plan.frozenJoint.jointAngle, min, max);
        float liveT = Mathf.InverseLerp(min, max, liveAngle);
        float targetT = Mathf.InverseLerp(min, max, targetAngle);

        Color oldColor = GUI.color;
        GUI.color = new Color(0.12f, 0.14f, 0.16f, 0.9f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        float lowX = Mathf.Lerp(rect.xMin, rect.xMax, Mathf.Min(liveT, targetT));
        float highX = Mathf.Lerp(rect.xMin, rect.xMax, Mathf.Max(liveT, targetT));
        Rect deltaRect = new Rect(lowX, rect.y, Mathf.Max(2f, highX - lowX), rect.height);
        GUI.color = new Color(0.35f, 0.8f, 1f, 0.45f);
        GUI.DrawTexture(deltaRect, Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(Mathf.Lerp(rect.xMin, rect.xMax, liveT) - 1f, rect.y - 1f, 2f, rect.height + 2f), Texture2D.whiteTexture);

        GUI.color = GetIntentColor(plan.intent);
        GUI.DrawTexture(new Rect(Mathf.Lerp(rect.xMin, rect.xMax, targetT) - 2f, rect.y - 2f, 4f, rect.height + 4f), Texture2D.whiteTexture);
        GUI.color = oldColor;
    }

    private void QueueIntentForEntireLimb(LimbPlan sourcePlan, LimbMoveIntent.MoveMode newMode)
    {
        if (newMode == LimbMoveIntent.MoveMode.Auto && forceAirborneMode)
        {
            forceAirborneMode = false;
            jumpTriggerArmed = false;
            isJumpNextSimulation = false;
        }

        if (sourcePlan == null || sourcePlan.liveBody == null) return;
        bool isLeg = IsLegLimb(sourcePlan.liveBody.name);
        bool isArm = IsArmLimb(sourcePlan.liveBody.name);
        bool isLeft = IsLeftSide(sourcePlan);

        foreach (var p in limbPlans)
        {
            if (p == null || p.liveBody == null || p.intent == null) continue;

            if (isLeg && IsLegLimb(p.liveBody.name) && IsLeftSide(p) == isLeft)
            {
                ApplyModeToPlan(p, newMode);
            }
            else if (isArm && IsArmLimb(p.liveBody.name) && IsLeftSide(p) == isLeft)
            {
                ApplyModeToPlan(p, newMode);
            }
            else if (!isLeg && !isArm && p == sourcePlan)
            {
                // If it is a core/torso body like Spine or Head, only toggle it individually!
                ApplyModeToPlan(p, newMode);
            }
        }
    }

    private void ApplyModeToPlan(LimbPlan plan, LimbMoveIntent.MoveMode mode)
    {
        switch (mode)
        {
            case LimbMoveIntent.MoveMode.Auto:
                plan.intent.QueueAuto();
                break;
            case LimbMoveIntent.MoveMode.BasicBlock:
                plan.intent.QueueBasicBlock();
                break;
            case LimbMoveIntent.MoveMode.Strike:
                plan.intent.QueueStrike();
                break;
        }
    }

    /// <summary>
    /// Finds the LimbPlan associated with a given frozen or live body GameObject,
    /// and sets the intent for its entire limb group to the specified mode.
    /// </summary>
    public void SetLimbIntentMode(GameObject bodyObj, LimbMoveIntent.MoveMode mode)
    {
        if (bodyObj == null || limbPlans == null) return;

        // Clean up the name to find the match (stripping clone suffixes)
        string targetName = bodyObj.name.Replace("(Clone)", "").Trim();

        foreach (var plan in limbPlans)
        {
            if (plan == null) continue;
            
            bool match = false;
            if (plan.liveBody != null && plan.liveBody.name == targetName) match = true;
            else if (plan.frozenBody != null && plan.frozenBody.name == targetName) match = true;

            if (match)
            {
                QueueIntentForEntireLimb(plan, mode);
                break;
            }
        }
    }

    public void StabilizeFrozenCopyPose()
    {
        if (frozenCopy == null) return;

        ClampFrozenRootMovement();
        HingeJoint2D[] frozenJoints = frozenCopy.GetComponentsInChildren<HingeJoint2D>(true);

        for (int pass = 0; pass < 8; pass++)
        {
            if (!ignoreJointLimits)
                ClampFrozenJointLimitsOnce(frozenJoints);

            ReconcileFrozenJointAnchorsOnce(frozenJoints);
        }

        Physics2D.SyncTransforms();
        ClampFrozenCopyAboveGround();
        RestoreFrozenAutoLegTargetsAfterPoseStabilization();

        foreach (var rb in frozenCopy.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (rb == null) continue;
            rb.rotation = NormalizeAngle(rb.rotation);
            rb.transform.rotation = Quaternion.Euler(0f, 0f, rb.rotation);
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        Physics2D.SyncTransforms();
    }

    public void ResetFrozenCopy()
    {
        if (currentSnapshot != null)
        {
            for (int i = 0; i < trackedBodies.Count && i < frozenChildren.Length; i++)
            {
                if (i >= currentSnapshot.Length) break;
                Transform frozenChild = frozenChildren[i];
                if (frozenChild == null) continue;

                frozenChild.position = currentSnapshot[i].position;
                frozenChild.rotation = Quaternion.Euler(0, 0, currentSnapshot[i].rotation);

                Rigidbody2D frozenRb = frozenChild.GetComponent<Rigidbody2D>();
                if (frozenRb != null)
                {
                    frozenRb.position = currentSnapshot[i].position;
                    frozenRb.rotation = currentSnapshot[i].rotation;
                    frozenRb.linearVelocity = Vector2.zero;
                    frozenRb.angularVelocity = 0f;
                }
            }
            StabilizeFrozenCopyPose();
        }
    }

    private void ClampFrozenJointLimitsOnce(HingeJoint2D[] frozenJoints)
    {
        foreach (var joint in frozenJoints)
        {
            if (joint == null || !joint.useLimits) continue;

            Rigidbody2D rb = joint.GetComponent<Rigidbody2D>();
            if (rb == null) continue;

            float jointAngle = NormalizeAngle(joint.jointAngle);
            float clampedAngle = Mathf.Clamp(jointAngle, joint.limits.min, joint.limits.max);
            float correction = Mathf.DeltaAngle(jointAngle, clampedAngle);
            if (Mathf.Abs(correction) <= 0.05f) continue;

            rb.rotation = NormalizeAngle(rb.rotation + correction);
            rb.transform.rotation = Quaternion.Euler(0f, 0f, rb.rotation);
        }
    }

    private void ReconcileFrozenJointAnchorsOnce(HingeJoint2D[] frozenJoints)
    {
        const float toleranceSqr = 0.000001f;

        foreach (var joint in frozenJoints)
        {
            if (joint == null) continue;

            Rigidbody2D rb = joint.GetComponent<Rigidbody2D>();
            if (rb == null) continue;

            Vector2 connectedAnchor = joint.connectedBody != null
                ? (Vector2)joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)
                : joint.connectedAnchor;
            Vector2 bodyAnchor = rb.transform.TransformPoint(joint.anchor);
            Vector2 correction = connectedAnchor - bodyAnchor;
            if (correction.sqrMagnitude <= toleranceSqr) continue;

            rb.position += correction;
            rb.transform.position = rb.position;
        }
    }

    private bool ClampFrozenCopyAboveGround()
    {
        if (!preventFrozenCopyGroundPenetration || frozenCopy == null) return false;

        float lift = 0f;
        Collider2D[] colliders = frozenCopy.GetComponentsInChildren<Collider2D>(true);
        foreach (var col in colliders)
        {
            if (col == null || !col.enabled || col.isTrigger) continue;

            Bounds bounds = col.bounds;
            Vector2 probe = new Vector2(bounds.center.x, bounds.min.y);
            if (!TryGetGroundPoint(probe, out Vector2 groundPoint)) continue;

            float desiredBottom = groundPoint.y + Mathf.Max(0f, frozenCopyGroundClearance);
            lift = Mathf.Max(lift, desiredBottom - bounds.min.y);
        }

        if (lift <= 0.0001f) return false;

        Vector2 correction = Vector2.up * lift;
        foreach (var rb in frozenCopy.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (rb == null) continue;
            rb.position += correction;
            rb.transform.position = rb.position;
        }

        Physics2D.SyncTransforms();
        return true;
    }

    private void RestoreFrozenAutoLegTargetsAfterPoseStabilization()
    {
        if (!frozenRootEditActive) return;
        if (!HasPlantedGroundContactLegTarget(frozenRootEditPlantedLegTargets)) return;

        Transform frozenRoot = GetFrozenRootTransform();
        if (frozenRoot == null) return;

        Vector2 rootTarget = frozenRoot.position;
        Vector2 rootDelta = rootTarget - frozenRootEditStartRoot;
        ApplyFrozenAutoLimbEditTargets(rootTarget, rootDelta);
        Physics2D.SyncTransforms();
    }

    private void ClampFrozenRootMovement()
    {
        if (!limitFrozenRootMovement) return;
        if (rootBodyIndex < 0 || rootBodyIndex >= trackedBodies.Count || rootBodyIndex >= frozenChildren.Length) return;

        Rigidbody2D liveRoot = trackedBodies[rootBodyIndex];
        Transform frozenRoot = frozenChildren[rootBodyIndex];
        if (liveRoot == null || frozenRoot == null) return;

        Vector2 rootReference = currentSnapshot != null && rootBodyIndex < currentSnapshot.Length
            ? currentSnapshot[rootBodyIndex].position
            : liveRoot.position;
        Vector2 delta = (Vector2)frozenRoot.position - rootReference;
        if (delta.magnitude <= maxRootMovePerSnapshot) return;

        Vector2 clampedDelta = delta.normalized * maxRootMovePerSnapshot;
        Vector2 correction = clampedDelta - delta;
        foreach (var rb in frozenCopy.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (rb == null) continue;
            rb.position += correction;
            rb.transform.position = rb.position;
        }
    }

    private float NormalizeAngle(float angle)
    {
        return Mathf.Repeat(angle + 180f, 360f) - 180f;
    }

    // ===== FROZEN COPY VISIBILITY =====

    /// <summary>
    /// Hide the frozen copy visually (disable renderers) but keep it ACTIVE
    /// so HingeJoint2D components still provide valid jointAngle data
    /// for PoseDriver to read during simulation.
    /// </summary>
    private void HideFrozenCopy()
    {
        SetFrozenCopyCollidersEnabled(false);
        foreach (var r in frozenCopy.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;
    }

    /// <summary>
    /// Show the frozen copy (re-enable renderers) for posing.
    /// </summary>
    private void ShowFrozenCopy()
    {
        frozenCopy.SetActive(true); // Ensure active
        SetFrozenCopyBodyType(frozenPhysicsPoseEditActive ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic);
        SetFrozenCopyCollidersEnabled(true);
        foreach (var r in frozenCopy.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
    }

    private void SetFrozenCopyCollidersEnabled(bool enabled)
    {
        if (frozenCopy == null) return;

        foreach (var col in frozenCopy.GetComponentsInChildren<Collider2D>(true))
            col.enabled = enabled;

        if (enabled)
            ReapplyRuntimeRigCollisionIgnores(frozenCopy);
    }

    private void ReapplyRuntimeRigCollisionIgnores(GameObject fighter)
    {
        if (fighter == null) return;

        foreach (var rig in fighter.GetComponentsInChildren<FighterRuntimeRigInstance>(true))
        {
            if (rig == null) continue;
            rig.ApplySelfCollisionIgnores();
        }
    }

    private void SetFrozenCopyBodyType(RigidbodyType2D bodyType)
    {
        if (frozenCopy == null) return;

        foreach (var rb in frozenCopy.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (rb == null) continue;
            if (rb.bodyType != bodyType)
            {
                rb.bodyType = bodyType;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            else if (bodyType == RigidbodyType2D.Kinematic)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
        }
    }

    public void BeginFrozenPhysicsPoseEdit(GameObject bodyPart = null)
    {
        if (frozenCopy == null) return;

        frozenPhysicsPoseEditActive = true;
        SetFrozenCopyBodyType(RigidbodyType2D.Dynamic);
        ZeroFrozenCopyVelocities();

        if (bodyPart != null)
        {
            Transform ownedBody = GetFrozenBodyOwner(bodyPart);
            foreach (var plan in limbPlans)
            {
                if (plan == null || plan.frozenBody == null || plan.intent == null) continue;
                if (ownedBody != plan.frozenBody) continue;

                if (IsLegLimb(plan.liveBody.name) && plan.intent.mode == LimbMoveIntent.MoveMode.Auto)
                {
                    QueueIntentForEntireLimb(plan, LimbMoveIntent.MoveMode.BasicBlock);
                }
            }
        }
    }

    public void EndFrozenPhysicsPoseEdit(GameObject draggedBody = null)
    {
        if (frozenCopy == null) return;

        StabilizeFrozenCopyPose();
        frozenPhysicsPoseEditActive = false;
        SetFrozenCopyBodyType(RigidbodyType2D.Kinematic);
        ZeroFrozenCopyVelocities();

        if (draggedBody != null)
        {
            Transform ownedBody = GetFrozenBodyOwner(draggedBody);
            foreach (var plan in limbPlans)
            {
                if (plan == null || plan.frozenBody == null || plan.intent == null) continue;
                if (ownedBody != plan.frozenBody) continue;

                if (IsLegLimb(plan.liveBody.name))
                {
                    LimbPlan footPlan = IsFootLimb(plan.liveBody.name) ? plan : FindFootPlanForSide(plan);
                    if (footPlan != null)
                    {
                        Collider2D col = footPlan.frozenBody.GetComponent<Collider2D>();
                        if (col != null)
                        {
                            Bounds bounds = col.bounds;
                            Vector2 probe = new Vector2(bounds.center.x, bounds.min.y);
                            if (TryGetGroundPoint(probe, out Vector2 groundPoint))
                            {
                                float dist = bounds.min.y - groundPoint.y;
                                if (dist <= plantedFootGroundTolerance)
                                {
                                    plan.intent.QueueAuto();
                                    footPlan.intent.QueueAuto();
                                    
                                    // Also queue auto for the rest of the leg
                                    foreach (var p in limbPlans) {
                                        if (p != null && p.intent != null && IsLegLimb(p.liveBody.name) && IsSameLegSide(p, plan)) {
                                            p.intent.QueueAuto();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // ===== FREEZE/UNFREEZE =====

    /// <summary>
    /// Freeze the live fighter's bodies by setting them to Kinematic.
    /// The frozen copy remains fully interactive for posing.
    /// </summary>
    private void FreezeLiveBodies()
    {
        ClearPlantedFootJoints();
        ClearReplantingFootJoints();

        foreach (var driver in poseDrivers)
        {
            if (driver == null) continue;
            driver.enablePoseDriver = false;
            driver.ResetRuntimeMultipliers();
        }

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
        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.poseDriver == null) continue;
            bool isAutoLeg = plan.intent != null
                && plan.intent.mode == LimbMoveIntent.MoveMode.Auto
                && IsLegLimb(plan.liveBody.name);
            plan.poseDriver.enablePoseDriver = !isAutoLeg || usePoseDriversForAutoLegs;
        }

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

    public bool CanManuallyPoseFrozenBody(GameObject bodyPart)
    {
        if (bodyPart == null) return false;
        if (IsFrozenRootBody(bodyPart)) return true;

        Transform ownedBody = GetFrozenBodyOwner(bodyPart);
        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.frozenBody == null || plan.intent == null) continue;
            if (ownedBody != plan.frozenBody) continue;

            // Legs are now editable even in Auto mode
            return true;
        }

        return true;
    }

    public bool IsFrozenRootBody(GameObject bodyPart)
    {
        if (bodyPart == null) return false;
        Transform ownedBody = GetFrozenBodyOwner(bodyPart);
        if (ownedBody != null && IsRootBodyName(ownedBody.name)) return true;
        if (frozenChildren == null || rootBodyIndex < 0 || rootBodyIndex >= frozenChildren.Length) return false;
        Transform frozenRoot = frozenChildren[rootBodyIndex];
        return frozenRoot != null && ownedBody == frozenRoot;
    }

    private bool IsRootBodyName(string bodyName)
    {
        string n = bodyName.ToLowerInvariant();
        return n.Contains("hip") || n.Contains("pelvis") || n.Contains("root");
    }

    public void BeginFrozenRootPoseEdit(GameObject bodyPart)
    {
        if (frozenCopy == null || !IsFrozenRootBody(bodyPart)) return;
        Transform frozenRoot = GetFrozenRootTransform();
        if (frozenRoot == null) return;

        frozenRootEditStartPoses.Clear();
        foreach (var rb in frozenCopy.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (rb == null) continue;
            frozenRootEditStartPoses[rb] = new FrozenRootEditPose
            {
                position = rb.position,
                rotation = rb.rotation
            };
        }

        frozenRootEditStartRoot = frozenRoot.position;
        CacheFrozenRootEditPlantedLegTargets();
        frozenRootEditActive = true;
    }

    public void UpdateFrozenRootPoseEdit(GameObject bodyPart, Vector2 targetRootPosition)
    {
        if (frozenCopy == null || !IsFrozenRootBody(bodyPart)) return;

        if (!frozenRootEditActive)
            BeginFrozenRootPoseEdit(bodyPart);
        if (!frozenRootEditActive) return;

        Vector2 rootTarget = ClampFrozenRootTarget(targetRootPosition);
        Vector2 rootDelta = rootTarget - frozenRootEditStartRoot;

        Vector2 pivot = frozenRootEditStartRoot + rootDelta;

        foreach (var kvp in frozenRootEditStartPoses)
        {
            Rigidbody2D rb = kvp.Key;
            if (rb == null || IsFrozenAutoLimbBody(rb)) continue;

            Vector2 newPos = kvp.Value.position + rootDelta;
            float newRot = kvp.Value.rotation;

            SetFrozenBodyPose(rb, newPos, newRot);
        }

        ApplyFrozenAutoLimbEditTargets(rootTarget, rootDelta);
        StabilizeFrozenCopyPose();
    }

    public void EndFrozenRootPoseEdit()
    {
        frozenRootEditActive = false;
        frozenRootEditStartPoses.Clear();
        frozenRootEditPlantedLegTargets.Clear();
    }

    private Transform GetFrozenRootTransform()
    {
        if (frozenChildren == null || rootBodyIndex < 0 || rootBodyIndex >= frozenChildren.Length)
            return null;

        return frozenChildren[rootBodyIndex];
    }

    private Vector2 ClampFrozenRootTarget(Vector2 targetRootPosition)
    {
        if (rootBodyIndex < 0 || rootBodyIndex >= trackedBodies.Count) return targetRootPosition;

        Vector2 rootReference = currentSnapshot != null && rootBodyIndex < currentSnapshot.Length
            ? currentSnapshot[rootBodyIndex].position
            : frozenRootEditStartRoot;
        Vector2 delta = targetRootPosition - rootReference;
        
        if (limitFrozenRootMovement && delta.magnitude > maxRootMovePerSnapshot)
        {
            targetRootPosition = rootReference + delta.normalized * maxRootMovePerSnapshot;
        }

        // LAUNCH READY (isJumpNextSimulation): The frozen copy stays grounded with
        // at least one foot planted. The user authors the launch direction by moving
        // the hips higher/farther — the live fighter previews the actual launch in sim.
        // Only skip constraints when already airborne (forceAirborneMode after launch).
        if (forceAirborneMode && !isJumpNextSimulation) return targetRootPosition;

        // Root dragging is kinematic, so carried non-Auto bodies would otherwise
        // tunnel into the floor and only be corrected after the legs were planned.
        // Clamp that floor contact up front so the generated crouch uses the real hip height.
        targetRootPosition = ClampFrozenRootTargetAboveGroundByCarriedBodies(targetRootPosition);

        if (!enableAutoWalkFromHipTarget) return targetRootPosition;

        // NEW CONSTRAINT: Strict horizontal stride limit relative to starting position
        float horizontalTravel = targetRootPosition.x - rootReference.x;
        if (Mathf.Abs(horizontalTravel) > walkMaxStrideDistance)
        {
            targetRootPosition.x = rootReference.x + Mathf.Sign(horizontalTravel) * walkMaxStrideDistance;
        }

        // NEW CONSTRAINT: Rigidly anchor hips to planted feet so feet are never pulled from the floor
        if (HasPlantedGroundContactLegTarget(frozenRootEditPlantedLegTargets))
        {
            Rigidbody2D swingFoot = null;
            TryGetFrozenAutoWalkEditSwingFoot(targetRootPosition, out swingFoot);
            targetRootPosition = ProjectFrozenRootIntoPlantedLegReach(targetRootPosition, swingFoot);
        }

        // Strict vertical height limit relative to the actual floor.
        // During LAUNCH READY, allow full leg extension so the user can
        // author how high/far the launch will go.
        if (TryGetGroundPoint(targetRootPosition, out Vector2 groundPoint))
        {
            float maxHeight = isJumpNextSimulation
                ? walkMaxHipGroundDistance * 1.15f  // Full extension for launch direction
                : walkMaxHipGroundDistance;
            float currentHeight = targetRootPosition.y - groundPoint.y;
            if (currentHeight > maxHeight)
            {
                targetRootPosition.y = groundPoint.y + maxHeight;
            }
        }

        return targetRootPosition;
    }

    private Vector2 ProjectFrozenRootIntoPlantedLegReach(Vector2 targetRootPosition, Rigidbody2D swingFoot)
    {
        const int projectionPasses = 3;
        float maxReach = Mathf.Max(0.01f, walkMaxHipGroundDistance);

        for (int pass = 0; pass < projectionPasses; pass++)
        {
            bool adjusted = false;

            foreach (var kvp in frozenRootEditPlantedLegTargets)
            {
                if (kvp.Key == null) continue;
                if (swingFoot != null && kvp.Key == swingFoot) continue;

                Vector2 plantedPos = kvp.Value;
                Vector2 plantedToRoot = targetRootPosition - plantedPos;
                float dist = plantedToRoot.magnitude;
                if (dist <= maxReach || dist <= 0.0001f) continue;

                targetRootPosition = plantedPos + plantedToRoot / dist * maxReach;
                adjusted = true;
            }

            if (!adjusted)
                break;
        }

        return targetRootPosition;
    }

    private Vector2 ClampFrozenRootTargetAboveGroundByCarriedBodies(Vector2 targetRootPosition)
    {
        Vector2 rootDelta = targetRootPosition - frozenRootEditStartRoot;
        float requiredLift = 0f;

        foreach (var kvp in frozenRootEditStartPoses)
        {
            Rigidbody2D rb = kvp.Key;
            if (rb == null || IsFrozenAutoLimbBody(rb)) continue;

            FrozenRootEditPose startPose = kvp.Value;
            Collider2D[] colliders = rb.GetComponents<Collider2D>();
            foreach (var col in colliders)
            {
                if (col == null || !col.enabled || col.isTrigger) continue;

                Bounds currentBounds = col.bounds;
                float minYOffset = currentBounds.min.y - rb.position.y;
                float centerXOffset = currentBounds.center.x - rb.position.x;
                float candidateMinY = startPose.position.y + rootDelta.y + minYOffset;
                float candidateCenterX = startPose.position.x + rootDelta.x + centerXOffset;
                Vector2 probe = new Vector2(candidateCenterX, candidateMinY);

                if (!TryGetGroundPoint(probe, out Vector2 groundPoint)) continue;

                float desiredBottom = groundPoint.y + Mathf.Max(0f, frozenCopyGroundClearance);
                requiredLift = Mathf.Max(requiredLift, desiredBottom - candidateMinY);
            }
        }

        if (requiredLift > 0.0001f)
            targetRootPosition.y += requiredLift;

        return targetRootPosition;
    }

    private void ApplyFrozenAutoLimbEditTargets(Vector2 rootTarget, Vector2 rootDelta)
    {
        bool editWalkActive = TryGetFrozenAutoWalkEditSwingFoot(rootTarget, out Rigidbody2D editSwingFoot);
        autoWalkActive = editWalkActive;

        foreach (var plan in limbPlans)
        {
            if (!IsAutoLimbPlan(plan)) continue;

            Rigidbody2D rb = plan.frozenBody.GetComponent<Rigidbody2D>();
            if (rb == null || !frozenRootEditStartPoses.TryGetValue(rb, out FrozenRootEditPose startPose)) continue;

            bool isSwingLeg = editWalkActive && IsSameLegSide(plan, editSwingFoot);
            bool isSwingContact = editWalkActive && plan.liveBody == editSwingFoot;
            Vector2 target = GetAutoLimbBodyTarget(plan, rootTarget, rootDelta, 0f, isSwingLeg, isSwingContact);
            float rotation = rotateFrozenAutoLegsToGeneratedTargets
                ? GetAutoLimbEditRotation(plan, rootTarget, rootDelta, startPose, isSwingLeg, isSwingContact)
                : startPose.rotation;
            SetFrozenBodyPose(rb, target, rotation);
        }
    }

    private bool TryGetFrozenAutoWalkEditSwingFoot(Vector2 rootTarget, out Rigidbody2D swingFoot)
    {
        swingFoot = null;
        if (isJumpNextSimulation) return false;
        if (!enableAutoWalkFromHipTarget) return false;
        if (!HasPlantedGroundContactLegTarget(frozenRootEditPlantedLegTargets)) return false;
        if (currentSnapshot == null || rootBodyIndex < 0 || rootBodyIndex >= currentSnapshot.Length) return false;

        Vector2 startRoot = currentSnapshot[rootBodyIndex].position;
        float horizontalTravel = rootTarget.x - startRoot.x;
        if (Mathf.Abs(horizontalTravel) < walkMinHipTravel) return false;

        if (!TryGetGroundPoint(rootTarget, out Vector2 groundPoint)) return false;

        float hipHeight = rootTarget.y - groundPoint.y;
        float legRange = walkMaxHipGroundDistance * 1.15f;
        if (hipHeight < 0f || hipHeight > legRange) return false;

        float direction = Mathf.Abs(horizontalTravel) > 0.001f ? Mathf.Sign(horizontalTravel) : autoWalkDirection;
        if (Mathf.Abs(direction) < 0.001f) direction = 1f;

        swingFoot = ChooseAutoWalkSwingFoot(direction, frozenRootEditPlantedLegTargets);
        return swingFoot != null;
    }

    private float GetAutoLimbEditRotation(
        LimbPlan plan,
        Vector2 rootTarget,
        Vector2 rootDelta,
        FrozenRootEditPose startPose,
        bool isSwingLeg,
        bool isSwingContact)
    {
        if (!TryGetAutoLimbEditRotationVectors(
            plan,
            rootTarget,
            rootDelta,
            isSwingLeg,
            isSwingContact,
            out Vector2 startVector,
            out Vector2 targetVector))
        {
            return startPose.rotation;
        }

        if (startVector.sqrMagnitude < 0.0001f || targetVector.sqrMagnitude < 0.0001f)
            return startPose.rotation;

        float startAngle = Mathf.Atan2(startVector.y, startVector.x) * Mathf.Rad2Deg;
        float targetAngle = Mathf.Atan2(targetVector.y, targetVector.x) * Mathf.Rad2Deg;
        return startPose.rotation + Mathf.DeltaAngle(startAngle, targetAngle);
    }

    private bool TryGetAutoLimbEditRotationVectors(
        LimbPlan plan,
        Vector2 rootTarget,
        Vector2 rootDelta,
        bool isSwingLeg,
        bool isSwingContact,
        out Vector2 startVector,
        out Vector2 targetVector)
    {
        startVector = Vector2.zero;
        targetVector = Vector2.zero;

        if (plan == null || plan.liveBody == null)
            return false;

        string limbName = plan.liveBody.name;
        Vector2 endTarget = GetAutoLimbEndTarget(plan, rootTarget, rootDelta, isSwingLeg || isSwingContact);
        Vector2 midTarget = GetAutoMidJointTarget(plan, rootTarget, endTarget, 0f, isSwingLeg);

        if (IsUpperLeg(limbName) || IsUpperArm(limbName))
        {
            if (!TryGetFrozenRootEditStartPosition(plan, out Vector2 upperStart)) return false;

            startVector = upperStart - frozenRootEditStartRoot;
            targetVector = midTarget - rootTarget;
            return true;
        }

        if (IsLowerLeg(limbName) || IsLowerArm(limbName))
        {
            if (!TryGetFrozenRootEditStartPosition(plan, out Vector2 lowerStart)) return false;

            bool isLeg = IsLegLimb(limbName);
            LimbPlan endPlan = isLeg ? FindFootPlanForSide(plan) : FindHandPlanForSide(plan);
            
            if (endPlan != null && TryGetFrozenRootEditStartPosition(endPlan, out Vector2 endStart))
                startVector = endStart - lowerStart;
            else if (TryGetFrozenRootEditStartPosition(isLeg ? FindUpperLegPlanForSide(plan) : FindUpperArmPlanForSide(plan), out Vector2 upperStart))
                startVector = lowerStart - upperStart;
            else
                startVector = lowerStart - frozenRootEditStartRoot;

            targetVector = endTarget - midTarget;
            return true;
        }

        return false;
    }

    private bool TryGetFrozenRootEditStartPosition(LimbPlan plan, out Vector2 position)
    {
        position = Vector2.zero;
        if (plan == null || plan.frozenBody == null) return false;

        Rigidbody2D rb = plan.frozenBody.GetComponent<Rigidbody2D>();
        if (rb == null) return false;

        if (!frozenRootEditStartPoses.TryGetValue(rb, out FrozenRootEditPose pose)) return false;

        position = pose.position;
        return true;
    }

    private LimbPlan FindUpperLegPlanForSide(LimbPlan sideSource)
    {
        foreach (var plan in limbPlans)
        {
            if (plan == null || plan.liveBody == null) continue;
            if (!IsUpperLeg(plan.liveBody.name)) continue;
            if (IsSameLegSide(plan, sideSource))
                return plan;
        }

        return null;
    }

    private bool IsAutoLimbPlan(LimbPlan plan)
    {
        return plan != null
            && plan.liveBody != null
            && plan.frozenBody != null
            && plan.intent != null
            && plan.intent.mode == LimbMoveIntent.MoveMode.Auto
            && (IsLegLimb(plan.liveBody.name) || IsArmLimb(plan.liveBody.name));
    }

    private bool IsFrozenAutoLimbBody(Rigidbody2D body)
    {
        if (body == null) return false;
        foreach (var plan in limbPlans)
        {
            if (plan.frozenBody == body && IsAutoLimbPlan(plan)) return true;
        }
        return false;
    }

    private Dictionary<Rigidbody2D, FrozenRootEditPose> CaptureFrozenCopyPose()
    {
        Dictionary<Rigidbody2D, FrozenRootEditPose> poses = new Dictionary<Rigidbody2D, FrozenRootEditPose>();
        if (frozenCopy == null) return poses;

        foreach (var rb in frozenCopy.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (rb == null) continue;
            poses[rb] = new FrozenRootEditPose
            {
                position = rb.position,
                rotation = rb.rotation
            };
        }

        return poses;
    }

    private void RestoreFrozenCopyPose(Dictionary<Rigidbody2D, FrozenRootEditPose> poses)
    {
        if (poses == null || poses.Count == 0) return;

        bool changed = false;
        foreach (var kvp in poses)
        {
            Rigidbody2D rb = kvp.Key;
            if (rb == null) continue;

            FrozenRootEditPose pose = kvp.Value;
            bool moved = (rb.position - pose.position).sqrMagnitude > 0.0000001f;
            bool rotated = Mathf.Abs(Mathf.DeltaAngle(rb.rotation, pose.rotation)) > 0.001f;
            if (!moved && !rotated) continue;

            SetFrozenBodyPose(rb, pose.position, pose.rotation);
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            changed = true;
        }

        if (changed)
            Physics2D.SyncTransforms();
    }

    private void SetFrozenBodyPose(Rigidbody2D rb, Vector2 position, float rotation)
    {
        if (rb == null) return;

        rb.position = position;
        rb.rotation = NormalizeAngle(rotation);
        rb.transform.position = position;
        rb.transform.rotation = Quaternion.Euler(0f, 0f, rb.rotation);
    }

    private void ZeroFrozenCopyVelocities()
    {
        if (frozenCopy == null) return;

        foreach (var rb in frozenCopy.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (rb == null) continue;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    private Transform GetFrozenBodyOwner(GameObject bodyPart)
    {
        if (bodyPart == null) return null;

        Collider2D collider = bodyPart.GetComponent<Collider2D>();
        if (collider != null && collider.attachedRigidbody != null)
            return collider.attachedRigidbody.transform;

        Rigidbody2D body = bodyPart.GetComponent<Rigidbody2D>();
        if (body != null)
            return body.transform;

        body = bodyPart.GetComponentInParent<Rigidbody2D>();
        return body != null ? body.transform : bodyPart.transform;
    }

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

    [ContextMenu("Apply Current Low-Force Defaults")]
    public void ApplyCurrentLowForceDefaults()
    {
        enablePhysicsPoseAssist = true;
        poseAssistOnlyRootAndUnjointedBodies = true;
        useForceLegAssistWhenNotWalking = false;

        enablePlantedFootStability = true;
        usePlantedFootJoints = true;
        usePlantedFootSpringFallback = false;

        enableAutoWalkFromHipTarget = true;
        driveAutoLegsToGeneratedTargets = true;
        useJointMotorsForAutoWalk = true;
        reduceAutoWalkLimbForcesWhenUsingMotors = true;
        skipAutoLegTargetForPlantedFeet = true;
        driveAutoLegTargetsOnlyDuringWalk = true;
        usePoseDriversForAutoLegs = false;
        rotateAutoLegsTowardGeneratedTargets = false;

        snapRuntimeRigFeetToGround = true;
        preventFrozenCopyGroundPenetration = true;
        syncReplayToFixedStep = true;
        skipDuplicateReplayFrames = true;
    }

    // ===== PRESET SYSTEM =====

    [Header("=== Presets ===")]
    [Tooltip("If set, this preset is loaded on Start. Leave empty to use inspector values.")]
    public string autoLoadPresetName = "tether_jumping";

    /// <summary>
    /// Save all current tuning values to a named preset JSON file.
    /// Files are stored in [ProjectRoot]/FRV2Presets/.
    /// </summary>
    [ContextMenu("Save Current Settings As Preset")]
    public void SaveCurrentPreset()
    {
        FRV2Preset preset = FRV2Preset.CaptureFrom(this, "manual_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        preset.description = "Manually saved from inspector.";
        preset.SaveToFile();
    }

    /// <summary>
    /// Load a preset by name (without .json extension).
    /// Call from code: frv2.LoadPreset("optimized_20260506_215130");
    /// </summary>
    public void LoadPreset(string presetName)
    {
        FRV2Preset preset = FRV2Preset.LoadFromFile(presetName);
        if (preset != null)
            preset.ApplyTo(this);
    }

    /// <summary>
    /// Load the auto-load preset if configured.
    /// Called automatically at the end of Start() if autoLoadPresetName is set.
    /// </summary>
    private void TryAutoLoadPreset()
    {
        if (string.IsNullOrEmpty(autoLoadPresetName)) return;

        string[] available = FRV2Preset.GetAvailablePresetNames();
        bool found = false;
        foreach (string name in available)
        {
            if (name == autoLoadPresetName) { found = true; break; }
        }

        if (!found)
        {
            Debug.LogWarning($"[FreezeReplayV2] Auto-load preset '{autoLoadPresetName}' not found in FRV2Presets/.");
            return;
        }

        LoadPreset(autoLoadPresetName);
        Debug.Log($"[FreezeReplayV2] Auto-loaded preset '{autoLoadPresetName}'.");
    }

    // ===== RAGDOLL SYSTEM (PHASE 1.3 & 1.4) =====

    /// <summary>
    /// Trigger full ragdoll for the live fighter, letting raw physics take over
    /// and applying a high-friction material for floor tumbling.
    /// </summary>
    public void TriggerRagdoll()
    {
        if (isRagdolled) return;
        isRagdolled = true;

        foreach (var plan in limbPlans)
        {
            if (plan != null && plan.poseDriver != null)
                plan.poseDriver.enablePoseDriver = false;
        }

        ClearPlantedFootJoints();
        ClearReplantingFootJoints();
        ClearPoseTethers();

        // Phase 1.4: Apply high-friction material for floor tumbling
        ApplyRagdollPhysicsMaterials();

        Debug.Log("[FreezeReplayV2] Fighter entered full RAGDOLL tumble state!");
    }

    /// <summary>
    /// Restores PoseDrivers and resets original physics materials on all body colliders.
    /// </summary>
    private void ResetRagdollState()
    {
        if (!isRagdolled) return;
        isRagdolled = false;

        foreach (var plan in limbPlans)
        {
            if (plan != null && plan.poseDriver != null)
                plan.poseDriver.enablePoseDriver = true;
        }

        RestoreOriginalPhysicsMaterials();
        Debug.Log("[FreezeReplayV2] Ragdoll state reset, PoseDrivers re-enabled.");
    }

    private void ApplyRagdollPhysicsMaterials()
    {
        if (trackedBodies == null) return;

        foreach (var rb in trackedBodies)
        {
            if (rb == null) continue;
            Collider2D[] colliders = rb.GetComponents<Collider2D>();
            foreach (var col in colliders)
            {
                if (col == null) continue;
                if (!originalMaterials.ContainsKey(col))
                {
                    originalMaterials[col] = col.sharedMaterial;
                }
                col.sharedMaterial = ragdollMaterial;
            }
        }
    }

    private void RestoreOriginalPhysicsMaterials()
    {
        foreach (var kvp in originalMaterials)
        {
            if (kvp.Key != null)
            {
                kvp.Key.sharedMaterial = kvp.Value;
            }
        }
        originalMaterials.Clear();
    }

    /// <summary>
    /// Re-evaluates and ignores collisions between all active live fighters and all frozen copy ghosts
    /// in the scene. This prevents physics interference from opponent posing ghosts.
    /// </summary>
    public void ReapplyFighterGhostCollisionIgnores()
    {
        FreezeReplayV2[] allControllers = FindObjectsOfType<FreezeReplayV2>();
        foreach (var frSource in allControllers)
        {
            if (frSource == null || frSource.playerObj == null) continue;
            Collider2D[] liveColliders = frSource.playerObj.GetComponentsInChildren<Collider2D>(true);

            foreach (var frTarget in allControllers)
            {
                if (frTarget == null || frTarget.frozenCopy == null) continue;
                Collider2D[] frozenColliders = frTarget.frozenCopy.GetComponentsInChildren<Collider2D>(true);

                foreach (var liveCol in liveColliders)
                {
                    foreach (var frozenCol in frozenColliders)
                    {
                        if (liveCol != null && frozenCol != null)
                            Physics2D.IgnoreCollision(liveCol, frozenCol);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Explicitly enables physical collisions between the live colliders of different fighters.
    /// This bypasses any custom IgnoreCollision calls or layer matrix overrides.
    /// </summary>
    public void EnableCrossFighterCollisions()
    {
        FreezeReplayV2[] allControllers = FindObjectsOfType<FreezeReplayV2>();
        for (int i = 0; i < allControllers.Length; i++)
        {
            var frA = allControllers[i];
            if (frA == null || frA.playerObj == null) continue;
            Collider2D[] collidersA = frA.playerObj.GetComponentsInChildren<Collider2D>(true);

            for (int j = i + 1; j < allControllers.Length; j++)
            {
                var frB = allControllers[j];
                if (frB == null || frB.playerObj == null) continue;
                Collider2D[] collidersB = frB.playerObj.GetComponentsInChildren<Collider2D>(true);

                foreach (var colA in collidersA)
                {
                    foreach (var colB in collidersB)
                    {
                        if (colA != null && colB != null)
                        {
                            // Explicitly force them to collide!
                            Physics2D.IgnoreCollision(colA, colB, false);
                        }
                    }
                }
            }
        }
    }
}
