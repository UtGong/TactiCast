using UnityEngine;

/// <summary>
/// Tracks playback time and triggers PiP + field visualization at keyframes.
/// No playback control ¡ª animations run on their own.
/// Time is tracked independently via deltaTime.
/// </summary>
public class PlaybackController : MonoBehaviour
{
    public static PlaybackController Instance { get; private set; }

    [Header("Shared Data")]
    public KeyframeData keyframeData;

    [Header("References")]
    public PiPManager pipManager;
    public FieldVisualizationManager fieldViz;
    public DirectionalArrowUI arrowUI;
    public Transform selectedPlayer;

    [Header("Settings")]
    public float clipDuration = 16f;

    // -------------------------------------------------------------------------

    public float CurrentTime { get; private set; }

    private int _lastTriggeredKeyframe = -1;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        // Track time
        CurrentTime += Time.deltaTime;
        if (CurrentTime > clipDuration) CurrentTime = 0f; // loop

        CheckKeyframeTriggers();
    }

    // -------------------------------------------------------------------------

    private void CheckKeyframeTriggers()
    {
        if (keyframeData == null || keyframeData.keyframes == null) return;

        for (int i = 0; i < keyframeData.keyframes.Length; i++)
        {
            float t = keyframeData.keyframes[i].time;
            if (CurrentTime >= t && CurrentTime < t + 0.1f)
            {
                if (_lastTriggeredKeyframe != i)
                {
                    _lastTriggeredKeyframe = i;
                    TriggerKeyframe(i);
                }
                return;
            }
        }
    }

    private void TriggerKeyframe(int index)
    {
        if (pipManager != null) pipManager.ShowPiP(index);
        if (fieldViz != null) fieldViz.ShowAtKeyframe(index);

        if (arrowUI == null || keyframeData == null) return;
        var kf = keyframeData.keyframes[index];
        int selIdx = TimelineCache.Instance != null
            ? TimelineCache.Instance.IndexOf(selectedPlayer) : -1;
        Vector3 playerFwd = selIdx >= 0
            ? TimelineCache.Instance.GetForward(selIdx, kf.time)
            : Vector3.forward;
        Quaternion baseRot = Quaternion.LookRotation(playerFwd, Vector3.up);
        Quaternion offsetRot = Quaternion.Euler(kf.recommendedEulerOffset);
        arrowUI.ShowArrow(baseRot * offsetRot);
    }
}