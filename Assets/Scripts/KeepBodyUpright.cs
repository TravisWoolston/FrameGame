using UnityEngine;

public class KeepBodyUpright : MonoBehaviour
{
    public Transform head; // Reference to the head GameObject
    public float forceStrength = 10.0f; // Adjust the force strength
    public float maxForce = 50.0f; // Maximum force to apply

    private Rigidbody2D headRigidbody; // Reference to the head's Rigidbody2D

    void Start()
    {
        // Ensure the head GameObject has a Rigidbody2D component
        headRigidbody = head.GetComponent<Rigidbody2D>();

        if (headRigidbody == null)
        {
            Debug.LogError("The head GameObject must have a Rigidbody2D component.");
            enabled = false;
        }
    }

    void FixedUpdate()
    {
        // Calculate the angle between the body and the upright direction (vertical axis)
        float angleToUpright = Vector2.SignedAngle(Vector2.up, transform.up);

        // Calculate the force needed to keep the body upright
        float force = angleToUpright * forceStrength;

        // Limit the force to the maximum defined value
        force = Mathf.Clamp(force, -maxForce, maxForce);

        // Calculate the force vector in the direction of the upright angle
        Vector2 forceVector = Quaternion.Euler(0, 0, angleToUpright) * Vector2.up * force;
    Debug.Log(forceVector);
        // Apply the force to the head's Rigidbody2D
        headRigidbody.AddForce(forceVector);
    }
}
