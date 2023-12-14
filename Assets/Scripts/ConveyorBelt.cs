using UnityEngine;

public class ConveyorBelt : MonoBehaviour
{
    private float speed = 0f; // Adjust the speed as needed
    private Rigidbody2D rb;
    public bool stick = true;
    
    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        // Set the initial velocity to move in the right direction
        rb.velocity = new Vector2(speed, 0f);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // Check if the object is on the conveyor belt layer or has a specific tag
        // if (other.gameObject.layer == LayerMask.NameToLayer("ConveyorBelt"))
        // {
            // Move the object with the conveyor belt using AddForce
            Rigidbody2D otherRb = other.GetComponent<Rigidbody2D>();
            if (otherRb != null && otherRb.gameObject.tag != "copy")
            {
                otherRb.position += Vector2.right * speed * Time.deltaTime;
                // otherRb.GetComponent<Rigidbody2D>().position = new Vector2(0, otherRb.position.y);
            }
        // }
    }
    
//        void FixedUpdate()
//     {
//         StickToGroundCheck();
//     }
//      private void StickToGroundCheck()
//     {
//         // Raycast to check if the object is close to the ground
//         RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, 0.1f);

//         // If the ray hits something, stick to the ground
//         if (hit.collider != null)
//         {
//             // Adjust the Rigidbody2D position to stick to the ground
//             // rb.position = new Vector2(rb.position.x, hit.point.y);
//             Debug.Log("stick check " + hit.collider.name);
//             // Optional: You can set the velocity's y component to zero to prevent bouncing
//             if(stick){
// // hit.collider.GetComponent<Rigidbody2D>().velocity = new Vector2(rb.velocity.x, 0f);
// // hit.collider.GetComponent<Rigidbody2D>().position = new Vector2(rb.position.x, hit.point.y);
//             }
            
//         }
//     }
}

