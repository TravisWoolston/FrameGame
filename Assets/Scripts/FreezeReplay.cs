using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine.SceneManagement;

public class FreezeReplay : MonoBehaviour
{
    public GameObject frozenCopy; // Assign in the Inspector
    public GameObject[] agentCopies;
    public GameObject[] agentObjs;
    public bool isResetting = false;
    public float resetInterval = 3.0f;
    public float lastSnapshotTime = 0.0f;
    private GameStateSnapshot snapshot;
    public GameObject playerObj;
    public List<Rigidbody2D> rbToCap = new List<Rigidbody2D>(); // List of 2D Rigidbodies to capture

    // public List<UnityEngine.U2D.SpriteShapeController> sscToCap =
    //     new List<UnityEngine.U2D.SpriteShapeController>();
    public GameObject[] nestedObjects;
    public GameObject[] frozenCopies;
    public GameStateReplay gtr;
    public bool replaying = false;
    public int tempScore = 0;
    public int score = 0;
    public UIController uIController;
    public GameObject marker;
    public GameObject target;
    public Renderer bgRenderer;
    public Vector2 bgOffset;
    public bool makeAgentCopies = false;
    public GameObject[] otherGameObjects;
    private int j = 0;

    [Header("=== Combat Integration ===")]
    [Tooltip("Save/restore health values with snapshots so replay collisions don't drain HP")]
    public bool enableHealthSnapshots = true;

    [Header("=== Replay Options ===")]
    [Tooltip("When a snapshot is captured (committed), replay the entire sequence up to this point")]
    public bool replayOnCapture = false;

    [Header("=== Turn System ===")]
    [Tooltip("Enable fixed-rate turn system. When on, physics pauses during posing and runs for exactly turnDuration seconds per commit.")]
    public bool enableTurnSystem = false;

    [Tooltip("How many seconds of physics simulation per turn (e.g., 0.5 = half second per commit)")]
    public float turnDuration = 0.5f;

    [Tooltip("Current turn phase (read-only in Inspector for debugging)")]
    public TurnPhase currentPhase = TurnPhase.Posing;

    // Track simulation time during the Simulating phase
    private float simulationTimeRemaining = 0f;
    private int turnCount = 0;

    public enum TurnPhase
    {
        Posing,      // Physics paused, player drags frozen copy
        Simulating,  // Physics running for turnDuration seconds
        Capturing    // Instant — snapshot the result, return to Posing
    }

    private void Start()
    {
        snapshot = new GameStateSnapshot(this);
        if (GameObject.FindGameObjectWithTag("Background"))
        {
            bgRenderer = GameObject
                .FindGameObjectWithTag("Background")
                .GetComponent<MeshRenderer>();
        }
        if (this.gameObject.GetComponent<UIController>())
        {
            uIController = this.gameObject.GetComponent<UIController>();
        }
        if (makeAgentCopies)
        {
            agentObjs = GameObject.FindGameObjectsWithTag("Agent");
        }
        if (this.gameObject.tag == "GameController")
        {
            playerObj = Instantiate(
                playerObj,
                new Vector3(transform.position.x, transform.position.y - 20, 0),
                Quaternion.identity
            );
        }
        else
        {
            playerObj = Instantiate(
                playerObj,
                new Vector3(transform.position.x, transform.position.y - 17.68f, 0),
                Quaternion.identity
            );

            // Create a new rotation quaternion based on the random rotation
            // Quaternion newRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-85, 85));

            // Apply the new rotation to the playerObj
            // playerObj.transform.rotation = newRotation;
        }

        if (playerObj.name == "FighterAgent(Clone)")
        {
            StickFigureAgentv2 sfa = playerObj.GetComponent<StickFigureAgentv2>();
            sfa.fr = this;
            sfa.EnableGUI(gameObject.name);
            if (marker)
            {
                Debug.Log("marker 1 set");
                sfa.marker = marker.transform;
            }
            if (target){
                Debug.Log("marker 2 set");
                sfa.target = target.transform;

            }
            sfa.copy = frozenCopy;

        }
        else {
            
            if(makeAgentCopies){
                int j = 0;
                target = playerObj;
                foreach(GameObject agent in agentObjs){
                    StickFigureAgentv2 sfa = agent.GetComponent<StickFigureAgentv2>();
                    sfa.fr = this;
                sfa.target = playerObj.transform;
                sfa.copy = agentCopies[j];
                }
                
            }
        }
        AutoAddRigidbodies(playerObj);

        // Tell GameStateReplay to only track THIS fighter's bodies (not all in scene)
        if (gtr != null)
            gtr.trackedBodies = rbToCap;

        frozenCopy = Instantiate(frozenCopy);
        if (makeAgentCopies)
        {
            for (int i = 0; i < agentCopies.Length; i++)
            {
                agentCopies[i] = Instantiate(agentCopies[i]);
            }
        }

        frozenCopies = new GameObject[rbToCap.Count];

        snapshot.Capture(frozenCopy.transform);
        snapshot.Restore(frozenCopies);

        // Transform parentTransform = playerObj.transform;
        //     GameObject[] nestedObjects = new GameObject[parentTransform.childCount];
        //     for(int i = 0; i < parentTransform.childCount; i++){
        //         Transform childTransform = parentTransform.GetChild(i);
        //         nestedObjects[i] = childTransform.gameObject;
        //     }
        //     for(int i = 0; i < parentTransform.childCount; i++){
        //         rbToCap[i].transform.position = nestedObjects[i].GetComponent<Rigidbody2D>().transform.position;
        //         rbToCap[i].transform.rotation = nestedObjects[i].GetComponent<Rigidbody2D>().transform.rotation;
        //         Debug.Log(rbToCap[i]);
        //     }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q))
        {
            // Restore timeScale before reloading
            Time.timeScale = 1f;
            Application.LoadLevel(Application.loadedLevel);
        }
        if (replaying)
        {
            return;
        }

        // === Turn System Mode ===
        if (enableTurnSystem)
        {
            UpdateTurnSystem();
            return;
        }

        // === Legacy Mode (original behavior when turn system is off) ===
        if (uIController)
        {
            tempScore++;
            uIController.score = tempScore;
        }
        if (Input.GetKeyDown(KeyCode.F))
        {
            playerObj.transform.GetChild(1).GetComponent<Rigidbody2D>().freezeRotation =
                !playerObj.transform.GetChild(1).GetComponent<Rigidbody2D>().freezeRotation;
        }
        if (Input.GetKeyDown(KeyCode.R))
        {
            replaying = true;
            frozenCopy.SetActive(false);
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            CommitSnapshot();
        }
        if (Input.GetKeyDown(KeyCode.G))
        {
            CommitSnapshot();
        }
        if (Input.GetKeyDown(KeyCode.H))
        {
            CommitSnapshot();
        }
        if (
            !isResetting && Time.time - lastSnapshotTime >= resetInterval
            || Input.GetKeyUp(KeyCode.Z)
            || Input.GetKeyUp(KeyCode.X)
            || Input.GetKeyUp(KeyCode.C)
            || Input.GetKeyUp(KeyCode.V)
        )
        {
            gtr.ClearCheckpoint();
            Transform parentTransform = frozenCopy.transform;
            nestedObjects = new GameObject[parentTransform.childCount];
            for (int i = 0; i < parentTransform.childCount; i++)
            {
                Transform childTransform = parentTransform.GetChild(i);
                nestedObjects[i] = childTransform.gameObject;
            }
            for (int i = 0; i < parentTransform.childCount; i++)
            {
                // rbToCap[i].transform.position = nestedObjects[i].GetComponent<Rigidbody2D>().transform.position;
                // rbToCap[i].transform.rotation = nestedObjects[i].GetComponent<Rigidbody2D>().transform.rotation;
            }

            snapshot.Restore(frozenCopies);
            RestoreAllFighterHealth();
            lastSnapshotTime = Time.time;
            isResetting = true;
        }
        else if (Time.time - lastSnapshotTime < resetInterval)
        {
            isResetting = false;
        }
        if (marker && this.gameObject.tag == "GameController")
        {
            if (playerObj.transform.GetChild(0).transform.position.x < marker.transform.position.x)
            {
                replaying = true;
                frozenCopy.SetActive(false);
                gtr.ReplayGameState();
            }
        }
    }

    // ===== Turn System =====

    /// <summary>
    /// The core turn loop: POSE → COMMIT → SIMULATE → CAPTURE → POSE
    /// </summary>
    private void UpdateTurnSystem()
    {
        switch (currentPhase)
        {
            case TurnPhase.Posing:
                // Physics is paused. Player can pose the frozen copy.
                // Time.timeScale is 0 but Update still runs (uses unscaled time).
                Time.timeScale = 0f;
                frozenCopy.SetActive(true);

                // Commit the pose on Space
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    // Capture the pose from frozen copy
                    gtr.MergeCheckpoint();
                    snapshot.Capture(frozenCopy.transform);
                    SaveAllFighterHealth();

                    // Start simulation
                    simulationTimeRemaining = turnDuration;
                    currentPhase = TurnPhase.Simulating;
                    Time.timeScale = 1f;
                    frozenCopy.SetActive(false);
                    turnCount++;

                    Debug.Log($"[FreezeReplay] Turn {turnCount}: Simulating {turnDuration}s...");
                }

                // Replay full sequence with R (uses unscaled time)
                if (Input.GetKeyDown(KeyCode.R))
                {
                    Time.timeScale = 1f;
                    replaying = true;
                    frozenCopy.SetActive(false);
                    gtr.ReplayGameState();
                }
                break;

            case TurnPhase.Simulating:
                // Physics is running. Count down the turn duration.
                simulationTimeRemaining -= Time.deltaTime;

                if (simulationTimeRemaining <= 0f)
                {
                    currentPhase = TurnPhase.Capturing;
                }
                break;

            case TurnPhase.Capturing:
                // Simulation done. Copy the live figure's END transforms
                // onto the frozen copy so it shows where the fighter landed.
                CopyLiveToFrozen();

                // Now capture this new frozen copy state as the snapshot
                snapshot.Capture(frozenCopy.transform);
                SaveAllFighterHealth();

                // Freeze physics again
                Time.timeScale = 0f;
                currentPhase = TurnPhase.Posing;
                frozenCopy.SetActive(true);

                Debug.Log($"[FreezeReplay] Turn {turnCount} complete. Pose next move.");
                break;
        }
    }

    /// <summary>
    /// Shared commit logic for legacy mode (Space/G/H keys).
    /// </summary>
    private void CommitSnapshot()
    {
        gtr.MergeCheckpoint();
        snapshot.Capture(frozenCopy.transform);
        SaveAllFighterHealth();
        if (replayOnCapture)
        {
            StartCoroutine(ReplayThenResume());
        }
    }

    /// <summary>
    /// Copies the live fighter's current position/rotation onto the frozen copy's
    /// matching children. Used after simulation to update the frozen copy to
    /// the live figure's END position (not the pre-simulation pose).
    /// </summary>
    private void CopyLiveToFrozen()
    {
        Transform frozenParent = frozenCopy.transform;
        int childCount = frozenParent.childCount;

        for (int i = 0; i < rbToCap.Count && i < childCount; i++)
        {
            Rigidbody2D liveRb = rbToCap[i];
            Transform frozenChild = frozenParent.GetChild(i);

            if (liveRb == null || frozenChild == null) continue;

            // Copy position and rotation from live figure to frozen copy
            frozenChild.position = liveRb.transform.position;
            frozenChild.rotation = liveRb.transform.rotation;

            // Also copy to the frozen child's Rigidbody2D if it has one
            Rigidbody2D frozenRb = frozenChild.GetComponent<Rigidbody2D>();
            if (frozenRb != null)
            {
                frozenRb.position = liveRb.position;
                frozenRb.rotation = liveRb.rotation;
                frozenRb.linearVelocity = Vector2.zero;
                frozenRb.angularVelocity = 0f;
            }
        }
    }

    public void MLRestore()
    {
        snapshot.Restore(frozenCopies);
        RestoreAllFighterHealth();
    }

    // ===== Health Snapshot Helpers =====

    /// <summary>
    /// Saves HP on all FighterHealth components found on the player (and agents).
    /// Called when a snapshot is committed — this is the "real" HP value.
    /// </summary>
    private void SaveAllFighterHealth()
    {
        if (!enableHealthSnapshots) return;
        FighterHealth[] healths = playerObj.GetComponentsInChildren<FighterHealth>();
        foreach (var h in healths) h.SaveHP();
        // Also save for the root in case FighterHealth is on the parent
        FighterHealth rootHealth = playerObj.GetComponent<FighterHealth>();
        if (rootHealth != null) rootHealth.SaveHP();
    }

    /// <summary>
    /// Restores HP on all FighterHealth components to their last saved value.
    /// Called on every Restore — replay collisions don't count.
    /// </summary>
    private void RestoreAllFighterHealth()
    {
        if (!enableHealthSnapshots) return;
        FighterHealth[] healths = playerObj.GetComponentsInChildren<FighterHealth>();
        foreach (var h in healths) h.RestoreHP();
        FighterHealth rootHealth = playerObj.GetComponent<FighterHealth>();
        if (rootHealth != null) rootHealth.RestoreHP();
    }

    // ===== Replay Then Resume =====

    /// <summary>
    /// Plays the full recorded sequence, then restores to the latest snapshot
    /// and resumes the game loop. Does NOT set replaying=true, so Update
    /// continues running during the replay (input is still processed).
    /// </summary>
    private System.Collections.IEnumerator ReplayThenResume()
    {
        // Hide the frozen copy during replay
        frozenCopy.SetActive(false);

        // Play the full sequence — the callback fires when done
        bool replayDone = false;
        gtr.ReplayGameState(() => { replayDone = true; });

        // Wait for replay to finish
        while (!replayDone)
            yield return null;

        // Restore to the latest snapshot (position/rotation/velocity)
        snapshot.Restore(frozenCopies);
        RestoreAllFighterHealth();

        // Re-show the frozen copy so the player can pose the next move
        frozenCopy.SetActive(true);
        lastSnapshotTime = Time.time;

        Debug.Log("[FreezeReplay] Replay complete — resumed game loop.");
    }

    private void AutoAddRigidbodies(GameObject go)
    {
        GameObject activeBodyGO = go;
        List<GameObject> activeBodyNested = GetNestedObjects(activeBodyGO);
        List<Rigidbody2D> allRigidbodies = GetRigidbodiesFromObjects(activeBodyNested);
        if (makeAgentCopies && j < agentObjs.Length)
        {
            AutoAddRigidbodies(agentObjs[j++]);
        }
        j = 0;
        List<Rigidbody2D> allBones = GetRigidbodiesFromObjects(
            GameObject.FindGameObjectsWithTag("Bone").ToList()
        );
        foreach (Rigidbody2D bone in allBones)
        {
            allRigidbodies.Add(bone);
        }
        Array.Reverse(allRigidbodies.ToArray());
        rbToCap.AddRange(allRigidbodies);
    }

    List<Rigidbody2D> GetRigidbodiesFromObjects(List<GameObject> objectsList)
    {
        List<Rigidbody2D> rigidbodies = new List<Rigidbody2D>();

        foreach (var obj in objectsList)
        {
            // Get the Rigidbody2D component from each GameObject
            Rigidbody2D rb = obj.GetComponent<Rigidbody2D>();

            // If the Rigidbody2D component is not null, add it to the list
            if (rb != null)
            {
                rigidbodies.Add(rb);
            }
        }

        return rigidbodies;
    }

    List<GameObject> GetNestedObjects(GameObject parentObject)
    {
        List<GameObject> nestedObjects = new List<GameObject>();

        // Iterate through all child transforms of the parentObject
        if (parentObject)
            foreach (Transform childTransform in parentObject.transform)
            {
                // Add the child GameObject to the list
                nestedObjects.Add(childTransform.gameObject);

                // Recursively call the function to get nested objects of the child
                List<GameObject> childNestedObjects = GetNestedObjects(childTransform.gameObject);
                nestedObjects.AddRange(childNestedObjects);
            }

        return nestedObjects;
    }
}

public class GameStateSnapshot : MonoBehaviour
{
    private Dictionary<Rigidbody2D, Rigidbody2DState> frozenStates =
        new Dictionary<Rigidbody2D, Rigidbody2DState>();
    private FreezeReplay freezeReplay;
    private int j = 0;
    GameObject frozenCopy;
    private bool playerCaptured = false;
    public GameStateSnapshot(FreezeReplay freezeReplay)
    {
        this.freezeReplay = freezeReplay;
    }

    public void Capture(Transform frozenParentTransform)
    {
        if (freezeReplay.bgRenderer)
        {
            freezeReplay.bgOffset = freezeReplay.bgRenderer.material.mainTextureOffset;
        }
        if (freezeReplay.uIController)
        {
            freezeReplay.score = freezeReplay.tempScore;
        }
        freezeReplay.lastSnapshotTime = Time.time;
        // GameObject[] rbCopies = GameObject.FindGameObjectsWithTag("copy");
        Transform parentTransform = frozenParentTransform;
        int childCount = parentTransform.childCount;

        for (int i = 0; i < freezeReplay.rbToCap.Count; i++)
        {
            if (!frozenStates.ContainsKey(freezeReplay.rbToCap[i]))
            {
                frozenStates.Add(
                    freezeReplay.rbToCap[i],
                    new Rigidbody2DState(freezeReplay.rbToCap[i])
                );
            }
            GameObject frozenObject;

            if (i < childCount)
            {
                Transform childTransform = parentTransform.GetChild(i);
                frozenObject = childTransform.gameObject;
            }
            else
            {
                // Fall back to the rigidbody's own GameObject for entries
                // that don't have a corresponding frozen copy child
                frozenObject = freezeReplay.rbToCap[i].gameObject;
            }
            freezeReplay.frozenCopies[i] = frozenObject;

            frozenStates[freezeReplay.rbToCap[i]].CaptureState(
                freezeReplay.rbToCap[i],
                frozenObject.GetComponent<Rigidbody2D>()
            );
        }
        if (freezeReplay.makeAgentCopies && j < freezeReplay.agentObjs.Length)
        {
            Capture(freezeReplay.agentCopies[j++].transform);
        }
        j = 0;

    }
    public void AgentCapture(Transform frozenParentTransform)
    {
        if (freezeReplay.bgRenderer)
        {
            freezeReplay.bgOffset = freezeReplay.bgRenderer.material.mainTextureOffset;
        }
        if (freezeReplay.uIController)
        {
            freezeReplay.score = freezeReplay.tempScore;
        }
        freezeReplay.lastSnapshotTime = Time.time;
        // GameObject[] rbCopies = GameObject.FindGameObjectsWithTag("copy");
        Transform parentTransform = frozenParentTransform;
        GameObject[] nestedObjects = new GameObject[parentTransform.childCount];
        Debug.Log($"{j} {frozenParentTransform}");
        for (int i = (j * nestedObjects.Length); i < (j + 1) * nestedObjects.Length; i++)
        {
            Debug.Log(i);
            if (!frozenStates.ContainsKey(freezeReplay.rbToCap[i]))
            {
                frozenStates.Add(
                    freezeReplay.rbToCap[i],
                    new Rigidbody2DState(freezeReplay.rbToCap[i])
                );
            }
            GameObject frozenObject;
            if (i < parentTransform.childCount * (j+1))
            {
                Transform childTransform = parentTransform.GetChild(i -(j*parentTransform.childCount));
                frozenObject = childTransform.gameObject;
            }
            else
            {
                frozenObject = freezeReplay.rbToCap[i].gameObject;
            }
            freezeReplay.frozenCopies[i] = frozenObject;

            frozenStates[freezeReplay.rbToCap[i]].CaptureState(
                freezeReplay.rbToCap[i],
                frozenObject.GetComponent<Rigidbody2D>()
            );

            // CaptureComponents(rbCopies[i]);
        }
        if (freezeReplay.makeAgentCopies && j < freezeReplay.agentObjs.Length)
        {
            Capture(freezeReplay.agentCopies[j++].transform);
        }
        j = 0;

    }

    public void Restore(GameObject[] frozenBodies)
    {
        if (freezeReplay.bgRenderer)
        {
            freezeReplay.bgRenderer.material.mainTextureOffset = freezeReplay.bgOffset;
        }
        if (freezeReplay.uIController)
        {
            freezeReplay.tempScore = freezeReplay.score;
        }
        // Restore the state of the specified 2D freezeReplay.rbToCap from the nested objects within the frozenCopy
        for (int i = 0; i < freezeReplay.rbToCap.Count; i++)
        {
            if (frozenStates.ContainsKey(freezeReplay.rbToCap[i]) && frozenBodies[i] != null)
            {
                frozenStates[freezeReplay.rbToCap[i]].RestoreState(
                    freezeReplay.rbToCap[i],
                    frozenBodies[i].GetComponent<Rigidbody2D>()
                );
            }
        }

        // Recursively restore the states of all components attached to the frozenCopy
        // RestoreComponents(freezeReplay.frozenCopy);
    }

    // Recursive method to capture states of all components
    private void CaptureComponents(GameObject obj)
    {
        // Capture the state of all components attached to the GameObject
        Component[] components = obj.GetComponents<Component>();
        foreach (var component in components)
        {
            // Capture the state of the component here
        }

        // Recursively capture the states of child objects
        foreach (Transform child in obj.transform)
        {
            CaptureComponents(child.gameObject);
        }
    }

    // Recursive method to restore states of all components
    private void RestoreComponents(GameObject obj)
    {
        // Restore the state of all components attached to the GameObject
        Component[] components = obj.GetComponents<Component>();
        foreach (var component in components)
        {
            // Restore the state of the component here
        }

        // Recursively restore the states of child objects
        foreach (Transform child in obj.transform)
        {
            RestoreComponents(child.gameObject);
        }
    }
}
