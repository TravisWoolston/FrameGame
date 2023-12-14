using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GroundCheck : MonoBehaviour
{

    private Rigidbody2D rb;
    private float startMass;
    public bool touchingFloor = false;
    public Color startingColor;
    private SpriteRenderer headRenderer;
    private UnityEngine.U2D.SpriteShapeRenderer ssRenderer;
    private Transform floorT;
    void Start()
    {
       rb = GetComponent<Rigidbody2D>();
       startMass = rb.mass;
       if(this.gameObject.name == "Head"){
        headRenderer = GetComponent<SpriteRenderer>();
        startingColor = GetComponent<SpriteRenderer>().color;
       }
       else{
        ssRenderer = GetComponent<UnityEngine.U2D.SpriteShapeRenderer>();
        startingColor = ssRenderer.color;
       }
       
    }

               private void OnTriggerStay2D(Collider2D other)
    {
        // Check if the object is on the conveyor belt layer or has a specific tag
        // if (other.gameObject.layer == LayerMask.NameToLayer("ConveyorBelt"))
        // {
            // Move the object with the conveyor belt using AddForce
            Rigidbody2D otherRb = other.GetComponent<Rigidbody2D>();
            if (otherRb.gameObject.name == "Conveyor")
            {
                floorT = otherRb.gameObject.transform;
                touchingFloor = true;
                // rb.mass = 200;
                // otherRb.GetComponent<Rigidbody2D>().position = new Vector2(0, otherRb.position.y);
            }
            
        // }
    }
    void OnCollisionExit2D(Collision2D other){
        // rb.mass = startMass;
        touchingFloor = false;
    }
    void FixedUpdate()
    {
                if(floorT)
        {

        if(Mathf.Abs(rb.transform.position.y - floorT.position.y)>3){
            touchingFloor = false;
        }
        }
        if(this.gameObject.name == "Head"){
                    if(touchingFloor){
            headRenderer.color = Color.red;

        }
        else {
                       headRenderer.color = startingColor;
 
        }
        }
        else if(touchingFloor){
            ssRenderer.color = Color.red;

        }
        else {
                       ssRenderer.color = startingColor;
 
        }
    }
}
