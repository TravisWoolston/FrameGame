using UnityEngine;

public class RotateHingeJoint : MonoBehaviour
{
    public HingeJoint2D hingeJoint;
    public float rotationSpeed = 150f;

    // public float rotationDamping = 2f;
    public KeyCode left;
    public KeyCode right;
    private JointMotor2D motor;
    public bool selected = false;
    public GameObject legLock;
    public InteractableObject IO;
    public GameObject Spine;

    public bool useStartLimits = false;
    public float startMin = 0;
    public float startMax = 0;
    public float startingAngle = 0;
    public float lastInput = 0;
    public float targetRotationRef = 0;
    public Rigidbody2D rb;
    public StickFigureAgent sfa;
    public bool useLimits = false;
    public float min = 0;
        public float max = 0;
        public float motorSpeed = 0;
        public float maxTorque = 5000;

void Awake(){
            rb = GetComponent<Rigidbody2D>();
        if (this.gameObject.name == "Head")
        {
            hingeJoint = Spine.GetComponent<InteractableObject>().headJoint;
        }
        if (legLock != null)
        {
            IO = legLock.GetComponent<InteractableObject>();
        }
        motor = hingeJoint.motor;
        startingAngle = hingeJoint.jointAngle;

         JointAngleLimits2D limits = hingeJoint.limits;
        
        if (limits.min < limits.max)
        {
            min = limits.min;
            max = limits.max;
        }
        else
        {
            max = limits.min;
            min = limits.max;
        }
}
    void Start()
    {

        // startMin = hingeJoint.limits.min;
        // startMax = hingeJoint.limits.max;
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        if (sfa)
        {
            if (
                other.gameObject.name == "Spine" && this.gameObject.name == "Lower Arm"
                || other.gameObject.name == "Spine" && this.gameObject.name == "Lower Arm 2"
                || other.gameObject.name == "Spine" && this.gameObject.name == "Calf"
                || other.gameObject.name == "Spine" && this.gameObject.name == "Calf 2"
            )
            {
                sfa.penalize();
            }
            else
            {
                sfa.reward();
            }
        }
    }

    void Update()
    {
        // Check for input to rotate the hinge joint
        if (selected)
            RotateHingeJointWithInput();
        else
        {
            if (legLock != null)
            {
                // unlockJoint();
            }
            if (hingeJoint.transform.parent.name == "Fighter Copy(Clone)")
                hingeJoint.useMotor = false;
        }
    }

    void RotateHingeJointWithInput()
    {
        // Check for left arrow key
        if (Input.GetKey(left))
        {
            if (legLock != null)
            {
                lockJoint();
            }
            RotateJoint(-1); // Rotate counterclockwise
        }
        // Check for right arrow key
        else if (Input.GetKey(right))
        {
            if (legLock != null)
            {
                lockJoint();
            }
            RotateJoint(1); // Rotate clockwise
        }
        else
        {
            // Apply damping effect when no keys are pressed
            if (legLock != null)
                unlockJoint();
            hingeJoint.useMotor = false;
        }
    }

    void lockJoint()
    {
        IO.spaceJoint.enabled = true;
        IO.spaceJoint.useMotor = true;
    }

    void unlockJoint()
    {
        IO.spaceJoint.enabled = false;
        IO.spaceJoint.useMotor = false;
    }
    public void SetJointAngle(float targetRotation){
        hingeJoint.motor = motor;
        // targetRotation = Mathf.Clamp(targetRotation, min, max);
        if(targetRotation < min || targetRotation > max) return;

        targetRotationRef = targetRotation;



        motor.motorSpeed = (targetRotation - hingeJoint.jointAngle) / Time.deltaTime;
        motorSpeed = motor.motorSpeed;
        motor.maxMotorTorque = 5000;
        hingeJoint.motor = motor;
        // if (setLimits == true)
        // {
        //     JointAngleLimits2D limits = hingeJoint.limits;
        //     limits.min = targetRotation - 1;
        //     limits.max = targetRotation + 1;
        //     hingeJoint.limits = limits;
        // }
    }
    public void SetMotorSpeed(float motorSpeed){
        motor.motorSpeed = motorSpeed;
        hingeJoint.motor = motor;
    }
    public void RotateJoint(int direction)
    {
        lastInput = direction;
        if (direction == 2)
            direction = -1;
        if (direction == 0)
        {
            return;
        }
        // Calculate the target rotation based on the direction
        float targetRotation = hingeJoint.jointAngle + direction * rotationSpeed * Time.deltaTime;
        
 
        
        // else{
        if (targetRotation > 360 || targetRotation < -360)
            targetRotation = 0;
        targetRotationRef = targetRotation;
        // }


        motor.motorSpeed = (targetRotation - hingeJoint.jointAngle) / Time.deltaTime;
        hingeJoint.motor = motor;
        // if (setLimits == true)
        // {
        //     JointAngleLimits2D limits = hingeJoint.limits;
        //     limits.min = targetRotation - 1;
        //     limits.max = targetRotation + 1;
        //     hingeJoint.limits = limits;
        // }
    }


    public void setLimits()
    {
        JointAngleLimits2D limits = hingeJoint.limits;
        
        if (limits.min < limits.max)
        {
            min = limits.min;
            max = limits.max;
        }
        else
        {
            max = limits.min;
            min = limits.max;
        }
    }
}
