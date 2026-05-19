using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FreezeReplayV2))]
public class FreezeReplayV2Editor : Editor
{
    private static bool showReplayAdvanced;
    private static bool showSpawnAdvanced;
    private static bool showPoseMotorAdvanced;
    private static bool showForceAdvanced;
    private static bool showWalkAdvanced;
    private static bool showMoveUiAdvanced;
    private static bool showRigAdvanced;
    private static bool showPresetsSection;
    private static bool showOtherSerializedFields;
    private static int selectedPresetIndex = 0;

    [MenuItem("FRV2/Create Built-in Presets")]
    private static void CreateBuiltInPresets()
    {
        FRV2Preset dualFighter = new FRV2Preset
        {
            presetName = "dual_fighter",
            description = "Two-fighter hit testing. Values match tether_jumping exactly except: ragdollDamageThreshold raised to 60 (avoid mid-preview ragdoll), walkMaxHipGroundDistance/walkMaxStrideDistance set to 0 (auto-sized per rig), and strike impulse tuned for a more visible snap.",
            createdAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

            // === Pose Driving — from tether_jumping ===
            poseKp = 180f,
            poseKd = 35f,
            poseMaxSpeed = 800f,
            ignoreJointLimits = true,
            limitFrozenRootMovement = false,
            maxRootMovePerSnapshot = 2f,

            // === Physics Pose Assist — from tether_jumping ===
            enablePhysicsPoseAssist = true,
            poseAssistOnlyRootAndUnjointedBodies = true,
            poseAssistSpring = 85f,
            poseAssistDamping = 25f,
            maxPoseAssistForce = 1200f,
            poseAssistAngularSpring = 150f,
            poseAssistAngularDamping = 30f,
            maxPoseAssistTorque = 1800f,
            jumpForceMultiplier = 1.5f,
            jumpExtensionThreshold = 0.9f,
            // Raised vs tether_jumping (25): prevents preview loop triggering ragdoll on moderate hits
            ragdollDamageThreshold = 60f,

            // === Pose Tethers — from tether_jumping ===
            enablePoseTethers = true,
            poseTetherMaxDistance = 0.05f,
            tetherShrinksOverTurn = true,
            startingTetherDistance = 1.0f,
            tetherBreakForce = 5000f,

            // === Planted Foot — from tether_jumping ===
            enablePlantedFootStability = true,
            usePlantedFootJoints = true,
            usePlantedFootSpringFallback = false,
            plantedFootGroundTolerance = 1.2f,
            plantedFootSpring = 120f,
            plantedFootDamping = 0f,
            maxPlantedFootForce = 0f,
            plantedFootDeadzone = 0.04f,
            plantedFootVelocityDeadzone = 0.08f,
            plantedFootJointFrequency = 8f,
            plantedFootJointDampingRatio = 1f,
            plantedFootJointBreakForce = 3500f,
            plantedFootJointBreakTorque = 1800f,
            plantedFootDetachThreshold = 0.4f,
            enableFootReplanting = true,
            replantFootFrequency = 3f,
            replantFootDamping = 0.9f,
            replantFootMaxForce = 800f,
            replantFootArrivalDistance = 0.15f,

            // === Auto Walk — from tether_jumping, except distances set to 0 ===
            // 0 triggers Start() to auto-calculate from each rig's anatomical leg length.
            enableAutoWalkFromHipTarget = true,
            useJointMotorsForAutoWalk = true,
            reduceAutoWalkLimbForcesWhenUsingMotors = true,
            walkMinHipTravel = 0.35f,
            walkMaxHipGroundDistance = 0f,
            walkMaxStrideDistance = 0f,
            walkStepLead = 0.85f,
            walkFootSpacing = 3.0f,
            walkStepLift = 0.8f,
            walkFootSpring = 0f,
            walkFootDamping = 0f,
            maxWalkFootForce = 0f,
            minAutoFootSeparation = 0.85f,
            autoKneeOutwardOffset = 0f,
            autoCrouchKneeLift = 0.35f,

            // === Auto Leg Driver — from tether_jumping ===
            driveAutoLegsToGeneratedTargets = true,
            skipAutoLegTargetForPlantedFeet = true,
            driveAutoLegTargetsOnlyDuringWalk = true,
            usePoseDriversForAutoLegs = true,
            autoLegTargetSpring = 0f,
            autoLegTargetDamping = 0f,
            maxAutoLegTargetForce = 0f,
            rotateAutoLegsTowardGeneratedTargets = false,
            autoLegTargetAngularSpring = 0f,
            autoLegTargetAngularDamping = 0f,
            maxAutoLegTargetTorque = 0f,

            // === Adaptive Leg — zeroed in tether_jumping ===
            enableAdaptiveLegAssist = true,
            useForceLegAssistWhenNotWalking = false,
            legStepSpring = 0f,
            legPlantForce = 0f,
            legLiftForce = 0f,
            maxLegAssistForce = 0f,

            // === Turn — from tether_jumping ===
            turnDuration = 1.0f,

            // === Combat — slightly elevated vs tether_jumping for clearer hit reads ===
            strikeRecoverySnapshots = 2,
            strikeVelocityMultiplier = 2f,
            strikeDamageMultiplier = 1.8f,
            strikeImpactImpulse = 10f,
            blockIncomingDamageMultiplier = 0.75f,

            // === Strike Impulse — tuned for visible snap across both fighters ===
            strikeImpulseAngularSpeed = 1400f,
            strikeImpulseFrames = 8,

            // === Hit Stun — unplant + local tethers + impulse on strike impact ===
            hitStunDurationPerDamage = 0.08f,
            maxHitStunDuration = 1.0f,
            hitStunImpulsePerDamage = 3f,
            maxHitStunImpulse = 40f,
        };
        dualFighter.SaveToFile();
        Debug.Log("[FRV2Editor] Created preset 'dual_fighter'.");
        AssetDatabase.Refresh();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        HashSet<string> drawn = new HashSet<string>();
        DrawScriptReference(drawn);

        EditorGUILayout.HelpBox(
            "FRV2 is currently tuned around marker-built fighters, low-force pose correction, planted-foot joints, hip-driven walking, and the move planning UI. The foldouts below keep older/experimental tuning available without making the normal inspector a wall of knobs.",
            MessageType.Info);

        DrawSection(drawn, "Core Setup",
            "playerPrefab",
            "spawnOffset",
            "frozenCopyColor",
            "centerMainCameraOnSnapshotStart",
            "groundMask");

        DrawSection(drawn, "Turn Loop",
            "turnDuration",
            "commitKey",
            "replayKey",
            "resetKey",
            "enablePosePreviewLoop",
            "previewLoopDuration",
            "replayOnCapture");

        DrawSection(drawn, "Frozen Copy Editing",
            "ignoreJointLimits",
            "enableEditInput",
            "limitFrozenRootMovement",
            "maxRootMovePerSnapshot");

        DrawSection(drawn, "Master / Slave",
            "isMasterFRV2",
            "masterFRV2");

        DrawSection(drawn, "Low-Force Movement",
            "enablePhysicsPoseAssist",
            "poseAssistOnlyRootAndUnjointedBodies",
            "enablePlantedFootStability",
            "usePlantedFootJoints",
            "plantedFootGroundTolerance",
            "plantedFootJointFrequency",
            "plantedFootJointDampingRatio",
            "enableAutoWalkFromHipTarget",
            "useJointMotorsForAutoWalk",
            "driveAutoLegsToGeneratedTargets");

        DrawSection(drawn, "Move Planning UI",
            "showMovePlanningUI",
            "moveUIStartMinimized",
            "defaultLegsToAuto",
            "strikeRecoverySnapshots",
            "strikeVelocityMultiplier",
            "strikeImpulseAngularSpeed",
            "strikeImpulseFrames");

        DrawSection(drawn, "Safety",
            "validateRigOnStart",
            "disableLegacyMotorControllers");

        DrawPresetSection(drawn);

        if (DrawLowForceDefaultsButton())
            serializedObject.Update();
        DrawRuntimeDebug(drawn);

        DrawAdvancedSection(ref showReplayAdvanced, drawn, "Advanced Replay / Snapshot",
            "enableHealthSnapshots",
            "syncReplayToFixedStep",
            "skipDuplicateReplayFrames",
            "duplicateReplayPositionTolerance",
            "duplicateReplayRotationTolerance",
            "disableDamageDuringPreview");

        DrawAdvancedSection(ref showSpawnAdvanced, drawn, "Advanced Spawn / Ground Probe",
            "snapRuntimeRigFeetToGround",
            "spawnGroundProbeHeight",
            "spawnGroundProbeDistance",
            "spawnGroundClearance",
            "groundProbeHeight",
            "groundProbeDistance",
            "footGroundOffset",
            "fallbackGroundProbeToAllLayers",
            "preventFrozenCopyGroundPenetration",
            "frozenCopyGroundClearance");

        DrawAdvancedSection(ref showPoseMotorAdvanced, drawn, "Advanced Joint Motors / PoseDriver",
            "poseKp",
            "poseKd",
            "poseMaxSpeed",
            "usePoseDriversForAutoLegs",
            "reduceAutoWalkLimbForcesWhenUsingMotors",
            "rotateFrozenAutoLegsToGeneratedTargets",
            "rotateAutoLegsTowardGeneratedTargets",
            "autoLegTargetAngularSpring",
            "autoLegTargetAngularDamping",
            "maxAutoLegTargetTorque");

        DrawAdvancedSection(ref showForceAdvanced, drawn, "Advanced Force Assist",
            "poseAssistSpring",
            "poseAssistDamping",
            "maxPoseAssistForce",
            "poseAssistAngularSpring",
            "poseAssistAngularDamping",
            "maxPoseAssistTorque",
            "enableAdaptiveLegAssist",
            "minStepDistance",
            "legStepSpring",
            "legPlantForce",
            "legLiftForce",
            "maxLegAssistForce",
            "useForceLegAssistWhenNotWalking",
            "usePlantedFootSpringFallback",
            "plantedFootSpring",
            "plantedFootDamping",
            "maxPlantedFootForce",
            "plantedFootDeadzone",
            "plantedFootVelocityDeadzone",
            "plantedFootJointBreakForce",
            "plantedFootJointBreakTorque",
            "plantedFootDetachThreshold",
            "enableFootReplanting",
            "replantFootFrequency",
            "replantFootDamping",
            "replantFootMaxForce",
            "replantFootArrivalDistance",
            "skipAutoLegTargetForPlantedFeet",
            "driveAutoLegTargetsOnlyDuringWalk",
            "autoLegTargetSpring",
            "autoLegTargetDamping",
            "maxAutoLegTargetForce");

        DrawAdvancedSection(ref showWalkAdvanced, drawn, "Advanced Auto Walk Shape",
            "walkMinHipTravel",
            "walkMaxHipGroundDistance",
            "walkStepLead",
            "walkFootSpacing",
            "walkStepLift",
            "walkFootSpring",
            "walkFootDamping",
            "maxWalkFootForce",
            "minAutoFootSeparation",
            "autoKneeOutwardOffset",
            "autoCrouchKneeLift");

        DrawAdvancedSection(ref showMoveUiAdvanced, drawn, "Advanced Move UI / Combat",
            "tintFrozenLimbsByIntent",
            "moveUIPanelOffset",
            "moveUIPanelWidth",
            "moveUIMinimizedWidth",
            "moveUIMinimizedHeight",
            "strikeDamageMultiplier",
            "strikeImpactImpulse",
            "blockIncomingDamageMultiplier",
            "ragdollDamageThreshold",
            "hitStunDurationPerDamage",
            "maxHitStunDuration",
            "hitStunImpulsePerDamage",
            "maxHitStunImpulse");

        DrawAdvancedSection(ref showRigAdvanced, drawn, "Advanced Rig Diagnostics",
            "logRigValidationDetails");

        DrawRemainingSerializedFields(drawn);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawScriptReference(HashSet<string> drawn)
    {
        SerializedProperty script = serializedObject.FindProperty("m_Script");
        if (script == null) return;

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.PropertyField(script);
        EditorGUI.EndDisabledGroup();
        drawn.Add("m_Script");
    }

    private void DrawSection(HashSet<string> drawn, string title, params string[] propertyNames)
    {
        GUILayout.Space(6f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

        EditorGUI.indentLevel++;
        DrawProperties(drawn, propertyNames);
        EditorGUI.indentLevel--;
    }

    private void DrawAdvancedSection(ref bool foldout, HashSet<string> drawn, string title, params string[] propertyNames)
    {
        GUILayout.Space(4f);
        foldout = EditorGUILayout.Foldout(foldout, title, true);
        if (!foldout) return;

        EditorGUI.indentLevel++;
        DrawProperties(drawn, propertyNames);
        EditorGUI.indentLevel--;
    }

    private void DrawProperties(HashSet<string> drawn, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            DrawProperty(drawn, propertyName);
        }
    }

    private void DrawProperty(HashSet<string> drawn, string propertyName)
    {
        if (drawn.Contains(propertyName)) return;

        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null) return;

        EditorGUILayout.PropertyField(property, true);
        drawn.Add(property.propertyPath);
    }

    private bool DrawLowForceDefaultsButton()
    {
        GUILayout.Space(8f);
        if (!GUILayout.Button("Apply Current Low-Force Defaults")) return false;

        foreach (Object targetObject in targets)
        {
            FreezeReplayV2 controller = targetObject as FreezeReplayV2;
            if (controller == null) continue;

            Undo.RecordObject(controller, "Apply FRV2 Low-Force Defaults");
            controller.ApplyCurrentLowForceDefaults();
            EditorUtility.SetDirty(controller);
        }

        return true;
    }

    private void DrawPresetSection(HashSet<string> drawn)
    {
        GUILayout.Space(8f);
        EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);

        EditorGUI.indentLevel++;
        DrawProperty(drawn, "autoLoadPresetName");

        string presetsDir = FRV2Preset.PresetsDirectory;
        string[] presetNames = FRV2Preset.GetAvailablePresetNames();
        EditorGUILayout.HelpBox($"Preset folder: {presetsDir}\nPresets found: {presetNames.Length}", MessageType.None);

        if (presetNames.Length > 0)
        {
            selectedPresetIndex = Mathf.Clamp(selectedPresetIndex, 0, presetNames.Length - 1);
            selectedPresetIndex = EditorGUILayout.Popup("Available Presets", selectedPresetIndex, presetNames);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Load Selected Preset"))
            {
                foreach (Object targetObject in targets)
                {
                    FreezeReplayV2 controller = targetObject as FreezeReplayV2;
                    if (controller == null) continue;
                    Undo.RecordObject(controller, "Load FRV2 Preset");
                    controller.LoadPreset(presetNames[selectedPresetIndex]);
                    EditorUtility.SetDirty(controller);
                }
                serializedObject.Update();
            }

            if (GUILayout.Button("Set As Auto-Load"))
            {
                serializedObject.FindProperty("autoLoadPresetName").stringValue = presetNames[selectedPresetIndex];
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Broadcast Preset To All FRV2s In Scene"))
            {
                FreezeReplayV2[] all = Object.FindObjectsByType<FreezeReplayV2>(FindObjectsSortMode.None);
                foreach (FreezeReplayV2 frv2 in all)
                {
                    Undo.RecordObject(frv2, "Broadcast FRV2 Preset");
                    frv2.LoadPreset(presetNames[selectedPresetIndex]);
                    EditorUtility.SetDirty(frv2);
                }
                serializedObject.Update();
                Debug.Log($"[FRV2Editor] Broadcast preset '{presetNames[selectedPresetIndex]}' to {all.Length} FRV2 instance(s).");
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No presets found. Play a few turns with FRV2Diagnostics to generate one, or use the Save button below.", MessageType.Info);
        }

        if (GUILayout.Button("Save Current Settings As Preset"))
        {
            foreach (Object targetObject in targets)
            {
                FreezeReplayV2 controller = targetObject as FreezeReplayV2;
                if (controller == null) continue;
                controller.SaveCurrentPreset();
            }
        }

        EditorGUI.indentLevel--;
    }

    private void DrawRuntimeDebug(HashSet<string> drawn)
    {
        GUILayout.Space(8f);
        EditorGUILayout.LabelField("Runtime Debug", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(true);
        EditorGUI.indentLevel++;
        DrawProperties(drawn, "currentPhase", "turnCount");
        EditorGUI.indentLevel--;
        EditorGUI.EndDisabledGroup();
    }

    private void DrawRemainingSerializedFields(HashSet<string> drawn)
    {
        GUILayout.Space(4f);
        showOtherSerializedFields = EditorGUILayout.Foldout(showOtherSerializedFields, "Other Serialized Fields", true);
        if (!showOtherSerializedFields) return;

        EditorGUI.indentLevel++;

        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (drawn.Contains(iterator.propertyPath)) continue;
            if (iterator.propertyPath == "playerObj"
                || iterator.propertyPath == "frozenCopy"
                || iterator.propertyPath == "trackedBodies")
                continue;

            EditorGUILayout.PropertyField(iterator, true);
            drawn.Add(iterator.propertyPath);
        }

        EditorGUI.indentLevel--;
    }
}
