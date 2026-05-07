using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Builds a playable physics fighter from marker-only authoring points.
/// The authoring prefab can contain top-level SpriteRenderer dots named like
/// Hips, Shoulders, Head, Elbow L/R, Hand L/R, Knee L/R, and Foot L/R.
/// </summary>
public class FighterRuntimeRigBuilder : MonoBehaviour
{
    [Header("=== Build ===")]
    public bool buildOnAwake = false;
    public bool rebuildExistingGeneratedRig = true;
    public bool hideAuthorMarkersOnBuild = true;
    public string generatedRootName = "_GeneratedRig";

    [Header("=== Components ===")]
    public bool addCombatComponents = true;
    public bool addHealthComponent = true;
    public bool addLimbMoveIntents = true;
    public bool ignoreSelfCollisions = true;

    [Header("=== Appearance ===")]
    public Color bodyColor = Color.white;
    public Color headColor = Color.white;
    public float limbThickness = 0.14f;
    public float coreThickness = 0.22f;
    public float jointRadius = 0.18f;
    public float headRadius = 0.36f;
    public float handRadius = 0.16f;
    public float footRadius = 0.18f;
    public float footLength = 0.45f;
    public float footThickness = 0.12f;
    public int bodySortingOrder = 5;
    public int jointSortingOrder = 8;

    [Header("=== Physics ===")]
    public float gravityScale = 1f;
    public float linearDamping = 0.5f;
    public float angularDamping = 5f;
    public float defaultMotorTorque = 250f;
    public float coreMass = 4f;
    public float spineMass = 3f;
    public float headMass = 0.8f;
    public float upperLimbMass = 0.8f;
    public float lowerLimbMass = 0.55f;
    public float handFootMass = 0.35f;
    public float bodyFriction = 0.45f;
    public float footFriction = 1.2f;
    public float physicsBounciness = 0f;

    private Transform generatedRoot;
    private readonly Dictionary<string, Transform> markers = new Dictionary<string, Transform>();
    private readonly Dictionary<string, Rigidbody2D> bodies = new Dictionary<string, Rigidbody2D>();
    private readonly List<Collider2D> generatedColliders = new List<Collider2D>();
    private readonly HashSet<Transform> consumedMarkers = new HashSet<Transform>();

    private static Sprite segmentSprite;
    private static Sprite circleSprite;
    private PhysicsMaterial2D bodyPhysicsMaterial;
    private PhysicsMaterial2D footPhysicsMaterial;

    void Awake()
    {
        if (buildOnAwake && GetComponent<FreezeReplayV2>() == null)
            BuildIfNeeded();
    }

    public void CopySettingsFrom(FighterRuntimeRigBuilder source)
    {
        if (source == null || source == this) return;

        buildOnAwake = source.buildOnAwake;
        rebuildExistingGeneratedRig = source.rebuildExistingGeneratedRig;
        hideAuthorMarkersOnBuild = source.hideAuthorMarkersOnBuild;
        generatedRootName = source.generatedRootName;

        addCombatComponents = source.addCombatComponents;
        addHealthComponent = source.addHealthComponent;
        addLimbMoveIntents = source.addLimbMoveIntents;
        ignoreSelfCollisions = source.ignoreSelfCollisions;

        bodyColor = source.bodyColor;
        headColor = source.headColor;
        limbThickness = source.limbThickness;
        coreThickness = source.coreThickness;
        jointRadius = source.jointRadius;
        headRadius = source.headRadius;
        handRadius = source.handRadius;
        footRadius = source.footRadius;
        footLength = source.footLength;
        footThickness = source.footThickness;
        bodySortingOrder = source.bodySortingOrder;
        jointSortingOrder = source.jointSortingOrder;

        gravityScale = source.gravityScale;
        linearDamping = source.linearDamping;
        angularDamping = source.angularDamping;
        defaultMotorTorque = source.defaultMotorTorque;
        coreMass = source.coreMass;
        spineMass = source.spineMass;
        headMass = source.headMass;
        upperLimbMass = source.upperLimbMass;
        lowerLimbMass = source.lowerLimbMass;
        handFootMass = source.handFootMass;
        bodyFriction = source.bodyFriction;
        footFriction = source.footFriction;
        physicsBounciness = source.physicsBounciness;
    }

    [ContextMenu("Build Runtime Rig Now")]
    public void BuildIfNeeded()
    {
        generatedRoot = transform.Find(generatedRootName);
        if (generatedRoot != null && generatedRoot.GetComponentsInChildren<Rigidbody2D>(true).Length > 0)
        {
            if (!rebuildExistingGeneratedRig)
                return;

            DestroyGeneratedRootImmediate();
        }

        BuildRig();
    }

    [ContextMenu("Clear Generated Rig")]
    public void ClearGeneratedRig()
    {
        DestroyGeneratedRootImmediate();
    }

    private void BuildRig()
    {
        EnsureSprites();
        CacheMarkers();

        Transform hips = FindMarker("Hips", "Hip", "Pelvis", "Root");
        Transform shoulders = FindMarker("Shoulders", "Shoulder", "Chest");
        Transform head = FindMarker("Head");

        if (hips == null)
        {
            Debug.LogError($"[FighterRuntimeRigBuilder] '{name}' needs a Hips marker.");
            return;
        }

        generatedRoot = new GameObject(generatedRootName).transform;
        generatedRoot.gameObject.layer = gameObject.layer;
        generatedRoot.SetParent(transform, false);
        generatedRoot.localPosition = Vector3.zero;
        generatedRoot.localRotation = Quaternion.identity;
        generatedRoot.localScale = Vector3.one;

        Rigidbody2D hipsBody = CreateCircleBody("Hips", hips.position, jointRadius, coreMass, bodyColor, jointSortingOrder, GetMarkerLayer(hips));

        Rigidbody2D spineBody = null;
        if (shoulders != null)
        {
            spineBody = CreateSegmentBody("Spine", hips.position, shoulders.position, coreThickness, spineMass, bodyColor, bodySortingOrder, GetMarkerLayer(shoulders));
            ConnectHinge(spineBody, hipsBody, hips.position, -35f, 35f, defaultMotorTorque * 1.4f);
        }

        if (head != null)
        {
            Rigidbody2D parent = spineBody != null ? spineBody : hipsBody;
            Vector2 headJoint = shoulders != null
                ? GetHeadJointPoint(shoulders.position, head.position)
                : Vector2.Lerp(hips.position, head.position, 0.7f);
            Rigidbody2D headBody = CreateCircleBody("Head", head.position, headRadius, headMass, headColor, jointSortingOrder + 2, GetMarkerLayer(head));
            ConnectHinge(headBody, parent, headJoint, -45f, 45f, defaultMotorTorque * 0.6f);
        }

        BuildArm(false, shoulders, spineBody, hipsBody);
        BuildArm(true, shoulders, spineBody, hipsBody);
        BuildLeg(false, hips, hipsBody);
        BuildLeg(true, hips, hipsBody);

        if (addHealthComponent && GetComponent<FighterHealth>() == null)
        {
            FighterHealth health = gameObject.AddComponent<FighterHealth>();
            if (bodies.TryGetValue("Head", out Rigidbody2D headBody))
                health.healthBarAnchor = headBody.transform;
        }

        FighterRuntimeRigInstance instance = GetComponent<FighterRuntimeRigInstance>();
        if (instance == null)
            instance = gameObject.AddComponent<FighterRuntimeRigInstance>();
        instance.ignoreSelfCollisions = ignoreSelfCollisions;
        instance.ApplySelfCollisionIgnores();

        if (hideAuthorMarkersOnBuild)
            HideConsumedMarkers();

        Debug.Log($"[FighterRuntimeRigBuilder] Built '{name}' from markers with {bodies.Count} bodies.");
    }

    private void BuildArm(bool rightSide, Transform shoulders, Rigidbody2D spineBody, Rigidbody2D hipsBody)
    {
        Transform elbow = FindSideMarker("Elbow", rightSide);
        Transform hand = FindSideMarker("Hand", rightSide);
        if (shoulders == null || elbow == null || hand == null) return;

        string upperName = rightSide ? "Arm 2" : "Arm";
        string lowerName = rightSide ? "Lower Arm 2" : "Lower Arm";
        string handName = rightSide ? "Hand 2" : "Hand";
        Rigidbody2D parent = spineBody != null ? spineBody : hipsBody;

        int armLayer = GetMarkerLayer(elbow);
        Rigidbody2D upper = CreateSegmentBody(upperName, shoulders.position, elbow.position, limbThickness, upperLimbMass, bodyColor, bodySortingOrder, armLayer);
        ConnectHinge(upper, parent, shoulders.position, -150f, 150f, defaultMotorTorque * 0.75f);

        Rigidbody2D lower = CreateSegmentBody(lowerName, elbow.position, hand.position, limbThickness * 0.9f, lowerLimbMass, bodyColor, bodySortingOrder, GetMarkerLayer(hand));
        if (rightSide)
            ConnectHinge(lower, upper, elbow.position, -155f, 10f, defaultMotorTorque * 0.6f);
        else
            ConnectHinge(lower, upper, elbow.position, -10f, 155f, defaultMotorTorque * 0.6f);

        Rigidbody2D handBody = CreateCircleBody(handName, hand.position, handRadius, handFootMass, bodyColor, jointSortingOrder, GetMarkerLayer(hand));
        ConnectHinge(handBody, lower, hand.position, -70f, 70f, defaultMotorTorque * 0.35f);
    }

    private void BuildLeg(bool rightSide, Transform hips, Rigidbody2D hipsBody)
    {
        Transform knee = FindSideMarker("Knee", rightSide);
        Transform foot = FindSideMarker("Foot", rightSide);
        if (knee == null || foot == null) return;

        string thighName = rightSide ? "Thigh 2" : "Thigh";
        string calfName = rightSide ? "Calf 2" : "Calf";
        string footName = rightSide ? "Foot 2" : "Foot";

        int legLayer = GetMarkerLayer(knee);
        Rigidbody2D thigh = CreateSegmentBody(thighName, hips.position, knee.position, limbThickness, upperLimbMass, bodyColor, bodySortingOrder, legLayer);
        if (rightSide)
            ConnectHinge(thigh, hipsBody, hips.position, -95f, 110f, defaultMotorTorque);
        else
            ConnectHinge(thigh, hipsBody, hips.position, -110f, 95f, defaultMotorTorque);

        Rigidbody2D calf = CreateSegmentBody(calfName, knee.position, foot.position, limbThickness * 0.9f, lowerLimbMass, bodyColor, bodySortingOrder, GetMarkerLayer(foot));
        if (rightSide)
            ConnectHinge(calf, thigh, knee.position, -150f, 10f, defaultMotorTorque * 0.85f);
        else
            ConnectHinge(calf, thigh, knee.position, -10f, 150f, defaultMotorTorque * 0.85f);

        Rigidbody2D footBody = CreateFootBody(footName, foot.position, rightSide, handFootMass, bodyColor, jointSortingOrder, GetMarkerLayer(foot));
        ConnectHinge(footBody, calf, foot.position, -50f, 50f, defaultMotorTorque * 0.5f);
    }

    private Rigidbody2D CreateSegmentBody(string bodyName, Vector2 start, Vector2 end, float thickness, float mass, Color color, int sortingOrder, int bodyLayer)
    {
        Vector2 delta = end - start;
        float length = Mathf.Max(0.05f, delta.magnitude);
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        GameObject body = new GameObject(bodyName);
        body.layer = bodyLayer;
        body.transform.SetParent(generatedRoot, true);
        body.transform.position = Vector2.Lerp(start, end, 0.5f);
        body.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        body.transform.localScale = Vector3.one;

        Rigidbody2D rb = ConfigureBody(body, mass);

        CapsuleCollider2D collider = body.AddComponent<CapsuleCollider2D>();
        collider.direction = CapsuleDirection2D.Horizontal;
        collider.size = new Vector2(length, Mathf.Max(0.02f, thickness));
        collider.sharedMaterial = GetBodyPhysicsMaterial(false);
        generatedColliders.Add(collider);

        SpriteRenderer renderer = body.AddComponent<SpriteRenderer>();
        renderer.sprite = segmentSprite;
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = collider.size;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;

        bodies[bodyName] = rb;
        return rb;
    }

    private Rigidbody2D CreateCircleBody(string bodyName, Vector2 position, float radius, float mass, Color color, int sortingOrder, int bodyLayer)
    {
        float safeRadius = Mathf.Max(0.03f, radius);
        GameObject body = new GameObject(bodyName);
        body.layer = bodyLayer;
        body.transform.SetParent(generatedRoot, true);
        body.transform.position = position;
        body.transform.rotation = Quaternion.identity;
        body.transform.localScale = Vector3.one * (safeRadius * 2f);

        Rigidbody2D rb = ConfigureBody(body, mass);

        CircleCollider2D collider = body.AddComponent<CircleCollider2D>();
        collider.radius = 0.5f;
        collider.sharedMaterial = GetBodyPhysicsMaterial(false);
        generatedColliders.Add(collider);

        SpriteRenderer renderer = body.AddComponent<SpriteRenderer>();
        renderer.sprite = circleSprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;

        bodies[bodyName] = rb;
        return rb;
    }

    private Rigidbody2D CreateFootBody(string bodyName, Vector2 position, bool rightSide, float mass, Color color, int sortingOrder, int bodyLayer)
    {
        GameObject body = new GameObject(bodyName);
        body.layer = bodyLayer;
        body.transform.SetParent(generatedRoot, true);
        body.transform.position = position;
        body.transform.rotation = Quaternion.identity;
        body.transform.localScale = Vector3.one;

        Rigidbody2D rb = ConfigureBody(body, mass);

        BoxCollider2D collider = body.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(Mathf.Max(0.05f, footLength), Mathf.Max(0.02f, footThickness));
        collider.sharedMaterial = GetBodyPhysicsMaterial(true);
        generatedColliders.Add(collider);

        SpriteRenderer renderer = body.AddComponent<SpriteRenderer>();
        renderer.sprite = segmentSprite;
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = collider.size;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;

        bodies[bodyName] = rb;
        return rb;
    }

    private Rigidbody2D ConfigureBody(GameObject body, float mass)
    {
        Rigidbody2D rb = body.AddComponent<Rigidbody2D>();
        rb.mass = Mathf.Max(0.01f, mass);
        rb.gravityScale = gravityScale;
        rb.linearDamping = linearDamping;
        rb.angularDamping = angularDamping;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (addCombatComponents)
            body.AddComponent<ImpactDamage>();
        if (addLimbMoveIntents)
            body.AddComponent<LimbMoveIntent>();

        return rb;
    }

    private void ConnectHinge(Rigidbody2D child, Rigidbody2D parent, Vector2 worldAnchor, float limitMin, float limitMax, float motorTorque)
    {
        if (child == null || parent == null) return;

        HingeJoint2D hinge = child.gameObject.AddComponent<HingeJoint2D>();
        hinge.connectedBody = parent;
        hinge.autoConfigureConnectedAnchor = false;
        hinge.anchor = child.transform.InverseTransformPoint(worldAnchor);
        hinge.connectedAnchor = parent.transform.InverseTransformPoint(worldAnchor);

        JointAngleLimits2D limits = new JointAngleLimits2D
        {
            min = limitMin,
            max = limitMax
        };
        hinge.limits = limits;
        hinge.useLimits = true;

        JointMotor2D motor = new JointMotor2D
        {
            motorSpeed = 0f,
            maxMotorTorque = Mathf.Max(1f, motorTorque)
        };
        hinge.motor = motor;
        hinge.useMotor = true;
    }

    private Vector2 GetHeadJointPoint(Vector2 shoulders, Vector2 head)
    {
        Vector2 toHead = head - shoulders;
        if (toHead.sqrMagnitude < 0.0001f)
            return head;

        float neckDistance = Mathf.Min(toHead.magnitude * 0.35f, headRadius * 0.75f);
        return head - toHead.normalized * neckDistance;
    }

    private int GetMarkerLayer(Transform marker)
    {
        return marker != null ? marker.gameObject.layer : gameObject.layer;
    }

    private PhysicsMaterial2D GetBodyPhysicsMaterial(bool isFoot)
    {
        if (isFoot)
        {
            if (footPhysicsMaterial == null)
            {
                footPhysicsMaterial = new PhysicsMaterial2D("Generated Fighter Foot");
                footPhysicsMaterial.friction = Mathf.Max(0f, footFriction);
                footPhysicsMaterial.bounciness = Mathf.Max(0f, physicsBounciness);
            }

            return footPhysicsMaterial;
        }

        if (bodyPhysicsMaterial == null)
        {
            bodyPhysicsMaterial = new PhysicsMaterial2D("Generated Fighter Body");
            bodyPhysicsMaterial.friction = Mathf.Max(0f, bodyFriction);
            bodyPhysicsMaterial.bounciness = Mathf.Max(0f, physicsBounciness);
        }

        return bodyPhysicsMaterial;
    }

    private void CacheMarkers()
    {
        markers.Clear();
        consumedMarkers.Clear();

        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child == null || child == transform) continue;
            if (generatedRoot != null && child.IsChildOf(generatedRoot)) continue;

            string key = NormalizeName(child.name);
            if (!markers.ContainsKey(key))
                markers[key] = child;
        }
    }

    private Transform FindMarker(params string[] aliases)
    {
        foreach (string alias in aliases)
        {
            Transform marker = FindMarkerByNormalizedName(alias);
            if (marker != null)
            {
                consumedMarkers.Add(marker);
                return marker;
            }
        }

        return null;
    }

    private Transform FindSideMarker(string jointName, bool rightSide)
    {
        string sideWord = rightSide ? "Right" : "Left";
        string sideLetter = rightSide ? "R" : "L";
        string sideNumber = rightSide ? "2" : "1";

        string[] aliases =
        {
            $"{jointName} {sideLetter}",
            $"{jointName}{sideLetter}",
            $"{sideLetter} {jointName}",
            $"{sideWord} {jointName}",
            $"{jointName} {sideWord}",
            $"{jointName} {sideNumber}",
            $"{jointName}{sideNumber}",
        };

        Transform marker = FindMarker(aliases);
        if (marker != null)
            return marker;

        return null;
    }

    private Transform FindMarkerByNormalizedName(string markerName)
    {
        markers.TryGetValue(NormalizeName(markerName), out Transform marker);
        return marker;
    }

    private string NormalizeName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return string.Empty;

        string n = rawName.ToLowerInvariant();
        n = n.Replace("_", string.Empty);
        n = n.Replace("-", string.Empty);
        n = n.Replace(" ", string.Empty);
        return n;
    }

    private void HideConsumedMarkers()
    {
        foreach (Transform marker in consumedMarkers)
        {
            if (marker == null) continue;
            marker.gameObject.SetActive(false);
        }
    }

    private void DestroyGeneratedRootImmediate()
    {
        Transform existing = transform.Find(generatedRootName);
        if (existing == null) return;

        if (Application.isPlaying)
            DestroyImmediate(existing.gameObject);
        else
            DestroyImmediate(existing.gameObject);
    }

    private void EnsureSprites()
    {
        if (segmentSprite == null)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            segmentSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        if (circleSprite == null)
        {
            const int size = 32;
            Texture2D texture = new Texture2D(size, size);
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.46f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = Mathf.Clamp01(radius + 0.75f - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            circleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}

/// <summary>
/// Lightweight runtime marker for generated fighters. It reapplies self-collision
/// ignores on cloned copies, including FreezeReplayV2's frozen copy.
/// </summary>
public class FighterRuntimeRigInstance : MonoBehaviour
{
    public bool ignoreSelfCollisions = true;

    void Awake()
    {
        ApplySelfCollisionIgnores();
    }

    public void ApplySelfCollisionIgnores()
    {
        if (!ignoreSelfCollisions) return;

        Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null) continue;
            for (int j = i + 1; j < colliders.Length; j++)
            {
                if (colliders[j] == null) continue;
                Physics2D.IgnoreCollision(colliders[i], colliders[j]);
            }
        }
    }
}
