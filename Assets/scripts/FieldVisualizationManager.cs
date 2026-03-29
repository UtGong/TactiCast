using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FieldVisualizationManager : MonoBehaviour
{
    [Header("Shared Data")]
    public KeyframeData keyframeData;

    [Header("Cache Indices")]
    public int selectedPlayerIndex = 5;
    public int ballIndex = 13;
    public int[] otherPlayerIndices = new int[] { 0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12 };

    [Header("Trajectory Settings")]
    public float trajDuration = 2f;
    public float vizDuration = 2f;
    public float fadeDuration = 0.3f;

    [Header("Rendering - Selected Player")]
    public Color selectedPlayerColor = new Color(1f, 0.9f, 0f, 1f);
    public float selectedPlayerWidth = 0.15f;

    [Header("Rendering - Nearby Players")]
    public Color nearbyPlayerColor = new Color(1f, 1f, 1f, 0.8f);
    public float nearbyPlayerWidth = 0.1f;

    [Header("Rendering - Ball")]
    public Color ballColor = new Color(1f, 0.5f, 0f, 1f);
    public float ballWidth = 0.1f;

    private List<LineRenderer> _lines = new List<LineRenderer>();
    private GameObject _lineRoot;
    private Coroutine _hideCoroutine;
    private bool _cacheReady;
    private int _pendingKeyframe = -1; // queued keyframe while cache loads

    private void Awake()
    {
        _lineRoot = new GameObject("TrajectoryLines");
        _lineRoot.transform.SetParent(this.transform, false);
    }

    private IEnumerator Start()
    {
        while (TimelineCache.Instance == null || !TimelineCache.Instance.IsReady)
            yield return null;

        _cacheReady = true;
        Debug.Log("[FieldViz] Cache ready!");

        // If a keyframe was triggered while we were waiting, run it now
        if (_pendingKeyframe >= 0)
        {
            int idx = _pendingKeyframe;
            _pendingKeyframe = -1;
            ShowAtKeyframe(idx);
        }
    }

    public void ShowAtKeyframe(int keyframeIndex)
    {
        if (keyframeData == null) return;
        if (keyframeIndex < 0 || keyframeIndex >= keyframeData.keyframes.Length) return;

        if (!_cacheReady)
        {
            // Queue the keyframe ¡ª will be shown once cache is ready
            _pendingKeyframe = keyframeIndex;
            Debug.Log("[FieldViz] Cache not ready, queuing keyframe " + keyframeIndex);
            return;
        }

        float kfTime = keyframeData.keyframes[keyframeIndex].time;
        var nearest = FindNearest2(kfTime);

        var drawList = new List<(int idx, Color color, float width)>();
        drawList.Add((selectedPlayerIndex, selectedPlayerColor, selectedPlayerWidth));
        foreach (int idx in nearest) drawList.Add((idx, nearbyPlayerColor, nearbyPlayerWidth));
        drawList.Add((ballIndex, ballColor, ballWidth));

        EnsureLineCount(drawList.Count);
        for (int i = 0; i < drawList.Count; i++)
        {
            var (idx, color, width) = drawList[i];
            DrawTrajectory(_lines[i], idx, kfTime, color, width);
        }
        for (int i = drawList.Count; i < _lines.Count; i++) _lines[i].enabled = false;

        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);
        _hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    private List<int> FindNearest2(float atTime)
    {
        var result = new List<int>();
        Vector3 origin = TimelineCache.Instance.GetPosition(selectedPlayerIndex, atTime);
        var sorted = new List<(float dist, int idx)>();
        foreach (int idx in otherPlayerIndices)
        {
            Vector3 pos = TimelineCache.Instance.GetPosition(idx, atTime);
            float dx = pos.x - origin.x, dz = pos.z - origin.z;
            sorted.Add((dx * dx + dz * dz, idx));
        }
        sorted.Sort((a, b) => a.dist.CompareTo(b.dist));
        for (int i = 0; i < Mathf.Min(2, sorted.Count); i++)
            result.Add(sorted[i].idx);
        return result;
    }

    private void DrawTrajectory(LineRenderer lr, int objIdx, float startTime, Color color, float width)
    {
        var cache = TimelineCache.Instance;
        int startFrame = cache.TimeToFrame(startTime);
        int endFrame = Mathf.Min(cache.TimeToFrame(startTime + trajDuration), cache.FrameCount - 1);
        int count = endFrame - startFrame + 1;

        if (count < 2) { lr.enabled = false; return; }

        var track = cache.GetPositionTrack(objIdx);
        if (track == null) { lr.enabled = false; return; }

        // Flatten all positions to ground level (y = 0.05 to avoid z-fighting)
        var positions = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 p = track[startFrame + i];
            positions[i] = new Vector3(p.x, 0.05f, p.z);
        }

        lr.positionCount = count;
        lr.SetPositions(positions);
        lr.startColor = color;
        lr.endColor = new Color(color.r, color.g, color.b, 0f);
        lr.startWidth = width;
        lr.endWidth = 0.02f;
        lr.numCapVertices = 4;
        lr.enabled = true;
    }

    private void EnsureLineCount(int count)
    {
        while (_lines.Count < count)
        {
            var go = new GameObject("TrajLine_" + _lines.Count);
            go.transform.SetParent(_lineRoot.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.enabled = false;
            _lines.Add(lr);
        }
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(vizDuration);
        yield return FadeOutLines();
    }

    private IEnumerator FadeOutLines()
    {
        float elapsed = 0f;
        var sc = new Color[_lines.Count];
        for (int i = 0; i < _lines.Count; i++)
            if (_lines[i].enabled) sc[i] = _lines[i].startColor;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            for (int i = 0; i < _lines.Count; i++)
            {
                if (!_lines[i].enabled) continue;
                Color c = sc[i]; c.a = Mathf.Lerp(sc[i].a, 0f, t);
                _lines[i].startColor = c;
                _lines[i].endColor = new Color(c.r, c.g, c.b, 0f);
            }
            yield return null;
        }
        foreach (var lr in _lines) lr.enabled = false;
    }
}