using UnityEngine;

public class VRCameraFollow : MonoBehaviour
{
    public Transform targetPlayer;
    public Vector3 offset = new Vector3(0, 1.7f, 0);

    void Update()
    {
        if (targetPlayer == null) return;
        transform.position = targetPlayer.position + offset;
    }
}