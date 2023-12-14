using UnityEngine;

public class Stand : MonoBehaviour
{
    public HingeJoint2D hingeJoint;
    public Transform groundCheck;
    public float rotationSpeed = 10.0f;
    private bool onFloor = false;
    void Start()
    {
        hingeJoint = GetComponent<HingeJoint2D>();
    }

    void FixedUpdate()
    {
        // Raycast to check if the leg is on the ground
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, 0.1f);
if (hit.collider != null)
        {
            // Calculate the angle between the current rotation and the angle to the ground.
            float targetRotation = Mathf.Atan2(hit.normal.y, hit.normal.x) * Mathf.Rad2Deg;

            // Calculate the difference between the current angle and the target angle.
            float angleDifference = targetRotation - hingeJoint.transform.eulerAngles.z;

            // Ensure the angle difference is between -180 and 180 degrees for smoother rotation.
            angleDifference = Mathf.Repeat(angleDifference + 180.0f, 360.0f) - 180.0f;

            // Calculate the rotation speed based on the angle difference.
            float speed = Mathf.Sign(angleDifference) * rotationSpeed;

            // Apply the motor speed to the hinge joint.
            JointMotor2D motor = hingeJoint.motor;
            motor.motorSpeed = speed;
            hingeJoint.motor = motor;

            // Enable the motor to apply rotation.
            hingeJoint.useMotor = true;
        }
        else
        {
            // No ground detected, disable the motor.
            hingeJoint.useMotor = false;
        }

    }
    private void OnCollisionStay2D(Collision2D collision){
        if(collision.gameObject.tag == "Floor"){

            onFloor=true;
// hingeJoint.useMotor = true;
        }
        else{
            onFloor = false;
        }
    }
}

