using UnityEngine;

/// <summary>
/// Physics-based impact damage system for FreezReplay.
/// Attach to each limb of the LIVE stick figure (not the frozen copy).
/// Calculates damage from collision velocity × mass, with multipliers
/// based on which body part is striking and which is being struck.
/// </summary>
public class ImpactDamage : MonoBehaviour
{
    [Header("=== Feature Toggles ===")]
    [Tooltip("Master toggle for impact damage on this limb")]
    public bool enableImpactDamage = true;

    [Tooltip("Show floating damage numbers on hit")]
    public bool showDamageNumbers = true;

    [Tooltip("Show a brief flash on the hit limb")]
    public bool showHitFlash = true;

    [Tooltip("Apply screen shake on high-damage hits")]
    public bool enableScreenShake = true;

    [Header("=== Limb Configuration ===")]
    [Tooltip("How much damage this limb deals when striking")]
    public float attackMultiplier = 1.0f;

    [Tooltip("How much damage this limb takes when struck (lower = better armor)")]
    public float vulnerabilityMultiplier = 1.0f;

    [Tooltip("Minimum relative velocity to register a hit (prevents micro-collision spam)")]
    public float velocityThreshold = 3.0f;

    [Tooltip("Cooldown between damage events on this limb (seconds)")]
    public float damageCooldown = 0.15f;

    [Header("=== Screen Shake ===")]
    [Tooltip("Damage threshold to trigger screen shake")]
    public float shakeThreshold = 15f;

    [Tooltip("Screen shake intensity multiplier")]
    public float shakeIntensity = 0.15f;

    [Tooltip("Screen shake duration in seconds")]
    public float shakeDuration = 0.2f;

    [Header("=== References ===")]
    [Tooltip("The FighterHealth component on this fighter's root")]
    public FighterHealth fighterHealth;

    // Runtime state
    private float lastDamageTime = -999f;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private UnityEngine.U2D.SpriteShapeRenderer ssRenderer;
    private Color originalColor;
    private bool isHead = false;
    private float flashTimer = 0f;
    private bool isFlashing = false;
    private bool suppressDamageEffects = false;
    private bool savedPreviewCooldown = false;
    private float savedPreviewLastDamageTime;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        isHead = (gameObject.name == "Head");

        if (isHead)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
                originalColor = spriteRenderer.color;
        }
        else
        {
            ssRenderer = GetComponent<UnityEngine.U2D.SpriteShapeRenderer>();
            if (ssRenderer != null)
                originalColor = ssRenderer.color;
        }

        // Auto-find FighterHealth on parent if not assigned
        if (fighterHealth == null)
            fighterHealth = GetComponentInParent<FighterHealth>();

        // Auto-configure multipliers based on limb name
        AutoConfigureLimb();
    }

    void Update()
    {
        // Handle hit flash fadeout
        if (isFlashing)
        {
            flashTimer -= Time.deltaTime;
            if (flashTimer <= 0f)
            {
                RestoreColor();
                isFlashing = false;
            }
        }
    }

    /// <summary>
    /// Sets attack and vulnerability multipliers based on the limb's name.
    /// </summary>
    void AutoConfigureLimb()
    {
        string n = gameObject.name.ToLower();

        if (n.Contains("lower arm") || n.Contains("hand") || n.Contains("fist"))
        {
            attackMultiplier = 1.5f;       // Fists hit hard
            vulnerabilityMultiplier = 0.3f; // Arms block well
        }
        else if (n.Contains("calf") || n.Contains("foot") || n.Contains("shin"))
        {
            attackMultiplier = 1.5f;       // Kicks hit hard
            vulnerabilityMultiplier = 0.7f;
        }
        else if (n.Contains("head"))
        {
            attackMultiplier = 2.0f;       // Headbutt is devastating but risky
            vulnerabilityMultiplier = 2.0f; // Head is the knockout zone
        }
        else if (n.Contains("arm") || n.Contains("shoulder"))
        {
            attackMultiplier = 0.8f;
            vulnerabilityMultiplier = 0.3f; // Natural guard
        }
        else if (n.Contains("thigh") || n.Contains("knee"))
        {
            attackMultiplier = 1.2f;       // Knee strike
            vulnerabilityMultiplier = 0.7f;
        }
        else if (n.Contains("spine") || n.Contains("torso") || n.Contains("body"))
        {
            attackMultiplier = 0.5f;       // Body check
            vulnerabilityMultiplier = 1.0f;
        }
        // else: keep defaults (1.0/1.0)
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (!enableImpactDamage) return;
        if (Time.time - lastDamageTime < damageCooldown) return;

        // Only process hits from opponents (different parent = different fighter)
        ImpactDamage otherLimb = collision.gameObject.GetComponent<ImpactDamage>();
        if (otherLimb == null) return;
        if (!otherLimb.enableImpactDamage) return;

        // Don't damage yourself — check if same fighter
        if (otherLimb.fighterHealth == this.fighterHealth) return;
        if (fighterHealth == null && !suppressDamageEffects) return;

        LimbMoveIntent attackerIntent = otherLimb.GetComponent<LimbMoveIntent>();
        LimbMoveIntent defenderIntent = GetComponent<LimbMoveIntent>();

        // Calculate damage from physics
        float relativeSpeed = collision.relativeVelocity.magnitude;
        if (relativeSpeed < velocityThreshold) return;

        float attackerMass = otherLimb.rb != null ? otherLimb.rb.mass : 1f;
        float rawDamage = relativeSpeed * attackerMass;
        float finalDamage = rawDamage * otherLimb.attackMultiplier * this.vulnerabilityMultiplier;
        if (attackerIntent != null)
            finalDamage *= attackerIntent.OutgoingDamageMultiplier;
        if (defenderIntent != null)
            finalDamage *= defenderIntent.IncomingDamageMultiplier;

        lastDamageTime = Time.time;

        ApplyStrikeImpulse(collision, attackerIntent);

        if (suppressDamageEffects)
            return;

        // Apply damage to this fighter's health
        fighterHealth.TakeDamage(finalDamage);

        // Visual feedback
        if (showHitFlash)
        {
            FlashColor(Color.red, 0.15f);
        }

        if (showDamageNumbers)
        {
            SpawnDamageNumber(collision.GetContact(0).point, finalDamage);
        }

        if (enableScreenShake && finalDamage >= shakeThreshold)
        {
            CameraShake(finalDamage);
        }

        if (fighterHealth.enableDebugLog)
        {
            Debug.Log($"[ImpactDamage] {otherLimb.gameObject.name} hit {gameObject.name} " +
                      $"| speed={relativeSpeed:F1} mass={attackerMass:F1} " +
                      $"| raw={rawDamage:F1} final={finalDamage:F1} " +
                      $"| HP remaining={fighterHealth.CurrentHP:F1}");
        }
    }

    void FlashColor(Color flashColor, float duration)
    {
        isFlashing = true;
        flashTimer = duration;

        if (isHead && spriteRenderer != null)
            spriteRenderer.color = flashColor;
        else if (ssRenderer != null)
            ssRenderer.color = flashColor;
    }

    void ApplyStrikeImpulse(Collision2D collision, LimbMoveIntent attackerIntent)
    {
        if (attackerIntent == null || !attackerIntent.IsActiveStrike) return;
        if (rb == null || attackerIntent.OutgoingImpulse <= 0f) return;

        Rigidbody2D attackerRb = collision.gameObject.GetComponent<Rigidbody2D>();
        Vector2 attackerPos = attackerRb != null
            ? attackerRb.worldCenterOfMass
            : (Vector2)collision.gameObject.transform.position;
        Vector2 direction = rb.worldCenterOfMass - attackerPos;

        if (direction.sqrMagnitude < 0.001f && collision.contactCount > 0)
            direction = rb.worldCenterOfMass - collision.GetContact(0).point;

        if (direction.sqrMagnitude < 0.001f)
            direction = collision.relativeVelocity;

        if (direction.sqrMagnitude < 0.001f) return;

        rb.AddForce(direction.normalized * attackerIntent.OutgoingImpulse, ForceMode2D.Impulse);
    }

    void RestoreColor()
    {
        if (isHead && spriteRenderer != null)
            spriteRenderer.color = originalColor;
        else if (ssRenderer != null)
            ssRenderer.color = originalColor;
    }

    public void SetPreviewSimulationMode(bool enabled)
    {
        if (enabled)
        {
            if (!suppressDamageEffects)
            {
                savedPreviewLastDamageTime = lastDamageTime;
                savedPreviewCooldown = true;
            }

            suppressDamageEffects = true;
            return;
        }

        suppressDamageEffects = false;
        if (savedPreviewCooldown)
        {
            lastDamageTime = savedPreviewLastDamageTime;
            savedPreviewCooldown = false;
        }
    }

    /// <summary>
    /// Spawns a floating damage number at the hit point.
    /// Uses a simple GameObject + TextMesh for now.
    /// </summary>
    void SpawnDamageNumber(Vector2 position, float damage)
    {
        GameObject dmgObj = new GameObject("DamageNumber");
        dmgObj.transform.position = new Vector3(position.x, position.y, -1f);

        TextMesh tm = dmgObj.AddComponent<TextMesh>();
        tm.text = Mathf.RoundToInt(damage).ToString();
        tm.fontSize = 32;
        tm.characterSize = 0.15f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;

        // Color based on damage severity
        if (damage >= shakeThreshold)
            tm.color = new Color(1f, 0.2f, 0.2f); // High damage = red
        else if (damage >= shakeThreshold * 0.5f)
            tm.color = new Color(1f, 0.6f, 0.2f); // Medium = orange
        else
            tm.color = new Color(1f, 1f, 0.4f);   // Low = yellow

        // Add a simple float-up-and-fade behavior
        DamageNumberFloat floater = dmgObj.AddComponent<DamageNumberFloat>();
        floater.lifetime = 1.0f;
        floater.floatSpeed = 2.0f;
    }

    void CameraShake(float damage)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        CameraShaker shaker = cam.GetComponent<CameraShaker>();
        if (shaker == null)
            shaker = cam.gameObject.AddComponent<CameraShaker>();

        float intensity = Mathf.Clamp(damage * shakeIntensity * 0.01f, 0.05f, 0.5f);
        shaker.Shake(intensity, shakeDuration);
    }
}

/// <summary>
/// Simple float-up-and-fade for damage numbers.
/// </summary>
public class DamageNumberFloat : MonoBehaviour
{
    public float lifetime = 1.0f;
    public float floatSpeed = 2.0f;

    private float timer;
    private TextMesh tm;

    void Start()
    {
        timer = lifetime;
        tm = GetComponent<TextMesh>();
    }

    void Update()
    {
        timer -= Time.deltaTime;
        transform.position += Vector3.up * floatSpeed * Time.deltaTime;

        if (tm != null)
        {
            Color c = tm.color;
            c.a = Mathf.Clamp01(timer / lifetime);
            tm.color = c;
        }

        if (timer <= 0f)
            Destroy(gameObject);
    }
}

/// <summary>
/// Minimal screen shake component. Auto-added to the main camera when needed.
/// </summary>
public class CameraShaker : MonoBehaviour
{
    private float shakeTimer = 0f;
    private float shakeIntensity = 0f;
    private Vector3 originalPosition;
    private bool isShaking = false;

    public void Shake(float intensity, float duration)
    {
        if (!isShaking)
            originalPosition = transform.localPosition;

        shakeIntensity = Mathf.Max(shakeIntensity, intensity);
        shakeTimer = Mathf.Max(shakeTimer, duration);
        isShaking = true;
    }

    void Update()
    {
        if (!isShaking) return;

        shakeTimer -= Time.deltaTime;
        if (shakeTimer <= 0f)
        {
            transform.localPosition = originalPosition;
            isShaking = false;
            shakeIntensity = 0f;
            return;
        }

        float currentIntensity = shakeIntensity * (shakeTimer / 0.2f);
        Vector3 offset = new Vector3(
            Random.Range(-currentIntensity, currentIntensity),
            Random.Range(-currentIntensity, currentIntensity),
            0f
        );
        transform.localPosition = originalPosition + offset;
    }
}
