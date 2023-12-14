using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HeavyBoots : MonoBehaviour
{
    private Rigidbody2D rb;
    private float startMass;
    public bool touchingFloor = false;
    public Transform floorT;
    public HingeJoint2D floorJoint;
    public Color startingColor;
    // Start is called before the first frame update
    void Start()
    {
       rb = GetComponent<Rigidbody2D>();
       startMass = rb.mass;
       startingColor = GetComponent<UnityEngine.U2D.SpriteShapeRenderer>().color;
    }

               private void OnTriggerEnter2D(Collider2D other)
    {
        // Check if the object is on the conveyor belt layer or has a specific tag
        // if (other.gameObject.layer == LayerMask.NameToLayer("ConveyorBelt"))
        // {
            // Move the object with the conveyor belt using AddForce
            Rigidbody2D otherRb = other.GetComponent<Rigidbody2D>();
            if (otherRb.gameObject.name == "Conveyor")
            {
                // if(sfa){
                //     sfa.hAD();
                // }
               
                floorT = otherRb.transform;
                touchingFloor = true;
                // rb.mass = 200;
                // otherRb.GetComponent<Rigidbody2D>().position = new Vector2(0, otherRb.position.y);
            }
            else {
                touchingFloor = false;
            }
            
        // }
    }
    void OnCollisionExit2D(Collision2D other){
        // rb.mass = startMass;
        touchingFloor = false;
        // Debug.Log(this.gameObject.name);
    }
    void FixedUpdate()
    {
        if(floorT)
        {

        if(Mathf.Abs(rb.transform.position.y - floorT.position.y)>3){
            touchingFloor = false;
        }
        }
       
        if(touchingFloor){
            GetComponent<UnityEngine.U2D.SpriteShapeRenderer>().color = Color.red;

        }
        else {
                       GetComponent<UnityEngine.U2D.SpriteShapeRenderer>().color = startingColor;
 
        }
    }
}
