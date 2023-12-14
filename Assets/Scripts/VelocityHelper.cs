using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VelocityHelper : MonoBehaviour
{
    public Rigidbody2D rbSpine;
    public Rigidbody2D rbCalf;
    public Rigidbody2D rbCalf2;
    public Rigidbody2D rbThigh;
    public Rigidbody2D rbThigh2;
    public Rigidbody2D rbHead;
    private float antiRotationForce = 1000f;
    void Start() { }

    // Update is called once per frame
    void FixedUpdate()
    {
        // Debug.Log(rbSpine.angularVelocity);
        // if (rbSpine.angularVelocity > 0.1f || rbSpine.angularVelocity < -0.1f)
        // {
            // Calculate and apply anti-rotation force
            float torque = (-rbSpine.angularVelocity*(float).8) * antiRotationForce;
            rbSpine.AddTorque(torque);
        // }
    
        // if (
        //     rbCalf.transform.rotation.z < rbThigh.transform.rotation.z
        //     && rbCalf2.transform.rotation.z < rbThigh2.transform.rotation.z
        // ) { 
        //     rbSpine.AddForce(-rbSpine.transform.up * 5000f, ForceMode2D.Force);
            
        
        // }
    }
}
