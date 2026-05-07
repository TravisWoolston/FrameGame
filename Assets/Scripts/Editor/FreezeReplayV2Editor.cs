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
            "limitFrozenRootMovement",
            "maxRootMovePerSnapshot");

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
            "strikeVelocityMultiplier");

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
            "blockIncomingDamageMultiplier");

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
