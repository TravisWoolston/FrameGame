using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Runtime diagnostics for FreezeReplayV2.
/// Attach to the same GameObject as FRV2. Samples tracking error every
/// FixedUpdate during simulation/preview, computes per-turn summaries,
/// and writes JSON reports with heuristic tuning suggestions.
///
/// Reports are written to [ProjectRoot]/FRV2Diagnostics/.
/// Press the reportKey (default: F8) to write a report at any time.
/// A report is also auto-written after every N turns (autoReportInterval).
/// </summary>
public class FRV2Diagnostics : MonoBehaviour
{
    [Header("Controls")]
    public KeyCode reportKey = KeyCode.F8;
    public KeyCode generatePresetKey = KeyCode.F9;
    public int autoReportInterval = 5;
    public bool logSuggestionsToConsole = true;
    public bool autoGeneratePresetWithReport = true;

    // --- internal state ---
    private FreezeReplayV2 frv2;
    private int lastTurnCount = -1;
    private int samplesThisTurn;
    private List<TurnSummary> turnHistory = new List<TurnSummary>();
    private Dictionary<string, BodyAccum> bodyAccums = new Dictionary<string, BodyAccum>();
    private BodySnapshot[] previewEndState;
    private bool wasSimulating;

    private struct BodySnapshot
    {
        public Vector2 position;
        public float rotation;
    }

    private class BodyAccum
    {
        public string name;
        public float totalPosError, maxPosError;
        public float totalAngError, maxAngError;
        public int signChanges;
        public float lastPosErrorSign;
        public int samples;
    }

    private class TurnSummary
    {
        public int turnNumber;
        public List<BodyResult> bodies = new List<BodyResult>();
        public float previewDivergence;
        public bool autoWalkWasActive;
        public int plantedFootCount;
    }

    private class BodyResult
    {
        public string name;
        public float avgPosError, maxPosError;
        public float avgAngError, maxAngError;
        public int oscillations, samples;
    }

    void Start()
    {
        frv2 = GetComponent<FreezeReplayV2>();
        if (frv2 == null)
        {
            Debug.LogError("[FRV2Diagnostics] No FreezeReplayV2 found on this GameObject.");
            enabled = false;
        }
    }

    void Update()
    {
        if (frv2 == null) return;

        if (Input.GetKeyDown(reportKey))
            WriteReport();

        if (Input.GetKeyDown(generatePresetKey))
            GenerateAndSaveOptimizedPreset();

        // Detect turn completion
        if (frv2.turnCount != lastTurnCount && lastTurnCount >= 0)
        {
            FinishTurn(lastTurnCount);
            if (autoReportInterval > 0 && turnHistory.Count % autoReportInterval == 0)
                WriteReport();
        }
        lastTurnCount = frv2.turnCount;

        // Capture preview end state for divergence comparison
        if (wasSimulating && frv2.currentPhase != FreezeReplayV2.TurnPhase.Simulating)
            CapturePreviewEndState();
        wasSimulating = frv2.currentPhase == FreezeReplayV2.TurnPhase.Simulating;
    }

    void FixedUpdate()
    {
        if (frv2 == null) return;
        bool active = frv2.currentPhase == FreezeReplayV2.TurnPhase.Simulating;
        if (!active) return;

        SampleAllBodies();
    }

    // ===== SAMPLING =====

    private void SampleAllBodies()
    {
        if (frv2.trackedBodies == null || frv2.frozenCopy == null) return;
        Rigidbody2D[] frozenRbs = frv2.frozenCopy.GetComponentsInChildren<Rigidbody2D>(true);
        int count = Mathf.Min(frv2.trackedBodies.Count, frozenRbs.Length);

        for (int i = 0; i < count; i++)
        {
            Rigidbody2D live = frv2.trackedBodies[i];
            Rigidbody2D frozen = frozenRbs[i];
            if (live == null || frozen == null) continue;

            string key = live.name;
            if (!bodyAccums.TryGetValue(key, out BodyAccum acc))
            {
                acc = new BodyAccum { name = key };
                bodyAccums[key] = acc;
            }

            float posErr = Vector2.Distance(live.position, frozen.position);
            float angErr = Mathf.Abs(Mathf.DeltaAngle(live.rotation, frozen.rotation));

            acc.totalPosError += posErr;
            acc.maxPosError = Mathf.Max(acc.maxPosError, posErr);
            acc.totalAngError += angErr;
            acc.maxAngError = Mathf.Max(acc.maxAngError, angErr);

            float sign = Mathf.Sign(live.position.x - frozen.position.x);
            if (acc.samples > 0 && sign != 0 && acc.lastPosErrorSign != 0 && sign != acc.lastPosErrorSign)
                acc.signChanges++;
            if (sign != 0) acc.lastPosErrorSign = sign;

            acc.samples++;
        }
        samplesThisTurn++;
    }

    private void CapturePreviewEndState()
    {
        if (frv2.trackedBodies == null) return;
        previewEndState = new BodySnapshot[frv2.trackedBodies.Count];
        for (int i = 0; i < frv2.trackedBodies.Count; i++)
        {
            Rigidbody2D rb = frv2.trackedBodies[i];
            if (rb == null) continue;
            previewEndState[i] = new BodySnapshot { position = rb.position, rotation = rb.rotation };
        }
    }

    private void FinishTurn(int turnNumber)
    {
        TurnSummary summary = new TurnSummary
        {
            turnNumber = turnNumber,
            previewDivergence = ComputePreviewDivergence()
        };

        foreach (var kvp in bodyAccums)
        {
            BodyAccum acc = kvp.Value;
            if (acc.samples == 0) continue;
            summary.bodies.Add(new BodyResult
            {
                name = acc.name,
                avgPosError = acc.totalPosError / acc.samples,
                maxPosError = acc.maxPosError,
                avgAngError = acc.totalAngError / acc.samples,
                maxAngError = acc.maxAngError,
                oscillations = acc.signChanges,
                samples = acc.samples
            });
        }

        turnHistory.Add(summary);
        bodyAccums.Clear();
        samplesThisTurn = 0;
        previewEndState = null;
    }

    private float ComputePreviewDivergence()
    {
        if (previewEndState == null || frv2.trackedBodies == null) return -1f;
        float totalDiv = 0f;
        int count = Mathf.Min(previewEndState.Length, frv2.trackedBodies.Count);
        for (int i = 0; i < count; i++)
        {
            Rigidbody2D rb = frv2.trackedBodies[i];
            if (rb == null) continue;
            totalDiv += Vector2.Distance(rb.position, previewEndState[i].position);
        }
        return count > 0 ? totalDiv / count : 0f;
    }

    // ===== REPORT GENERATION =====

    [ContextMenu("Write Diagnostics Report")]
    public void WriteReport()
    {
        string dir = Path.Combine(Application.dataPath, "..", "FRV2Diagnostics");
        Directory.CreateDirectory(dir);
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string path = Path.Combine(dir, $"frv2_report_{timestamp}.json");

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"timestamp\": \"{timestamp}\",");
        sb.AppendLine($"  \"turnsRecorded\": {turnHistory.Count},");

        // Settings snapshot
        WriteSettings(sb);

        // Turn data
        sb.AppendLine("  \"turns\": [");
        for (int t = 0; t < turnHistory.Count; t++)
        {
            TurnSummary ts = turnHistory[t];
            sb.AppendLine("    {");
            sb.AppendLine($"      \"turn\": {ts.turnNumber},");
            sb.AppendLine($"      \"previewDivergence\": {ts.previewDivergence:F4},");
            sb.AppendLine("      \"bodies\": [");
            for (int b = 0; b < ts.bodies.Count; b++)
            {
                BodyResult br = ts.bodies[b];
                sb.Append($"        {{ \"name\": \"{br.name}\", ");
                sb.Append($"\"avgPosErr\": {br.avgPosError:F4}, \"maxPosErr\": {br.maxPosError:F4}, ");
                sb.Append($"\"avgAngErr\": {br.avgAngError:F2}, \"maxAngErr\": {br.maxAngError:F2}, ");
                sb.Append($"\"oscillations\": {br.oscillations}, \"samples\": {br.samples} }}");
                sb.AppendLine(b < ts.bodies.Count - 1 ? "," : "");
            }
            sb.AppendLine("      ]");
            sb.Append("    }");
            sb.AppendLine(t < turnHistory.Count - 1 ? "," : "");
        }
        sb.AppendLine("  ],");

        // Analysis
        List<string> suggestions = GenerateSuggestions();
        sb.AppendLine("  \"suggestions\": [");
        for (int i = 0; i < suggestions.Count; i++)
        {
            sb.Append($"    \"{EscapeJson(suggestions[i])}\"");
            sb.AppendLine(i < suggestions.Count - 1 ? "," : "");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[FRV2Diagnostics] Report written to: {path}");

        if (logSuggestionsToConsole && suggestions.Count > 0)
        {
            Debug.Log($"[FRV2Diagnostics] === {suggestions.Count} TUNING SUGGESTIONS ===");
            foreach (string s in suggestions)
                Debug.LogWarning($"[FRV2Diagnostics] {s}");
        }
        else if (logSuggestionsToConsole)
        {
            Debug.Log("[FRV2Diagnostics] No issues detected. Current settings look reasonable.");
        }

        if (autoGeneratePresetWithReport && turnHistory.Count > 0)
            GenerateAndSaveOptimizedPreset();
    }

    private void WriteSettings(StringBuilder sb)
    {
        if (frv2 == null) { sb.AppendLine("  \"settings\": {},"); return; }

        sb.AppendLine("  \"settings\": {");
        // Pose driving
        Sf(sb, "poseKp", frv2.poseKp);
        Sf(sb, "poseKd", frv2.poseKd);
        Sf(sb, "poseMaxSpeed", frv2.poseMaxSpeed);
        Sb(sb, "ignoreJointLimits", frv2.ignoreJointLimits);
        // Pose assist
        Sb(sb, "enablePhysicsPoseAssist", frv2.enablePhysicsPoseAssist);
        Sb(sb, "poseAssistOnlyRootAndUnjointedBodies", frv2.poseAssistOnlyRootAndUnjointedBodies);
        Sf(sb, "poseAssistSpring", frv2.poseAssistSpring);
        Sf(sb, "poseAssistDamping", frv2.poseAssistDamping);
        Sf(sb, "maxPoseAssistForce", frv2.maxPoseAssistForce);
        Sf(sb, "poseAssistAngularSpring", frv2.poseAssistAngularSpring);
        Sf(sb, "poseAssistAngularDamping", frv2.poseAssistAngularDamping);
        Sf(sb, "maxPoseAssistTorque", frv2.maxPoseAssistTorque);
        // Planted foot
        Sb(sb, "usePlantedFootJoints", frv2.usePlantedFootJoints);
        Sf(sb, "plantedFootGroundTolerance", frv2.plantedFootGroundTolerance);
        Sf(sb, "plantedFootJointFrequency", frv2.plantedFootJointFrequency);
        Sf(sb, "plantedFootJointBreakForce", frv2.plantedFootJointBreakForce);
        Sf(sb, "plantedFootJointBreakTorque", frv2.plantedFootJointBreakTorque);
        // Walk
        Sb(sb, "enableAutoWalkFromHipTarget", frv2.enableAutoWalkFromHipTarget);
        Sb(sb, "useJointMotorsForAutoWalk", frv2.useJointMotorsForAutoWalk);
        Sf(sb, "walkMinHipTravel", frv2.walkMinHipTravel);
        Sf(sb, "walkStepLead", frv2.walkStepLead);
        Sf(sb, "walkFootSpacing", frv2.walkFootSpacing);
        Sf(sb, "walkStepLift", frv2.walkStepLift);
        Sf(sb, "walkFootSpring", frv2.walkFootSpring);
        Sf(sb, "maxWalkFootForce", frv2.maxWalkFootForce);
        // Turn
        Sf(sb, "turnDuration", frv2.turnDuration, true);
        sb.AppendLine("  },");
    }

    private void Sf(StringBuilder sb, string name, float val, bool last = false)
    {
        sb.AppendLine($"    \"{name}\": {val:F3}{(last ? "" : ",")}");
    }

    private void Sb(StringBuilder sb, string name, bool val, bool last = false)
    {
        sb.AppendLine($"    \"{name}\": {(val ? "true" : "false")}{(last ? "" : ",")}");
    }

    private string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ===== HEURISTIC ANALYSIS =====

    private List<string> GenerateSuggestions()
    {
        List<string> suggestions = new List<string>();
        if (turnHistory.Count == 0) return suggestions;

        // Aggregate across recent turns
        Dictionary<string, AggregateMeta> agg = new Dictionary<string, AggregateMeta>();
        int recentCount = Mathf.Min(turnHistory.Count, 5);
        float totalDivergence = 0f;
        int divCount = 0;

        for (int t = turnHistory.Count - recentCount; t < turnHistory.Count; t++)
        {
            TurnSummary ts = turnHistory[t];
            if (ts.previewDivergence >= 0) { totalDivergence += ts.previewDivergence; divCount++; }

            foreach (var br in ts.bodies)
            {
                if (!agg.TryGetValue(br.name, out AggregateMeta am))
                {
                    am = new AggregateMeta { name = br.name };
                    agg[br.name] = am;
                }
                am.totalAvgPosErr += br.avgPosError;
                am.totalMaxPosErr = Mathf.Max(am.totalMaxPosErr, br.maxPosError);
                am.totalAvgAngErr += br.avgAngError;
                am.totalOscillations += br.oscillations;
                am.totalSamples += br.samples;
                am.turnCount++;
            }
        }

        foreach (var kvp in agg)
        {
            AggregateMeta am = kvp.Value;
            if (am.turnCount == 0) continue;

            float avgPos = am.totalAvgPosErr / am.turnCount;
            float avgAng = am.totalAvgAngErr / am.turnCount;
            float oscPerTurn = (float)am.totalOscillations / am.turnCount;
            float samplesPerTurn = am.totalSamples > 0 ? (float)am.totalSamples / am.turnCount : 1;
            float oscRate = oscPerTurn / Mathf.Max(1, samplesPerTurn);

            // High position error — forces too weak
            if (avgPos > 0.8f)
            {
                suggestions.Add($"[{am.name}] High avg position error ({avgPos:F2}). " +
                    $"Consider increasing poseAssistSpring (now {frv2.poseAssistSpring:F0}) " +
                    $"or maxPoseAssistForce (now {frv2.maxPoseAssistForce:F0}).");
            }
            else if (avgPos > 0.4f)
            {
                suggestions.Add($"[{am.name}] Moderate position error ({avgPos:F2}). " +
                    $"Try poseKp {frv2.poseKp:F0} -> {frv2.poseKp * 1.3f:F0} for tighter tracking.");
            }

            // High angular error
            if (avgAng > 25f)
            {
                suggestions.Add($"[{am.name}] High avg angular error ({avgAng:F1} deg). " +
                    $"Increase poseKp (now {frv2.poseKp:F0}) or check if joint limits are blocking.");
            }

            // Oscillation — needs more damping
            if (oscRate > 0.35f && avgPos > 0.2f)
            {
                suggestions.Add($"[{am.name}] Oscillating (rate={oscRate:F2}). " +
                    $"Increase poseKd from {frv2.poseKd:F0} to {frv2.poseKd * 1.5f:F0}, " +
                    $"or increase poseAssistDamping from {frv2.poseAssistDamping:F0} to {frv2.poseAssistDamping * 1.4f:F0}.");
            }

            // Extreme max error — something exploded
            if (am.totalMaxPosErr > 3f)
            {
                suggestions.Add($"[{am.name}] Extreme max position error ({am.totalMaxPosErr:F2}). " +
                    $"Body may be flying away. Check for conflicting forces or broken joints.");
            }
        }

        // Preview vs commit divergence
        if (divCount > 0)
        {
            float avgDiv = totalDivergence / divCount;
            if (avgDiv > 0.3f)
            {
                suggestions.Add($"Preview-to-commit divergence is high ({avgDiv:F3} avg). " +
                    "Check disableDamageDuringPreview, step counts, or collision ignores.");
            }
        }

        // Setting-level checks (independent of telemetry)
        if (frv2.poseKp > 0 && frv2.poseKd < frv2.poseKp * 0.1f)
            suggestions.Add($"poseKd ({frv2.poseKd:F0}) is very low relative to poseKp ({frv2.poseKp:F0}). " +
                $"Ratio is {frv2.poseKd / frv2.poseKp:F2}, recommend >= 0.15 to prevent overshoot.");

        if (frv2.poseAssistSpring > 0 && frv2.maxPoseAssistForce < frv2.poseAssistSpring * 5f)
            suggestions.Add($"maxPoseAssistForce ({frv2.maxPoseAssistForce:F0}) may clamp too early for " +
                $"poseAssistSpring ({frv2.poseAssistSpring:F0}). Force saturates at just {frv2.maxPoseAssistForce / frv2.poseAssistSpring:F1} units of error.");

        if (frv2.enableAutoWalkFromHipTarget && !frv2.useJointMotorsForAutoWalk)
            suggestions.Add("Auto walk is enabled but useJointMotorsForAutoWalk is off. " +
                "Legs will only be force-driven, which often looks floaty.");

        if (frv2.ignoreJointLimits)
            suggestions.Add("ignoreJointLimits is ON. This disables all joint angle constraints " +
                "and can cause limbs to fold through each other.");

        return suggestions;
    }

    private class AggregateMeta
    {
        public string name;
        public float totalAvgPosErr, totalMaxPosErr;
        public float totalAvgAngErr;
        public int totalOscillations, totalSamples, turnCount;
    }

    // ===== PRESET GENERATION =====

    /// <summary>
    /// Save current FRV2 settings as a named preset without modifications.
    /// </summary>
    [ContextMenu("Save Current Settings As Preset")]
    public void SaveCurrentAsPreset()
    {
        if (frv2 == null) { Debug.LogError("[FRV2Diagnostics] No FRV2 found."); return; }
        FRV2Preset preset = FRV2Preset.CaptureFrom(frv2, "current_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        preset.description = "Snapshot of current FRV2 settings.";
        preset.SaveToFile();
    }

    /// <summary>
    /// Generate an optimized preset by starting from current settings and
    /// applying heuristic adjustments based on collected telemetry data.
    /// Saves to FRV2Presets/ and logs the changes.
    /// </summary>
    [ContextMenu("Generate Optimized Preset From Diagnostics")]
    public void GenerateAndSaveOptimizedPreset()
    {
        if (frv2 == null) { Debug.LogError("[FRV2Diagnostics] No FRV2 found."); return; }
        if (turnHistory.Count == 0)
        {
            Debug.LogWarning("[FRV2Diagnostics] No turn data yet. Play a few turns first.");
            return;
        }

        // Start from current settings
        FRV2Preset preset = FRV2Preset.CaptureFrom(frv2, "optimized_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        List<string> changes = new List<string>();

        // Aggregate recent telemetry
        Dictionary<string, AggregateMeta> agg = new Dictionary<string, AggregateMeta>();
        int recentCount = Mathf.Min(turnHistory.Count, 5);
        float totalDivergence = 0f;
        int divCount = 0;

        for (int t = turnHistory.Count - recentCount; t < turnHistory.Count; t++)
        {
            TurnSummary ts = turnHistory[t];
            if (ts.previewDivergence >= 0) { totalDivergence += ts.previewDivergence; divCount++; }
            foreach (var br in ts.bodies)
            {
                if (!agg.TryGetValue(br.name, out AggregateMeta am))
                {
                    am = new AggregateMeta { name = br.name };
                    agg[br.name] = am;
                }
                am.totalAvgPosErr += br.avgPosError;
                am.totalMaxPosErr = Mathf.Max(am.totalMaxPosErr, br.maxPosError);
                am.totalAvgAngErr += br.avgAngError;
                am.totalOscillations += br.oscillations;
                am.totalSamples += br.samples;
                am.turnCount++;
            }
        }

        // Compute global averages
        float globalAvgPosErr = 0f;
        float globalMaxOscRate = 0f;
        float globalAvgAngErr = 0f;
        int bodyCount = 0;

        foreach (var kvp in agg)
        {
            AggregateMeta am = kvp.Value;
            if (am.turnCount == 0) continue;
            float avgPos = am.totalAvgPosErr / am.turnCount;
            float avgAng = am.totalAvgAngErr / am.turnCount;
            float samplesPerTurn = am.totalSamples > 0 ? (float)am.totalSamples / am.turnCount : 1;
            float oscRate = ((float)am.totalOscillations / am.turnCount) / Mathf.Max(1, samplesPerTurn);

            globalAvgPosErr += avgPos;
            globalAvgAngErr += avgAng;
            globalMaxOscRate = Mathf.Max(globalMaxOscRate, oscRate);
            bodyCount++;
        }

        if (bodyCount > 0)
        {
            globalAvgPosErr /= bodyCount;
            globalAvgAngErr /= bodyCount;
        }

        // === Apply heuristic adjustments ===

        // 1. Position tracking: adjust springs and force caps
        if (globalAvgPosErr > 0.8f)
        {
            preset.poseAssistSpring = frv2.poseAssistSpring * 1.5f;
            preset.maxPoseAssistForce = frv2.maxPoseAssistForce * 1.4f;
            preset.poseKp = frv2.poseKp * 1.3f;
            changes.Add($"poseAssistSpring: {frv2.poseAssistSpring:F0} -> {preset.poseAssistSpring:F0} (high pos error)");
            changes.Add($"maxPoseAssistForce: {frv2.maxPoseAssistForce:F0} -> {preset.maxPoseAssistForce:F0}");
            changes.Add($"poseKp: {frv2.poseKp:F0} -> {preset.poseKp:F0}");
        }
        else if (globalAvgPosErr > 0.4f)
        {
            preset.poseAssistSpring = frv2.poseAssistSpring * 1.2f;
            preset.poseKp = frv2.poseKp * 1.15f;
            changes.Add($"poseAssistSpring: {frv2.poseAssistSpring:F0} -> {preset.poseAssistSpring:F0} (moderate pos error)");
            changes.Add($"poseKp: {frv2.poseKp:F0} -> {preset.poseKp:F0}");
        }
        else if (globalAvgPosErr < 0.1f && globalAvgPosErr > 0f)
        {
            // Tracking is very tight — we can reduce forces to save stability
            preset.poseAssistSpring = frv2.poseAssistSpring * 0.85f;
            preset.maxPoseAssistForce = frv2.maxPoseAssistForce * 0.9f;
            changes.Add($"poseAssistSpring: {frv2.poseAssistSpring:F0} -> {preset.poseAssistSpring:F0} (very tight, reducing for stability)");
        }

        // 2. Oscillation: increase damping
        if (globalMaxOscRate > 0.35f)
        {
            preset.poseKd = frv2.poseKd * 1.5f;
            preset.poseAssistDamping = frv2.poseAssistDamping * 1.4f;
            preset.poseAssistAngularDamping = frv2.poseAssistAngularDamping * 1.3f;
            changes.Add($"poseKd: {frv2.poseKd:F0} -> {preset.poseKd:F0} (oscillation detected)");
            changes.Add($"poseAssistDamping: {frv2.poseAssistDamping:F1} -> {preset.poseAssistDamping:F1}");
        }
        else if (globalMaxOscRate < 0.1f && frv2.poseKd > 5f)
        {
            // Very stable — can slightly reduce damping for responsiveness
            preset.poseKd = frv2.poseKd * 0.9f;
            changes.Add($"poseKd: {frv2.poseKd:F0} -> {preset.poseKd:F0} (very stable, slight reduction)");
        }

        // 3. Angular error: adjust angular springs
        if (globalAvgAngErr > 25f)
        {
            preset.poseAssistAngularSpring = frv2.poseAssistAngularSpring * 1.4f;
            preset.maxPoseAssistTorque = frv2.maxPoseAssistTorque * 1.3f;
            changes.Add($"poseAssistAngularSpring: {frv2.poseAssistAngularSpring:F0} -> {preset.poseAssistAngularSpring:F0} (high angular error)");
            changes.Add($"maxPoseAssistTorque: {frv2.maxPoseAssistTorque:F0} -> {preset.maxPoseAssistTorque:F0}");
        }

        // 4. Damping-to-spring ratio check
        if (preset.poseKp > 0 && preset.poseKd < preset.poseKp * 0.15f)
        {
            float targetKd = preset.poseKp * 0.2f;
            changes.Add($"poseKd: {preset.poseKd:F0} -> {targetKd:F0} (ratio correction: kd/kp was {preset.poseKd / preset.poseKp:F2})");
            preset.poseKd = targetKd;
        }

        // 5. Force cap vs spring ratio
        if (preset.poseAssistSpring > 0 && preset.maxPoseAssistForce < preset.poseAssistSpring * 8f)
        {
            float newCap = preset.poseAssistSpring * 12f;
            changes.Add($"maxPoseAssistForce: {preset.maxPoseAssistForce:F0} -> {newCap:F0} (force cap headroom)");
            preset.maxPoseAssistForce = newCap;
        }

        // 6. Walk tuning based on walk-specific body errors
        float legAvgErr = 0f;
        int legCount = 0;
        foreach (var kvp in agg)
        {
            string n = kvp.Key.ToLowerInvariant();
            if (n.Contains("thigh") || n.Contains("calf") || n.Contains("foot"))
            {
                legAvgErr += kvp.Value.totalAvgPosErr / Mathf.Max(1, kvp.Value.turnCount);
                legCount++;
            }
        }
        if (legCount > 0) legAvgErr /= legCount;

        if (legAvgErr > 0.6f)
        {
            preset.walkFootSpring = frv2.walkFootSpring * 1.3f;
            preset.maxWalkFootForce = frv2.maxWalkFootForce * 1.3f;
            preset.autoLegTargetSpring = frv2.autoLegTargetSpring * 1.25f;
            changes.Add($"walkFootSpring: {frv2.walkFootSpring:F0} -> {preset.walkFootSpring:F0} (leg tracking)");
            changes.Add($"maxWalkFootForce: {frv2.maxWalkFootForce:F0} -> {preset.maxWalkFootForce:F0}");
            changes.Add($"autoLegTargetSpring: {frv2.autoLegTargetSpring:F0} -> {preset.autoLegTargetSpring:F0}");
        }

        // 7. Enable joint motors if walk is on but motors are off
        if (frv2.enableAutoWalkFromHipTarget && !frv2.useJointMotorsForAutoWalk)
        {
            preset.useJointMotorsForAutoWalk = true;
            changes.Add("useJointMotorsForAutoWalk: false -> true (recommended for auto walk)");
        }

        // Build description
        StringBuilder desc = new StringBuilder();
        desc.AppendLine($"Auto-optimized from {turnHistory.Count} turns of telemetry.");
        desc.AppendLine($"Global avg position error: {globalAvgPosErr:F3}");
        desc.AppendLine($"Global avg angular error: {globalAvgAngErr:F1} deg");
        desc.AppendLine($"Max oscillation rate: {globalMaxOscRate:F2}");
        if (divCount > 0)
            desc.AppendLine($"Avg preview divergence: {totalDivergence / divCount:F4}");
        desc.AppendLine($"Changes applied: {changes.Count}");
        preset.description = desc.ToString();

        preset.SaveToFile();

        // Log changes
        if (changes.Count > 0)
        {
            Debug.Log($"[FRV2Diagnostics] Generated optimized preset with {changes.Count} adjustments:");
            foreach (string c in changes)
                Debug.Log($"  • {c}");
            Debug.Log($"[FRV2Diagnostics] Load it with: Right-click FRV2 > Load Preset, or press it in the Inspector.");
        }
        else
        {
            Debug.Log("[FRV2Diagnostics] Current settings look optimal — preset saved without changes.");
        }
    }
}
