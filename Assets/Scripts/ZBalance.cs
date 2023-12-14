using UnityEngine;

public class ZBalance : MonoBehaviour
{
    private float forceStrength = 200.0f; // Adjust the force strength
    private float maxTorque = 1000.0f; // Maximum torque to apply
    public float targetZRotation = 90.0f; // Target z-axis rotation angle

    private Rigidbody2D rb; // Reference to the object's Rigidbody2D

    void Start()
    {
        // Get the Rigidbody2D component of the object
        rb = GetComponent<Rigidbody2D>();

        if (rb == null)
        {
            Debug.LogError("The GameObject must have a Rigidbody2D component.");
            enabled = false;
        }
    }

    void FixedUpdate()
    {
        // Calculate the angle between the current z-rotation and the target z-rotation
        float angleDifference = targetZRotation - transform.eulerAngles.z;
        Debug.Log(transform.eulerAngles.z);
        // Calculate the torque needed to keep the z-rotation at the target angle
        float torque = angleDifference * forceStrength;

        // Limit the torque to the maximum defined value
        torque = Mathf.Clamp(torque, -maxTorque, maxTorque);

        // Apply the torque to the Rigidbody2D
        rb.AddTorque(torque);
    }
}

