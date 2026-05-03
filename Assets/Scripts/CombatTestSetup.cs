using UnityEngine;

/// <summary>
/// Drop-in combat test setup for FreezReplay.
/// Attach to the same object as FreezeReplay.
///
/// On Start (after FreezeReplay spawns the player), this script:
/// 1. Adds FighterHealth + ImpactDamage to the player's fighter
/// 2. Spawns a target dummy nearby using StickFigureFactory
///
/// Toggle enableCombatTest to turn on/off.
/// </summary>
public class CombatTestSetup : MonoBehaviour
{
    [Header("=== Feature Toggles ===")]
    [Tooltip("Master toggle — set to false to disable everything")]
    public bool enableCombatTest = true;

    [Tooltip("Spawn a target dummy to hit")]
    public bool spawnDummy = true;

    [Tooltip("Add combat components to the player's fighter automatically")]
    public bool equipPlayer = true;

    [Header("=== Dummy Settings ===")]
    [Tooltip("Horizontal offset from the player's spawn position")]
    public float dummyOffsetX = 5f;

    [Tooltip("Dummy HP (higher = takes more hits)")]
    public float dummyHP = 200f;

    [Tooltip("Color of the dummy fighter")]
    public Color dummyColor = new Color(0.6f, 0.6f, 0.6f);

    [Header("=== References ===")]
    public FreezeReplay freezeReplay;

    private GameObject dummyFighter;

    void Start()
    {
        if (!enableCombatTest) return;

        if (freezeReplay == null)
            freezeReplay = GetComponent<FreezeReplay>();

        // Delay to let FreezeReplay.Start() finish spawning the player
        Invoke(nameof(SetupCombatTest), 0.2f);
    }

    void SetupCombatTest()
    {
        if (freezeReplay == null || freezeReplay.playerObj == null)
        {
            Debug.LogWarning("[CombatTestSetup] FreezeReplay or playerObj not ready.");
            return;
        }

        // === Equip the player's fighter with combat components ===
        if (equipPlayer)
        {
            EquipFighter(freezeReplay.playerObj, "Player");
        }

        // === Spawn a target dummy ===
        if (spawnDummy)
        {
            SpawnTargetDummy();
        }
    }

    /// <summary>
    /// Adds FighterHealth to the root and ImpactDamage to every limb
    /// that has a Rigidbody2D.
    /// </summary>
    void EquipFighter(GameObject fighter, string label)
    {
        // FighterHealth on root
        FighterHealth health = fighter.GetComponent<FighterHealth>();
        if (health == null)
        {
            health = fighter.AddComponent<FighterHealth>();
            // Try to anchor health bar to spine or head
            Transform anchor = fighter.transform.Find("Spine");
            if (anchor == null && fighter.transform.childCount > 0)
                anchor = fighter.transform.GetChild(0);
            health.healthBarAnchor = anchor;
        }

        // ImpactDamage on every rigidbody
        Rigidbody2D[] bodies = fighter.GetComponentsInChildren<Rigidbody2D>();
        int added = 0;
        foreach (var rb in bodies)
        {
            if (rb.GetComponent<ImpactDamage>() == null)
            {
                ImpactDamage dmg = rb.gameObject.AddComponent<ImpactDamage>();
                dmg.fighterHealth = health;
                added++;
            }
            else
            {
                // Ensure existing ImpactDamage points to the right health
                rb.GetComponent<ImpactDamage>().fighterHealth = health;
            }
        }

        Debug.Log($"[CombatTestSetup] Equipped {label}: FighterHealth + {added} ImpactDamage components on {fighter.name}");
    }

    void SpawnTargetDummy()
    {
        // Get spawn position offset from the player
        Vector3 playerPos = freezeReplay.playerObj.transform.position;
        // Find a child to get a better center (e.g., spine)
        Transform spine = freezeReplay.playerObj.transform.Find("Spine");
        if (spine != null)
            playerPos = spine.position;

        Vector3 dummyPos = playerPos + Vector3.right * dummyOffsetX;

        // Use StickFigureFactory if available, otherwise build a simple dummy
        StickFigureFactory factory = GetComponent<StickFigureFactory>();
        if (factory == null)
            factory = gameObject.AddComponent<StickFigureFactory>();

        factory.bodyColor = dummyColor;
        factory.headColor = dummyColor;
        factory.addCombatComponents = true;
        factory.addRotateHingeJoints = false; // Dummy doesn't need posing controls
        factory.addGroundChecks = false;      // GroundCheck requires SpriteShapeRenderer

        dummyFighter = factory.CreateFighter(dummyPos);
        dummyFighter.name = "TargetDummy";

        // Configure health
        FighterHealth health = dummyFighter.GetComponent<FighterHealth>();
        if (health != null)
        {
            health.maxHP = dummyHP;
            health.ResetHealth();
            health.enableDebugLog = true; // Log hits to console so you can see damage values

            // Subscribe to KO event
            health.OnKO += () =>
            {
                Debug.Log("[CombatTestSetup] TARGET DUMMY KNOCKED OUT! Respawning in 3 seconds...");
                Invoke(nameof(RespawnDummy), 3f);
            };
        }

        Debug.Log($"[CombatTestSetup] Spawned target dummy at {dummyPos} with {dummyHP} HP");
    }

    void RespawnDummy()
    {
        if (dummyFighter != null)
            Destroy(dummyFighter);

        SpawnTargetDummy();
    }
}
