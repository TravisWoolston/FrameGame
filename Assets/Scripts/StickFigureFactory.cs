using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generates a complete, physics-tuned stick figure fighter at runtime.
/// Call StickFigureFactory.CreateFighter(position) to spawn one.
/// 
/// The generated figure has:
/// - Proper mass distribution (heavy core, light extremities)
/// - Realistic joint limits (elbows/knees don't hyperextend)
/// - Tuned motors for snappy, responsive posing
/// - All combat components (ImpactDamage, FighterHealth)
/// - Clean capsule/circle visuals
/// - RotateHingeJoint on all limbs for compatibility with existing systems
/// </summary>
public class StickFigureFactory : MonoBehaviour
{
    [Header("=== Feature Toggles ===")]
    public bool addCombatComponents = true;
    public bool addRotateHingeJoints = true;
    public bool addGroundChecks = true;

    [Header("=== Appearance ===")]
    public Color bodyColor = Color.white;
    public Color headColor = Color.white;

    [Header("=== Scale ===")]
    [Tooltip("Overall size multiplier for the fighter. 2.0 matches the hand-made figure.")]
    public float sizeScale = 2.0f;

    [Header("=== Spawn ===")]
    [Tooltip("Press this key to spawn a test fighter at origin")]
    public KeyCode spawnKey = KeyCode.F1;

    // Body part definition
    struct BodyPartDef
    {
        public string name;
        public string tag;
        public Vector2 localPos;      // Position relative to root
        public Vector2 size;          // Width x Height for capsule
        public float mass;
        public bool isCircle;         // True for head
        public string connectTo;      // Name of body to connect joint to
        public Vector2 anchor;        // Joint anchor on this body (local)
        public Vector2 connectedAnchor; // Joint anchor on connected body (local)
        public float limitMin;
        public float limitMax;
        public float motorTorque;
    }

    void Update()
    {
        if (Input.GetKeyDown(spawnKey))
        {
            CreateFighter(transform.position);
        }
    }

    /// <summary>
    /// Creates a complete stick figure fighter at the given world position.
    /// Returns the root GameObject.
    /// </summary>
    public GameObject CreateFighter(Vector3 position)
    {
        float s = sizeScale;

        // Define all body parts
        BodyPartDef[] parts = new BodyPartDef[]
        {
            // HIPS — the anchor/root body
            new BodyPartDef {
                name = "Hips", tag = "Untagged",
                localPos = new Vector2(0, 0),
                size = new Vector2(0.5f * s, 0.18f * s),
                mass = 4.0f, isCircle = false,
                connectTo = null, // Root — no parent joint
                limitMin = 0, limitMax = 0, motorTorque = 0
            },
            // SPINE — connects to hips
            new BodyPartDef {
                name = "Spine", tag = "Spine",
                localPos = new Vector2(0, 0.75f * s),
                size = new Vector2(0.15f * s, 1.0f * s),
                mass = 3.0f, isCircle = false,
                connectTo = "Hips",
                anchor = new Vector2(0, -0.5f * s),
                connectedAnchor = new Vector2(0, 0.09f * s),
                limitMin = -25f, limitMax = 25f, motorTorque = 300f
            },
            // HEAD — connects to spine
            new BodyPartDef {
                name = "Head", tag = "Head",
                localPos = new Vector2(0, 1.6f * s),
                size = new Vector2(0.35f * s, 0.35f * s),
                mass = 0.8f, isCircle = true,
                connectTo = "Spine",
                anchor = new Vector2(0, -0.2f * s),
                connectedAnchor = new Vector2(0, 0.5f * s),
                limitMin = -40f, limitMax = 40f, motorTorque = 100f
            },
            // LEFT ARM — connects to spine (shoulder)
            new BodyPartDef {
                name = "Arm", tag = "Untagged",
                localPos = new Vector2(-0.5f * s, 0.95f * s),
                size = new Vector2(0.6f * s, 0.1f * s),
                mass = 0.6f, isCircle = false,
                connectTo = "Spine",
                anchor = new Vector2(0.3f * s, 0),
                connectedAnchor = new Vector2(-0.08f * s, 0.42f * s),
                limitMin = -135f, limitMax = 135f, motorTorque = 150f
            },
            // RIGHT ARM — connects to spine (shoulder)
            new BodyPartDef {
                name = "Arm 2", tag = "Untagged",
                localPos = new Vector2(0.5f * s, 0.95f * s),
                size = new Vector2(0.6f * s, 0.1f * s),
                mass = 0.6f, isCircle = false,
                connectTo = "Spine",
                anchor = new Vector2(-0.3f * s, 0),
                connectedAnchor = new Vector2(0.08f * s, 0.42f * s),
                limitMin = -135f, limitMax = 135f, motorTorque = 150f
            },
            // LEFT LOWER ARM — connects to arm (elbow)
            new BodyPartDef {
                name = "Lower Arm", tag = "Untagged",
                localPos = new Vector2(-1.0f * s, 0.55f * s),
                size = new Vector2(0.55f * s, 0.08f * s),
                mass = 0.4f, isCircle = false,
                connectTo = "Arm",
                anchor = new Vector2(0.275f * s, 0),
                connectedAnchor = new Vector2(-0.3f * s, 0),
                limitMin = -5f, limitMax = 145f, motorTorque = 120f
            },
            // RIGHT LOWER ARM — connects to arm 2 (elbow)
            new BodyPartDef {
                name = "Lower Arm 2", tag = "Untagged",
                localPos = new Vector2(1.0f * s, 0.55f * s),
                size = new Vector2(0.55f * s, 0.08f * s),
                mass = 0.4f, isCircle = false,
                connectTo = "Arm 2",
                anchor = new Vector2(-0.275f * s, 0),
                connectedAnchor = new Vector2(0.3f * s, 0),
                limitMin = -145f, limitMax = 5f, motorTorque = 120f
            },
            // LEFT THIGH — connects to hips
            new BodyPartDef {
                name = "Thigh", tag = "Untagged",
                localPos = new Vector2(-0.12f * s, -0.55f * s),
                size = new Vector2(0.12f * s, 0.7f * s),
                mass = 1.5f, isCircle = false,
                connectTo = "Hips",
                anchor = new Vector2(0, 0.35f * s),
                connectedAnchor = new Vector2(-0.12f * s, -0.09f * s),
                limitMin = -100f, limitMax = 80f, motorTorque = 250f
            },
            // RIGHT THIGH — connects to hips
            new BodyPartDef {
                name = "Thigh 2", tag = "Untagged",
                localPos = new Vector2(0.12f * s, -0.55f * s),
                size = new Vector2(0.12f * s, 0.7f * s),
                mass = 1.5f, isCircle = false,
                connectTo = "Hips",
                anchor = new Vector2(0, 0.35f * s),
                connectedAnchor = new Vector2(0.12f * s, -0.09f * s),
                limitMin = -80f, limitMax = 100f, motorTorque = 250f
            },
            // LEFT CALF — connects to thigh (knee)
            new BodyPartDef {
                name = "Calf", tag = "Untagged",
                localPos = new Vector2(-0.12f * s, -1.2f * s),
                size = new Vector2(0.1f * s, 0.65f * s),
                mass = 1.0f, isCircle = false,
                connectTo = "Thigh",
                anchor = new Vector2(0, 0.325f * s),
                connectedAnchor = new Vector2(0, -0.35f * s),
                limitMin = -5f, limitMax = 140f, motorTorque = 200f
            },
            // RIGHT CALF — connects to thigh 2 (knee)
            new BodyPartDef {
                name = "Calf 2", tag = "Untagged",
                localPos = new Vector2(0.12f * s, -1.2f * s),
                size = new Vector2(0.1f * s, 0.65f * s),
                mass = 1.0f, isCircle = false,
                connectTo = "Thigh 2",
                anchor = new Vector2(0, 0.325f * s),
                connectedAnchor = new Vector2(0, -0.35f * s),
                limitMin = -140f, limitMax = 5f, motorTorque = 200f
            },
        };

        // Create root
        GameObject root = new GameObject("Fighter");
        root.transform.position = position;

        // Create a shared white pixel texture for all sprites
        Texture2D whiteTex = new Texture2D(1, 1);
        whiteTex.SetPixel(0, 0, Color.white);
        whiteTex.Apply();
        Sprite rectSprite = Sprite.Create(whiteTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        // Store created parts by name for joint lookups
        Dictionary<string, Rigidbody2D> bodyMap = new Dictionary<string, Rigidbody2D>();

        foreach (var part in parts)
        {
            GameObject go = new GameObject(part.name);
            go.transform.SetParent(root.transform);
            go.transform.localPosition = part.localPos;
            go.tag = part.tag;

            // Rigidbody2D
            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.mass = part.mass;
            rb.angularDamping = 5.0f;
            rb.linearDamping = 0.5f;
            rb.gravityScale = 1.0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            bodyMap[part.name] = rb;

            // Collider + Visual
            // localScale controls the visual size (stretches the 1x1 sprite).
            // Collider.size = Vector2.one so it fills the scaled transform exactly.
            // This avoids the double-shrink bug where size × scale made everything tiny.
            if (part.isCircle)
            {
                // Head — circle
                go.transform.localScale = new Vector3(part.size.x * 2, part.size.x * 2, 1);
                CircleCollider2D col = go.AddComponent<CircleCollider2D>();
                col.radius = 0.5f; // 0.5 in local space × scale = correct world radius

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = rectSprite;
                sr.color = headColor;
                sr.sortingOrder = 10;
            }
            else
            {
                // Limbs — set scale first, then collider fills it
                go.transform.localScale = new Vector3(part.size.x, part.size.y, 1);
                CapsuleCollider2D col = go.AddComponent<CapsuleCollider2D>();
                col.direction = (part.size.x > part.size.y)
                    ? CapsuleDirection2D.Horizontal
                    : CapsuleDirection2D.Vertical;
                col.size = Vector2.one; // Fills the scaled transform exactly

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = rectSprite;
                sr.color = bodyColor;
                sr.sortingOrder = 5;
            }

            // HingeJoint2D — connect to parent body
            if (part.connectTo != null && bodyMap.ContainsKey(part.connectTo))
            {
                HingeJoint2D hinge = go.AddComponent<HingeJoint2D>();
                hinge.connectedBody = bodyMap[part.connectTo];
                hinge.autoConfigureConnectedAnchor = false;
                hinge.anchor = part.anchor;
                hinge.connectedAnchor = part.connectedAnchor;

                // Joint limits
                hinge.useLimits = true;
                JointAngleLimits2D limits = new JointAngleLimits2D();
                limits.min = part.limitMin;
                limits.max = part.limitMax;
                hinge.limits = limits;

                // Motor
                hinge.useMotor = true;
                JointMotor2D motor = new JointMotor2D();
                motor.maxMotorTorque = part.motorTorque;
                motor.motorSpeed = 0f;
                hinge.motor = motor;

                // Add RotateHingeJoint for compatibility with posing/AI systems
                if (addRotateHingeJoints)
                {
                    RotateHingeJoint rhj = go.AddComponent<RotateHingeJoint>();
                    rhj.hingeJoint = hinge;
                    rhj.rotationSpeed = 25f;
                }
            }

            // Combat components
            if (addCombatComponents)
            {
                go.AddComponent<ImpactDamage>();
            }

            // Note: GroundCheck requires SpriteShapeRenderer which factory parts don't have.
            // Skipping it for factory-generated fighters.
        }

        // Add FighterHealth to root
        if (addCombatComponents)
        {
            FighterHealth health = root.AddComponent<FighterHealth>();
            // Anchor the health bar to the head
            if (bodyMap.ContainsKey("Head"))
                health.healthBarAnchor = bodyMap["Head"].transform;
        }

        Debug.Log($"[StickFigureFactory] Created fighter '{root.name}' with {parts.Length} body parts at {position}");
        return root;
    }
}
