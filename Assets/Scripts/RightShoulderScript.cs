using UnityEngine;

public class RightShoulderScript : MonoBehaviour
{
    public HingeJoint2D shoulderJoint; // Reference to the right shoulder's HingeJoint2D
    public Transform spine; // Reference to the spine GameObject
    public float targetSpineZRotation = 90.0f; // Target spine z-rotation angle
    public float rotationSpeed = 10.0f; // Adjust the rotation speed

    private void Update()
    {
        if (spine == null)
        {
            Debug.LogWarning("Spine reference not assigned. Please assign the spine GameObject.");
            return;
        }

        // Calculate the angle difference between the spine's z-rotation and the target angle
        float angleDifference = targetSpineZRotation - spine.eulerAngles.z;

        // Calculate the target angle for the shoulder joint
        float targetAngle = shoulderJoint.jointAngle + angleDifference;

        // Adjust the target angle based on the rotation speed
        targetAngle = Mathf.MoveTowardsAngle(shoulderJoint.jointAngle, targetAngle, rotationSpeed * Time.deltaTime);

        // Apply the target angle to the shoulder joint
        JointMotor2D motor = shoulderJoint.motor;
        motor.motorSpeed = (targetAngle - shoulderJoint.jointAngle) / Time.deltaTime;
        shoulderJoint.motor = motor;
    }
}
