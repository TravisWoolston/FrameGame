using UnityEngine;

public class LegZBalance : MonoBehaviour
{
  public Transform targetObject; // Reference to the target GameObject
    public float rotationSpeed = 10.0f; // Adjust the rotation speed

    private HingeJoint2D hingeJoint; // Reference to the leg's HingeJoint2D

    void Start()
    {
        hingeJoint = GetComponent<HingeJoint2D>();
        if (hingeJoint == null)
        {
            Debug.LogError("The leg GameObject must have a HingeJoint2D component.");
            enabled = false;
        }
    }

    void Update()
    {
        if (targetObject == null)
        {
            return; // No target to match rotation with
        }

        // Calculate the desired angle difference between the current z-rotation and the target z-rotation
        float desiredAngleDifference = targetObject.eulerAngles.z - transform.eulerAngles.z;

        // Calculate the target angle for the hinge joint based on the desired angle difference
        float targetAngle = hingeJoint.jointAngle + desiredAngleDifference;
        Debug.Log("targetAngle " + targetAngle);
        // Adjust the target angle based on the rotation speed
        targetAngle = Mathf.MoveTowardsAngle(hingeJoint.jointAngle, targetAngle, rotationSpeed * Time.deltaTime);

        // Set the hinge joint's target angle to match the calculated target angle
        hingeJoint.useMotor = true;
        JointMotor2D motor = hingeJoint.motor;
        motor.motorSpeed = (targetAngle - hingeJoint.jointAngle) / Time.deltaTime;
        hingeJoint.motor = motor;
    }
}