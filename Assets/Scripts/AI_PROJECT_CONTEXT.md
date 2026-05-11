# Frame Game AI Handoff

This document is for future language-model assistants working on this Unity project. It summarizes the current direction, the important scripts, and the fragile areas that should be understood before changing code.

## Project Goal

The game is a 2D turn-based stick-fighter inspired by `Your Only Move Is Hustle`.

The desired loop is:

1. Player edits a translucent frozen copy of the fighter.
2. The live fighter repeatedly previews what will happen from the current snapshot toward that frozen target.
3. Player commits the turn.
4. The live fighter simulates for a fixed duration.
5. The resulting live pose is captured as the next snapshot.
6. The full motion can be replayed as a final animation.

The current direction favors:

- marker-authored fighters that build their physics rig at runtime
- **angle-driven movement** via PoseDriver hinge motors as the primary control method
- minimal force — only the root/hips body gets translational force to maintain structure
- planted feet using temporary FixedJoint2D for ground contact stability
- Auto legs used primarily for walking/standing/upper-body positioning
- upper-body limbs manually poseable as Basic/Block or Strike
- **chain-isolated dragging** — moving a hand only affects its arm chain; moving a foot only affects its leg chain

## Current Architecture

### `FreezeReplayV2.cs`

This is the main controller. Start here for most gameplay issues.

Responsibilities:

- instantiates the live fighter prefab
- optionally builds a runtime rig from marker-only authoring points
- clones the live fighter into the frozen copy
- manages turn phases: `Posing`, `Simulating`, `Capturing`
- records replay frames on `FixedUpdate`
- runs the posing preview loop
- wires `PoseDriver` components
- manages `LimbMoveIntent` state
- drives Auto-leg walking
- handles planted foot joints
- stabilizes and clamps the frozen copy
- draws the simple IMGUI move-planning UI
- **loads/saves presets** via `FRV2Preset` system
- **auto-loads a named preset on Start** via `autoLoadPresetName`

Important methods:

- `Start()`: instantiate/build/wire initial fighter and frozen copy; calls `TryAutoLoadPreset()`
- `CommitPose()`: stop preview, restore snapshot, begin committed sim
- `StartPosePreviewLoop()`: stabilize frozen copy, restore snapshot, simulate preview
- `FixedUpdate()`: applies assist systems and advances committed/preview fixed steps
- `BeginLimbPlansForSimulation()`: begins limb intents, caches planted feet, creates planted-foot joints
- `ApplyAdaptiveLegAssist()`: decides whether Auto-walk is active
- `ApplyAutoWalkLegAssist()`: current Auto-walk driver
- `GetAutoKneeTarget()`: 2-bone IK for knee placement — knees go UP (y-axis), with small lateral offset for visual separation
- `StabilizeFrozenCopyPose()`: clamps frozen copy root, joint limits, joint anchors, and floor penetration
- `ApplyCurrentLowForceDefaults()`: context-menu/reset button for the current intended tuning
- `SaveCurrentPreset()` / `LoadPreset()`: preset file I/O

The custom inspector in `Editor/FreezeReplayV2Editor.cs` hides many experimental fields in foldouts. The fields still exist and are serialized. The inspector also has a **Presets** section for loading/saving presets.

### `FRV2Preset.cs`

Serializable preset system for FRV2 tuning values.

- `FRV2Preset.CaptureFrom(frv2)`: snapshot all current settings into a preset
- `preset.ApplyTo(frv2)`: apply a preset to FRV2
- `preset.SaveToFile()` / `FRV2Preset.LoadFromFile(name)`: JSON files in `[ProjectRoot]/FRV2Presets/`
- `FRV2Preset.GetAvailablePresetNames()`: lists saved presets

Presets cover: PoseDriver gains, pose assist springs/forces, planted foot joints, auto-walk, adaptive leg assist, combat multipliers, turn duration, and more.

### `FRV2Diagnostics.cs`

Runtime telemetry and auto-tuning system. Attach to the same GameObject as FRV2.

- Samples tracking error (position + angular) every `FixedUpdate` during simulation
- Computes per-turn summaries with per-body metrics (avg/max error, oscillation count)
- Detects preview-vs-commit divergence
- Writes JSON reports to `[ProjectRoot]/FRV2Diagnostics/`
- Generates **optimized presets** based on heuristic analysis of telemetry data
- F8: write report; F9: generate optimized preset

Heuristic rules:
- High position error → suggest increasing poseAssistSpring / maxPoseAssistForce
- Oscillation → suggest increasing poseKd / poseAssistDamping
- Force saturation → suggest increasing force caps
- Damping-to-spring ratio checks
- Walk-specific leg error analysis

### `FighterRuntimeRigBuilder.cs`

Builds a playable physics fighter from marker-only prefabs.

Expected marker names include:

- `Hips`
- `Shoulders`
- `Head`
- `Elbow L`, `Elbow R`
- `Hand L`, `Hand R`
- `Knee L`, `Knee R`
- `Foot L`, `Foot R`

The builder creates:

- `Rigidbody2D`
- colliders
- sprite visuals
- `HingeJoint2D`
- optional `ImpactDamage`
- optional `FighterHealth`
- optional `LimbMoveIntent`
- `FighterRuntimeRigInstance` for self-collision ignores

`FreezeReplayV2` auto-adds this builder when the assigned prefab looks like a marker-only fighter and has no child `Rigidbody2D` components. If a `FighterRuntimeRigBuilder` is also placed on the same GameObject as `FreezeReplayV2`, its settings are copied to the spawned fighter builder.

### `SmartPoser.cs`

Handles mouse interaction with the frozen copy.

Current behavior:

- root/hips drag is handled directly through `FreezeReplayV2.BeginFrozenRootPoseEdit`, `UpdateFrozenRootPoseEdit`, and `EndFrozenRootPoseEdit`
- non-root limb drag temporarily switches the frozen copy to `Dynamic`, creates a `TargetJoint2D`, then returns the frozen copy to `Kinematic`
- after release, it calls `FreezeReplayV2.StabilizeFrozenCopyPose()` and requests preview restart
- **chain-isolated dragging**: when dragging, `BuildLimbChain()` identifies all bodies in the same kinematic chain (e.g., Hand → Lower Arm → Arm), then anchors all bodies NOT in that chain with `TargetJoint2D` anchors, ensuring only the dragged limb chain moves

Chain isolation logic:
- Walks DOWN from dragged body to find HingeJoint2D children
- Walks UP via `connectedBody` links, stopping at root bodies or branch points (bodies with multiple jointed children like hips/spine)
- All non-chain bodies get anchored in place

**Component placement**: SmartPoser lives on the **same GameObject as FRV2**. It references FRV2 to access the frozen copy and call BeginFrozenPhysicsPoseEdit / EndFrozenPhysicsPoseEdit. Its settings (dragForce, frequency, etc.) are specific to the drag interaction and do NOT overlap with FRV2's simulation settings.

Important: frozen copy should not be left dynamic after a drag. If frozen limbs drift after release, check the `BeginFrozenPhysicsPoseEdit` / `EndFrozenPhysicsPoseEdit` path.

### `PoseDriver.cs`

A PD motor controller on live hinge-jointed limbs. It drives a live `HingeJoint2D` toward the matching frozen-copy `HingeJoint2D`.

**Component placement**: PoseDriver instances are added to **each limb body of the LIVE fighter** (not the frozen copy, not the FRV2 controller). They are automatically wired by `FreezeReplayV2.WirePoseDrivers()` during setup. There is also a `PoseDriverSetup.cs` utility script, but FRV2 handles wiring directly.

`FreezeReplayV2` wires these in `WirePoseDrivers()` and re-applies:

- `poseKp`
- `poseKd`
- `poseMaxSpeed`
- `ignoreJointLimits`

Current relevance:

- **Primary control method** in the angle-driven preset — all jointed limbs are driven by PoseDriver hinge motors
- Auto legs use PoseDriver when `usePoseDriversForAutoLegs = true`
- Auto-walk also has its own joint-motor path controlled by `useJointMotorsForAutoWalk`
- PoseDriver settings on FRV2 (poseKp, poseKd, etc.) are global and override the per-PoseDriver values during `ApplyPoseDriverSettings()`

### `LimbMoveIntent.cs`

Per-limb action state.

Modes:

- `Auto`
- `BasicBlock`
- `Strike`
- `Recovery`

Strike behavior:

- strike limbs get a velocity multiplier during simulation
- strike damage and impulse are applied through `ImpactDamage`
- after a strike, the limb enters recovery for `strikeRecoverySnapshots`

Legs default to `Auto` when `defaultLegsToAuto` is enabled. In Auto, legs are not manually poseable through `SmartPoser`.

### `ImpactDamage.cs`

Collision damage and strike impulse.

Important preview detail:

- preview mode suppresses HP/UI effects
- strike impulses still run during preview so preview physics better matches committed simulation

This is controlled through `SetPreviewSimulationMode()`, called by `FreezeReplayV2.SetPreviewDamageEnabled()`.

Note: damage cooldown uses `Time.time` which can cause minor divergence between preview and commit because the same collision might register at a different wall-clock moment.

## Editor Setup

Typical scene setup:

1. Create a controller GameObject.
2. Add `FreezeReplayV2`.
3. Add `SmartPoser` to the same GameObject.
4. Optionally add `FRV2Diagnostics` to the same GameObject for telemetry.
5. Assign `FreezeReplayV2.playerPrefab`.
6. Set `FreezeReplayV2.groundMask` to the real ground layer.
7. Optional: add `FighterRuntimeRigBuilder` to the same controller GameObject if you want its inspector values copied into spawned marker rigs.

For marker-only fighters:

- assign a prefab containing the authoring marker points listed above
- the prefab does not need child physics components
- `FreezeReplayV2` will build the generated runtime rig under `_GeneratedRig`

For older prebuilt fighters:

- ensure child bodies have `Rigidbody2D`, `Collider2D`, and `HingeJoint2D`
- ensure hierarchy/order matches between live clone and frozen clone
- avoid old limb motor scripts fighting `FreezeReplayV2`

## Turn And Preview Flow

`FreezeReplayV2` uses `FixedUpdate` for committed capture and preview stepping.

Preview:

1. `StartPosePreviewLoop()` stabilizes the frozen copy and restores `currentSnapshot`.
2. It begins limb plans and creates planted foot joints.
3. It unfreezes live bodies.
4. Fixed steps run for the same count as a committed turn.
5. It restarts from the snapshot unless the player commits or changes the frozen copy.

Commit:

1. `CommitPose()` stops preview, stabilizes frozen copy, and restores snapshot.
2. It hides the frozen copy and then begins limb plans from the same frozen target.
3. It simulates exactly `turnDuration` using `FixedUpdate`.
4. `UpdateCapturing()` copies live to frozen, captures snapshot, and returns to posing.

Preview-vs-commit alignment (fixed in this session):
- Both paths now call `StabilizeFrozenCopyPose()` before starting
- Frozen copy is hidden (colliders disabled) before `BeginLimbPlansForSimulation()` in commit
- The order of operations in commit now matches preview more closely

If preview and capture still do not match, check:

- whether `ImpactDamage` cooldown timing via `Time.time` differs between paths
- whether some script behaves differently when `disableDamageDuringPreview` is active
- whether runtime collision ignores were reapplied
- whether `turnDuration` and `Time.fixedDeltaTime` produce the expected number of fixed steps

## Frozen Copy Rules

The frozen copy is a planning ghost, not a fully simulated body.

Current intended behavior:

- visible/idle frozen copy is `Kinematic`
- non-root limb drag temporarily makes it `Dynamic`
- root/hip drag is handled kinematically
- after edits, `StabilizeFrozenCopyPose()`:
  - clamps root movement
  - clamps joint limits unless `ignoreJointLimits` is on
  - reconciles hinge anchors so connected limbs stay attached
  - lifts the ghost above the detected floor if `preventFrozenCopyGroundPenetration` is on
  - zeros velocities

If the frozen copy goes through the floor:

- check `groundMask`
- check `preventFrozenCopyGroundPenetration`
- check `frozenCopyGroundClearance`
- check `TryGetGroundPoint()` is not hitting fighter colliders instead of ground

## Auto-Walk and Auto-Leg IK

Auto-walk is experimental and is one of the more fragile current systems.

### Activation conditions

- `enableAutoWalkFromHipTarget` is true
- at least one planted foot was detected at snapshot start
- frozen hip/root moved horizontally by at least `walkMinHipTravel`
- hip target is within leg range of the floor

### Current Auto-walk behavior

- one foot is selected as the swing foot
- planted foot bodies can get temporary `FixedJoint2D` anchors
- the swing foot's planted joint is released
- `ApplyAutoWalkLegAssist()` drives walking
- `useJointMotorsForAutoWalk` rotates Auto thigh/calf/foot joints toward generated targets
- `reduceAutoWalkLimbForcesWhenUsingMotors` reduces direct forces on thigh/calf so joints do more of the work

### Knee IK (GetAutoKneeTarget)

Uses 2-bone IK triangle math to compute knee position:
- IK height = `√(halfLeg² - halfDist²)` applied **perpendicular** to the hip→foot line
- The perpendicular is chosen so knees always have a positive y-component (bend forward/up, never behind/under)
- When standing upright: hip-foot line is vertical → perpendicular is horizontal → knees go forward
- When crouching: hip-foot line is more horizontal → perpendicular is more vertical → knees go up naturally
- Small lateral offset (`autoKneeOutwardOffset * 0.3f`) for visual leg separation
- Outward offset decreases during deep crouch (`1 - crouch * 0.5`)
- Swing leg gets additional lift during walk phase

If calves arch wrong or legs do the splits, the issue is in `GetAutoKneeTarget()`.

### Troubleshooting

If calves rotate around knees while thighs do not move:

- verify thigh bodies are named `Thigh` / `Thigh 2`
- verify thigh `HingeJoint2D.connectedBody` is hips
- verify `useJointMotorsForAutoWalk` is enabled
- verify `poseKp`, `poseKd`, and `poseMaxSpeed` are not too weak
- verify the thigh joint motor torque from `FighterRuntimeRigBuilder.defaultMotorTorque` is not too low
- check `ignoreJointLimits`: limits may prevent desired thigh rotation

## Planted Foot System

### Current behavior

- Detected at the start of a preview/turn from `currentSnapshot` by checking which feet are within `plantedFootGroundTolerance` of the ground
- Get temporary `FixedJoint2D` world anchors when planted
- The selected swing foot releases its planted joint during walking
- Joints break when force/torque exceed `plantedFootJointBreakForce` / `plantedFootJointBreakTorque`

### Intent-Based Foot Detachment

At the start of each simulation/preview, `PreReleasePlantedFeetByFrozenTarget()` compares each planted foot's anchor position (from `CachePlantedFootTargets`) to the frozen copy's current foot position. If the frozen foot has moved beyond `plantedFootDetachThreshold`, the foot is removed from `plantedFootTargets` before `CreatePlantedFootJoints()` runs — so no FixedJoint2D is created for it, and the foot is free to chase its new target.

- **`plantedFootDetachThreshold`** (default: 0.4): distance threshold for intent-based release. Set to 0 to disable.
- Called from `BeginLimbPlansForSimulation()` between `CachePlantedFootTargets()` and `CreatePlantedFootJoints()`
- Works for both preview and committed simulation paths
- Logged to console when a foot is pre-released
- Included in the preset system (`FRV2Preset`)

Feet can still break free via FixedJoint2D break force/torque during simulation if needed.

### Spine-to-Hips Weakness

The HingeJoint2D connecting the spine to the hips can be weak, causing the torso to slouch. This is a known issue with the angle-driven preset.

Potential fixes:
- Increase PoseDriver `poseKp` specifically for the spine joint (requires per-joint tuning, not currently supported by global FRV2 settings)
- Increase the spine joint's `maxMotorTorque` in `FighterRuntimeRigBuilder`
- Apply a small amount of angular pose assist specifically to spine/torso bodies

## Preset System

### File locations

- Presets: `[ProjectRoot]/FRV2Presets/*.json`
- Diagnostics reports: `[ProjectRoot]/FRV2Diagnostics/*.json`

### Current active preset: `angle_driven_minimal_force`

Philosophy: **angle-primary with minimal root support**.

Key settings:
- `ignoreJointLimits: true` — joint limits off for now
- `poseKp: 55, poseKd: 14` — moderate PD gains
- `enablePhysicsPoseAssist: true` but `poseAssistOnlyRootAndUnjointedBodies: true` — **only root/hips get translational force**; all jointed limbs are purely motor-driven
- `usePoseDriversForAutoLegs: true` — legs use PoseDriver motors
- `useJointMotorsForAutoWalk: true` — walking via joint motors
- All walk/leg force values zeroed — no force-driving of limbs
- `enablePlantedFootStability: true, usePlantedFootJoints: true` — FixedJoint2D for planted feet
- Planted foot spring fallback disabled

### How to use presets

1. Play a few turns with FRV2Diagnostics attached
2. Press F8 for report + auto-generated optimized preset, or F9 for preset only
3. Open Presets section in FRV2 inspector → select from dropdown → Load
4. Set `autoLoadPresetName` to auto-apply on Start

## Component Relationships (Same GameObject)

All three of these live on the **same controller GameObject**:

| Component | Purpose | Settings overlap? |
|---|---|---|
| `FreezeReplayV2` | Turn loop, simulation, assist systems, preset loading | Master — its poseKp/Kd/maxSpeed override all PoseDrivers |
| `SmartPoser` | Frozen copy drag-to-pose UI | **No overlap** — dragForce/frequency are for the TargetJoint2D drag spring, not simulation |
| `PoseDriverSetup` | Utility for wiring PoseDrivers | **Mostly unused** — FRV2.WirePoseDrivers() handles this directly |

PoseDriver instances live on **each limb body of the live fighter**, not on the controller. FRV2 creates/wires them during setup.

## Low-Force / Stability Notes

The project has moved toward angle-driven control with minimal force.

Current defaults (angle-driven preset):

- `poseAssistOnlyRootAndUnjointedBodies = true` — force only on root
- `useForceLegAssistWhenNotWalking = false`
- `usePlantedFootJoints = true`
- `usePlantedFootSpringFallback = false`
- `driveAutoLegTargetsOnlyDuringWalk = true`
- `usePoseDriversForAutoLegs = true`
- `useJointMotorsForAutoWalk = true`

If feet stick to the floor:

- check `autoWalkActive`
- check `autoWalkSwingFoot`
- check `ReleasePlantedFootJoint()`
- check `plantedFootGroundTolerance`
- check whether the actual foot body is named `Foot` / `Foot 2`; if no foot exists, calves are treated as contact legs
- **check if frozen foot has moved** — may need smarter detachment logic (see Planted Foot section)

## Combat / Limb UI

The move-planning UI is immediate-mode GUI drawn by `FreezeReplayV2.OnGUI()`.

It supports:

- minimization
- Auto toggle for legs
- Basic/Block
- Strike
- recovery display
- angle delta bar between live joint and frozen target joint

Combat intent is stored on `LimbMoveIntent`.

Strike effects:

- velocity multiplier for PoseDrivers
- outgoing damage multiplier
- outgoing impulse
- recovery for `strikeRecoverySnapshots`

## Legacy Scripts

This project contains many older experimental scripts, including:

- `LeftThighScript.cs`
- `RightThighScript.cs`
- `RotateHingeJoint.cs`
- `PoseDriverSetup.cs`
- older `FreezeReplay.cs`
- balance/boot/agent scripts

`FreezeReplayV2.disableLegacyMotorControllers` tries to disable older scripts that may write hinge motors. Before re-enabling legacy scripts, inspect whether they conflict with `FreezeReplayV2`, `PoseDriver`, or the runtime rig builder.

In general, prefer improving the current `FreezeReplayV2` + marker rig path over reviving older motor scripts.

## Current Known Issues

1. **Spine-to-hips weakness**: The spine joint's motor is too weak in the angle-driven preset, causing the torso to slouch. Needs per-joint tuning or increased motor torque.
2. ~~**Planted foot detachment**~~: **Resolved** — intent-based pre-release via `PreReleasePlantedFeetByFrozenTarget()` and `plantedFootDetachThreshold`.
3. **ImpactDamage cooldown uses Time.time**: Minor preview-vs-commit divergence from wall-clock based cooldowns.
4. **Auto-walk fragility**: Mixes generated targets, hinge motors, and limited foot forces. Still experimental.
5. **Kinematic frozen-copy stabilization**: Custom logic that can introduce unexpected whole-body lifts if ground detection is wrong.
6. **Custom inspector hides many fields**: Check advanced foldouts or source before assuming a setting is gone.
7. **No .sln or .csproj** currently visible from Assets/Scripts, so command-line compilation has not been available.
8. **PoseDriverSetup.cs on controller**: May be redundant — FRV2.WirePoseDrivers() handles PoseDriver creation. Verify it's not conflicting.

## Quick Debug Checklist

When another model receives a bug report, start with these checks:

1. Is the bug about planning ghost editing, preview simulation, committed simulation, or replay playback?
2. Is the fighter marker-built by `FighterRuntimeRigBuilder` or an older prebuilt hierarchy?
3. Is `groundMask` correct?
4. Is `ignoreJointLimits` enabled?
5. Are legs in `Auto`, `BasicBlock`, `Strike`, or `Recovery`?
6. Are old motor scripts enabled and fighting FRV2?
7. Are planted foot joints being created/released as expected?
8. Does the preview path differ from commit through damage, strike impulse, or fixed-step count?
9. Are PoseDriver settings relevant to this limb, or is Auto-walk bypassing normal PoseDrivers?
10. **Which preset is loaded?** Check `autoLoadPresetName` and compare current inspector values to the preset file.
11. **Is FRV2Diagnostics attached?** Check its reports for tracking error data.

## Most Relevant Files

- `FreezeReplayV2.cs`: primary controller and active gameplay path
- `Editor/FreezeReplayV2Editor.cs`: compact inspector for FRV2 with preset UI
- `FRV2Preset.cs`: preset save/load system
- `FRV2Diagnostics.cs`: runtime telemetry and auto-tuning
- `FighterRuntimeRigBuilder.cs`: marker-only fighter builder
- `SmartPoser.cs`: frozen copy drag/edit interaction (chain-isolated)
- `PoseDriver.cs`: hinge motor PD driver (on live limb bodies)
- `LimbMoveIntent.cs`: per-limb Auto/Basic/Strike/Recovery state
- `ImpactDamage.cs`: collision damage and strike impulse
- `FighterHealth.cs`: health snapshots and health bar
