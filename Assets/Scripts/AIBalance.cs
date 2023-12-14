using UnityEngine;

public class AIBalance : MonoBehaviour
{
    public HingeJoint2D leftHip;
    public HingeJoint2D rightHip;
    public HingeJoint2D leftKnee;
    public HingeJoint2D rightKnee;
    public HingeJoint2D leftShoulder;
    public HingeJoint2D rightShoulder;
    public HingeJoint2D leftElbow;
    public HingeJoint2D rightElbow;
    public Transform spine; // Reference to the spine GameObject

    private Vector2 initialPosition; // Store the initial position of the stick figure

    public float hipBalanceFactor = 0.5f; // Priority given to hip motors for balance
    public float shoulderBalanceFactor = 0.5f; // Priority given to shoulder motors for balance
    public float kneeBalanceFactor = 0.1f; // Sensitivity for knee rotation
    public float balanceIntegral = 0.0f;
    public float balanceDerivative = 0.0f;
    public float fallThreshold = 45.0f; // Threshold angle for detecting a fall
    public float recoveryTorque = 500.0f; // Torque applied for recovery
    public float desiredBodyHeight = 2.0f; // The desired height of the stick figure
    public float kneeRotationSpeed = 100.0f; // Speed to rotate the knees towards the ground

    private float lastLeanError = 0.0f;

    private void Start()
    {
        // Store the initial position of the stick figure
        initialPosition = transform.position;
    }

    private void Update()
    {
        // Calculate the current lean angle of the stick figure based on the spine's rotation
        float leanAngle = 90.0f - spine.eulerAngles.z;

        // Check if the stick figure has fallen over (exceeded the fall threshold)
        if (Mathf.Abs(leanAngle) > fallThreshold)
        {
            // Apply torque to the hips to help the stick figure stand up
            JointMotor2D leftHipMotor = leftHip.motor;
            leftHipMotor.motorSpeed = recoveryTorque;
            leftHip.motor = leftHipMotor;

            JointMotor2D rightHipMotor = rightHip.motor;
            rightHipMotor.motorSpeed = -recoveryTorque;
            rightHip.motor = rightHipMotor;
        }
        else
        {
            // If not fallen over, apply the balancing logic

            // Calculate the error between the desired and actual knee angles
            float leftKneeError = -kneeRotationSpeed - leftKnee.jointAngle;
            float rightKneeError = -kneeRotationSpeed - rightKnee.jointAngle;

            // Calculate the motor speed for the knee hinge joints to rotate towards the ground
            float kneeMotorSpeed = kneeBalanceFactor * (leftKneeError + rightKneeError);

            // Apply the calculated motor speed to the knee hinge joints
            JointMotor2D leftKneeMotor = leftKnee.motor;
            leftKneeMotor.motorSpeed = kneeMotorSpeed;
            leftKnee.motor = leftKneeMotor;

            JointMotor2D rightKneeMotor = rightKnee.motor;
            rightKneeMotor.motorSpeed = kneeMotorSpeed;
            rightKnee.motor = rightKneeMotor;

            // Calculate the error between the left and right shoulder angles
            float leftShoulderAngle = leftShoulder.jointAngle;
            float rightShoulderAngle = rightShoulder.jointAngle;
            float shoulderError = leftShoulderAngle - rightShoulderAngle;

            // Calculate the motor speed for the shoulder hinge joints to balance the shoulders
            float shoulderMotorSpeed = shoulderBalanceFactor * shoulderError;

            // Apply the calculated motor speed to the shoulder hinge joints
            JointMotor2D leftShoulderMotor = leftShoulder.motor;
            leftShoulderMotor.motorSpeed = shoulderMotorSpeed;
            leftShoulder.motor = leftShoulderMotor;

            JointMotor2D rightShoulderMotor = rightShoulder.motor;
            rightShoulderMotor.motorSpeed = shoulderMotorSpeed;
            rightShoulder.motor = rightShoulderMotor;

            // Calculate the integral and derivative of the error
            balanceIntegral += leanAngle * Time.deltaTime;
            balanceDerivative = (leanAngle - lastLeanError) / Time.deltaTime;

            // Calculate the motor speed for the hip hinge joints
            float hipMotorSpeed = hipBalanceFactor * (leanAngle + balanceIntegral * 1.0f + balanceDerivative * 0.1f);

            // Apply the calculated motor speed to the hip hinge joints
            JointMotor2D leftHipMotor = leftHip.motor;
            leftHipMotor.motorSpeed = hipMotorSpeed;
            leftHip.motor = leftHipMotor;

            JointMotor2D rightHipMotor = rightHip.motor;
            rightHipMotor.motorSpeed = hipMotorSpeed;
            rightHip.motor = rightHipMotor;

            // Remember the current error for the next frame
            lastLeanError = leanAngle;
        }
    }
}
