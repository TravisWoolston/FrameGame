using UnityEngine;
using System.Collections.Generic;

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

    [Tooltip("Color of the preview (temporary) damage segment on the health bar")]
    public Color previewDamageColor = new Color(1f, 0.85f, 0.1f, 0.85f);

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
    private SpriteRenderer barPreviewDamage;
    private Transform barContainer;

    // Smooth health bar animation
    private float displayedHP;
    private float smoothVelocity;

    // Preview damage — temporary damage taken during preview simulation.
    // Visualised as a yellow segment on the health bar; cleared on RestoreHP().
    private float previewDamageHP = 0f;

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
    /// Pass isPreview = true during the preview loop so the damage is tracked
    /// separately and shown as a yellow bar segment without persisting.
    /// </summary>
    public void TakeDamage(float damage, bool isPreview = false)
    {
        if (!enableHealthSystem) return;
        if (IsKO) return;

        float finalDamage = damage * damageReduction;
        currentHP = Mathf.Max(0f, currentHP - finalDamage);

        if (isPreview)
            previewDamageHP = Mathf.Min(previewDamageHP + finalDamage, snapshotHP);

        OnDamageTaken?.Invoke(finalDamage, currentHP);

        if (enableDebugLog)
            Debug.Log($"[FighterHealth] {gameObject.name} took {finalDamage:F1} damage{(isPreview ? " (preview)" : "")}. HP: {currentHP:F1}/{maxHP}");

        if (IsKO && enableKO && !isPreview)
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
        previewDamageHP = 0f;
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
        previewDamageHP = 0f;
    }

    /// <summary>
    /// Set HP to an exact value without triggering damage events or KO.
    /// Used during replay playback to mirror recorded health states.
    /// </summary>
    public void SetDisplayHP(float hp)
    {
        currentHP = Mathf.Clamp(hp, 0f, maxHP);
        displayedHP = currentHP;
        previewDamageHP = 0f;
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

        // Make the corpse non-interactive with all living fighters while
        // keeping ground/arena collision intact so the body rests on the floor.
        IgnoreCollisionsWithAllFighters();
    }

    /// <summary>
    /// Tells the 2D physics engine to ignore collisions between every collider
    /// on this (dead) fighter and every collider on all other fighters in the scene.
    /// Also ignores collisions with frozen copies owned by any FreezeReplayV2.
    /// Ground/arena collisions are unaffected because those are separate colliders.
    /// </summary>
    private void IgnoreCollisionsWithAllFighters()
    {
        Collider2D[] myColliders = GetComponentsInChildren<Collider2D>(true);
        if (myColliders.Length == 0) return;

        // Collect every fighter-related collider in the scene (live fighters + frozen copies)
        List<Collider2D> otherColliders = new List<Collider2D>();

        FighterHealth[] allFighters = FindObjectsByType<FighterHealth>(FindObjectsSortMode.None);
        foreach (FighterHealth other in allFighters)
        {
            if (other == null || other == this) continue;
            otherColliders.AddRange(other.GetComponentsInChildren<Collider2D>(true));
        }

        // Also include frozen copies — they can become dynamic during physics pose edits
        FreezeReplayV2[] allFRV2 = FindObjectsByType<FreezeReplayV2>(FindObjectsSortMode.None);
        foreach (FreezeReplayV2 frv2 in allFRV2)
        {
            if (frv2 == null || frv2.frozenCopy == null) continue;
            otherColliders.AddRange(frv2.frozenCopy.GetComponentsInChildren<Collider2D>(true));
        }

        foreach (Collider2D mine in myColliders)
        {
            if (mine == null) continue;
            foreach (Collider2D theirs in otherColliders)
            {
                if (theirs == null) continue;
                Physics2D.IgnoreCollision(mine, theirs, true);
            }
        }

        Debug.Log($"[FighterHealth] Corpse '{gameObject.name}' now ignores collisions with all fighters and frozen copies.");
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

        // Preview damage segment (yellow), rendered on top of fill
        GameObject previewObj = CreateBarSprite("HealthBar_Preview", previewDamageColor, 3);
        barPreviewDamage = previewObj.GetComponent<SpriteRenderer>();
        previewObj.transform.SetParent(barContainer);
        previewObj.transform.localScale = new Vector3(0f, healthBarHeight, 1f);

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

        // Displayed HP smoothly follows current HP (which is already restored on preview restart)
        float hpPercent = Mathf.Clamp01(displayedHP / maxHP);
        float fillWidth = healthBarWidth * hpPercent;

        // Main fill — represents committed HP
        Vector3 fillScale = barFill.transform.localScale;
        fillScale.x = fillWidth;
        barFill.transform.localScale = fillScale;

        // Offset fill so it drains from the right
        float xOffset = -(healthBarWidth - fillWidth) * 0.5f;
        barFill.transform.localPosition = new Vector3(xOffset, 0f, 0f);

        // Lerp color from green to red
        barFill.color = Color.Lerp(lowHealthColor, fullHealthColor, hpPercent);

        // Preview damage segment — yellow strip sitting immediately to the right of the fill's right edge,
        // representing damage accumulated during the preview loop that will be cleared on RestoreHP.
        if (barPreviewDamage != null)
        {
            float clampedPreview = Mathf.Clamp(previewDamageHP, 0f, displayedHP);
            float previewPercent = clampedPreview / maxHP;
            float previewWidth = healthBarWidth * previewPercent;

            if (previewWidth > 0.001f)
            {
                barPreviewDamage.gameObject.SetActive(true);

                // The preview strip replaces the rightmost portion of the fill.
                // Its right edge aligns with the fill's right edge.
                Vector3 pvScale = barPreviewDamage.transform.localScale;
                pvScale.x = previewWidth;
                barPreviewDamage.transform.localScale = pvScale;

                // Centre of the preview strip: fill right edge minus half preview width
                float fillRightEdge = xOffset + fillWidth * 0.5f;
                float previewCentre = fillRightEdge - previewWidth * 0.5f;
                barPreviewDamage.transform.localPosition = new Vector3(previewCentre, 0f, 0f);
            }
            else
            {
                barPreviewDamage.gameObject.SetActive(false);
            }
        }
    }

    void OnDestroy()
    {
        if (barContainer != null)
            Destroy(barContainer.gameObject);
    }
}
