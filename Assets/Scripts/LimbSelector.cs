using UnityEngine;

public class LimbSelector : MonoBehaviour
{
    private KeyCode leftKey = KeyCode.LeftArrow;
    private KeyCode rightKey = KeyCode.RightArrow;
    private KeyCode upKey = KeyCode.UpArrow;
    private KeyCode downKey = KeyCode.DownArrow;
    public GameObject selectedLimb;
    public GameObject selectedLimb2;
    public GameObject Spine;
    public GameObject Calf;
    public GameObject Calf2;
    public GameObject Head;
    private InteractableObject spaceLock;
    private RotateHingeJoint calfLock;
    private RotateHingeJoint calf2Lock;
    private JointAngleLimits2D limits;

    void Start() { }

    void Update()
    {
        
        if (Spine == null)
        {
            Head = this.gameObject
                .GetComponent<FreezeReplay>()
                .frozenCopy.transform.GetChild(0)
                .gameObject;
            Spine = this.gameObject
                .GetComponent<FreezeReplay>()
                .frozenCopy.transform.GetChild(1)
                .gameObject;
            Calf = this.gameObject
                .GetComponent<FreezeReplay>()
                .frozenCopy.transform.GetChild(4)
                .gameObject;
            Calf2 = this.gameObject
                .GetComponent<FreezeReplay>()
                .frozenCopy.transform.GetChild(5)
                .gameObject;
            spaceLock = Spine.GetComponent<InteractableObject>();
            calfLock = Calf.GetComponent<RotateHingeJoint>();
            calf2Lock = Calf2.GetComponent<RotateHingeJoint>();
            limits = spaceLock.spaceJoint.limits;
        }
        if (selectedLimb != null)
            transform.position = selectedLimb.transform.position;
        else
        {
            selectedLimb = GameObject.FindGameObjectWithTag("copy");
            selectedLimb2 = GameObject.FindGameObjectWithTag("copy");
        }
        if(Input.GetMouseButtonDown(0))selectedLimb = Calf;
        // Check for arrow key input
        Vector2 direction = Vector2.zero;
        if (selectedLimb.name == "Head")
        {
            spaceLock.headJoint.enabled = true;
        }
        else
        {
            spaceLock.headJoint.enabled = false;
        }
        if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.X))
        {
            spaceLock.spaceJoint.enabled = true;

            limits.min = spaceLock.spaceJoint.jointAngle;
            limits.max = spaceLock.spaceJoint.jointAngle;
            spaceLock.spaceJoint.limits = limits;
            spaceLock.spaceJoint.useLimits = true;

            limits = calfLock.hingeJoint.limits;
            limits.min = calfLock.hingeJoint.jointAngle;
            limits.max = calfLock.hingeJoint.jointAngle;
            calfLock.hingeJoint.limits = limits;
            calfLock.hingeJoint.useLimits = true;

            limits = calf2Lock.hingeJoint.limits;
            limits.min = calf2Lock.hingeJoint.jointAngle;
            limits.max = calf2Lock.hingeJoint.jointAngle;
            calf2Lock.hingeJoint.limits = limits;
            calf2Lock.hingeJoint.useLimits = true;
        }
        else if (Input.GetKeyUp(KeyCode.Z) || Input.GetKeyUp(KeyCode.X))
        {
            spaceLock.spaceJoint.enabled = false;
            spaceLock.spaceJoint.useLimits = false;
            calfLock.hingeJoint.useLimits = false;
            calf2Lock.hingeJoint.useLimits = false;
        }
        if (Input.GetKeyDown(leftKey))
            direction = Vector2.left;
        else if (Input.GetKeyDown(rightKey))
            direction = Vector2.right;
        else if (Input.GetKeyDown(upKey))
            direction = Vector2.up;
        else if (Input.GetKeyDown(downKey))
            direction = Vector2.down;

        // Select nearest limb in the specified direction
        if (direction != Vector2.zero)
            SelectNearestLimb(direction);

        // Set the position to the selected limb's position
    }

    void SelectNearestLimb(Vector2 direction)
    {
        // Cast a ray in the specified direction
        RaycastHit2D[] hitArray = Physics2D.RaycastAll(
            transform.position,
            direction,
            Mathf.Infinity
        );
        if (hitArray.Length == 0)
            return;
        int i = 0;
        foreach (RaycastHit2D shit in hitArray)
        {
            if (
                shit.collider.gameObject.tag != "copy"
                || shit.collider.gameObject == selectedLimb
                || shit.collider.gameObject == selectedLimb2
            )
            {
                i++;
            }
            else
            {
                break;
            }
        }

        if (i == hitArray.Length && Input.GetKeyDown(upKey))
        {
            ResetPreviousSelection();
            selectedLimb = Head;
            // jointEnabler();
            Paint(selectedLimb, Color.red);

            // Check if the selected limb has a LimbGroup
            LimbGroup limbGroup = selectedLimb.GetComponent<LimbGroup>();
            if (limbGroup != null)
            {
                // Select other limbs in the same group
                SelectLimbsInGroup(limbGroup.groupName);
            }
        }
        else if(i == hitArray.Length){
            return;
        }
        else
        {
            RaycastHit2D hit = hitArray[i];
            if (hit.collider != null && hit.collider.CompareTag("copy"))
            {
                ResetPreviousSelection();
                selectedLimb = hit.collider.gameObject;
                // jointEnabler();
                Paint(selectedLimb, Color.red);

                // Check if the selected limb has a LimbGroup
                LimbGroup limbGroup = selectedLimb.GetComponent<LimbGroup>();
                if (limbGroup != null)
                {
                    // Select other limbs in the same group
                    SelectLimbsInGroup(limbGroup.groupName);
                }
            }
        }
    }

    void Paint(GameObject paint, Color color)
    {
        if (paint.GetComponent<UnityEngine.U2D.SpriteShapeRenderer>() == null)
        {
            paint.GetComponent<SpriteRenderer>().color = color;
        }
        else
        {
            paint.GetComponent<UnityEngine.U2D.SpriteShapeRenderer>().color = color;
        }
    }

    // Example: Reset the color of the previously selected limb
    void ResetPreviousSelection()
    {
        if (selectedLimb != null)
        {
            jointDisabler();
            Paint(selectedLimb, Color.white);
            Paint(selectedLimb2, Color.white);
        }
    }

    // Select other limbs in the same group
    void SelectLimbsInGroup(string groupName)
    {
        LimbGroup[] limbGroups = FindObjectsOfType<LimbGroup>();
        foreach (var limbGroup in limbGroups)
        {
            if (limbGroup.groupName == groupName && limbGroup.gameObject != selectedLimb)
            {
                // Optionally, perform additional actions for each limb in the group

                selectedLimb2 = limbGroup.gameObject;
                
                Paint(selectedLimb2, Color.red);
            }
        }
        if (selectedLimb2 == null || selectedLimb.name == "Head")
            selectedLimb2 = selectedLimb;
            jointEnabler();
    }

    void jointEnabler()
    {
        if (selectedLimb.GetComponent<RotateHingeJoint>() != null)
            selectedLimb.GetComponent<RotateHingeJoint>().selected = true;
        if (selectedLimb2.GetComponent<RotateHingeJoint>() != null)
            selectedLimb2.GetComponent<RotateHingeJoint>().selected = true;
    }

    void jointDisabler()
    {
        if (selectedLimb.GetComponent<RotateHingeJoint>() != null)
            selectedLimb.GetComponent<RotateHingeJoint>().selected = false;
        if (selectedLimb2.GetComponent<RotateHingeJoint>() != null)
            selectedLimb2.GetComponent<RotateHingeJoint>().selected = false;
    }
}
