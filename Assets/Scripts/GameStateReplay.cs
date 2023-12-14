using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections;
using System;
public class GameStateReplay : MonoBehaviour
{
    private bool isReplaying = false;
    [System.Serializable]
    public class Rigidbody2DState
    {
        public Vector2 position;
        public float rotation;
        public Vector2 velocity;

        public Rigidbody2DState(Rigidbody2D rb)
        {
            position = rb.transform.position;
            rotation = rb.transform.rotation.eulerAngles.z; // Assuming you want the Z rotation.
            velocity = rb.velocity;
        }
    }

    public List<Dictionary<Rigidbody2D, Rigidbody2DState>> stateHistory = new List<Dictionary<Rigidbody2D, Rigidbody2DState>>();
    private List<Dictionary<Rigidbody2D, Rigidbody2DState>> stateHistoryCheckpoint = new List<Dictionary<Rigidbody2D, Rigidbody2DState>>();

    // Capture the state of all Rigidbody2D components in the scene
    public void CaptureGameState()
    {
        var currentFrameState = new Dictionary<Rigidbody2D, Rigidbody2DState>();
        var allRigidbodies = FindObjectsOfType<Rigidbody2D>();

        foreach (var rb in allRigidbodies)
        {
            currentFrameState.Add(rb, new Rigidbody2DState(rb));
        }

        stateHistoryCheckpoint.Add(currentFrameState);
    }

     public void ReplayGameState()
    {
        if (isReplaying)
            return;

        StartCoroutine(ReplayLoop());
    }
private IEnumerator ReplayLoop()
{
    var allRigidbodies = FindObjectsOfType<Rigidbody2D>();
    
    foreach (Rigidbody2D body in allRigidbodies)
    {
        body.bodyType = RigidbodyType2D.Static;
    }

    float totalTime = 0f;
    float frameDelay = 0.001f;

    foreach (var frameState in stateHistory)
    {
        foreach (var kvp in frameState)
        {
            var rb = kvp.Key;
            var state = kvp.Value;

            rb.transform.position = state.position;
            rb.transform.rotation = Quaternion.Euler(0, 0, state.rotation);
            // rb.velocity = state.velocity;
        }

        // Wait until the next frame according to the frameDelay
        while (totalTime < frameDelay)
        {
            yield return null;
            totalTime += Time.deltaTime;
        }

        totalTime -= frameDelay; // Subtract frameDelay to account for the time spent waiting
    }

    Debug.Log("Replay done");
}
public void MergeCheckpoint(){
    Debug.Log("state history length " + stateHistory.Count);
    stateHistory.AddRange(stateHistoryCheckpoint);
    stateHistoryCheckpoint = new List<Dictionary<Rigidbody2D, Rigidbody2DState>>();
}
public void ClearCheckpoint(){
    stateHistoryCheckpoint = new List<Dictionary<Rigidbody2D, Rigidbody2DState>>();
}

    // Example usage
    void Update()
    {

            CaptureGameState();
        

        if (Input.GetKeyDown(KeyCode.R))
        {
            ReplayGameState();
        }
    }
}
