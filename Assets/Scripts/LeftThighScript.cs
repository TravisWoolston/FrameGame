using UnityEngine;

public class LeftThighScript : MonoBehaviour
{
    public HingeJoint2D thighJoint; // Reference to the right thigh's HingeJoint2D
    public Transform spine; // Reference to the spine GameObject
    public float rotationSpeed = 10.0f; // Adjust the rotation speed

    private Quaternion initialRotation; // Initial rotation of the thigh

    private void Start()
    {
        if (spine == null)
        {
            Debug.LogWarning("Spine reference not assigned. Please assign the spine GameObject.");
            enabled = false;
            return;
        }

        // Record the initial rotation of the thigh
        initialRotation = transform.rotation;
    }

    private void FixedUpdate()
    {
        // Calculate the rotation difference between the current rotation and the desired rotation
        Quaternion desiredRotation = Quaternion.FromToRotation(Vector3.up, spine.up) * initialRotation;

        // Calculate the rotation difference as an angle-axis representation
        Vector3 rotationDifferenceAxis;
        float rotationDifferenceAngle;
        Quaternion rotationDifference = desiredRotation * Quaternion.Inverse(transform.rotation);
        rotationDifference.ToAngleAxis(out rotationDifferenceAngle, out rotationDifferenceAxis);

        // Calculate the target angular velocity to rotate the thigh
        float targetAngularVelocity = rotationDifferenceAxis.z * rotationDifferenceAngle * rotationSpeed;

        // Apply the target angular velocity to the thigh joint
        JointMotor2D motor = thighJoint.motor;
        motor.motorSpeed = targetAngularVelocity;
        thighJoint.motor = motor;
    }
}