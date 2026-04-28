using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // The GameObject the camera should follow
    public float smoothSpeed = 0.125f; // Smoothing factor for camera movement
    public Vector2 offset; // Offset from the target's position (use Vector2)
    public float highScore = float.MinValue;
    public float bestTime = float.MaxValue;
 private float updateInterval = 5.0f; // Interval in seconds
    private float nextUpdateTime = 0f; // Time for the next update

    void FixedUpdate()
    {
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
        if (target != null)
        {
            Vector2 desiredPosition = (Vector2)target.position + offset;
            Vector2 smoothedPosition = Vector2.Lerp((Vector2)transform.position, desiredPosition, smoothSpeed);
            transform.position = new Vector3(smoothedPosition.x, smoothedPosition.y, transform.position.z);
        }
    }

    Transform FindBestAgent()
    {
        StickFigureAgentv2[] agents = FindObjectsOfType<StickFigureAgentv2>();
        Transform bestAgent = null;
        highScore = -999;
        foreach (var agent in agents)
        {
            float agentScore = agent.GetTotalReward(); // Assuming you have a method to get the agent's total reward
            if (agentScore > highScore)
            {
                highScore = agentScore;
                bestAgent = agent.transform;
            }
        }

        return bestAgent;
    }
    public void followHighScore(){
        return;
    }
}
