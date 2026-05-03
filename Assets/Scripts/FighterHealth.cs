using UnityEngine;

/// <summary>
/// Health system for a FreezReplay fighter.
/// Attach to the ROOT of each stick figure (the parent that contains all limbs).
/// Each limb's ImpactDamage component reports damage here.
/// Renders a world-space health bar above the fighter.
/// </summary>
public class FighterHealth : MonoBehaviour
{
    [Header("=== Feature Toggles ===")]
    [Tooltip("Master toggle for the health system")]
    public bool enableHealthSystem = true;

    [Tooltip("Show the health bar above this fighter")]
    public bool showHealthBar = true;

    [Tooltip("Log damage events to the console")]
    public bool enableDebugLog = false;

    [Tooltip("Enable KO detection (fighter loses when HP reaches 0)")]
    public bool enableKO = true;

    [Header("=== Health Settings ===")]
    [Tooltip("Maximum hit points")]
    public float maxHP = 100f;

    [Tooltip("Current hit points")]
    [SerializeField]
    private float currentHP;

    [Tooltip("Damage reduction multiplier (0.5 = take half damage)")]
    public float damageReduction = 1.0f;

    [Header("=== Health Bar Appearance ===")]
    [Tooltip("Height offset above the fighter for the health bar")]
    public float healthBarYOffset = 3.0f;

    [Tooltip("Width of the health bar in world units")]
    public float healthBarWidth = 2.5f;

    [Tooltip("Height of the health bar in world units")]
    public float healthBarHeight = 0.25f;

    [Tooltip("Color of the health bar when full")]
    public Color fullHealthColor = new Color(0.2f, 0.9f, 0.3f);

    [Tooltip("Color of the health bar when low")]
    public Color lowHealthColor = new Color(0.9f, 0.2f, 0.2f);

    [Tooltip("Color of the health bar background")]
    public Color backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);

    [Header("=== References ===")]
    [Tooltip("The transform to track for health bar positioning (e.g., Head or Spine)")]
    public Transform healthBarAnchor;

    // Public accessor
    public float CurrentHP => currentHP;
    public float HPPercent => Mathf.Clamp01(currentHP / maxHP);
    public bool IsKO => currentHP <= 0f;

    // Events (other scripts can subscribe)
    public System.Action<float, float> OnDamageTaken;  // (damage, remainingHP)
    public System.Action OnKO;

    // Health bar rendering
    private SpriteRenderer barBackground;
    private SpriteRenderer barFill;
    private SpriteRenderer barBorder;
    private Transform barContainer;

    // Smooth health bar animation
    private float displayedHP;
    private float smoothVelocity;

    void Awake()
    {
        currentHP = maxHP;
        displayedHP = maxHP;
    }

    void Start()
    {
        // Auto-assign anchor to spine or first child if not set
        if (healthBarAnchor == null)
        {
            Transform spine = transform.Find("Spine");
            if (spine != null)
                healthBarAnchor = spine;
            else if (transform.childCount > 0)
                healthBarAnchor = transform.GetChild(0);
            else
                healthBarAnchor = transform;
        }

        if (showHealthBar)
            CreateHealthBar();
    }

    void LateUpdate()
    {
        if (!enableHealthSystem) return;

        // Smooth the displayed HP for a satisfying drain animation
        displayedHP = Mathf.SmoothDamp(displayedHP, currentHP, ref smoothVelocity, 0.15f);

        if (showHealthBar && barContainer != null)
            UpdateHealthBar();
    }

    /// <summary>
    /// Apply damage to this fighter. Called by ImpactDamage on individual limbs.
    /// </summary>
    public void TakeDamage(float damage)
    {
        if (!enableHealthSystem) return;
        if (IsKO) return;

        float finalDamage = damage * damageReduction;
        currentHP = Mathf.Max(0f, currentHP - finalDamage);

        OnDamageTaken?.Invoke(finalDamage, currentHP);

        if (enableDebugLog)
            Debug.Log($"[FighterHealth] {gameObject.name} took {finalDamage:F1} damage. HP: {currentHP:F1}/{maxHP}");

        if (IsKO && enableKO)
        {
            HandleKO();
        }
    }

    /// <summary>
    /// Heal this fighter.
    /// </summary>
    public void Heal(float amount)
    {
        if (!enableHealthSystem) return;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
    }

    /// <summary>
    /// Reset HP to full.
    /// </summary>
    public void ResetHealth()
    {
        currentHP = maxHP;
        displayedHP = maxHP;
        snapshotHP = maxHP;
    }

    // ===== Snapshot Integration (for FreezeReplay) =====
    private float snapshotHP;

    /// <summary>
    /// Save current HP as a checkpoint. Called by FreezeReplay on Capture.
    /// </summary>
    public void SaveHP()
    {
        snapshotHP = currentHP;
    }

    /// <summary>
    /// Restore HP to the last saved checkpoint. Called by FreezeReplay on Restore.
    /// This prevents replay collisions from draining health — only committed damage counts.
    /// </summary>
    public void RestoreHP()
    {
        currentHP = snapshotHP;
        displayedHP = snapshotHP;
    }

    void HandleKO()
    {
        Debug.Log($"[FighterHealth] {gameObject.name} has been KNOCKED OUT!");
        OnKO?.Invoke();

        // Make all limbs go fully ragdoll (disable motors)
        HingeJoint2D[] joints = GetComponentsInChildren<HingeJoint2D>();
        foreach (var joint in joints)
        {
            joint.useMotor = false;
        }
    }

    // ===== Health Bar Rendering =====

    void CreateHealthBar()
    {
        // Container
        barContainer = new GameObject("HealthBar").transform;
        barContainer.SetParent(null); // World space, not parented to fighter (avoid physics influence)

        // Background
        GameObject bgObj = CreateBarSprite("HealthBar_BG", backgroundColor, 1);
        barBackground = bgObj.GetComponent<SpriteRenderer>();
        bgObj.transform.SetParent(barContainer);
        bgObj.transform.localScale = new Vector3(healthBarWidth + 0.1f, healthBarHeight + 0.06f, 1f);

        // Fill
        GameObject fillObj = CreateBarSprite("HealthBar_Fill", fullHealthColor, 2);
        barFill = fillObj.GetComponent<SpriteRenderer>();
        fillObj.transform.SetParent(barContainer);
        fillObj.transform.localScale = new Vector3(healthBarWidth, healthBarHeight, 1f);

        barContainer.gameObject.SetActive(showHealthBar);
    }

    GameObject CreateBarSprite(string name, Color color, int sortOrder)
    {
        GameObject obj = new GameObject(name);
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();

        // Create a 1x1 white pixel sprite
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        sr.color = color;
        sr.sortingOrder = sortOrder + 200; // High sorting order to render on top

        return obj;
    }

    void UpdateHealthBar()
    {
        if (healthBarAnchor == null) return;

        // Position above the fighter
        Vector3 barPos = healthBarAnchor.position + Vector3.up * healthBarYOffset;
        barPos.z = -5f; // In front of everything
        barContainer.position = barPos;

        // Update fill width based on displayed HP (smoothed)
        float hpPercent = Mathf.Clamp01(displayedHP / maxHP);
        Vector3 fillScale = barFill.transform.localScale;
        fillScale.x = healthBarWidth * hpPercent;
        barFill.transform.localScale = fillScale;

        // Offset fill so it drains from the right
        float xOffset = -(healthBarWidth - fillScale.x) * 0.5f;
        barFill.transform.localPosition = new Vector3(xOffset, 0f, 0f);

        // Lerp color from green to red
        barFill.color = Color.Lerp(lowHealthColor, fullHealthColor, hpPercent);
    }

    void OnDestroy()
    {
        if (barContainer != null)
            Destroy(barContainer.gameObject);
    }
}
