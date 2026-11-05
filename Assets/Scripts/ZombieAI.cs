using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Simple zombie AI that drives a slave FreezeReplayV2 each turn.
/// Pushes the frozen copy's hips horizontally toward the player and
/// uses physics-based TargetJoint2D pulls (like SmartPoser) to extend
/// hands toward the player. FRV2's stabilization and auto-walk handle
/// all constraint enforcement.
/// Attach to the same GameObject as a slave FRV2.
/// </summary>
[RequireComponent(typeof(FreezeReplayV2))]
public class ZombieAI : MonoBehaviour
{
    [Header("=== Target ===")]
    [Tooltip("The player's FreezeReplayV2 (usually the master). If null, auto-discovers the master.")]
    public FreezeReplayV2 playerFRV2;

    [Header("=== Timing ===")]
    [Tooltip("Delay in seconds before the AI authors its pose each turn.")]
    public float authorDelay = 0.15f;

    [Tooltip("Number of physics frames to let TargetJoint2D pull hands toward the target.")]
    public int handPhysicsFrames = 12;

    [Header("=== Hand Pull Settings ===")]
    [Tooltip("Force applied by the TargetJoint2D pulling hands toward the player.")]
    public float handPullForce = 500f;

    [Tooltip("Damping ratio for the hand pull spring (1 = critically damped).")]
    public float handPullDamping = 1f;

    [Tooltip("Frequency of the hand pull spring.")]
    public float handPullFrequency = 5f;

    [Tooltip("Force used to anchor non-chain bodies in place while hands are pulled.")]
    public float anchorForce = 2200f;

    [Tooltip("Damping ratio for anchor springs.")]
    public float anchorDamping = 1f;

    [Tooltip("Frequency of anchor springs.")]
    public float anchorFrequency = 8f;

    [Header("=== Behavior ===")]
    [Tooltip("When true, the zombie AI will automatically author each turn.")]
    public bool enableAI = true;

    private FreezeReplayV2 frv2;
    private int lastAuthoredTurn = -1;
    private float authorTimer = -1f;
    private bool isAuthoring = false;

    private readonly List<TargetJoint2D> tempJoints = new List<TargetJoint2D>();

    void Awake()
    {
        frv2 = GetComponent<FreezeReplayV2>();
    }

    void Start()
    {
        if (playerFRV2 == null)
        {
            FreezeReplayV2[] all = FindObjectsByType<FreezeReplayV2>(FindObjectsSortMode.None);
            foreach (var candidate in all)
            {
                if (candidate != frv2 && candidate.isMasterFRV2)
                {
                    playerFRV2 = candidate;
                    break;
                }
            }
        }

        if (playerFRV2 == null)
        {
            Debug.LogWarning("[ZombieAI] No player FRV2 found. Disabling AI.");
            enableAI = false;
        }

        // Disable manual editing -- the AI controls this fighter
        frv2.enableEditInput = false;
    }

    void Update()
    {
        if (!enableAI || isAuthoring) return;
        if (frv2 == null || playerFRV2 == null) return;

        // Dead fighters don't act — hide frozen copy and stop authoring
        if (IsKnockedOut())
        {
            if (frv2.frozenCopy != null && frv2.frozenCopy.activeSelf)
            {
                foreach (var r in frv2.frozenCopy.GetComponentsInChildren<Renderer>(true))
                    r.enabled = false;
            }
            return;
        }

        if (frv2.currentPhase != FreezeReplayV2.TurnPhase.Posing) return;
        if (frv2.turnCount == lastAuthoredTurn) return;

        // Start the delay timer on first frame of a new turn
        if (authorTimer < 0f)
        {
            authorTimer = authorDelay;
            return;
        }

        authorTimer -= Time.deltaTime;
        if (authorTimer > 0f) return;

        // Time to author
        authorTimer = -1f;
        lastAuthoredTurn = frv2.turnCount;
        StartCoroutine(AuthorZombiePose());
    }

    /// <summary>
    /// Authors the zombie's frozen copy over multiple frames:
    /// 1. Move hips toward player (kinematic, via FRV2 root edit API)
    /// 2. Pull hands toward player (physics-based, via TargetJoint2D like SmartPoser)
    /// Skipped entirely while the fighter is airborne — the frozen copy stays
    /// at the captured snapshot position until the fighter lands and a new
    /// grounded snapshot is taken.
    /// </summary>
    private IEnumerator AuthorZombiePose()
    {
        isAuthoring = true;

        if (frv2.frozenCopy == null || playerFRV2.playerObj == null)
        {
            isAuthoring = false;
            yield break;
        }

        // Don't author any pose while airborne — avoid physics pulling the
        // frozen copy to the ground via gravity during hand-pull frames.
        if (frv2.forceAirborneMode)
        {
            isAuthoring = false;
            yield break;
        }

        Vector2 playerPosition = GetPlayerRootPosition();

        // --- Step 1: Move hips toward the player (kinematic) ---
        MoveHipsTowardPlayer(playerPosition);

        // --- Step 2: Pull hands toward player using physics ---
        yield return PullHandsTowardPlayer(playerPosition);

        isAuthoring = false;
    }

    /// <summary>
    /// Pushes the frozen copy's root/hips horizontally toward the player
    /// using the FRV2 root edit API, which respects stride limits and
    /// triggers auto-walk leg placement.
    /// </summary>
    private void MoveHipsTowardPlayer(Vector2 playerPosition)
    {
        GameObject frozenRoot = FindFrozenBodyByName("hip", "pelvis", "root");
        if (frozenRoot == null) return;

        Vector2 currentHips = (Vector2)frozenRoot.transform.position;
        Vector2 targetHips = new Vector2(playerPosition.x, currentHips.y);

        frv2.BeginFrozenRootPoseEdit(frozenRoot);
        frv2.UpdateFrozenRootPoseEdit(frozenRoot, targetHips);
        frv2.EndFrozenRootPoseEdit();
    }

    /// <summary>
    /// Uses the same physics-based approach as SmartPoser to pull hands:
    /// - Switch frozen copy to Dynamic via BeginFrozenPhysicsPoseEdit
    /// - Build chain isolation: anchor all non-hand-chain bodies
    /// - Add TargetJoint2D on each hand pointing at the player
    /// - Wait several physics frames for the constraint solver
    /// - Clean up and call EndFrozenPhysicsPoseEdit
    /// </summary>
    private IEnumerator PullHandsTowardPlayer(Vector2 targetPosition)
    {
        if (frv2.frozenCopy == null) yield break;

        Rigidbody2D[] allBodies = frv2.frozenCopy.GetComponentsInChildren<Rigidbody2D>(true);
        List<Rigidbody2D> handBodies = new List<Rigidbody2D>();

        foreach (var rb in allBodies)
        {
            if (rb == null) continue;
            if (rb.name.ToLowerInvariant().Contains("hand"))
                handBodies.Add(rb);
        }

        if (handBodies.Count == 0) yield break;

        // Build the combined chain for all hands
        HashSet<Rigidbody2D> combinedChain = new HashSet<Rigidbody2D>();
        foreach (var hand in handBodies)
        {
            BuildLimbChain(hand, combinedChain);
        }

        // Switch frozen copy to dynamic for physics-based posing
        frv2.BeginFrozenPhysicsPoseEdit();

        // Anchor everything NOT in the hand chains
        foreach (var body in allBodies)
        {
            if (body == null || combinedChain.Contains(body)) continue;
            TargetJoint2D anchor = body.gameObject.AddComponent<TargetJoint2D>();
            anchor.autoConfigureTarget = false;
            anchor.anchor = Vector2.zero;
            anchor.target = body.position;
            anchor.maxForce = anchorForce;
            anchor.dampingRatio = anchorDamping;
            anchor.frequency = anchorFrequency;
            tempJoints.Add(anchor);
        }

        // Add TargetJoint2D to each hand, pulling toward the player
        foreach (var hand in handBodies)
        {
            TargetJoint2D pull = hand.gameObject.AddComponent<TargetJoint2D>();
            pull.autoConfigureTarget = false;
            pull.anchor = Vector2.zero;
            pull.target = targetPosition;
            pull.maxForce = handPullForce;
            pull.dampingRatio = handPullDamping;
            pull.frequency = handPullFrequency;
            tempJoints.Add(pull);
        }

        // Let the physics solver work over several fixed steps
        for (int i = 0; i < handPhysicsFrames; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        // Clean up all temporary joints
        CleanupTempJoints();

        // Finalize: stabilize and return to kinematic
        frv2.EndFrozenPhysicsPoseEdit();
    }

    /// <summary>
    /// Builds the set of Rigidbody2Ds in the same limb chain as the given body.
    /// Walks DOWN to find jointed children and UP through the HingeJoint2D chain,
    /// stopping at root bodies or branch points (like SmartPoser.BuildLimbChain).
    /// </summary>
    private void BuildLimbChain(Rigidbody2D startBody, HashSet<Rigidbody2D> chain)
    {
        if (startBody == null || frv2.frozenCopy == null) return;
        chain.Add(startBody);

        Rigidbody2D[] allBodies = frv2.frozenCopy.GetComponentsInChildren<Rigidbody2D>(true);

        // Walk DOWN: find children whose HingeJoint2D connects to a chain member
        foreach (var body in allBodies)
        {
            if (body == null || chain.Contains(body)) continue;
            HingeJoint2D childJoint = body.GetComponent<HingeJoint2D>();
            if (childJoint != null && chain.Contains(childJoint.connectedBody))
                chain.Add(body);
        }

        // Walk UP: follow HingeJoint2D.connectedBody, stop at root or branch points
        Rigidbody2D current = startBody;
        while (current != null)
        {
            HingeJoint2D joint = current.GetComponent<HingeJoint2D>();
            if (joint == null || joint.connectedBody == null) break;

            Rigidbody2D parent = joint.connectedBody;

            // Stop at the root body
            if (frv2.IsFrozenRootBody(parent.gameObject)) break;

            // Stop at branch points (body with multiple jointed children)
            int childCount = 0;
            foreach (var body in allBodies)
            {
                if (body == null || body == parent) continue;
                HingeJoint2D j = body.GetComponent<HingeJoint2D>();
                if (j != null && j.connectedBody == parent)
                    childCount++;
            }
            if (childCount > 1) break;

            chain.Add(parent);
            current = parent;
        }
    }

    /// <summary>
    /// Destroys all temporary TargetJoint2D components created during hand pulling.
    /// </summary>
    private void CleanupTempJoints()
    {
        foreach (var joint in tempJoints)
        {
            if (joint != null)
                Destroy(joint);
        }
        tempJoints.Clear();
    }

    /// <summary>
    /// Returns the world position of the player's live fighter root body.
    /// This is the player's starting position for the current turn.
    /// </summary>
    private Vector2 GetPlayerRootPosition()
    {
        if (playerFRV2.playerObj == null) return Vector2.zero;

        Rigidbody2D[] playerBodies = playerFRV2.playerObj.GetComponentsInChildren<Rigidbody2D>(true);
        foreach (var rb in playerBodies)
        {
            if (rb == null) continue;
            string n = rb.name.ToLowerInvariant();
            if (n.Contains("hip") || n.Contains("pelvis") || n.Contains("root"))
                return rb.position;
        }

        return playerBodies.Length > 0 ? playerBodies[0].position : (Vector2)playerFRV2.playerObj.transform.position;
    }

    /// <summary>
    /// Finds a body in the frozen copy by name keywords.
    /// </summary>
    private GameObject FindFrozenBodyByName(params string[] keywords)
    {
        if (frv2.frozenCopy == null) return null;

        Rigidbody2D[] frozenRbs = frv2.frozenCopy.GetComponentsInChildren<Rigidbody2D>(true);
        foreach (var rb in frozenRbs)
        {
            if (rb == null) continue;
            string n = rb.name.ToLowerInvariant();
            foreach (string keyword in keywords)
            {
                if (n.Contains(keyword))
                    return rb.gameObject;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if this fighter is KO'd (no HP remaining).
    /// </summary>
    private bool IsKnockedOut()
    {
        if (frv2.playerObj == null) return true;

        FighterHealth health = frv2.playerObj.GetComponentInChildren<FighterHealth>();
        return health != null && health.IsKO;
    }
}
