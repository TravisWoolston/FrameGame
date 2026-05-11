using UnityEngine;
using System.IO;

/// <summary>
/// Serializable preset for FreezeReplayV2 tuning values.
/// Save/load as JSON to [ProjectRoot]/FRV2Presets/.
/// </summary>
[System.Serializable]
public class FRV2Preset
{
    public string presetName = "default";
    public string description = "";
    public string createdAt = "";

    // --- Pose Driving ---
    public float poseKp = 40f;
    public float poseKd = 8f;
    public float poseMaxSpeed = 800f;
    public bool ignoreJointLimits = false;
    public bool limitFrozenRootMovement = true;
    public float maxRootMovePerSnapshot = 2f;

    // --- Physics Pose Assist ---
    public bool enablePhysicsPoseAssist = true;
    public bool poseAssistOnlyRootAndUnjointedBodies = true;
    
    // --- Pose Tethers ---
    public bool enablePoseTethers = false;
    public float poseTetherMaxDistance = 0.05f;
    public bool tetherShrinksOverTurn = true;
    public float startingTetherDistance = 1.0f;
    public float tetherBreakForce = 4000f;

    public float poseAssistSpring = 35f;
    public float poseAssistDamping = 8f;
    public float maxPoseAssistForce = 500f;
    public float poseAssistAngularSpring = 8f;
    public float poseAssistAngularDamping = 1.2f;
    public float maxPoseAssistTorque = 350f;

    // --- Planted Foot ---
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
    public float plantedFootDetachThreshold = 0.4f;
    public bool enableFootReplanting = true;
    public float replantFootFrequency = 3f;
    public float replantFootDamping = 0.9f;
    public float replantFootMaxForce = 800f;
    public float replantFootArrivalDistance = 0.15f;

    // --- Auto Walk ---
    public bool enableAutoWalkFromHipTarget = true;
    public bool useJointMotorsForAutoWalk = true;
    public bool reduceAutoWalkLimbForcesWhenUsingMotors = true;
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

    // --- Auto Leg Driver ---
    public bool driveAutoLegsToGeneratedTargets = true;
    public bool skipAutoLegTargetForPlantedFeet = true;
    public bool driveAutoLegTargetsOnlyDuringWalk = true;
    public bool usePoseDriversForAutoLegs = false;
    public float autoLegTargetSpring = 160f;
    public float autoLegTargetDamping = 18f;
    public float maxAutoLegTargetForce = 1400f;
    public bool rotateAutoLegsTowardGeneratedTargets = false;
    public float autoLegTargetAngularSpring = 10f;
    public float autoLegTargetAngularDamping = 1.5f;
    public float maxAutoLegTargetTorque = 450f;

    // --- Adaptive Leg (legacy forces) ---
    public bool enableAdaptiveLegAssist = true;
    public bool useForceLegAssistWhenNotWalking = false;
    public float legStepSpring = 55f;
    public float legPlantForce = 45f;
    public float legLiftForce = 70f;
    public float maxLegAssistForce = 400f;

    // --- Turn ---
    public float turnDuration = 0.5f;

    // --- Combat ---
    public int strikeRecoverySnapshots = 2;
    public float strikeVelocityMultiplier = 2f;
    public float strikeDamageMultiplier = 1.5f;
    public float strikeImpactImpulse = 8f;
    public float blockIncomingDamageMultiplier = 0.75f;

    // ===== SNAPSHOT FROM FRV2 =====

    public static FRV2Preset CaptureFrom(FreezeReplayV2 frv2, string name = "captured")
    {
        if (frv2 == null) return new FRV2Preset();
        return new FRV2Preset
        {
            presetName = name,
            createdAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            poseKp = frv2.poseKp,
            poseKd = frv2.poseKd,
            poseMaxSpeed = frv2.poseMaxSpeed,
            ignoreJointLimits = frv2.ignoreJointLimits,
            limitFrozenRootMovement = frv2.limitFrozenRootMovement,
            maxRootMovePerSnapshot = frv2.maxRootMovePerSnapshot,
            enablePhysicsPoseAssist = frv2.enablePhysicsPoseAssist,
            poseAssistOnlyRootAndUnjointedBodies = frv2.poseAssistOnlyRootAndUnjointedBodies,
            poseAssistSpring = frv2.poseAssistSpring,
            poseAssistDamping = frv2.poseAssistDamping,
            maxPoseAssistForce = frv2.maxPoseAssistForce,
            poseAssistAngularSpring = frv2.poseAssistAngularSpring,
            poseAssistAngularDamping = frv2.poseAssistAngularDamping,
            maxPoseAssistTorque = frv2.maxPoseAssistTorque,
            enablePlantedFootStability = frv2.enablePlantedFootStability,
            usePlantedFootJoints = frv2.usePlantedFootJoints,
            usePlantedFootSpringFallback = frv2.usePlantedFootSpringFallback,
            plantedFootGroundTolerance = frv2.plantedFootGroundTolerance,
            plantedFootSpring = frv2.plantedFootSpring,
            plantedFootDamping = frv2.plantedFootDamping,
            maxPlantedFootForce = frv2.maxPlantedFootForce,
            plantedFootDeadzone = frv2.plantedFootDeadzone,
            plantedFootVelocityDeadzone = frv2.plantedFootVelocityDeadzone,
            plantedFootJointFrequency = frv2.plantedFootJointFrequency,
            plantedFootJointDampingRatio = frv2.plantedFootJointDampingRatio,
            plantedFootJointBreakForce = frv2.plantedFootJointBreakForce,
            plantedFootJointBreakTorque = frv2.plantedFootJointBreakTorque,
            plantedFootDetachThreshold = frv2.plantedFootDetachThreshold,
            enableFootReplanting = frv2.enableFootReplanting,
            replantFootFrequency = frv2.replantFootFrequency,
            replantFootDamping = frv2.replantFootDamping,
            replantFootMaxForce = frv2.replantFootMaxForce,
            replantFootArrivalDistance = frv2.replantFootArrivalDistance,
            enableAutoWalkFromHipTarget = frv2.enableAutoWalkFromHipTarget,
            useJointMotorsForAutoWalk = frv2.useJointMotorsForAutoWalk,
            reduceAutoWalkLimbForcesWhenUsingMotors = frv2.reduceAutoWalkLimbForcesWhenUsingMotors,
            walkMinHipTravel = frv2.walkMinHipTravel,
            walkMaxHipGroundDistance = frv2.walkMaxHipGroundDistance,
            walkMaxStrideDistance = frv2.walkMaxStrideDistance,
            walkStepLead = frv2.walkStepLead,
            walkFootSpacing = frv2.walkFootSpacing,
            walkStepLift = frv2.walkStepLift,
            walkFootSpring = frv2.walkFootSpring,
            walkFootDamping = frv2.walkFootDamping,
            maxWalkFootForce = frv2.maxWalkFootForce,
            minAutoFootSeparation = frv2.minAutoFootSeparation,
            autoKneeOutwardOffset = frv2.autoKneeOutwardOffset,
            autoCrouchKneeLift = frv2.autoCrouchKneeLift,
            driveAutoLegsToGeneratedTargets = frv2.driveAutoLegsToGeneratedTargets,
            skipAutoLegTargetForPlantedFeet = frv2.skipAutoLegTargetForPlantedFeet,
            driveAutoLegTargetsOnlyDuringWalk = frv2.driveAutoLegTargetsOnlyDuringWalk,
            usePoseDriversForAutoLegs = frv2.usePoseDriversForAutoLegs,
            autoLegTargetSpring = frv2.autoLegTargetSpring,
            autoLegTargetDamping = frv2.autoLegTargetDamping,
            maxAutoLegTargetForce = frv2.maxAutoLegTargetForce,
            rotateAutoLegsTowardGeneratedTargets = frv2.rotateAutoLegsTowardGeneratedTargets,
            autoLegTargetAngularSpring = frv2.autoLegTargetAngularSpring,
            autoLegTargetAngularDamping = frv2.autoLegTargetAngularDamping,
            maxAutoLegTargetTorque = frv2.maxAutoLegTargetTorque,
            enableAdaptiveLegAssist = frv2.enableAdaptiveLegAssist,
            useForceLegAssistWhenNotWalking = frv2.useForceLegAssistWhenNotWalking,
            legStepSpring = frv2.legStepSpring,
            legPlantForce = frv2.legPlantForce,
            legLiftForce = frv2.legLiftForce,
            maxLegAssistForce = frv2.maxLegAssistForce,
            turnDuration = frv2.turnDuration,
            strikeRecoverySnapshots = frv2.strikeRecoverySnapshots,
            strikeVelocityMultiplier = frv2.strikeVelocityMultiplier,
            strikeDamageMultiplier = frv2.strikeDamageMultiplier,
            strikeImpactImpulse = frv2.strikeImpactImpulse,
            blockIncomingDamageMultiplier = frv2.blockIncomingDamageMultiplier
        };
    }

    // ===== APPLY TO FRV2 =====

    public void ApplyTo(FreezeReplayV2 frv2)
    {
        if (frv2 == null) return;
        frv2.poseKp = poseKp;
        frv2.poseKd = poseKd;
        frv2.poseMaxSpeed = poseMaxSpeed;
        frv2.ignoreJointLimits = ignoreJointLimits;
        frv2.limitFrozenRootMovement = limitFrozenRootMovement;
        frv2.maxRootMovePerSnapshot = maxRootMovePerSnapshot;
        frv2.enablePoseTethers = enablePoseTethers;
        frv2.poseTetherMaxDistance = poseTetherMaxDistance;
        frv2.tetherShrinksOverTurn = tetherShrinksOverTurn;
        frv2.startingTetherDistance = startingTetherDistance;
        frv2.tetherBreakForce = tetherBreakForce;

        frv2.poseAssistSpring = poseAssistSpring;
        frv2.poseAssistDamping = poseAssistDamping;
        frv2.maxPoseAssistForce = maxPoseAssistForce;
        frv2.poseAssistAngularSpring = poseAssistAngularSpring;
        frv2.poseAssistAngularDamping = poseAssistAngularDamping;
        frv2.maxPoseAssistTorque = maxPoseAssistTorque;
        frv2.enablePlantedFootStability = enablePlantedFootStability;
        frv2.usePlantedFootJoints = usePlantedFootJoints;
        frv2.usePlantedFootSpringFallback = usePlantedFootSpringFallback;
        frv2.plantedFootGroundTolerance = plantedFootGroundTolerance;
        frv2.plantedFootSpring = plantedFootSpring;
        frv2.plantedFootDamping = plantedFootDamping;
        frv2.maxPlantedFootForce = maxPlantedFootForce;
        frv2.plantedFootDeadzone = plantedFootDeadzone;
        frv2.plantedFootVelocityDeadzone = plantedFootVelocityDeadzone;
        frv2.plantedFootJointFrequency = plantedFootJointFrequency;
        frv2.plantedFootJointDampingRatio = plantedFootJointDampingRatio;
        frv2.plantedFootJointBreakForce = plantedFootJointBreakForce;
        frv2.plantedFootJointBreakTorque = plantedFootJointBreakTorque;
        frv2.plantedFootDetachThreshold = plantedFootDetachThreshold;
        frv2.enableFootReplanting = enableFootReplanting;
        frv2.replantFootFrequency = replantFootFrequency;
        frv2.replantFootDamping = replantFootDamping;
        frv2.replantFootMaxForce = replantFootMaxForce;
        frv2.replantFootArrivalDistance = replantFootArrivalDistance;
        frv2.enableAutoWalkFromHipTarget = enableAutoWalkFromHipTarget;
        frv2.useJointMotorsForAutoWalk = useJointMotorsForAutoWalk;
        frv2.reduceAutoWalkLimbForcesWhenUsingMotors = reduceAutoWalkLimbForcesWhenUsingMotors;
        frv2.walkMinHipTravel = walkMinHipTravel;
        frv2.walkMaxHipGroundDistance = walkMaxHipGroundDistance;
        frv2.walkMaxStrideDistance = walkMaxStrideDistance;
        frv2.walkStepLead = walkStepLead;
        frv2.walkFootSpacing = walkFootSpacing;
        frv2.walkStepLift = walkStepLift;
        frv2.walkFootSpring = walkFootSpring;
        frv2.walkFootDamping = walkFootDamping;
        frv2.maxWalkFootForce = maxWalkFootForce;
        frv2.minAutoFootSeparation = minAutoFootSeparation;
        frv2.autoKneeOutwardOffset = autoKneeOutwardOffset;
        frv2.autoCrouchKneeLift = autoCrouchKneeLift;
        frv2.driveAutoLegsToGeneratedTargets = driveAutoLegsToGeneratedTargets;
        frv2.skipAutoLegTargetForPlantedFeet = skipAutoLegTargetForPlantedFeet;
        frv2.driveAutoLegTargetsOnlyDuringWalk = driveAutoLegTargetsOnlyDuringWalk;
        frv2.usePoseDriversForAutoLegs = usePoseDriversForAutoLegs;
        frv2.autoLegTargetSpring = autoLegTargetSpring;
        frv2.autoLegTargetDamping = autoLegTargetDamping;
        frv2.maxAutoLegTargetForce = maxAutoLegTargetForce;
        frv2.rotateAutoLegsTowardGeneratedTargets = rotateAutoLegsTowardGeneratedTargets;
        frv2.autoLegTargetAngularSpring = autoLegTargetAngularSpring;
        frv2.autoLegTargetAngularDamping = autoLegTargetAngularDamping;
        frv2.maxAutoLegTargetTorque = maxAutoLegTargetTorque;
        frv2.enableAdaptiveLegAssist = enableAdaptiveLegAssist;
        frv2.useForceLegAssistWhenNotWalking = useForceLegAssistWhenNotWalking;
        frv2.legStepSpring = legStepSpring;
        frv2.legPlantForce = legPlantForce;
        frv2.legLiftForce = legLiftForce;
        frv2.maxLegAssistForce = maxLegAssistForce;
        frv2.turnDuration = turnDuration;
        frv2.strikeRecoverySnapshots = strikeRecoverySnapshots;
        frv2.strikeVelocityMultiplier = strikeVelocityMultiplier;
        frv2.strikeDamageMultiplier = strikeDamageMultiplier;
        frv2.strikeImpactImpulse = strikeImpactImpulse;
        frv2.blockIncomingDamageMultiplier = blockIncomingDamageMultiplier;
        Debug.Log($"[FRV2Preset] Applied preset '{presetName}' to FreezeReplayV2.");
    }

    // ===== FILE I/O =====

    public static string PresetsDirectory
    {
        get
        {
            string dir = Path.Combine(Application.dataPath, "..", "FRV2Presets");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public void SaveToFile(string fileName = null)
    {
        if (string.IsNullOrEmpty(fileName))
            fileName = presetName;
        // Sanitize
        foreach (char c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');

        string path = Path.Combine(PresetsDirectory, fileName + ".json");
        string json = JsonUtility.ToJson(this, true);
        File.WriteAllText(path, json);
        Debug.Log($"[FRV2Preset] Saved preset to: {path}");
    }

    public static FRV2Preset LoadFromFile(string fileName)
    {
        string path = Path.Combine(PresetsDirectory, fileName + ".json");
        if (!File.Exists(path))
        {
            Debug.LogError($"[FRV2Preset] Preset file not found: {path}");
            return null;
        }

        string json = File.ReadAllText(path);
        FRV2Preset preset = JsonUtility.FromJson<FRV2Preset>(json);
        Debug.Log($"[FRV2Preset] Loaded preset '{preset.presetName}' from: {path}");
        return preset;
    }

    public static string[] GetAvailablePresetNames()
    {
        string dir = PresetsDirectory;
        string[] files = Directory.GetFiles(dir, "*.json");
        string[] names = new string[files.Length];
        for (int i = 0; i < files.Length; i++)
            names[i] = Path.GetFileNameWithoutExtension(files[i]);
        return names;
    }
}
