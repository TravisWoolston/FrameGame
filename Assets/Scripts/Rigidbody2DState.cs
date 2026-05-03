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
        initialVelocity = rb.linearVelocity;
    }

    public void CaptureState(Rigidbody2D rb, Rigidbody2D frozenRB)
    {
        frozenRB.transform.position = rb.transform.position;
        frozenRB.transform.rotation = rb.transform.rotation;
        initialPosition = frozenRB.transform.position;
        initialRotation = frozenRB.transform.rotation;

        initialVelocity = rb.linearVelocity;
        if(rb.gameObject.name != "Hips")
        activeAngle = rb.gameObject.GetComponent<HingeJoint2D>().jointAngle;
    }

    public void RestoreState(Rigidbody2D rb, Rigidbody2D frozenRB)
    {
        // Teleport to the checkpoint position (this is the YOMI revert mechanic)
        rb.transform.position = initialPosition;
        rb.transform.rotation = initialRotation;
        rb.linearVelocity = initialVelocity;

        // Joint matching is now handled by PoseDriver (PD controller) which
        // continuously and smoothly drives each joint toward the frozen copy's angles.
        // The old approach set motor.motorSpeed = difference * 10 (no damping) and
        // locked limits to ±1° (making the figure stiff). PoseDriver replaces both.
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
