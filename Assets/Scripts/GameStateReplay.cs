using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections;
using System;
public class GameStateReplay : MonoBehaviour
{
    private bool isReplaying = false;

    /// <summary>
    /// Set this from FreezeReplay to limit capture/replay to specific bodies.
    /// If null/empty, falls back to FindObjectsOfType (legacy behavior).
    /// </summary>
    [HideInInspector]
    public List<Rigidbody2D> trackedBodies = null;

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
            velocity = rb.linearVelocity;
        }
    }

    public List<Dictionary<Rigidbody2D, Rigidbody2DState>> stateHistory = new List<Dictionary<Rigidbody2D, Rigidbody2DState>>();
    private List<Dictionary<Rigidbody2D, Rigidbody2DState>> stateHistoryCheckpoint = new List<Dictionary<Rigidbody2D, Rigidbody2DState>>();

    /// <summary>
    /// Returns the rigidbodies to track — either the explicit list or all in scene.
    /// </summary>
    private Rigidbody2D[] GetBodiesToTrack()
    {
        if (trackedBodies != null && trackedBodies.Count > 0)
            return trackedBodies.ToArray();
        return FindObjectsOfType<Rigidbody2D>();
    }

    // Capture the state of tracked Rigidbody2D components
    public void CaptureGameState()
    {
        var currentFrameState = new Dictionary<Rigidbody2D, Rigidbody2DState>();
        var bodies = GetBodiesToTrack();

        foreach (var rb in bodies)
        {
            if (rb != null)
                currentFrameState[rb] = new Rigidbody2DState(rb);
        }

        stateHistoryCheckpoint.Add(currentFrameState);
    }

     public void ReplayGameState()
    {
        ReplayGameState(null);
    }

    public void ReplayGameState(System.Action onComplete)
    {
        if (isReplaying)
            return;

        StartCoroutine(ReplayLoop(onComplete));
    }
private IEnumerator ReplayLoop(System.Action onComplete = null)
{
    isReplaying = true;

    // Only freeze/restore the tracked bodies, not everything in the scene
    var bodies = GetBodiesToTrack();
    
    // Store original body types so we can restore them
    var originalTypes = new Dictionary<Rigidbody2D, RigidbodyType2D>();
    foreach (Rigidbody2D body in bodies)
    {
        if (body == null) continue;
        originalTypes[body] = body.bodyType;
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

            if (rb == null) continue;
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

    // Restore original body types
    foreach (var kvp in originalTypes)
    {
        if (kvp.Key != null)
            kvp.Key.bodyType = kvp.Value;
    }

    isReplaying = false;
    Debug.Log("Replay done");
    onComplete?.Invoke();
}
public void MergeCheckpoint(){
    Debug.Log("state history length " + stateHistory.Count);
    stateHistory.AddRange(stateHistoryCheckpoint);
    stateHistoryCheckpoint = new List<Dictionary<Rigidbody2D, Rigidbody2DState>>();
}
public void ClearCheckpoint(){
    stateHistoryCheckpoint = new List<Dictionary<Rigidbody2D, Rigidbody2DState>>();
}

    void Update()
    {
        CaptureGameState();

        if (Input.GetKeyDown(KeyCode.R))
        {
            ReplayGameState();
        }
    }
}
