using UnityEngine;

public class Grabber : MonoBehaviour
{
    public KeyCode grabKey;
    public Rigidbody2D grabberRB;
    public string targetTag = "Bone"; // Change this to the tag of the bone type you want to grab
    public float jointDistance = 1.0f;
    public float jointDampingRatio = 0.5f; // Adjust this to control the damping of the joint
    public bool isGrabbing = false;
    public SpringJoint2D springJoint;

    private void Start()
    {
        grabberRB = GetComponent<Rigidbody2D>();
        // springJoint = gameObject.AddComponent<SpringJoint2D>();
        springJoint.enabled = false;
        springJoint.autoConfigureDistance = false;
        springJoint.distance = jointDistance;
        springJoint.dampingRatio = jointDampingRatio;
    }

    private void Update()
    {
        if (Input.GetKeyDown(grabKey))
        {
            ToggleGrab();
        }
    }

    private void ToggleGrab()
    {
        if (springJoint.enabled)
        {
            // If the joint is enabled, disable it
            isGrabbing = false;
            springJoint.enabled = false;

            // Clear the connected body
            springJoint.connectedBody = null;
        }
        else
        {
            // If the joint is disabled, enable it
            FindNearestBone();
            isGrabbing = true;
            springJoint.enabled = true;
        }
    }

    private void FindNearestBone()
    {
        GameObject[] bones = GameObject.FindGameObjectsWithTag(targetTag);
        float nearestDistance = Mathf.Infinity;
        Rigidbody2D nearestBone = null;

        foreach (GameObject bone in bones)
        {
            Rigidbody2D boneRB = bone.GetComponent<Rigidbody2D>();
            if (boneRB != null)
            {
                float distance = Vector2.Distance(grabberRB.position, boneRB.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestBone = boneRB;
                }
            }
        }

        if (nearestBone != null)
        {
            springJoint.connectedBody = nearestBone;
        }
    }
}
