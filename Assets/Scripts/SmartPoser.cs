using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Smart Posing system for FreezReplay.
/// Click and drag any body part of the frozen copy to pose it.
/// Uses TargetJoint2D so Unity's physics constraint solver handles the IK automatically
/// through the existing HingeJoint2D chain — no custom IK math needed.
/// </summary>
public class SmartPoser : MonoBehaviour
{
    [Header("=== Feature Toggles ===")]
    [Tooltip("Master toggle for the smart posing system")]
    public bool enableSmartPosing = true;

    [Tooltip("Show a line from the dragged body part to the mouse cursor")]
    public bool showDragLine = true;

    [Tooltip("Highlight the body part under the mouse cursor")]
    public bool showHoverHighlight = true;

    [Tooltip("Show a ghost dot at the mouse target position")]
    public bool showTargetIndicator = true;

    [Header("=== Drag Settings ===")]
    [Tooltip("How strongly the dragged part is pulled toward the cursor")]
    public float dragForce = 500f;

    [Tooltip("Damping on the drag spring (1 = critically damped, < 1 = bouncy)")]
    public float dampingRatio = 1f;

    [Tooltip("Frequency of the drag spring oscillation")]
    public float frequency = 5f;

    [Tooltip("Maximum distance from a body part to start dragging")]
    public float clickRadius = 1.5f;

    [Header("=== References ===")]
    [Tooltip("Assign the FreezeReplay (V1) component — leave null if using V2")]
    public FreezeReplay freezeReplay;

    [Tooltip("Assign the FreezeReplayV2 component — leave null if using V1")]
    public FreezeReplayV2 freezeReplayV2;

    // Runtime state
    private TargetJoint2D activeTargetJoint;
    private Rigidbody2D draggedBody;
    private SpriteRenderer hoveredRenderer;
    private UnityEngine.U2D.SpriteShapeRenderer hoveredSSRenderer;
    private Color hoveredOriginalColor;
    private bool hoveredIsHead = false;

    // Visual feedback objects
    private LineRenderer dragLineRenderer;
    private GameObject targetDot;

    void Start()
    {
        if (freezeReplay == null)
            freezeReplay = GetComponent<FreezeReplay>();
        if (freezeReplayV2 == null)
            freezeReplayV2 = GetComponent<FreezeReplayV2>();

        // Create the drag line renderer
        GameObject lineObj = new GameObject("SmartPoser_DragLine");
        lineObj.transform.SetParent(transform);
        dragLineRenderer = lineObj.AddComponent<LineRenderer>();
        dragLineRenderer.startWidth = 0.05f;
        dragLineRenderer.endWidth = 0.05f;
        dragLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        dragLineRenderer.startColor = new Color(0f, 1f, 0.5f, 0.7f);
        dragLineRenderer.endColor = new Color(0f, 1f, 0.5f, 0.2f);
        dragLineRenderer.positionCount = 2;
        dragLineRenderer.enabled = false;
        dragLineRenderer.sortingOrder = 100;

        // Create the target dot indicator
        targetDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        targetDot.name = "SmartPoser_TargetDot";
        targetDot.transform.SetParent(transform);
        targetDot.transform.localScale = Vector3.one * 0.3f;
        // Remove the 3D collider so it doesn't interfere with 2D physics
        Destroy(targetDot.GetComponent<Collider>());
        Renderer dotRenderer = targetDot.GetComponent<Renderer>();
        dotRenderer.material = new Material(Shader.Find("Sprites/Default"));
        dotRenderer.material.color = new Color(0f, 1f, 0.5f, 0.5f);
        dotRenderer.sortingOrder = 100;
        targetDot.SetActive(false);
    }

    void Update()
    {
        if (!enableSmartPosing) return;
        if (freezeReplay != null && freezeReplay.replaying) return;
        if (freezeReplayV2 != null && freezeReplayV2.currentPhase != FreezeReplayV2.TurnPhase.Posing) return;

        HandleHoverHighlight();

        if (Input.GetMouseButtonDown(0))
        {
            TryStartDrag();
        }

        if (Input.GetMouseButton(0) && activeTargetJoint != null)
        {
            UpdateDrag();
        }

        if (Input.GetMouseButtonUp(0))
        {
            EndDrag();
        }

        // Update visual feedback
        UpdateDragLine();
        UpdateTargetDot();
    }

    /// <summary>
    /// Highlights the body part currently under the mouse cursor.
    /// </summary>
    void HandleHoverHighlight()
    {
        if (!showHoverHighlight || draggedBody != null) return;

        // Clear previous hover
        ClearHoverHighlight();

        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Collider2D[] hits = Physics2D.OverlapCircleAll(mouseWorld, clickRadius * 0.5f);
        
        Collider2D validHit = null;
        foreach (var hit in hits)
        {
            if (IsFrozenCopyPart(hit.gameObject))
            {
                validHit = hit;
                break;
            }
        }

        if (validHit != null)
        {
            hoveredIsHead = (validHit.gameObject.name == "Head");

            if (hoveredIsHead)
            {
                hoveredRenderer = validHit.GetComponent<SpriteRenderer>();
                if (hoveredRenderer != null)
                {
                    hoveredOriginalColor = hoveredRenderer.color;
                    hoveredRenderer.color = new Color(0.3f, 1f, 0.5f, 1f);
                }
            }
            else
            {
                hoveredSSRenderer = validHit.GetComponent<UnityEngine.U2D.SpriteShapeRenderer>();
                if (hoveredSSRenderer != null)
                {
                    hoveredOriginalColor = hoveredSSRenderer.color;
                    hoveredSSRenderer.color = new Color(0.3f, 1f, 0.5f, 1f);
                }
            }
        }
    }

    void ClearHoverHighlight()
    {
        if (hoveredRenderer != null)
        {
            hoveredRenderer.color = hoveredOriginalColor;
            hoveredRenderer = null;
        }
        if (hoveredSSRenderer != null)
        {
            hoveredSSRenderer.color = hoveredOriginalColor;
            hoveredSSRenderer = null;
        }
    }

    /// <summary>
    /// Attempts to start dragging a frozen copy body part under the mouse.
    /// Creates a TargetJoint2D which uses Unity's constraint solver as a natural IK system.
    /// </summary>
    void TryStartDrag()
    {
        GameObject frozenObj = GetFrozenCopy();
        if (frozenObj == null) return;

        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Collider2D[] hits = Physics2D.OverlapCircleAll(mouseWorld, clickRadius);
        
        Collider2D validHit = null;
        foreach (var hit in hits)
        {
            if (IsFrozenCopyPart(hit.gameObject))
            {
                validHit = hit;
                break;
            }
        }

        if (validHit != null)
        {
            Rigidbody2D rb = validHit.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                draggedBody = rb;
                ClearHoverHighlight();

                // Create a TargetJoint2D — this is the magic.
                // It pulls the body part toward the mouse position while
                // all the HingeJoint2D constraints stay active, creating
                // natural IK-like behavior through the joint chain.
                activeTargetJoint = rb.gameObject.AddComponent<TargetJoint2D>();
                activeTargetJoint.autoConfigureTarget = false;
                activeTargetJoint.target = mouseWorld;
                activeTargetJoint.maxForce = dragForce;
                activeTargetJoint.dampingRatio = dampingRatio;
                activeTargetJoint.frequency = frequency;

                // Anchor at the click point on the body part
                activeTargetJoint.anchor = rb.transform.InverseTransformPoint(mouseWorld);
            }
        }
    }

    /// <summary>
    /// Updates the drag target to follow the mouse.
    /// </summary>
    void UpdateDrag()
    {
        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        activeTargetJoint.target = mouseWorld;
    }

    /// <summary>
    /// Ends the drag and removes the temporary TargetJoint2D.
    /// </summary>
    void EndDrag()
    {
        if (activeTargetJoint != null)
        {
            Destroy(activeTargetJoint);
            activeTargetJoint = null;
        }
        draggedBody = null;
    }

    /// <summary>
    /// Checks if a GameObject is a child of the frozen copy.
    /// </summary>
    bool IsFrozenCopyPart(GameObject go)
    {
        GameObject frozenObj = GetFrozenCopy();
        if (frozenObj == null) return false;
        return go.transform.IsChildOf(frozenObj.transform);
    }

    /// <summary>
    /// Returns the active frozen copy from whichever system is running.
    /// </summary>
    GameObject GetFrozenCopy()
    {
        if (freezeReplayV2 != null && freezeReplayV2.frozenCopy != null)
            return freezeReplayV2.frozenCopy;
        if (freezeReplay != null && freezeReplay.frozenCopy != null)
            return freezeReplay.frozenCopy;
        return null;
    }

    /// <summary>
    /// Draws a line from the dragged body part to the mouse cursor.
    /// </summary>
    void UpdateDragLine()
    {
        if (!showDragLine || draggedBody == null || dragLineRenderer == null)
        {
            if (dragLineRenderer != null) dragLineRenderer.enabled = false;
            return;
        }

        dragLineRenderer.enabled = true;
        dragLineRenderer.SetPosition(0, draggedBody.transform.position);
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0;
        dragLineRenderer.SetPosition(1, mouseWorld);
    }

    /// <summary>
    /// Shows a dot at the mouse target position while dragging.
    /// </summary>
    void UpdateTargetDot()
    {
        if (!showTargetIndicator || draggedBody == null || targetDot == null)
        {
            if (targetDot != null) targetDot.SetActive(false);
            return;
        }

        targetDot.SetActive(true);
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0;
        targetDot.transform.position = mouseWorld;
    }

    void OnDestroy()
    {
        // Clean up if destroyed mid-drag
        if (activeTargetJoint != null)
            Destroy(activeTargetJoint);
    }
}
