using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MotorEnabler : MonoBehaviour
{
    public HingeJoint2D hinge;
    private JointMotor2D motor;
    public Grabber grabber;
    // Start is called before the first frame update
    void Start()
    {
        hinge = GetComponent<HingeJoint2D>();
        motor = hinge.motor;
        motor.motorSpeed = 0;
    }

    // Update is called once per frame
    void Update()
    {
            JointAngleLimits2D limits = hinge.limits;

        if(hinge.jointAngle > limits.min && hinge.jointAngle < limits.max && hinge.transform.parent.name == "Fighter(Clone)"){
            hinge.useMotor = false;
        } else {
            // hinge.useMotor = true;
        }
        if(grabber != null)
        if(grabber.isGrabbing){
            hinge.useLimits = false;
        }

    }
}
