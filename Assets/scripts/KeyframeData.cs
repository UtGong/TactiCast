using UnityEngine;

/// <summary>
/// ScriptableObject storing all keyframe data shared across scripts.
/// Create via: Assets > Create > TactiCast > KeyframeData
/// </summary>
[CreateAssetMenu(fileName = "KeyframeData", menuName = "TactiCast/KeyframeData")]
public class KeyframeData : ScriptableObject
{
    public KeyframeEntry[] keyframes;
}

[System.Serializable]
public class KeyframeEntry
{
    [Tooltip("Time in seconds when this keyframe occurs.")]
    public float time;

    [Tooltip("PiP camera offset from player forward. X=pitch, Y=yaw, Z=keep 0.")]
    public Vector3 recommendedEulerOffset;
}
