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
            StickFigureAgent sfa = playerObj.GetComponent<StickFigureAgent>();
            sfa.fr = this;
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
                    StickFigureAgent sfa = agent.GetComponent<StickFigureAgent>();
                    sfa.fr = this;
                sfa.target = playerObj.transform;
                sfa.copy = agentCopies[j];
                }
                
            }
        }
        AutoAddRigidbodies(playerObj);

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
            Application.LoadLevel(Application.loadedLevel);
        }
        if (replaying)
        {
            // frozenCopy.SetActive(false);
            return;
        }
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
            gtr.MergeCheckpoint();
            // Take a snapshot when the spacebar is pressed
            snapshot.Capture(frozenCopy.transform);
        }
        if (Input.GetKeyDown(KeyCode.G))
        {
            gtr.MergeCheckpoint();
            snapshot.Capture(frozenCopy.transform);
        }
        if (Input.GetKeyDown(KeyCode.H))
        {
            gtr.MergeCheckpoint();
            snapshot.Capture(frozenCopy.transform);
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

    public void MLRestore()
    {
        snapshot.Restore(frozenCopies);
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
        GameObject[] nestedObjects = new GameObject[parentTransform.childCount];

        int k = 0;
        for (int i = freezeReplay.rbToCap.Count - ((j+1) * nestedObjects.Length); i < freezeReplay.rbToCap.Count - (j * nestedObjects.Length); i++)
        {
            if (!frozenStates.ContainsKey(freezeReplay.rbToCap[i]))
            {
                frozenStates.Add(
                    freezeReplay.rbToCap[i],
                    new Rigidbody2DState(freezeReplay.rbToCap[i])
                );
            }
            GameObject frozenObject;
            
            // if (i < (parentTransform.childCount * (j+1)))
            // {
                Transform childTransform = parentTransform.GetChild(k);
                k++;
                frozenObject = childTransform.gameObject;
            // }
            // else
            // {
            //     frozenObject = freezeReplay.rbToCap[i].gameObject;
            // }
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
            // if (frozenStates.ContainsKey(freezeReplay.rbToCap[i]))
            // {

            frozenStates[freezeReplay.rbToCap[i]].RestoreState(
                freezeReplay.rbToCap[i],
                frozenBodies[i].GetComponent<Rigidbody2D>()
            );
            // }
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
