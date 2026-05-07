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

    [Tooltip("Fallback pickup radius for root bodies like hips/pelvis when they have no direct collider.")]
    public float rootFallbackSelectionRadius = 1.75f;

    [Header("=== Pose Constraints ===")]
    [Tooltip("Limit how far one limb can be adjusted during a single drag.")]
    public bool limitDraggedLimbMotion = true;

    [Tooltip("Maximum world-space movement allowed for the dragged point in one edit.")]
    public float maxLimbAdjustment = 1.4f;

    [Tooltip("Extra reach allowed beyond the current joint-to-click distance.")]
    public float jointReachPadding = 0.25f;

    [Tooltip("Use a root anchor only as a fallback when no feet are planted.")]
    public bool anchorRootWhileDragging = true;

    [Tooltip("Keep the dragged limb's parent stable as a fallback when no feet are planted. Leave off for full-body propagation.")]
    public bool anchorParentWhileDragging = false;

    [Tooltip("Anchor frozen feet that are touching the floor while editing another limb.")]
    public bool anchorPlantedFeetWhileDragging = true;

    public float rootAnchorForce = 2500f;
    public float parentAnchorForce = 1800f;
    public float plantedFootAnchorForce = 2200f;
    public float anchoredFootGroundTolerance = 0.35f;
    public float anchorDampingRatio = 1f;
    public float anchorFrequency = 8f;

    [Header("=== References ===")]
    [Tooltip("Assign the FreezeReplay (V1) component — leave null if using V2")]
    public FreezeReplay freezeReplay;

    [Tooltip("Assign the FreezeReplayV2 component — leave null if using V1")]
    public FreezeReplayV2 freezeReplayV2;

    // Runtime state
    private TargetJoint2D activeTargetJoint;
    private Rigidbody2D draggedBody;
    private HingeJoint2D draggedJoint;
    private Vector2 draggedStartPosition;
    private float dragStartJointReach;
    private Vector2 currentDragTarget;
    private bool directRootDrag;
    private Vector2 directRootMouseOffset;
    private readonly List<TargetJoint2D> poseAnchorJoints = new List<TargetJoint2D>();
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

        if (Input.GetMouseButton(0) && (activeTargetJoint != null || directRootDrag))
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
        Collider2D validHit = PickBestFrozenHit(hits, mouseWorld);
        Rigidbody2D hoverBody = PickPreferredBody(validHit, mouseWorld, out GameObject hoverSource);

        if (hoverBody != null)
            HighlightHoverTarget(hoverSource, hoverBody);
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
        Collider2D validHit = PickBestFrozenHit(hits, mouseWorld);
        Rigidbody2D rb = PickPreferredBody(validHit, mouseWorld, out _);

        if (rb != null)
            StartDraggingBody(frozenObj, rb, mouseWorld);
    }

    void StartDraggingBody(GameObject frozenObj, Rigidbody2D rb, Vector2 mouseWorld)
    {
        if (freezeReplayV2 != null && freezeReplayV2.IsFrozenRootBody(rb.gameObject))
        {
            StartDraggingRootBody(rb, mouseWorld);
            return;
        }

        draggedBody = rb;
        draggedJoint = rb.GetComponent<HingeJoint2D>();
        draggedStartPosition = mouseWorld;
        currentDragTarget = mouseWorld;
        dragStartJointReach = GetCurrentJointReach(rb, mouseWorld);
        ClearHoverHighlight();
        CreatePoseAnchors(frozenObj, rb);

        activeTargetJoint = rb.gameObject.AddComponent<TargetJoint2D>();
        activeTargetJoint.autoConfigureTarget = false;
        activeTargetJoint.target = currentDragTarget;
        activeTargetJoint.maxForce = dragForce;
        activeTargetJoint.dampingRatio = dampingRatio;
        activeTargetJoint.frequency = frequency;
        activeTargetJoint.anchor = rb.transform.InverseTransformPoint(mouseWorld);
    }

    void StartDraggingRootBody(Rigidbody2D rb, Vector2 mouseWorld)
    {
        draggedBody = rb;
        draggedJoint = null;
        directRootDrag = true;
        directRootMouseOffset = rb.position - mouseWorld;
        draggedStartPosition = rb.position;
        currentDragTarget = rb.position;
        dragStartJointReach = 0f;
        ClearHoverHighlight();

        freezeReplayV2.BeginFrozenRootPoseEdit(rb.gameObject);
    }

    /// <summary>
    /// Updates the drag target to follow the mouse.
    /// </summary>
    void UpdateDrag()
    {
        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        if (directRootDrag)
        {
            currentDragTarget = mouseWorld + directRootMouseOffset;
            if (freezeReplayV2 != null && draggedBody != null)
                freezeReplayV2.UpdateFrozenRootPoseEdit(draggedBody.gameObject, currentDragTarget);
            return;
        }

        currentDragTarget = ClampDragTarget(mouseWorld);
        activeTargetJoint.target = currentDragTarget;
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
        if (directRootDrag && freezeReplayV2 != null)
            freezeReplayV2.EndFrozenRootPoseEdit();
        ClearPoseAnchors();
        StabilizeFrozenPose();
        if (freezeReplayV2 != null)
            freezeReplayV2.RequestPosePreviewRestart();
        draggedBody = null;
        draggedJoint = null;
        directRootDrag = false;
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

    Collider2D PickBestFrozenHit(Collider2D[] hits, Vector2 mouseWorld)
    {
        Collider2D bestHit = null;
        float bestScore = float.MaxValue;

        foreach (var hit in hits)
        {
            if (hit == null || !IsPoseableFrozenCopyPart(hit.gameObject)) continue;

            float score = Vector2.Distance(mouseWorld, hit.ClosestPoint(mouseWorld));
            if (IsRootBody(hit.gameObject))
                score -= 100f;

            if (score < bestScore)
            {
                bestScore = score;
                bestHit = hit;
            }
        }

        return bestHit;
    }

    Rigidbody2D GetBodyFromHit(Collider2D hit)
    {
        if (hit == null) return null;
        if (hit.attachedRigidbody != null) return hit.attachedRigidbody;
        return hit.GetComponentInParent<Rigidbody2D>();
    }

    Rigidbody2D PickPreferredBody(Collider2D validHit, Vector2 mouseWorld, out GameObject source)
    {
        Rigidbody2D hitBody = GetBodyFromHit(validHit);
        Rigidbody2D rootBody = PickFallbackRootBody(mouseWorld);

        if (ShouldPreferRootFallback(rootBody, validHit, hitBody, mouseWorld))
        {
            source = rootBody.gameObject;
            return rootBody;
        }

        source = validHit != null
            ? validHit.gameObject
            : hitBody != null ? hitBody.gameObject : null;
        return hitBody != null ? hitBody : rootBody;
    }

    bool ShouldPreferRootFallback(Rigidbody2D rootBody, Collider2D validHit, Rigidbody2D hitBody, Vector2 mouseWorld)
    {
        if (rootBody == null) return false;
        if (hitBody == null) return true;
        if (hitBody == rootBody) return true;

        float rootDistance = GetBodySelectionDistance(rootBody, mouseWorld);
        if (rootDistance <= rootFallbackSelectionRadius * 0.45f)
            return true;

        if (validHit == null) return true;

        float hitDistance = Vector2.Distance(mouseWorld, validHit.ClosestPoint(mouseWorld));
        return rootDistance + 0.2f < hitDistance;
    }

    Rigidbody2D PickFallbackRootBody(Vector2 mouseWorld)
    {
        GameObject frozenObj = GetFrozenCopy();
        if (frozenObj == null) return null;

        Rigidbody2D bestBody = null;
        float bestDistance = Mathf.Max(clickRadius, rootFallbackSelectionRadius);

        foreach (var body in frozenObj.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (body == null || !IsRootBody(body.gameObject)) continue;
            if (!IsPoseableFrozenCopyPart(body.gameObject)) continue;

            float distance = GetBodySelectionDistance(body, mouseWorld);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                bestBody = body;
            }
        }

        return bestBody;
    }

    float GetBodySelectionDistance(Rigidbody2D body, Vector2 point)
    {
        if (body == null) return float.MaxValue;

        float bestDistance = Vector2.Distance(point, body.position);

        foreach (var collider in body.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider == null || !collider.enabled) continue;
            if (GetBodyOwner(collider.gameObject) != body) continue;

            Vector2 closest = collider.ClosestPoint(point);
            bestDistance = Mathf.Min(bestDistance, Vector2.Distance(point, closest));
        }

        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled) continue;
            if (GetBodyOwner(renderer.gameObject) != body) continue;

            bestDistance = Mathf.Min(bestDistance, DistanceToBounds(renderer.bounds, point));
        }

        return bestDistance;
    }

    float DistanceToBounds(Bounds bounds, Vector2 point)
    {
        float dx = Mathf.Max(Mathf.Max(bounds.min.x - point.x, 0f), point.x - bounds.max.x);
        float dy = Mathf.Max(Mathf.Max(bounds.min.y - point.y, 0f), point.y - bounds.max.y);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    Rigidbody2D GetBodyOwner(GameObject go)
    {
        if (go == null) return null;

        Collider2D collider = go.GetComponent<Collider2D>();
        if (collider != null && collider.attachedRigidbody != null)
            return collider.attachedRigidbody;

        Rigidbody2D body = go.GetComponent<Rigidbody2D>();
        if (body != null)
            return body;

        return go.GetComponentInParent<Rigidbody2D>();
    }

    void HighlightHoverTarget(GameObject source, Rigidbody2D body)
    {
        if (body == null) return;

        hoveredIsHead = body.name == "Head";

        if (TryHighlightSpriteRenderer(source, body)) return;
        TryHighlightSpriteShapeRenderer(source, body);
    }

    bool TryHighlightSpriteRenderer(GameObject source, Rigidbody2D body)
    {
        SpriteRenderer renderer = source != null ? source.GetComponent<SpriteRenderer>() : null;
        if (renderer == null)
            renderer = body.GetComponent<SpriteRenderer>();

        if (renderer == null)
        {
            foreach (var candidate in body.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (candidate == null || !candidate.enabled) continue;
                if (GetBodyOwner(candidate.gameObject) != body) continue;

                renderer = candidate;
                break;
            }
        }

        if (renderer == null) return false;

        hoveredRenderer = renderer;
        hoveredOriginalColor = renderer.color;
        hoveredRenderer.color = new Color(0.3f, 1f, 0.5f, 1f);
        return true;
    }

    bool TryHighlightSpriteShapeRenderer(GameObject source, Rigidbody2D body)
    {
        UnityEngine.U2D.SpriteShapeRenderer renderer = source != null
            ? source.GetComponent<UnityEngine.U2D.SpriteShapeRenderer>()
            : null;
        if (renderer == null)
            renderer = body.GetComponent<UnityEngine.U2D.SpriteShapeRenderer>();

        if (renderer == null)
        {
            foreach (var candidate in body.GetComponentsInChildren<UnityEngine.U2D.SpriteShapeRenderer>(true))
            {
                if (candidate == null || !candidate.enabled) continue;
                if (GetBodyOwner(candidate.gameObject) != body) continue;

                renderer = candidate;
                break;
            }
        }

        if (renderer == null) return false;

        hoveredSSRenderer = renderer;
        hoveredOriginalColor = renderer.color;
        hoveredSSRenderer.color = new Color(0.3f, 1f, 0.5f, 1f);
        return true;
    }

    bool IsPoseableFrozenCopyPart(GameObject go)
    {
        if (!IsFrozenCopyPart(go)) return false;
        return freezeReplayV2 == null || freezeReplayV2.CanManuallyPoseFrozenBody(go);
    }

    bool IsRootBody(GameObject go)
    {
        if (go == null) return false;
        if (freezeReplayV2 != null)
            return freezeReplayV2.IsFrozenRootBody(go);

        string n = go.name.ToLowerInvariant();
        return n.Contains("hip") || n.Contains("pelvis") || n.Contains("root") || n.Contains("spine") || n.Contains("torso");
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
        dragLineRenderer.SetPosition(1, currentDragTarget);
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
        targetDot.transform.position = currentDragTarget;
    }

    Vector2 ClampDragTarget(Vector2 desiredTarget)
    {
        if (!limitDraggedLimbMotion || draggedBody == null)
            return desiredTarget;

        Vector2 fromStart = desiredTarget - draggedStartPosition;
        if (fromStart.magnitude > maxLimbAdjustment)
            desiredTarget = draggedStartPosition + fromStart.normalized * maxLimbAdjustment;

        if (draggedJoint != null && draggedJoint.connectedBody != null && dragStartJointReach > 0.001f)
        {
            Vector2 pivot = draggedJoint.connectedBody.transform.TransformPoint(draggedJoint.connectedAnchor);
            Vector2 fromPivot = desiredTarget - pivot;
            float maxReach = dragStartJointReach + jointReachPadding;
            if (fromPivot.magnitude > maxReach)
                desiredTarget = pivot + fromPivot.normalized * maxReach;
        }

        return desiredTarget;
    }

    float GetCurrentJointReach(Rigidbody2D rb, Vector2 target)
    {
        HingeJoint2D joint = rb.GetComponent<HingeJoint2D>();
        if (joint == null || joint.connectedBody == null)
            return 0f;

        Vector2 pivot = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
        return Vector2.Distance(pivot, target);
    }

    void CreatePoseAnchors(GameObject frozenObj, Rigidbody2D rb)
    {
        ClearPoseAnchors();

        Rigidbody2D root = FindRootBody(frozenObj);
        int plantedAnchorCount = anchorPlantedFeetWhileDragging
            ? AddPlantedFootAnchors(frozenObj, rb)
            : 0;

        bool needsFallbackAnchor = plantedAnchorCount == 0;
        if (needsFallbackAnchor && anchorRootWhileDragging && root != null && root != rb)
            AddPoseAnchor(root, root.position, rootAnchorForce);

        if (needsFallbackAnchor && anchorParentWhileDragging)
        {
            HingeJoint2D joint = rb.GetComponent<HingeJoint2D>();
            Rigidbody2D parent = joint != null ? joint.connectedBody : null;
            if (parent != null && parent != rb && parent != root)
                AddPoseAnchor(parent, parent.position, parentAnchorForce);
        }
    }

    int AddPlantedFootAnchors(GameObject frozenObj, Rigidbody2D draggedRb)
    {
        int anchorCount = 0;
        Rigidbody2D[] bodies = frozenObj.GetComponentsInChildren<Rigidbody2D>(true);
        foreach (var body in bodies)
        {
            if (body == null || body == draggedRb) continue;
            if (!IsLowerLeg(body.name)) continue;

            if (TryGetGroundPoint(body.position, out Vector2 groundPoint))
            {
                float groundCenterOffset = GetBodyGroundCenterOffset(body);
                float footDistance = Mathf.Abs(body.position.y - (groundPoint.y + groundCenterOffset));
                if (footDistance <= anchoredFootGroundTolerance)
                {
                    AddPoseAnchor(body, body.position, plantedFootAnchorForce);
                    anchorCount++;
                }
            }
        }

        return anchorCount;
    }

    TargetJoint2D AddPoseAnchor(Rigidbody2D body, Vector2 target, float maxForce)
    {
        TargetJoint2D anchor = body.gameObject.AddComponent<TargetJoint2D>();
        anchor.autoConfigureTarget = false;
        anchor.anchor = Vector2.zero;
        anchor.target = target;
        anchor.maxForce = maxForce;
        anchor.dampingRatio = anchorDampingRatio;
        anchor.frequency = anchorFrequency;
        poseAnchorJoints.Add(anchor);
        return anchor;
    }

    void ClearPoseAnchors()
    {
        for (int i = 0; i < poseAnchorJoints.Count; i++)
        {
            if (poseAnchorJoints[i] != null)
                Destroy(poseAnchorJoints[i]);
        }
        poseAnchorJoints.Clear();
    }

    Rigidbody2D FindRootBody(GameObject frozenObj)
    {
        Rigidbody2D[] bodies = frozenObj.GetComponentsInChildren<Rigidbody2D>(true);
        Rigidbody2D fallback = null;

        foreach (var body in bodies)
        {
            if (body == null) continue;
            if (fallback == null) fallback = body;

            string n = body.name.ToLowerInvariant();
            if (n.Contains("hip") || n.Contains("pelvis") || n.Contains("root"))
                return body;
        }

        foreach (var body in bodies)
        {
            if (body != null && body.GetComponent<HingeJoint2D>() == null)
                return body;
        }

        foreach (var body in bodies)
        {
            if (body == null) continue;
            string n = body.name.ToLowerInvariant();
            if (n.Contains("spine") || n.Contains("torso"))
                return body;
        }

        return fallback;
    }

    bool IsLowerLeg(string limbName)
    {
        string n = limbName.ToLowerInvariant();
        return n.Contains("calf") || n.Contains("foot") || n.Contains("shin");
    }

    bool TryGetGroundPoint(Vector2 point, out Vector2 groundPoint)
    {
        if (freezeReplayV2 != null && freezeReplayV2.TryGetGroundPoint(point, out groundPoint))
            return true;

        RaycastHit2D hit = Physics2D.Raycast(point + Vector2.up * 2f, Vector2.down, 6f);
        if (hit.collider != null && !IsFrozenCopyPart(hit.collider.gameObject))
        {
            groundPoint = hit.point;
            return true;
        }

        groundPoint = point;
        return false;
    }

    float GetFootGroundOffset()
    {
        return freezeReplayV2 != null ? freezeReplayV2.footGroundOffset : 0.15f;
    }

    float GetBodyGroundCenterOffset(Rigidbody2D body)
    {
        if (body == null) return GetFootGroundOffset();

        float offset = GetFootGroundOffset();
        Collider2D[] colliders = body.GetComponents<Collider2D>();
        foreach (var col in colliders)
        {
            if (col == null || !col.enabled) continue;
            offset = Mathf.Max(offset, col.bounds.extents.y);
        }

        return offset;
    }

    void StabilizeFrozenPose()
    {
        if (freezeReplayV2 != null)
        {
            freezeReplayV2.StabilizeFrozenCopyPose();
            return;
        }

        GameObject frozenObj = GetFrozenCopy();
        if (frozenObj == null) return;

        foreach (var rb in frozenObj.GetComponentsInChildren<Rigidbody2D>(true))
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    void OnDestroy()
    {
        // Clean up if destroyed mid-drag
        if (activeTargetJoint != null)
            Destroy(activeTargetJoint);
        ClearPoseAnchors();
    }
}
