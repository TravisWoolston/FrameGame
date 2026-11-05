using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // The GameObject the camera should follow
    public float smoothSpeed = 0.125f; // Smoothing factor for camera movement
    public Vector2 offset; // Offset from the target's position (use Vector2)
    [Tooltip("When enabled, FreezeReplayV2 can pin the camera to each newly captured turn-start position.")]
    public bool holdSnapshotCenter = false;
    public float highScore = float.MinValue;
    public float bestTime = float.MaxValue;
    private float updateInterval = 5.0f; // Interval in seconds
    private float nextUpdateTime = 0f; // Time for the next update

    void FixedUpdate()
    {
        if (holdSnapshotCenter)
            return;

        if (Time.time >= nextUpdateTime)
        {
            // Update camera target based on the highest score
            Transform bestAgent = FindBestAgent();
            if (bestAgent != null)
            {
                target = bestAgent;
            }
            nextUpdateTime = Time.time + updateInterval; // Schedule the next update
        }
    }

    void LateUpdate()
    {
        if (holdSnapshotCenter)
            return;

        if (target != null)
        {
            Vector2 desiredPosition = (Vector2)target.position + offset;
            Vector2 smoothedPosition = Vector2.Lerp((Vector2)transform.position, desiredPosition, smoothSpeed);
            transform.position = new Vector3(smoothedPosition.x, smoothedPosition.y, transform.position.z);
        }
    }

    public void CenterOnSnapshot(Vector2 snapshotPosition)
    {
        holdSnapshotCenter = true;
        target = null;

        Vector2 desiredPosition = snapshotPosition + offset;
        transform.position = new Vector3(desiredPosition.x, desiredPosition.y, transform.position.z);
    }

    public void ResumeAutoFollow()
    {
        holdSnapshotCenter = false;
    }

    public void followHighScore()
    {
        return;
    }
}
