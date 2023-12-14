using UnityEngine;

public class Rigidbody2DState : MonoBehaviour
{
    private Vector2 initialPosition;
    private Quaternion initialRotation;
    private Vector2 initialVelocity;
    private float activeAngle;
    public float isGrabbing;

    // Capture the current state of the Rigidbody2D
    public Rigidbody2DState(Rigidbody2D rb)
    {
        initialPosition = rb.transform.position;
        initialRotation = rb.transform.rotation;
        initialVelocity = rb.velocity;
    }

    public void CaptureState(Rigidbody2D rb, Rigidbody2D frozenRB)
    {
        frozenRB.transform.position = rb.transform.position;
        frozenRB.transform.rotation = rb.transform.rotation;
        initialPosition = frozenRB.transform.position;
        initialRotation = frozenRB.transform.rotation;

        initialVelocity = rb.velocity;
        if(rb.gameObject.name != "Hips")
        activeAngle = rb.gameObject.GetComponent<HingeJoint2D>().jointAngle;
    }

    public void RestoreState(Rigidbody2D rb, Rigidbody2D frozenRB)
    {
        rb.transform.position = initialPosition;
        rb.transform.rotation = initialRotation;
        if (
            rb.gameObject.tag != "Head"
            && rb.gameObject.tag != "Spine"
            && rb.gameObject.name != "Hips"
            && !rb.name.Contains("bone")
        )
        {
            HingeJoint2D frozenAngle = frozenRB.gameObject.GetComponent<HingeJoint2D>();
            float jointAngle = frozenAngle.jointAngle;
            HingeJoint2D realAngle = rb.gameObject.GetComponent<HingeJoint2D>();
            JointAngleLimits2D limits = realAngle.limits;
            // Debug.Log("activeAngle " + activeAngle.jointAngle);
            float angleDifference = Mathf.DeltaAngle(activeAngle, frozenAngle.jointAngle);

            // Set the motor speed and direction
            JointMotor2D motor = realAngle.motor;
            // motor.motorSpeed = Mathf.Abs((activeAngle.jointAngle - frozenAngle.jointAngle) * 10);

            if (angleDifference > 0)
            {
                // If the shortest path is clockwise, set positive motor speed
                motor.motorSpeed = Mathf.Abs((activeAngle - frozenAngle.jointAngle) * 10);
            }
            else if (angleDifference < 0)
            {
                // If the shortest path is counterclockwise, set negative motor speed
                motor.motorSpeed = -Mathf.Abs((activeAngle - frozenAngle.jointAngle) * 10);
            }

            // Set the motor
            realAngle.motor = motor;

            // Debug.Log(motor.maxMotorTorque);

            if (
                rb.transform.parent.name != "FighterAgent"
                && rb.transform.parent.name != "FighterAgent(Clone)"
            )
            {
                limits.min = jointAngle - 1;
                limits.max = jointAngle + 1;
                realAngle.limits = limits;
            }

            //    rb.gameObject.GetComponent<HingeJoint2D>().limits.min = frozenAngle - 1;
            //    rb.gameObject.GetComponent<HingeJoint2D>().limits.max = frozenAngle + 1;
        }

        // rb.transform.position = frozenRB.transform.position;
        // rb.transform.rotation = frozenRB.transform.rotation;
        rb.velocity = initialVelocity;
    }

    public Vector2 GetInitialPosition()
    {
        return initialPosition;
    }

    public Quaternion GetInitialRotation()
    {
        return initialRotation;
    }
}
