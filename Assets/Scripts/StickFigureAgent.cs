using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Linq;

public class StickFigureAgent : Agent
{
    private Vector3 startingPosition = Vector3.zero;
    private Quaternion startingQuat = Quaternion.identity;

    // Reference to the stick figure's hinge joints
    public GameObject[] jointGOs;
    public HingeJoint2D[] joints;
    public Vector2[] initialPositions;
    private Quaternion[] initialRotations;

    // Speed at which torque can be applied
    public float torqueSpeed = 100f;

    public float torque;

    // Define observations
    public Transform head;
    public Transform calfT;
    public Transform calf2T;
    public Transform thighT;
    public Transform thigh2T;
    public Transform armT;
    public Transform arm2T;
    public Transform lowerArmT;
    public Transform lowerArm2T;
    public Transform marker;
    public Transform target;
    public GameObject calf;
    public GameObject calf2;
    private MagnetBoot calfMag;
    private MagnetBoot calf2Mag;
    public Transform hips;
    public GameObject GC;
    public FreezeReplay fr;
    public float maxDistance = -10000;
    public Transform spine;

    private HeavyBoots calfBoot;
    private HeavyBoots calf2Boot;
    public Transform floorT;
    private float rotationSpeed = 500f;

    // private bool updateSpeed = false;
    // private bool useLimits = true;
    private bool updateBools = false;
    private float timer = 0;
    private float bestTime = 99999;
    private float bestReward = 0;
    public RayPerceptionSensorComponent2D[] RPSArray;
    private CameraFollow cameraFollow;
    private GroundCheck headCheck;
    private bool reachedTarget = false;
    private float timeToReachtarget = 0f;
    private float someThreshold = 8f;
    private Dictionary<string, float> rewardSources = new Dictionary<string, float>();
    private Dictionary<string, float> rewardRefs = new Dictionary<string, float>();
    public GameObject copy;
    public bool magnetsActive = true;
    private RotateHingeJoint[] RHJs;
    private int loopLength;
    public bool isMagActive = false;
    public bool isMag2Active = false;
    private float distanceWhenMagActivated = 0f;
    private float distanceWhenMag2Activated = 0f;
    private float UIRewards = 0;
    private Rigidbody2D[] rbs;
    private string bestNet = "";
    private float bestNetScore = -99999;
    private string worstNet = "";
    private float worstNetScore = 99999;
    private float highScore = -999999;
    private float prevScore = -999999;
    public Texture2D arrowTexture;
    private Vector2 toTarget;
    public bool updateMotorSpeed = false;
    private float distance;
    private float spineReward;
    private float hToFReward = 2.5f;
    private float hAHReward;
    private bool recovering = false;

    void Awake()
    {
        calfBoot = calf.GetComponent<HeavyBoots>();
        calf2Boot = calf2.GetComponent<HeavyBoots>();
        calfMag = calf.GetComponent<MagnetBoot>();
        calf2Mag = calf2.GetComponent<MagnetBoot>();
        headCheck = head.gameObject.GetComponent<GroundCheck>();
        loopLength = jointGOs.Length;
        rbs = new Rigidbody2D[jointGOs.Length];
        if (magnetsActive)
        {
            RHJs = new RotateHingeJoint[loopLength + 2];
            loopLength++;
        }
        else
        {
            RHJs = new RotateHingeJoint[loopLength];
        }
        for (int h = 0; h < loopLength; h++)
        {
            if (h == jointGOs.Length)
            {
                RHJs[h] = calf.GetComponents<RotateHingeJoint>()[1];
                RHJs[h + 1] = calf2.GetComponents<RotateHingeJoint>()[1];
            }
            else
            {
                rbs[h] = jointGOs[h].GetComponent<Rigidbody2D>();
                RHJs[h] = jointGOs[h].GetComponent<RotateHingeJoint>();
            }
        }
    }

    void Start()
    {
        initialPositions = new Vector2[jointGOs.Length];
        initialRotations = new Quaternion[jointGOs.Length];
        int i = 0;
        if (GameObject.FindGameObjectWithTag("MainCamera"))
            cameraFollow = GameObject
                .FindGameObjectWithTag("MainCamera")
                .GetComponent<CameraFollow>();

        foreach (GameObject go in jointGOs)
        {
            // go.GetComponent<RotateHingeJoint>().setLimits();
            // go.GetComponent<RotateHingeJoint>().setLimits = true;
            initialPositions[i] = go.GetComponent<Transform>().localPosition;
            initialRotations[i] = go.GetComponent<Transform>().rotation;
            i++;
        }

        // if (!Academy.Instance.IsCommunicatorOn)
        // {
        //     this.MaxStep = 20000;
        // }
    }


    private void HandleReward(string source, float value)
    {
        UIRewards += value;
        // Update the reward sources dictionary
        if (rewardSources.ContainsKey(source))
        {
            rewardSources[source] += value;
            AddReward(value);
            if (rewardSources[source] > bestNetScore)
            {
                bestNetScore = rewardSources[source];
                bestNet = source;
            }
                     else if (rewardSources[source] < worstNetScore)
            {
                worstNetScore = rewardSources[source];
                worstNet = source;
            }
        }
        else
        {
            rewardSources.Add(source, value);
            AddReward(value);
        }
    }

    public void penalize()
    {
        // HandleReward("penalize (external)", -0.1f);
    }

    public void reward()
    {
        // HandleReward("reward (external)", 0.1f);
    }

    public override void Initialize() { }

    public bool feetOnGround()
    {
        return calfBoot.touchingFloor && calf2Boot.touchingFloor;
    }

    // Define actions
    public override void OnActionReceived(ActionBuffers vectorAction)
    {
        timer += Time.deltaTime;
        float multiplier = 1;
        // if(isMagActive || isMag2Active){
        //     multiplier = 2;
        // }
        float newDistance = GetDistance();
        if(distance !=0)
        HandleReward("Distance Reward", (distance - newDistance)*multiplier * 2);
        if((distance - newDistance) < 0){
            HandleReward("Distance Reward", -.1f);
        }
        distance = newDistance;
        float newSpineReward = CalculateRewardReturn(
            spine.rotation.eulerAngles.z,
            90,
            "spine rotation"
        );
        HandleReward("Spine Reward", (newSpineReward - spineReward));
        recovering = ((newSpineReward - spineReward) >= -.05);
        spineReward = newSpineReward;
        if(floorT){
                        float newHToF = headToFloorReturn();
            HandleReward("HToF Reward", newHToF - hToFReward);
            hToFReward = newHToF;
            float newHAH = headAboveHipsReturn();
            HandleReward("Head above hips", newHAH - hAHReward);
            hAHReward = newHAH;

        }


        for (int i = 0; i < RHJs.Length; i++)
        {
            // float scaledValue = vectorAction.ContinuousActions[i] * 180;

            // RHJs[i].SetJointAngle(scaledValue);
            HingeJoint2D hinge = RHJs[i].hingeJoint;
            float middle = (hinge.limits.min + hinge.limits.max) / 2f;
            float normalisedAngle = (hinge.jointAngle - middle) / (hinge.limits.max - middle);
            float difference = vectorAction.ContinuousActions[i] - normalisedAngle;

            JointMotor2D motor = hinge.motor;
            motor.motorSpeed = Mathf.Clamp(difference * 3000f, -200, 200);
            hinge.motor = motor;
            if (headCheck)
                if (headCheck.touchingFloor)
                {
                    // HandleReward("Head touching floor", -.1f);
                    HandleReward("Head touching floor", -100f);
                    OnEpisodeEnd();
                    EndEpisode();
                    // return;
                }
                else
                {
                    // HandleReward("Head touching floor", .1f);
                }
            if (i >= jointGOs.Length)
                continue;

            GroundCheck groundCheck = jointGOs[i].GetComponent<GroundCheck>();
            if (groundCheck)
            {
                GroundChecker(groundCheck);
            }
        }

        if (updateMotorSpeed)
        {
            int j = 0;
            for (int i = RHJs.Length; i < RHJs.Length + 8; i++)
            {
                RHJs[j].SetMotorSpeed(vectorAction.ContinuousActions[i] * RHJs[j].motorSpeed);
                j++;
            }
        }
        if (magnetsActive)
        {
            calfMag.sfaBool = vectorAction.DiscreteActions[0];
            calf2Mag.sfaBool = vectorAction.DiscreteActions[1];

            isMagActive = calfMag.joint.enabled;
            isMag2Active = calf2Mag.joint.enabled;

            // if (!isMagActive && !isMag2Active)
            // {
            //     HandleReward("Magnet usage", -.1f);
            // }
            // else
            // {
            //     HandleReward("Magnet usage", .1f);
            //     // calfDotReward();
            // }
        }
        // balancedLegPositions();


        if (feetOnGround() || floorT)
        {
            if (!calfBoot.floorT)
            {
                calfBoot.floorT = floorT;
            }
            else
            {
                floorT = calfBoot.floorT;
            }

            // hipsToFloor();
            // headToFloor();
            // headAboveHips();
            // CalculateReward(calfT.rotation.z, -90, "calf rotation");
            // CalculateReward(calf2T.rotation.z, -90, "calf rotation");
        }
        if (spine.localPosition.y < target.localPosition.y - 20)
        {
            Debug.Log("SOMEBODY FELL");
            HandleReward("Fell", -1000f);
            OnEpisodeEnd();
            EndEpisode();
        }

        // if (head.localPosition.y > spine.localPosition.y)
        // {
        //     HandleReward("head to spine", 0.1f);
        // }
        // else
        // {
        //     HandleReward("head to spine", -0.1f);
        // }

        // spineToCalf();
        if (target && !reachedTarget)
        {
            float distanceToTarget = Vector3.Distance(spine.position, target.position);
            if (distanceToTarget < someThreshold) // Adjust someThreshold as needed
            {
                reachedTarget = true;
                timeToReachtarget = timer;
            }
        }
        if (reachedTarget)
        {
            // Calculate time-based reward
            float timeReward = 100;
            if (timer < bestTime)
            {
                bestTime = timer;
                // if (bestTime < cameraFollow.bestTim)
                // {
                //     cameraFollow.followBestTime(transform, bestTime);
                // }
            }

            if (timeReward > bestReward)
                bestReward = timeReward;
            HandleReward("Time to target", timeReward);
            OnEpisodeEnd();
            EndEpisode();
            // return;
        }
        if (target)
        {
            // dotToTargetReward();
        }
    }

    private void GroundChecker(GroundCheck groundCheck)
    {
        if (
            groundCheck.gameObject.name == "Lower Arm"
            || groundCheck.gameObject.name == "Lower Arm 2"
        )
        {
            if (groundCheck.touchingFloor)
            {
                HandleReward("arms touching floor", -.1f);
                if(!recovering){
                    HandleReward("arms touching floor", -100f);
                OnEpisodeEnd();
                EndEpisode();
                }
                // HandleReward("Hands on floor", -10f);
                // HandleReward("arms touching floor", -.01f);
                
            }
            else
            {
                // HandleReward("arms touching floor", .01f);
            }
        }
        else if (
            groundCheck.touchingFloor
            && groundCheck.gameObject.name != "Lower Arm"
            && groundCheck.gameObject.name != "Lower Arm 2"
        )
        {
            HandleReward("Limbs touching floor", -.1f);
            // HandleReward("Limbs touching floor", -100f);
            // OnEpisodeEnd();
            // EndEpisode();
            // return;
        }
    }

    private void hipsToFloor()
    {
        float hipsToFloor = (hips.localPosition.y - floorT.localPosition.y) / 10;
        AddLabel("hipsToFloor", hipsToFloor);
        if (hipsToFloor < 2)
        {
            HandleReward("hips to floor", -(2f - hipsToFloor));
        }
        else
        {
            HandleReward("hips to floor", hipsToFloor/100);
        }
    }

    private float hipsToFloorReturn()
    {
        float hipsToFloor = hips.localPosition.y - floorT.localPosition.y;
        AddLabel("hipsToFloor", hipsToFloor);
        return hipsToFloor;
    }

    private void headAboveHips()
    {
        float hToF = head.localPosition.y - floorT.localPosition.y;
        float hipsToFloor = hips.localPosition.y - floorT.localPosition.y;
        if (hipsToFloor > hToF)
        {
            HandleReward("Head above hips", -.1f);
        }
        else
        {
            HandleReward("Head above hips", .1f);
        }
    }
  private float headAboveHipsReturn()
    {
        float hToF = head.localPosition.y - floorT.localPosition.y;
        float hipsToFloor = hips.localPosition.y - floorT.localPosition.y;
        return hToF - hipsToFloor;
    }
    private void headToFloor()
    {
        float hToF = (head.localPosition.y - floorT.localPosition.y) / 10;
        AddLabel("headToFloor", hToF);
        if (hToF < 2.5)
        {
            HandleReward("Head to Floor", -(2.5f - hToF));
        }
        else
        {
            HandleReward("Head to Floor", hToF/100);
        }
    }

    private float headToFloorReturn()
    {
        float hToF = head.localPosition.y - floorT.localPosition.y;
        AddLabel("headToFloor", hToF);
        return hToF;
    }

    private void spineToCalf()
    {
        float spineToCalf = (Mathf.Abs(spine.localPosition.y - calfT.localPosition.y) / 10) - .5f;
        float spineToCalf2 = (Mathf.Abs(spine.localPosition.y - calf2T.localPosition.y) / 10) - .5f;

        if (spineToCalf < 0)
        {
            HandleReward("spine to calf", -.1f);
        }
        else
        {
            HandleReward("spine to calf", spineToCalf);
        }
        if (spineToCalf2 < 0)
        {
            HandleReward("spine to calf 2", -.1f);
        }
        else
        {
            HandleReward("spine to calf 2", spineToCalf2);
        }
    }

    private void headToCalf()
    {
        if (head.localPosition.y > calfT.localPosition.y)
        {
            HandleReward("head to calf", 0.01f);
        }
        else
        {
            HandleReward("head to calf", -0.1f);
        }
        if (head.localPosition.y > calf2T.localPosition.y)
        {
            HandleReward("head to calf 2", 0.01f);
        }
        else
        {
            HandleReward("head to calf 2", -0.1f);
        }
        if (
            head.localPosition.y > calfT.localPosition.y + 10
            && head.localPosition.y > calf2T.localPosition.y + 10
        )
        {
            HandleReward("head to calf(S)", 0.1f);
        }
        else
        {
            HandleReward("head to calf(S)", -0.1f);
        }
    }

    private void dotToTargetReward()
    {
        Vector2 toTarget = target.position - spine.position;

        // Calculate the dot product between the agent's velocity and the direction to the target
        float dot = Vector2.Dot(
            spine.gameObject.GetComponent<Rigidbody2D>().velocity.normalized,
            toTarget.normalized
        );

        // Reward the agent if the dot product is positive (i.e., moving towards the target)
        if (dot > 0)
        {
            HandleReward("dot", 10f); // You can adjust the reward value as needed
            AddLabel("dot", dot);
        }
        // Punish the agent if the dot product is negative (i.e., moving away from the target)
        else
        {
            HandleReward("dot", -12f); // You can adjust the punishment value as needed
        }
    }

    private float RescaleValue(float value, float minValue, float maxValue, bool useAtan)
    {
        float val = (value - minValue) / (maxValue - minValue);
        //print(val);
        if (useAtan)
        {
            return Mathf.Atan(val) / (Mathf.PI / 2f);
        }
        return val;
    }

    private float GetDistance()
    {
        return Vector3.Distance(spine.localPosition, target.localPosition);
    }

    void balancedLegPositions()
    {
        if (
            (
                thighT.localPosition.x < spine.transform.localPosition.x
                && thigh2T.localPosition.x < spine.transform.localPosition.x
            )
            || (
                thighT.localPosition.x > spine.transform.localPosition.x
                && thigh2T.localPosition.x > spine.transform.localPosition.x
            )
        )
        {
            HandleReward("balanced leg positions", -1f);
        }
        else
        {
            HandleReward("balanced leg positions", 1f);
        }
    }

    void calfDotReward()
    {
        Transform targetTransform = target.transform;
        float calfDistance = Vector3.Distance(calfT.localPosition, target.localPosition);
        float calf2Distance = Vector3.Distance(calf2T.localPosition, target.localPosition);
        float spineDistance = Vector3.Distance(spine.transform.localPosition, target.localPosition);
        if ((isMagActive && !isMag2Active) || (!isMagActive && isMag2Active))
        {
            GameObject dotCalf = null;
            if (isMagActive && !isMag2Active)
            {
                dotCalf = calf2;
            }
            if (!isMagActive && isMag2Active)
            {
                dotCalf = calf;
            }
            Vector2 toTarget = target.localPosition - dotCalf.transform.localPosition;

            // Calculate the dot product between the agent's velocity and the direction to the target
            float dot = Vector2.Dot(
                dotCalf.GetComponent<Rigidbody2D>().velocity.normalized,
                toTarget.normalized
            );
            float calfDotReward = 10f;
            if (dot > 0)
            {
                HandleReward("calf dot", 10f); // You can adjust the reward value as needed
                AddLabel("calf dot", dot);
            }
            // Punish the agent if the dot product is negative (i.e., moving away from the target)
            else
            {
                HandleReward("calf dot", -12f); // You can adjust the punishment value as needed
            }
        }
    }

    void AddLabel(string source, float value)
    {
        if (rewardRefs.ContainsKey(source))
        {
            rewardRefs[source] = value;
        }
        else
        {
            rewardRefs.Add(source, value);
        }
    }

    private void TrackRewardPercentages()
    {
        string logString = "";
        bestNet = "";
        bestNetScore = -99999;
        worstNet = "";
        worstNetScore = 99999;
        float netScore = 0;
        var sortedRewardList = rewardSources.OrderByDescending(kvp => kvp.Value).ToList();
        foreach (var kvp in sortedRewardList)
        {
            logString += $"{kvp.Key}: {kvp.Value}\n";
            netScore += kvp.Value;
            if (kvp.Value > bestNetScore)
            {
                bestNetScore = kvp.Value;
                bestNet = kvp.Key;
            }
            if (kvp.Value < worstNetScore)
            {
                worstNetScore = kvp.Value;
                worstNet = kvp.Key;
            }
        }
        if (netScore > highScore)
        {
            highScore = netScore;
        }
        prevScore = netScore;
        if (netScore > cameraFollow.highScore)
        {
            cameraFollow.followHighScore(transform, netScore);
        }


        // string leaderBoard =
        //     $"Highest: {bestNet} {bestNetScore}\n Lowest: {worstNet} {worstNetScore}\n";
        // Debug.Log($"{leaderBoard} {logString}");
    }

    void OnEpisodeEnd()
    {
        reachedTarget = false;
        // Call the function to track and log reward percentages
        TrackRewardPercentages();

        // Reset the reward sources at the end of each episode
        rewardSources.Clear();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (target)
        {
            toTarget = target.localPosition - spine.transform.localPosition;
            sensor.AddObservation(toTarget.normalized);
            sensor.AddObservation(Vector3.Distance(spine.localPosition, target.localPosition));

            float proximity = Mathf.Abs(spine.rotation.eulerAngles.z - 90);
            float reward = 1.0f - Mathf.Clamp01(proximity / 180f);
            sensor.AddObservation(proximity);
            sensor.AddObservation(reward);
        }
        foreach (RayPerceptionSensorComponent2D RPS in RPSArray)
        {
            var rayOutputs = RayPerceptionSensor.Perceive(RPS.GetRayPerceptionInput()).RayOutputs;

            if (rayOutputs != null)
            {
                var lengthOfRayOutputs = RayPerceptionSensor
                    .Perceive(RPS.GetRayPerceptionInput())
                    .RayOutputs.Length;

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

        // if (floorT)
        //     sensor.AddObservation(floorT.transform.localPosition);
        // sensor.AddObservation(headArenaDistance);
        // sensor.AddObservation(maxDistance);
        // Observe joint angles
        foreach (RotateHingeJoint RHJ in RHJs)
        {
            HingeJoint2D hinge = RHJ.hingeJoint;
            Rigidbody2D rb = RHJ.GetComponent<Rigidbody2D>();
            Transform t = rb.transform;
            float middle = (hinge.limits.min + hinge.limits.max) / 2f;
            float normalisedAngle = (hinge.jointAngle - middle) / (hinge.limits.max - middle);
            sensor.AddObservation(normalisedAngle);
            sensor.AddObservation(RescaleValue(hinge.jointSpeed, 0, 200, false));
            sensor.AddObservation(rb.velocity);
            sensor.AddObservation(t.localPosition);
            sensor.AddObservation(t.rotation.z);
        }
        //         foreach (var joint in RHJs)
        // {
        //     sensor.AddObservation(joint.hingeJoint.jointAngle);
        //     sensor.AddObservation(joint.hingeJoint.jointSpeed);
        // }
        int i = 0;
        foreach (GameObject go in jointGOs)
        {
            RotateHingeJoint rHJ = go.GetComponent<RotateHingeJoint>();
            // Transform t = go.GetComponent<Transform>();
            sensor.AddObservation(rHJ.min);
            sensor.AddObservation(rHJ.max);

            // sensor.AddObservation(t.localPosition);
            // sensor.AddObservation(t.rotation.z);
            // sensor.AddObservation(go.GetComponent<RotateHingeJoint>().startingAngle);
            // sensor.AddObservation(rHJ.targetRotationRef);
            // sensor.AddObservation(rHJ.motorSpeed);
            
            i++;
        }
        if (magnetsActive)
        {
            sensor.AddObservation(calfMag.locked);
            sensor.AddObservation(calf2Mag.locked);
        }
        sensor.AddObservation(calfBoot.touchingFloor);
        sensor.AddObservation(calf2Boot.touchingFloor);
        sensor.AddObservation(spine.rotation.eulerAngles.z);
    }

    private bool isInRange(float value, float num1, float num2)
    {
        float min;
        float max;
        if (num1 < num2)
        {
            min = num1;
            max = num2;
        }
        else
        {
            min = num2;
            max = num1;
        }
        return value >= min && value <= max;
    }

    private void CalculateReward(float spineRotation, float targetRotation, string rewardType)
    {
        float proximity = Mathf.Abs(spineRotation - targetRotation);
        float reward = 1.0f - Mathf.Clamp01(proximity / 180f); // Normalize to [0, 1]
        AddLabel("spine reward", reward);
        if (reward < 0.5)
        {
            HandleReward(rewardType, -1f);
            // OnEpisodeEnd();
            // EndEpisode();
        }
        else if (reward < 0.8)
        {
            HandleReward(rewardType, -(.8f - reward) * 10);
        }
        else
        {
            HandleReward(rewardType, reward);
        }
    }

    private float CalculateRewardReturn(
        float spineRotation,
        float targetRotation,
        string rewardType
    )
    {
        float proximity = Mathf.Abs(spineRotation - targetRotation);
        float reward = 1.0f - Mathf.Clamp01(proximity / 180f); // Normalize to [0, 1]
        return reward;
    }

    private float headCalfDistance()
    {
        return head.localPosition.y - calfT.localPosition.y;
    }

    public override void OnEpisodeBegin()
    {
        float randomValue = Random.value;
        if (randomValue > .5f)
        {
            target.localPosition = new Vector3(
                copy.transform.position.x + 60,
                copy.transform.position.y,
                0
            );
        }
        else
        {
            target.localPosition = new Vector3(
                copy.transform.position.x - 60,
                copy.transform.position.y,
                0
            );
        }
        rewardSources.Clear();
        UIRewards = 0;
        if (magnetsActive)
        {
            calfMag.sfaBool = 1;
            calf2Mag.sfaBool = 1;
            calfBoot.touchingFloor = false;
            calf2Boot.touchingFloor = false;
        }
        float bestNetScore = -99999;
        float worstNetScore = 99999;
        maxDistance = Vector3.Distance(copy.transform.localPosition, target.localPosition);
        floorT = null;
        timer = 0;
        if (fr)
            fr.MLRestore();
    }
    
    void OnGUI()
    {
        if (!fr)
            return;
        if (!Camera.main)
            return;
        Vector3 screenPosition = Camera.main.WorldToScreenPoint(fr.gameObject.transform.position);
        GUI.Label(
            new Rect(screenPosition.x, Screen.height - screenPosition.y - 215, 200, 20),
            "time: " + timer
        );
        GUI.Label(
            new Rect(screenPosition.x, Screen.height - screenPosition.y - 200, 200, 20),
            "best time: " + bestTime
        );
        GUI.Label(
            new Rect(screenPosition.x, Screen.height - screenPosition.y - 185, 200, 20),
            "best time reward: " + bestReward
        );
        GUI.Label(
            new Rect(screenPosition.x, Screen.height - screenPosition.y - 160, 200, 20),
            "high score: " + highScore
        );
        GUI.Label(
            new Rect(screenPosition.x, Screen.height - screenPosition.y - 145, 200, 20),
            "prev score: " + prevScore
        );
        // Draw GUI label at the converted screen position
        GUI.Label(
            new Rect(screenPosition.x, Screen.height - screenPosition.y, 200, 20),
            UIRewards.ToString()
        );

        {
            GUI.Label(
                new Rect(screenPosition.x, Screen.height - screenPosition.y - 100, 200, 20),
                bestNet + ": " + bestNetScore
            );
            GUI.Label(
                new Rect(screenPosition.x, Screen.height - screenPosition.y - 85, 200, 20),
                worstNet + ": " + worstNetScore
            );
        }

        if (rewardRefs.ContainsKey("headToFloor") && floorT)
        {
            screenPosition = Camera.main.WorldToScreenPoint(
                new Vector3(floorT.position.x + 20, floorT.position.y)
            );
            if (rewardSources.ContainsKey("hipsToFloor"))
                GUI.Label(
                    new Rect(screenPosition.x - 200, Screen.height - screenPosition.y, 200, 20),
                    "hipsToFloor: " + rewardSources["hips to floor"]
                );
            // GUI.Label(
            //     new Rect(screenPosition.x, Screen.height - screenPosition.y + 15, 200, 20),
            //     "headToFloor: " + rewardRefs["headToFloor"]
            // );
            if (rewardSources.ContainsKey("HToF Reward"))
                GUI.Label(
                    new Rect(
                        screenPosition.x - 200,
                        Screen.height - screenPosition.y + 15,
                        200,
                        20
                    ),
                    "HToF Reward: " + rewardSources["HToF Reward"]
                );
            if (rewardSources.ContainsKey("balanced leg positions"))
                GUI.Label(
                    new Rect(screenPosition.x, Screen.height - screenPosition.y + 30, 200, 20),
                    "leg balance: " + rewardSources["balanced leg positions"]
                );
            GUI.Label(
                new Rect(screenPosition.x, Screen.height - screenPosition.y + 45, 200, 20),
                "Distance Reward: " + rewardSources["Distance Reward"]
            );
            GUI.Label(
                new Rect(screenPosition.x, Screen.height - screenPosition.y + 60, 200, 20),
                "Spine Reward " + rewardSources["Spine Reward"]
            );
            if(rewardSources.ContainsKey("Head above Hips"))
            GUI.Label(
                new Rect(screenPosition.x, Screen.height - screenPosition.y + 75, 200, 20),
                "head above hips " + rewardSources["Head above hips"]
            );
        }
        if (floorT)
        {
            screenPosition = Camera.main.WorldToScreenPoint(
                new Vector3(spine.position.x, floorT.position.y - 5)
            );

            // Draw GUI label at the converted screen position
            if (rewardSources.ContainsKey("Distance From target"))
            {
                GUI.Label(
                    new Rect(screenPosition.x, Screen.height - screenPosition.y + 15, 200, 20),
                    "Distance From target: " + rewardSources["Distance From target"].ToString()
                );
                GUI.Label(
                    new Rect(screenPosition.x, Screen.height - screenPosition.y + 30, 200, 20),
                    "distanceToTarget: " + rewardRefs["distanceToTarget"]
                );
                GUI.Label(
                    new Rect(screenPosition.x, Screen.height - screenPosition.y + 45, 200, 20),
                    "proximityReward: " + rewardRefs["proximityReward"]
                );
            }
            if (rewardSources.ContainsKey("correct foot forward"))
            {
                GUI.Label(
                    new Rect(screenPosition.x, Screen.height - screenPosition.y + 45, 200, 20),
                    "correct foot forward: " + rewardSources["correct foot forward"]
                );
                // screenPosition = Camera.main.WorldToScreenPoint(calfT.position);
                // GUI.Label(
                //     new Rect(screenPosition.x, Screen.height - screenPosition.y, 200, 20),
                //     "calf"
                // );
            }
        }
        if (target == null || arrowTexture == null || toTarget == null)
            return;
        screenPosition = Camera.main.WorldToScreenPoint(spine.position);

        float angle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
        GUIUtility.RotateAroundPivot(
            angle,
            new Vector2(screenPosition.x, Screen.height - screenPosition.y)
        );

        GUI.DrawTexture(
            new Rect(screenPosition.x, Screen.height - screenPosition.y, 200, 20),
            arrowTexture
        );

        // Reset the rotation after drawing the arrow
        // GUIUtility.RotateAroundPivot(-angle, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // RayCastInfo(RPSHead);
    }
}
