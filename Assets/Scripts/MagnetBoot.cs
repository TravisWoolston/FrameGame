using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MagnetBoot : MonoBehaviour
{
    public KeyCode inputKey;
    public HingeJoint2D joint;
    public bool locked = false;
    public int sfaBool = 0;
    private HeavyBoots calf;
    private Rigidbody2D rb;
    private HeavyBoots hb;
    // Start is called before the first frame update
    void Start()
    {
        calf = GetComponent<HeavyBoots>();
        rb = GetComponent<Rigidbody2D>();
        rb.constraints = RigidbodyConstraints2D.None;
        hb = GetComponent<HeavyBoots>();
    }

    // Update is called once per frame
    void Update()
    {
        if(joint.enabled == true){
            GetComponent<UnityEngine.U2D.SpriteShapeRenderer>().color = Color.blue;
        }
        else if(sfaBool == 1){
            if(hb.touchingFloor == false){
                GetComponent<UnityEngine.U2D.SpriteShapeRenderer>().color = Color.yellow;
            }
            else {
                GetComponent<UnityEngine.U2D.SpriteShapeRenderer>().color = Color.green;
            }
             
        }
        else {
        //   GetComponent<UnityEngine.U2D.SpriteShapeRenderer>().color = Color.white;  
        }
        // if(!joint.enabled && calf.touchingFloor){
        //     sfaBool = 1;
        // }
        if(!calf.touchingFloor){
            locked = false;
            joint.enabled = false;
        }
        if(Input.GetKeyDown(inputKey) || sfaBool == 1){
            if(calf.touchingFloor){
                locked = true;

            joint.enabled = locked;
            }
            else {
                locked = false;
                joint.enabled = locked;
            }
            
        }
         else {
                locked = false;
                joint.enabled = locked;
            }
            if(locked){
                 joint.connectedBody = calf.floorT.gameObject.GetComponent<Rigidbody2D>();
            }
            else {
                joint.connectedBody = null;
            }
        // if(locked){
        //     rb.constraints = RigidbodyConstraints2D.FreezePositionY;
        // }
        // else{
        //     rb.constraints = RigidbodyConstraints2D.None;
        // }
        
    }
}
