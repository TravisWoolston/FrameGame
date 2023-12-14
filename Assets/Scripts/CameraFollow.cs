using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // The GameObject the camera should follow
    public float smoothSpeed = 0.125f; // Smoothing factor for camera movement
    public Vector2 offset; // Offset from the target's position (use Vector2)
    public float highScore = 0;
    public float bestTime = 999;
    public void followHighScore(Transform agent, float score){
        target = agent;
        highScore = score;
    }
     public void followBestTime(Transform agent, float score){
        target = agent;
        bestTime = score;
    }
    void LateUpdate()
    {
        if (target != null)
        {
            Vector2 desiredPosition = (Vector2)target.position + offset;
            Vector2 smoothedPosition = Vector2.Lerp((Vector2)transform.position, desiredPosition, smoothSpeed);
            transform.position = new Vector3(smoothedPosition.x, smoothedPosition.y, transform.position.z);
        }
    }
}
