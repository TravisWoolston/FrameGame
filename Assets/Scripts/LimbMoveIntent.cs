using UnityEngine;

/// <summary>
/// Per-limb turn intent for FreezeReplayV2.
/// A limb can be Auto-controlled, guard as Basic/Block, be queued as a Strike,
/// or recover for a fixed number of committed snapshots after striking.
/// </summary>
public class LimbMoveIntent : MonoBehaviour
{
    public enum MoveMode
    {
        Auto,
        BasicBlock,
        Strike,
        Recovery
    }

    [Header("=== Runtime State ===")]
    public MoveMode mode = MoveMode.BasicBlock;
    public int recoverySnapshotsRemaining = 0;

    [Header("=== Strike Tuning ===")]
    public float strikeVelocityMultiplier = 2f;
    public float strikeDamageMultiplier = 1.5f;
    public float strikeImpactImpulse = 8f;
    public float blockIncomingDamageMultiplier = 0.75f;

    public Rigidbody2D Body { get; private set; }
    public bool IsActiveStrike { get; private set; }
    public bool WasActiveStrikeThisTurn { get; private set; }
    public bool IsAuto => mode == MoveMode.Auto;
    public bool IsRecovering => recoverySnapshotsRemaining > 0 && !IsActiveStrike;
    public bool CanQueueStrike => recoverySnapshotsRemaining <= 0 && !IsActiveStrike;

    public float CurrentVelocityMultiplier => IsActiveStrike ? Mathf.Max(0.01f, strikeVelocityMultiplier) : 1f;
    public float OutgoingDamageMultiplier => IsActiveStrike ? Mathf.Max(0f, strikeDamageMultiplier) : 1f;
    public float OutgoingImpulse => IsActiveStrike ? Mathf.Max(0f, strikeImpactImpulse) : 0f;
    public float IncomingDamageMultiplier => mode == MoveMode.BasicBlock ? Mathf.Max(0f, blockIncomingDamageMultiplier) : 1f;

    void Awake()
    {
        Body = GetComponent<Rigidbody2D>();
    }

    public void Configure(float velocityMultiplier, float damageMultiplier, float impactImpulse, float blockDamageMultiplier)
    {
        strikeVelocityMultiplier = velocityMultiplier;
        strikeDamageMultiplier = damageMultiplier;
        strikeImpactImpulse = impactImpulse;
        blockIncomingDamageMultiplier = blockDamageMultiplier;
    }

    public void QueueBasicBlock()
    {
        if (IsRecovering) return;
        mode = MoveMode.BasicBlock;
    }

    public void QueueAuto()
    {
        if (IsRecovering) return;
        mode = MoveMode.Auto;
    }

    public bool QueueStrike()
    {
        if (!CanQueueStrike) return false;

        mode = MoveMode.Strike;
        return true;
    }

    public bool ToggleStrikeQueue()
    {
        if (mode == MoveMode.Strike && !IsActiveStrike)
        {
            mode = MoveMode.BasicBlock;
            return true;
        }

        return QueueStrike();
    }

    public void BeginSimulation()
    {
        IsActiveStrike = false;
        WasActiveStrikeThisTurn = false;

        if (mode == MoveMode.Auto)
            return;

        if (mode == MoveMode.Strike && CanQueueStrike)
        {
            IsActiveStrike = true;
            WasActiveStrikeThisTurn = true;
            return;
        }

        mode = recoverySnapshotsRemaining > 0 ? MoveMode.Recovery : MoveMode.BasicBlock;
    }

    public void FinishSnapshot(int recoverySnapshotsAfterStrike)
    {
        if (WasActiveStrikeThisTurn)
        {
            IsActiveStrike = false;
            recoverySnapshotsRemaining = Mathf.Max(0, recoverySnapshotsAfterStrike);
            mode = recoverySnapshotsRemaining > 0 ? MoveMode.Recovery : MoveMode.BasicBlock;
            WasActiveStrikeThisTurn = false;
            return;
        }

        IsActiveStrike = false;

        if (mode == MoveMode.Auto)
        {
            WasActiveStrikeThisTurn = false;
            return;
        }

        if (recoverySnapshotsRemaining > 0)
        {
            recoverySnapshotsRemaining--;
            mode = recoverySnapshotsRemaining > 0 ? MoveMode.Recovery : MoveMode.BasicBlock;
        }
        else if (mode == MoveMode.Recovery)
        {
            mode = MoveMode.BasicBlock;
        }

        WasActiveStrikeThisTurn = false;
    }

    public void ClearRuntimePreview()
    {
        IsActiveStrike = false;
        WasActiveStrikeThisTurn = false;

        if (recoverySnapshotsRemaining > 0)
            mode = MoveMode.Recovery;
    }

    public string GetDisplayName()
    {
        if (IsActiveStrike) return "Strike";
        if (IsRecovering) return $"Recovery {recoverySnapshotsRemaining}";
        if (mode == MoveMode.Auto) return "Auto";
        return mode == MoveMode.Strike ? "Strike" : "Basic/Block";
    }
}
