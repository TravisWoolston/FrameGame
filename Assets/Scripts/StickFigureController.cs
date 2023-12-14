using UnityEngine;

public class StickFigureController : MonoBehaviour
{
    public Rigidbody2D[] bodyParts; // Assign the Rigidbody2D components of your stick figure parts

    public float maxAngle = 10.0f; // The maximum allowed lean angle
    public float balanceForce = 5.0f; // The force applied to correct leaning

    private void Update()
    {
        // Calculate the angle of the stick figure
        float averageAngle = 0.0f;
        foreach (Rigidbody2D rb in bodyParts)
        {
            averageAngle += rb.transform.eulerAngles.z;
        }
        averageAngle /= bodyParts.Length;

        // Calculate the corrective force
        float angleDiff = averageAngle - 90.0f; // Assuming upright is 90 degrees
        if (Mathf.Abs(angleDiff) > maxAngle)
        {
            // Apply a force to counterbalance the lean
            Vector2 forceDirection = Quaternion.Euler(0, 0, -angleDiff) * Vector2.up;
            foreach (Rigidbody2D rb in bodyParts)
            {
                rb.AddForce(forceDirection * balanceForce);
            }
        }
    }
}
