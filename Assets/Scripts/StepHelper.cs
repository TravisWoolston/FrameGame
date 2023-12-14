using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StepHelper : MonoBehaviour
{
    public GameObject calf;
    public GameObject calf2;
    public GameObject copyCalf;
    public GameObject copyCalf2;
    private Rigidbody2D calfRB;
    private Rigidbody2D calf2RB;
    private HeavyBoots boot;
    private HeavyBoots boot2;
    private HeavyBoots copyBoot;
    private HeavyBoots copyBoot2;
    private HingeJoint2D hinge;
    private HingeJoint2D hinge2;
    public bool locked = true;
    void Start()
    {
        calfRB = calf.GetComponent<Rigidbody2D>();
        calfRB.constraints = RigidbodyConstraints2D.None;
        calf2RB = calf2.GetComponent<Rigidbody2D>();
        calf2RB.constraints = RigidbodyConstraints2D.None;
        copyCalf = GameObject.FindGameObjectWithTag("StickManCopy").transform.GetChild(4).gameObject;
        copyCalf2 = GameObject.FindGameObjectWithTag("StickManCopy").transform.GetChild(5).gameObject;
        boot = calf.GetComponent<HeavyBoots>();
        boot2 = calf2.GetComponent<HeavyBoots>();
        copyBoot = copyCalf.GetComponent<HeavyBoots>();
        copyBoot2 = copyCalf2.GetComponent<HeavyBoots>();
        hinge = boot.floorJoint;
        hinge2 =  boot2.floorJoint;
    }

    // Update is called once per frame
   
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.K)){
            locked = !locked;
        }
        if(boot.touchingFloor && copyBoot.touchingFloor &&locked){
            hinge.enabled = true; 
        }
        else {
            hinge.enabled = false;
        }
        if(boot2.touchingFloor && copyBoot2.touchingFloor &&locked){
            hinge2.enabled = true; 
        }
        else {
            hinge2.enabled = false;
        }
        // if(calfRB.transform.position.x > calf2RB.transform.position.x){
        //     calfRB.constraints = RigidbodyConstraints2D.FreezePositionY;
        //     calf2RB.constraints = RigidbodyConstraints2D.None;

        // } else {
        //     calf2RB.constraints = RigidbodyConstraints2D.FreezePositionY;
        //     calfRB.constraints = RigidbodyConstraints2D.None;
        // }
    }
}
