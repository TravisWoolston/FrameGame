using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HingelessInteractable : MonoBehaviour
{
    private Rigidbody2D rb;
    private Vector3 offset;
    private bool isDragging = false;
    public FreezeReplay fR;
    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
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
            // rb.bodyType = RigidbodyType2D.Dynamic;
        }
    }

    private void OnMouseUp()
    {
        isDragging = false;
        // fR.isDragging = false;
        // rb.simulated = false;
        // rb.bodyType = RigidbodyType2D.Kinematic;
        
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
