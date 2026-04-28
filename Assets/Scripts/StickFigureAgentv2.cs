using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using Unity.MLAgents.Policies;

public class StickFigureAgentv2 : Agent
{
    public GameObject[] jointGOs;
    public HingeJoint2D[] joints;
    public Transform head,
        spine,
        target,
        marker;
    public GameObject calf,
        calf2,
        thigh,
        thigh2,
        arm,
        arm2,
        lowerArm,
        lowerArm2;
    public MagnetBoot calfMag,
        calf2Mag;
    public HeavyBoots calfBoot,
        calf2Boot;
    public RayPerceptionSensorComponent2D[] RPSArray;
    public RotateHingeJoint[] rotateHingeJoints;

    private float torque = 100f;
    private float timer = 0f;
    private float bestTime = float.MaxValue;
    private float bestReward = 0f;
    private Dictionary<string, float> rewardSources = new Dictionary<string, float>();
    private Dictionary<string, string> rewardStringSources = new Dictionary<string, string>();
    public FreezeReplay fr;
    public GameObject copy;
    private int agentIndex;
    private float maxMotorSpeed = 5000;

    private float totalHeight;
    private const int actionHistorySize = 10;
    private List<float> actionHistory;
    private List<float> actionHistoryDiscrete;
    private BehaviorParameters behaviorParameters;
    private Vector3 defaultArmPosition,
        defaultArm2Position,
        defaultLowerArmPosition,
        defaultLowerArm2Position;

    public void EnableGUI(string objectName)
    {
        // Extract the index from the fr GameObject's parent name
        string parentName = objectName;
        if (
            int.TryParse(
                parentName.Replace("AgentGameController (", "").Replace(")", ""),
                out int index
            )
        )
        {
            agentIndex = index;
        }
        else
        {
            Debug.LogError("Failed to parse AgentGameController index.");
        }
    }

    void OnGUI()
    {
        // Check if the camera game object is active
        if (Camera.main == null || !Camera.main.gameObject.activeSelf)
        {
            return;
        }
        // Calculate the screen position of the agent's starting location
        Vector3 screenPosition = Camera.main.WorldToScreenPoint(transform.position);

        // Offset the GUI elements based on this position
        float yOffset = (Screen.height - screenPosition.y) + 20; // Invert y-axis for GUI positioning
        float xOffset = screenPosition.x;

        // Create a static panel on the screen to display reward values
        GUI.Box(new Rect(xOffset, yOffset, 300, 400), $"Agent {agentIndex} Reward Values");

        // Draw reward source values inside the panel
        DrawRewardSource("distanceToTarget", xOffset + 10, yOffset + 30);
        DrawRewardSource("reachedTarget", xOffset + 10, yOffset + 50);
        DrawRewardSource("armDeviationReward", xOffset + 10, yOffset + 50);
        DrawRewardSource("lastHeightReward", xOffset + 10, yOffset + 70);
        DrawRewardSource("headAboveSpine", xOffset + 10, yOffset + 50);
        DrawRewardSource("targetHeight", xOffset + 10, yOffset + 90);
        DrawRewardSource("headAwayFromFloor", xOffset + 10, yOffset + 110);
        DrawRewardSource("headHeight", xOffset + 10, yOffset + 110);
        DrawRewardSource("armsBelowHead", xOffset + 10, yOffset + 130);
        DrawRewardSource("uprightnessReward", xOffset + 10, yOffset + 150);
        DrawRewardSource("calfToThigh1", xOffset + 10, yOffset + 170);
        DrawRewardSource("calfToThigh2", xOffset + 10, yOffset + 190);
        DrawRewardSource("Continuous Action History", xOffset + 10, yOffset + 210);
        DrawRewardSource("Discrete Action History", xOffset + 10, yOffset + 230);
        DrawRewardSource("proximityReward", xOffset + 10, yOffset + 250);

        DrawRewardSource("Total", xOffset + 10, yOffset + 300);
        
    }

    void DrawRewardSource(string source, float xPosition, float yPosition)
    {
        if (rewardSources.ContainsKey(source))
        {
            // Set the color based on whether the reward was positive or negative
            GUI.color = rewardSources[source] > 0 ? Color.green : Color.red;
            GUI.Label(
                new Rect(xPosition, yPosition, 280, 20),
                $"{source}: {rewardSources[source]:F2}"
            );
            GUI.color = Color.white; // Reset color to default
        }
        if (rewardStringSources.ContainsKey(source))
        {
            // Set the color based on whether the reward was positive or negative
            // GUI.color = rewardStringSources[source] > 0 ? Color.green : Color.red;
            GUI.Label(
                new Rect(xPosition, yPosition, 280, 20),
                $"{source}: {rewardStringSources[source]:F2}"
            );
            GUI.color = Color.white; // Reset color to default
        }
    }

    void DrawLabel(Transform transform, string label)
    {
        Vector3 screenPosition = Camera.main.WorldToScreenPoint(transform.position);
        GUI.Label(
            new Rect(screenPosition.x, Screen.height - screenPosition.y, 200, 20),
            $"{label}"
        );
    }

    void Start()
    {
        behaviorParameters = GetComponent<BehaviorParameters>();
        int numContinuousActions = behaviorParameters
            .BrainParameters
            .ActionSpec
            .NumContinuousActions;
        int numDiscreteActions = behaviorParameters
            .BrainParameters
            .ActionSpec
            .NumDiscreteActions;
        actionHistory = new List<float>(new float[actionHistorySize * numContinuousActions]);
        actionHistoryDiscrete = new List<float>(new float[actionHistorySize * numDiscreteActions]);
        InitializeAgent();
    }

    void InitializeAgent()
    {
        calfBoot = calf.GetComponent<HeavyBoots>();
        calf2Boot = calf2.GetComponent<HeavyBoots>();
        calfMag = calf.GetComponent<MagnetBoot>();
        calf2Mag = calf2.GetComponent<MagnetBoot>();

        rotateHingeJoints = new RotateHingeJoint[joints.Length];
        for (int i = 0; i < joints.Length; i++)
        {
            rotateHingeJoints[i] = joints[i].GetComponent<RotateHingeJoint>();
        }
        // Store default positions
        defaultArmPosition = arm.transform.localPosition;
        defaultArm2Position = arm2.transform.localPosition;
        defaultLowerArmPosition = lowerArm.transform.localPosition;
        defaultLowerArm2Position = lowerArm2.transform.localPosition;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        TrackStringInfo("Continuous Action History", string.Join(" ", actionHistory.ToArray()));
        TrackStringInfo("Discrete Action History", string.Join(" ", actionHistoryDiscrete.ToArray()));
        // Add observations for head, spine, and target positions
        sensor.AddObservation(head.localPosition);
        sensor.AddObservation(spine.localPosition);
        sensor.AddObservation(target.localPosition);
        sensor.AddObservation(Vector3.Distance(spine.localPosition, target.localPosition));
        sensor.AddObservation(spine.rotation.eulerAngles.z);
        sensor.AddObservation(feetOnGround());
        sensor.AddObservation(actionHistory.ToArray());
        sensor.AddObservation(actionHistoryDiscrete.ToArray());
        // Add observations for each joint's position, rotation, angular velocity, and velocity
        foreach (var joint in joints)
        {
            sensor.AddObservation(joint.transform.localPosition);
            sensor.AddObservation(joint.transform.localRotation.eulerAngles.z);
            sensor.AddObservation(joint.GetComponent<Rigidbody2D>().angularVelocity);
            sensor.AddObservation(joint.GetComponent<Rigidbody2D>().linearVelocity);
        }

        // Ray Perception Sensor observations
        foreach (RayPerceptionSensorComponent2D RPS in RPSArray)
        {
            var rayOutputs = RayPerceptionSensor.Perceive(RPS.GetRayPerceptionInput()).RayOutputs;

            foreach (var ray in rayOutputs)
            {
                GameObject goHit = ray.HitGameObject;
                sensor.AddObservation(ray.HasHit);
                if (goHit)
                {
                    sensor.AddObservation(goHit.transform.localPosition);
                    sensor.AddObservation(ray.HitFraction);
                    sensor.AddObservation(
                        Vector3.Distance(goHit.transform.localPosition, target.localPosition)
                    );
                    sensor.AddObservation(
                        (target.localPosition - goHit.transform.localPosition).normalized
                    );
                }
            }
        }
    }

    void ApplyMotorSpeeds(
        ActionSegment<float> continuousActions,
        ActionSegment<int> discreteActions
    )
    {
        for (int i = 0; i < rotateHingeJoints.Length; i++)
        {
            float speed = Mathf.Clamp(continuousActions[i], -1f, 1f) * maxMotorSpeed;
            int direction = discreteActions[i] == 2 ? -1 : discreteActions[i]; // Convert action to direction (-1, 0, 1)
            rotateHingeJoints[i].sfaRotateJoint(direction, speed);
        }
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        var continuousActions = actionBuffers.ContinuousActions;
        var discreteActions = actionBuffers.DiscreteActions;

        ApplyMotorSpeeds(continuousActions, discreteActions);

        // Update decision history
        int numContinuousActions = behaviorParameters
            .BrainParameters
            .ActionSpec
            .NumContinuousActions;
        int numDiscreteActions = behaviorParameters
            .BrainParameters
            .ActionSpec
            .NumDiscreteActions;
        for (int i = 0; i < numContinuousActions; i++)
        {
            actionHistory.RemoveAt(0);
            actionHistory.Add(actionBuffers.ContinuousActions[i]);
        }
        for (int i = 0; i < numDiscreteActions; i++)
        {
            actionHistoryDiscrete.RemoveAt(0);
            actionHistoryDiscrete.Add(actionBuffers.DiscreteActions[i]);
        }

        EvaluateAgentPerformance();
    }

    float previousDistanceToTarget = 1;

float DistanceToTarget()
{
    // Calculate the current distance to the target
    float distanceToTarget = Vector3.Distance(spine.position, target.position);

    // Reward for moving closer to the target
    float rewardForMovingCloser = previousDistanceToTarget - distanceToTarget;
    // Reward for staying near the target
    float proximityRewardScale = 1f; // Adjust this value as needed
    float proximityReward = proximityRewardScale / (1.0f + distanceToTarget);

    // Combine the rewards
    float totalReward = rewardForMovingCloser + proximityReward;

    // Update previous distance
    previousDistanceToTarget = distanceToTarget;

    // Track the rewards for debugging
    TrackInfo("distanceToTarget", rewardForMovingCloser);
    TrackInfo("proximityReward", proximityReward);
    TrackInfo("totalDistanceReward", totalReward);

    return totalReward;
}


    float HeadAboveSpine()
    {
        float reward = head.localPosition.y > spine.localPosition.y + 1f ? .1f : -1f;
        TrackReward("headAboveSpine", reward);
        if(head.localPosition.y < spine.localPosition.y + 1f){
            AddReward(-2000);
            EndEpisode();
        }
        return reward;
    }

    float CalfToThigh1()
    {
        float thighF = thigh.transform.localPosition.y - 2f;
        float calfF = calf.transform.localPosition.y;
        float reward = thighF > calfF ? Mathf.Abs(thighF - calfF) : -Mathf.Abs(calfF - thighF)/10;
        TrackReward("calfToThigh1", reward);
        return reward;
    }

    float CalfToThigh2()
    {
        float thighF = thigh2.transform.localPosition.y - 2f;
        float calfF = calf2.transform.localPosition.y;
        float reward = thighF > calfF ? Mathf.Abs(thighF - calfF) : -Mathf.Abs(calfF - thighF)/10;
        TrackReward("calfToThigh2", reward);
        return reward;
    }

    void EvaluateAgentPerformance()
    {
        timer += Time.deltaTime;

        AddReward(DistanceToTarget());

        // Reward for legs being in an upright position
        float standingTallReward = CalculateStandingTallReward();
        AddReward(standingTallReward);

        // Reward for reaching the target
        // if (IsLowerArmOverlappingTarget())
        // {
        //     AddReward(1.0f); // Reward for touching the target with the lower arm
        //     EndEpisode(); // End the episode once the target is reached
        // }

        // Penalty for touching the floor with arms
        // if (IsArmTouchingFloor())
        // {
        // TrackReward("armTouchingFloor", -.5f);
        //     AddReward(-0.5f);
        // }

        // Reward for walking in a human-like way (one foot in front of the other)
        // AddReward(CalculateWalkingReward());


        // Reward for keeping the head away from the floor
        AddReward(CalfToThigh1());
        AddReward(CalfToThigh2());
        AddReward(HeadAboveSpine());

        // Reward for keeping arms below the head
        if (AreArmsBelowHead())
        {
            float armsReward = .1f;
            AddReward(armsReward);
            TrackReward("armsBelowHead", armsReward);
        }

        // Check if the agent has fallen
        // if (spine.localPosition.y < 0.2f)
        // {
        //     AddReward(-1); // Penalize for falling
        //     // EndEpisode();
        //     // OnEpisodeEnd();
        //     // fr.MLRestore();
        // }
        //     else {
        //         float armReward = CalculateArmDeviationReward();
        // AddReward(armReward);
        //     }
        // Update reward sources dictionary
        // TrackReward("reachedTarget", IsLowerArmOverlappingTarget() ? 1.0f : 0.0f);
        // TrackReward("headAwayFromFloor", head.localPosition.y > spine.localPosition.y + 1.0f ? 0.1f : 0.0f);
        // TrackReward("uprightnessReward", CalculateStandingTallReward());
    }

    float CalculateArmDeviationReward()
    {
        // Calculate the distance from default positions for each arm segment
        float armDeviation = Vector3.Distance(arm.transform.localPosition, defaultArmPosition);
        float arm2Deviation = Vector3.Distance(arm2.transform.localPosition, defaultArm2Position);
        float lowerArmDeviation = Vector3.Distance(
            lowerArm.transform.localPosition,
            defaultLowerArmPosition
        );
        float lowerArm2Deviation = Vector3.Distance(
            lowerArm2.transform.localPosition,
            defaultLowerArm2Position
        );

        // Combine the deviations to calculate the total deviation
        float totalDeviation =
            armDeviation + arm2Deviation + lowerArmDeviation + lowerArm2Deviation;

        // Define a scaling factor for the reward
        float rewardScale = 0.1f; // Adjust this value as needed

        // Calculate the reward (inverse of the deviation)
        float reward = rewardScale / (1.0f + totalDeviation);

        // Track the reward value for debugging
        TrackInfo("armDeviationReward", reward);

        return reward;
    }

    void TrackReward(string source, float value)
    {
        if (rewardSources.ContainsKey(source))
        {
            rewardSources[source] += value;
        }
        else
        {
            rewardSources.Add(source, value);
        }
        if (rewardSources.ContainsKey("Total"))
        {
            rewardSources["Total"] += value;
        }
        else
        {
            rewardSources.Add("Total", value);
        }
    }
void TrackStringInfo(string source, string value)
    {
        rewardStringSources[source] = value;
    }
    void TrackInfo(string source, float value)
    {
        rewardSources[source] = value;
    }

    bool IsLowerArmOverlappingTarget()
    {
        return lowerArm.GetComponent<Collider2D>().IsTouching(target.GetComponent<Collider2D>())
            || lowerArm2.GetComponent<Collider2D>().IsTouching(target.GetComponent<Collider2D>());
    }

    bool IsArmTouchingFloor()
    {
        return arm.GetComponent<GroundCheck>().touchingFloor
            || lowerArm.GetComponent<GroundCheck>().touchingFloor
            || arm2.GetComponent<GroundCheck>().touchingFloor
            || lowerArm2.GetComponent<GroundCheck>().touchingFloor;
    }

    bool AreArmsBelowHead()
    {
        return lowerArm.transform.localPosition.y < head.localPosition.y
            && lowerArm2.transform.localPosition.y < head.localPosition.y
            && arm.transform.localPosition.y < head.localPosition.y
            && arm2.transform.localPosition.y < head.localPosition.y;
    }

    public float GetTotalReward()
    {
        if (!rewardSources.ContainsKey("Total"))
            return 0;
        return rewardSources["Total"];
    }

    float CalculateWalkingReward()
    {
        // Reward based on alternating legs' positions
        float reward = 0.0f;
        if (
            thigh.transform.localPosition.x < spine.transform.localPosition.x
            && thigh2.transform.localPosition.x > spine.transform.localPosition.x
        )
        {
            reward += 0.1f; // Legs are positioned correctly
        }
        else if (
            thigh.transform.localPosition.x > spine.transform.localPosition.x
            && thigh2.transform.localPosition.x < spine.transform.localPosition.x
        )
        {
            reward += 0.1f; // Legs are positioned correctly
        }
        else
        {
            reward -= 0.1f; // Legs are not positioned correctly
        }
        TrackReward("walkingReward", reward);
        return reward;
    }

    float CalculateStandingTallReward()
    {
        // Define the target height as 80% of the total height
        float targetHeight = 0.8f * totalHeight;

        // Calculate the height of the head from the ground
        float headHeight = Mathf.Max(1f, head.localPosition.y); // Assuming y-axis is vertical in local coordinates
        TrackInfo("targetHeight", targetHeight);
        TrackInfo("headHeight", headHeight);
        // Calculate the reward based on the head height relative to the target height
        float reward;
        if (headHeight < targetHeight)
        {
            reward = -1 * (targetHeight / headHeight);
        }
        else
        {
            reward = headHeight / targetHeight;
        }


        TrackInfo("lastHeightReward", reward);
        TrackReward("uprightnessReward", reward);
        return reward;
    }

    public override void OnEpisodeBegin()
    {
        ResetAgent();
        // Calculate the lengths of each body part
        float calfLength = Vector3.Distance(
            calf.transform.localPosition,
            thigh.transform.localPosition
        );
        float calf2Length = Vector3.Distance(
            calf2.transform.localPosition,
            thigh2.transform.localPosition
        );
        float thighLength = Vector3.Distance(
            thigh.transform.localPosition,
            spine.transform.localPosition
        );
        float thigh2Length = Vector3.Distance(
            thigh2.transform.localPosition,
            spine.transform.localPosition
        );
        float spineLength = Vector3.Distance(
            spine.transform.localPosition,
            head.transform.localPosition
        );

        // Calculate the total height of the stick figure
        totalHeight = (calfLength + calf2Length + thighLength + thigh2Length + spineLength) / 2;
    }

    public void OnEpisodeEnd()
    {

        ResetAgent();
    }

    void ResetAgent()
    {
        
        behaviorParameters = GetComponent<BehaviorParameters>();
        int numContinuousActions = behaviorParameters
            .BrainParameters
            .ActionSpec
            .NumContinuousActions;
        int numDiscreteActions = behaviorParameters
            .BrainParameters
            .ActionSpec
            .NumDiscreteActions;
        actionHistory = new List<float>(new float[actionHistorySize * numContinuousActions]);
        actionHistoryDiscrete = new List<float>(new float[actionHistorySize * numDiscreteActions]);
        timer = 0f;
        fr.MLRestore();
        rewardSources.Clear();

        // No need to reset position and rotation since fr.MLRestore handles it
    }

    bool feetOnGround()
    {
        return calfBoot.touchingFloor && calf2Boot.touchingFloor;
    }
}
