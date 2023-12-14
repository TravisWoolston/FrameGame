using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InteractableObject : MonoBehaviour
{
    private Rigidbody2D rb;
    private Vector3 offset;
    private bool isDragging = false;
    public FreezeReplay fR;
    public HingeJoint2D joint;
    public HingeJoint2D spaceJoint;
    public HingeJoint2D headJoint;
    public GameObject Spine;
    public GameObject[] limbsToLock;
    public List<HingeJoint2D> spaceJoints;

    private void Start()
    {
        // if(this.gameObject.tag != "Head") joint = GetComponent<HingeJoint2D>();
        rb = GetComponent<Rigidbody2D>();
        if (Spine == null)
            Spine = GameObject.FindGameObjectWithTag("Spine");
        if (this.gameObject.name == "Spine")
        {
            for (int i = 0; i < limbsToLock.Length; i++)
            {
                spaceJoints.Add(limbsToLock[i].GetComponent<InteractableObject>().spaceJoint);
            }
        }
    }

    private void OnMouseDown()
    {
        if (Input.GetMouseButtonDown(0))
        {
            // Calculate the offset between the mouse position and the object's position
            offset = transform.position - Camera.main.ScreenToWorldPoint(Input.mousePosition);
            isDragging = true;
            // fR.isDragging = true;
            Debug.Log("clicked");
            rb.simulated = true;
            if (this.gameObject.name == "Spine")
            {
                spaceJoint.enabled = false;
                headJoint.enabled = false;
                for (int i = 0; i < spaceJoints.Count; i++)
                {
                    spaceJoints[i].enabled = true;
                }
            }
        }
    }

    void OnCollisionEnter2D(Collision2D collision2D) { }

    private void OnMouseUp()
    {
        isDragging = false;
        if (this.gameObject.name == "Spine")
        {
            spaceJoint.enabled = true;
            for (int i = 0; i < spaceJoints.Count; i++)
            {
                spaceJoints[i].enabled = false;
            }
        }
        // fR.isDragging = false;
        // rb.simulated = false;
    }

    private void Update()
    {
        if (isDragging)
        {
            // Calculate the new position based on the mouse position and offset
            Vector3 newPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition) + offset;
            rb.MovePosition(newPosition);
        }
    }
}
