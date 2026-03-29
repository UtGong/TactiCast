using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Pre-bakes all tracked object positions by scrubbing Animator normalized time.
/// Saves to StreamingAssets/TimelineCache.json on first run.
/// On subsequent runs loads from file — no baking needed.
/// Tick "Force Rebake" to regenerate.
/// </summary>
public class TimelineCache : MonoBehaviour
{
    public static TimelineCache Instance { get; private set; }

    [Header("Animators to scrub (same list as PlaybackController)")]
    public List<Animator> animators = new List<Animator>();

    [Header("Objects to track (players + ball, same order as animators)")]
    public List<Transform> trackedObjects = new List<Transform>();

    [Header("Bake Settings")]
    public float bakeFps = 30f;

    [Tooltip("Force re-bake and overwrite saved file even if it already exists.")]
    public bool forceRebake = false;

    // -------------------------------------------------------------------------
    // Runtime cache (Vector3 arrays, not serialized)
    // -------------------------------------------------------------------------

    private Vector3[][] _positions;
    private Vector3[][] _forwards;
    private int _frameCount;
    private float _duration;
    private bool _ready;

    public bool IsReady => _ready;
    public float Duration => _duration;
    public int FrameCount => _frameCount;

    private static string CacheFilePath =>
        Path.Combine(Application.streamingAssetsPath, "TimelineCache.json");

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private IEnumerator Start()
    {
        while (PlaybackController.Instance == null) yield return null;
        _duration = PlaybackController.Instance.clipDuration;

        if (!forceRebake && File.Exists(CacheFilePath))
        {
            Debug.Log("[TimelineCache] Cache file found, loading from disk...");
            LoadFromFile();
        }
        else
        {
            Debug.Log("[TimelineCache] Baking cache...");
            yield return StartCoroutine(Bake());
            SaveToFile();
        }
    }

    // -------------------------------------------------------------------------
    // Bake
    // -------------------------------------------------------------------------

    private IEnumerator Bake()
    {
        if (trackedObjects.Count == 0 || animators.Count == 0)
        {
            Debug.LogWarning("[TimelineCache] trackedObjects or animators not set.");
            yield break;
        }

        _frameCount = Mathf.CeilToInt(_duration * bakeFps) + 1;
        int objCount = trackedObjects.Count;

        _positions = new Vector3[objCount][];
        _forwards = new Vector3[objCount][];
        for (int i = 0; i < objCount; i++)
        {
            _positions[i] = new Vector3[_frameCount];
            _forwards[i] = new Vector3[_frameCount];
        }

        foreach (var anim in animators) if (anim) anim.speed = 0f;

        for (int f = 0; f < _frameCount; f++)
        {
            float normalizedTime = Mathf.Clamp01((f / bakeFps) / _duration);

            foreach (var anim in animators)
            {
                if (anim == null) continue;
                var stateInfo = anim.GetCurrentAnimatorStateInfo(0);
                anim.Play(stateInfo.shortNameHash, 0, normalizedTime);
                anim.Update(0f);
            }

            yield return null; // wait one frame for transforms to update

            for (int i = 0; i < objCount; i++)
            {
                var t = trackedObjects[i];
                if (t == null) continue;
                _positions[i][f] = t.position;
                Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up);
                _forwards[i][f] = fwd == Vector3.zero ? Vector3.forward : fwd.normalized;
            }

            if (f % 30 == 0)
                Debug.Log($"[TimelineCache] Baking {f}/{_frameCount}...");
        }

        // Restore all animators to normal playback
        foreach (var anim in animators)
        {
            if (anim == null) continue;
            var stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            anim.Play(stateInfo.shortNameHash, 0, 0f);
            anim.speed = 1f;
            anim.enabled = true;
        }

        _ready = true;
        Debug.Log($"[TimelineCache] Bake done: {_frameCount} frames, {objCount} objects.");
    }

    // -------------------------------------------------------------------------
    // Save / Load  (flat 1D float arrays — JsonUtility compatible)
    // -------------------------------------------------------------------------

    private void SaveToFile()
    {
        if (!Directory.Exists(Application.streamingAssetsPath))
            Directory.CreateDirectory(Application.streamingAssetsPath);

        int objCount = trackedObjects.Count;
        int stride = _frameCount * 3; // x,y,z per frame

        var data = new CacheData
        {
            frameCount = _frameCount,
            objectCount = objCount,
            bakeFps = this.bakeFps,
            duration = _duration,
            // FIX: flat 1D arrays — fully supported by JsonUtility
            positions = new float[objCount * stride],
            forwards = new float[objCount * stride]
        };

        for (int i = 0; i < objCount; i++)
        {
            for (int f = 0; f < _frameCount; f++)
            {
                int idx = i * stride + f * 3;
                data.positions[idx] = _positions[i][f].x;
                data.positions[idx + 1] = _positions[i][f].y;
                data.positions[idx + 2] = _positions[i][f].z;
                data.forwards[idx] = _forwards[i][f].x;
                data.forwards[idx + 1] = _forwards[i][f].y;
                data.forwards[idx + 2] = _forwards[i][f].z;
            }
        }

        File.WriteAllText(CacheFilePath, JsonUtility.ToJson(data));
        Debug.Log($"[TimelineCache] Saved to {CacheFilePath}");
    }

    private void LoadFromFile()
    {
        var data = JsonUtility.FromJson<CacheData>(File.ReadAllText(CacheFilePath));

        _frameCount = data.frameCount;
        _duration = data.duration;
        bakeFps = data.bakeFps;

        int objCount = data.objectCount;
        int stride = _frameCount * 3;

        _positions = new Vector3[objCount][];
        _forwards = new Vector3[objCount][];

        for (int i = 0; i < objCount; i++)
        {
            _positions[i] = new Vector3[_frameCount];
            _forwards[i] = new Vector3[_frameCount];
            for (int f = 0; f < _frameCount; f++)
            {
                int idx = i * stride + f * 3;
                _positions[i][f] = new Vector3(
                    data.positions[idx], data.positions[idx + 1], data.positions[idx + 2]);
                _forwards[i][f] = new Vector3(
                    data.forwards[idx], data.forwards[idx + 1], data.forwards[idx + 2]);
            }
        }

        _ready = true;
        Debug.Log($"[TimelineCache] Loaded: {_frameCount} frames, {objCount} objects.");
    }

    // -------------------------------------------------------------------------
    // Query API
    // -------------------------------------------------------------------------

    public int TimeToFrame(float time)
        => Mathf.Clamp(Mathf.RoundToInt(time * bakeFps), 0, _frameCount - 1);

    public int CurrentFrame()
    {
        if (PlaybackController.Instance == null) return 0;
        return TimeToFrame(PlaybackController.Instance.CurrentTime);
    }

    public Vector3 GetPosition(int objectIndex, float time)
    {
        if (!_ready || objectIndex < 0 || objectIndex >= _positions.Length) return Vector3.zero;
        return _positions[objectIndex][TimeToFrame(time)];
    }

    public Vector3 GetForward(int objectIndex, float time)
    {
        if (!_ready || objectIndex < 0 || objectIndex >= _forwards.Length) return Vector3.forward;
        return _forwards[objectIndex][TimeToFrame(time)];
    }

    public Vector3[] GetPositionTrack(int objectIndex)
    {
        if (!_ready || objectIndex < 0 || objectIndex >= _positions.Length) return null;
        return _positions[objectIndex];
    }

    public int IndexOf(Transform t) => trackedObjects.IndexOf(t);

    // -------------------------------------------------------------------------
    // Serialization data class — flat arrays only, JsonUtility compatible
    // -------------------------------------------------------------------------

    [System.Serializable]
    private class CacheData
    {
        public int frameCount;
        public int objectCount;
        public float bakeFps;
        public float duration;
        public float[] positions; // flat: [objIdx * frameCount*3 + frameIdx*3 + axis]
        public float[] forwards;  // same layout
    }
}