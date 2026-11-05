using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // The GameObject the camera should follow
    public float smoothSpeed = 0.125f; // Smoothing factor for camera movement
    public Vector2 offset; // Offset from the target's position (use Vector2)
    [Tooltip("When enabled, FreezeReplayV2 can pin the camera to each newly captured turn-start position.")]
    public bool holdSnapshotCenter = false;

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
}
