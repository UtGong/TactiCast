using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 在主视角球员附近显示未来辅助可视化：
/// 1. 虚线轨迹（可隐藏）
/// 2. 脚下圆盘
/// 3. 圆盘外围实心箭头
///
/// 依赖：
/// - 原场景中的 PlaybackController
/// - 原场景中的 TimelineCache
///
/// 说明：
/// - showDashedLines = false 时，只显示圆盘 + 箭头
/// - 箭头为 Mesh 填充形状，不再使用 LineRenderer 箭头，减少翼边闪烁
/// - 箭头方向优先使用未来轨迹方向，其次回退到 TimelineCache forward，再回退到 live forward
/// </summary>
public class NearbyFutureTacticalLineVisualizer : MonoBehaviour
{
    [Header("Reference")]
    public PlaybackController playbackController;

    [Header("Candidates")]
    [Tooltip("必须使用原 TimelineCache.trackedObjects 中的同一批 Transform")]
    public List<Transform> candidatePlayers = new List<Transform>();

    [Tooltip("排除对象，例如 Ball")]
    public List<Transform> excludedObjects = new List<Transform>();

    [Header("Selection")]
    public bool includeSelectedPlayer = true;

    [Range(1, 5)]
    public int nearbyPlayerCount = 2;

    [Tooltip("是否用 live transform 位置来判断最近两人")]
    public bool useLivePositionForSelection = true;

    [Tooltip("邻近球员最短保持时间，避免每帧切换")]
    [Min(0f)]
    public float minHoldSeconds = 0.7f;

    [Tooltip("新球员必须比当前球员近出这么多，才允许切换（单位：米）")]
    [Min(0f)]
    public float switchDistanceMargin = 0.35f;

    [Header("Time")]
    [Min(0.1f)]
    public float previewSeconds = 2f;

    [Range(0.02f, 0.5f)]
    public float sampleInterval = 0.08f;

    [Tooltip("是否允许未来轨迹跨越 clip 末尾回绕到开头")]
    public bool allowWrapAcrossClipEnd = false;

    [Header("Sync Correction")]
    [Tooltip("圆盘是否直接跟随 live transform，而不是 TimelineCache 当前点")]
    public bool useLiveAnchorForDisc = true;

    [Tooltip("轨迹起点是否直接使用 live transform")]
    public bool useLiveAnchorForTrajectoryStart = true;

    [Tooltip("给 TimelineCache 未来采样加一点时间补偿，修正系统性滞后/超前")]
    [Range(-0.3f, 0.3f)]
    public float syncOffsetSeconds = 0.06f;

    [Tooltip("如果相邻采样点距离过大，直接截断，避免怪异长直线")]
    [Min(0.1f)]
    public float maxAllowedSegmentLength = 2.5f;

    [Header("Position")]
    [Tooltip("脚底视觉补偿。TimelineCache 记录的是 Transform.position，不一定是脚底")]
    public float footOffsetY = -0.9f;

    [Tooltip("线条离地抬高一点，避免与地面闪烁")]
    public float yOffset = 0.03f;

    public bool useFixedGroundY = false;
    public float fixedGroundY = 0f;

    [Header("Line Style")]
    [Tooltip("是否显示虚线轨迹")]
    public bool showDashedLines = true;

    public float lineWidth = 0.04f;
    public Color selectedPlayerColor = new Color(1f, 0.85f, 0.15f, 1f);
    public Color nearbyPlayerColorA = new Color(0.2f, 0.9f, 1f, 1f);
    public Color nearbyPlayerColorB = new Color(0.2f, 0.6f, 1f, 1f);

    [Min(0.01f)]
    public float dashLength = 0.35f;

    [Min(0.01f)]
    public float gapLength = 0.14f;

    [Header("Disc Marker")]
    public bool showDiscMarkers = true;

    [Tooltip("圆盘半径")]
    [Min(0.01f)]
    public float discRadius = 0.22f;

    [Tooltip("圆盘离地高度")]
    public float discYOffset = 0.01f;

    [Tooltip("圆盘透明度")]
    [Range(0f, 1f)]
    public float discAlpha = 0.95f;

    [Tooltip("圆盘边数，越大越圆")]
    [Range(8, 64)]
    public int discSegments = 24;

    [Tooltip("圆盘线宽")]
    [Min(0.005f)]
    public float discLineWidth = 0.03f;

    [Header("Arrow Marker")]
    [Tooltip("是否显示圆盘外围箭头")]
    public bool showArrowMarkers = true;

    [Tooltip("箭头从圆盘外缘再往外留出的间隔")]
    [Min(0f)]
    public float arrowGap = 0.03f;

    [Tooltip("箭头总长度")]
    [Min(0.01f)]
    public float arrowLength = 0.30f;

    [Tooltip("箭头最宽处宽度")]
    [Min(0.01f)]
    public float arrowWidth = 0.18f;

    [Tooltip("箭头尾部宽度")]
    [Min(0.01f)]
    public float arrowBackWidth = 0.08f;

    [Tooltip("箭头尾部凹口深度")]
    [Min(0f)]
    public float arrowNotchDepth = 0.05f;

    [Tooltip("箭头肩部位置占总长度的比例")]
    [Range(0.1f, 0.9f)]
    public float arrowShoulderRatio = 0.38f;

    [Tooltip("箭头透明度")]
    [Range(0f, 1f)]
    public float arrowAlpha = 0.95f;

    [Tooltip("箭头离地高度")]
    public float arrowYOffset = 0.015f;

    [Tooltip("箭头方向平滑速度，越大越跟手，越小越稳")]
    [Range(0f, 30f)]
    public float arrowDirectionSmooth = 12f;

    [Header("Performance")]
    [Range(8, 256)]
    public int maxDashSegmentsPerTrack = 96;

    [Header("Debug")]
    public bool verboseDebug = true;

    private readonly List<Transform> _trackedPlayers = new List<Transform>(8);
    private readonly List<Vector3> _samplePoints = new List<Vector3>(128);
    private readonly List<TrackRenderer> _trackRenderers = new List<TrackRenderer>(8);
    private readonly List<DiscMarker> _discMarkers = new List<DiscMarker>(8);
    private readonly List<ArrowMarker> _arrowMarkers = new List<ArrowMarker>(8);
    private readonly HashSet<string> _loggedKeys = new HashSet<string>();

    private Material _sharedLineMaterial;
    private Material _sharedDiscMaterial;
    private Material _sharedArrowMaterial;
    private bool _printedStateOnce = false;

    // 只针对 nearby 槽位，不含主视角球员
    private Transform[] _lockedNearbyPlayers;
    private float[] _nearbyLockExpireTimes;

    // 箭头方向平滑缓存
    private Vector3[] _smoothedArrowDirections;

    private void Awake()
    {
        if (playbackController == null)
        {
            playbackController = PlaybackController.Instance;
        }

        CreateSharedMaterials();
        RebuildVisualPools();
        EnsureLockArrays();
    }

    private void LateUpdate()
    {
        if (!_printedStateOnce)
        {
            _printedStateOnce = true;
            PrintState();
        }

        if (!CanRender())
        {
            HideAllTracks();
            HideAllDiscs();
            HideAllArrows();
            return;
        }

        Transform selected = playbackController.selectedPlayer;
        if (selected == null)
        {
            LogOnce("SelectedNull", "[FutureLine] selectedPlayer is null");
            HideAllTracks();
            HideAllDiscs();
            HideAllArrows();
            return;
        }

        CollectTrackedPlayers(selected);
        RenderTracks(playbackController.CurrentTime);
        RenderDiscMarkers();
        RenderArrowMarkers();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _trackRenderers.Count; i++)
        {
            _trackRenderers[i].Dispose();
        }
        _trackRenderers.Clear();

        for (int i = 0; i < _discMarkers.Count; i++)
        {
            _discMarkers[i].Dispose();
        }
        _discMarkers.Clear();

        for (int i = 0; i < _arrowMarkers.Count; i++)
        {
            _arrowMarkers[i].Dispose();
        }
        _arrowMarkers.Clear();

        if (_sharedLineMaterial != null) Destroy(_sharedLineMaterial);
        if (_sharedDiscMaterial != null) Destroy(_sharedDiscMaterial);
        if (_sharedArrowMaterial != null) Destroy(_sharedArrowMaterial);
    }

    private bool CanRender()
    {
        if (playbackController == null)
        {
            playbackController = PlaybackController.Instance;
        }

        if (playbackController == null)
        {
            LogOnce("PlaybackNull", "[FutureLine] playbackController is null");
            return false;
        }

        if (TimelineCache.Instance == null)
        {
            LogOnce("TimelineNull", "[FutureLine] TimelineCache.Instance is null");
            return false;
        }

        if (!TimelineCache.Instance.IsReady)
        {
            LogOnce("TimelineNotReady", "[FutureLine] TimelineCache is not ready");
            return false;
        }

        if (_sharedLineMaterial == null || _sharedDiscMaterial == null || _sharedArrowMaterial == null)
        {
            LogOnce("MaterialNull", "[FutureLine] shared material is null");
            return false;
        }

        if (candidatePlayers == null || candidatePlayers.Count == 0)
        {
            LogOnce("CandidateEmpty", "[FutureLine] candidatePlayers is empty");
            return false;
        }

        int expected = includeSelectedPlayer ? 1 + nearbyPlayerCount : nearbyPlayerCount;
        if (_trackRenderers.Count != expected || _discMarkers.Count != expected || _arrowMarkers.Count != expected)
        {
            RebuildVisualPools();
        }

        EnsureLockArrays();
        return true;
    }

    private void CollectTrackedPlayers(Transform selected)
    {
        _trackedPlayers.Clear();

        if (includeSelectedPlayer)
        {
            _trackedPlayers.Add(selected);
        }

        if (TimelineCache.Instance == null || !TimelineCache.Instance.IsReady)
        {
            LogOnce("Select_TimelineNotReady", "[FutureLine] TimelineCache not ready in CollectTrackedPlayers");
            return;
        }

        float currentTime = playbackController.CurrentTime;

        int selectedIndex = TimelineCache.Instance.IndexOf(selected);
        if (selectedIndex < 0)
        {
            LogOnce(
                "Select_SelectedMissing_" + selected.name,
                $"[FutureLine] selectedPlayer not found in TimelineCache.trackedObjects: {selected.name}"
            );
            return;
        }

        Vector3 selectedPos = useLivePositionForSelection
            ? selected.position
            : GetInterpolatedPositionFromTimeline(selected, currentTime);

        List<PlayerDistance> distances = new List<PlayerDistance>(candidatePlayers.Count);

        for (int i = 0; i < candidatePlayers.Count; i++)
        {
            Transform candidate = candidatePlayers[i];
            if (candidate == null) continue;
            if (candidate == selected) continue;
            if (IsExcluded(candidate)) continue;

            int candidateIndex = TimelineCache.Instance.IndexOf(candidate);
            if (candidateIndex < 0)
            {
                LogOnce(
                    "Select_CandidateMissing_" + candidate.name,
                    $"[FutureLine] candidate not found in TimelineCache.trackedObjects: {candidate.name}"
                );
                continue;
            }

            Vector3 candidatePos = useLivePositionForSelection
                ? candidate.position
                : GetInterpolatedPositionFromTimeline(candidate, currentTime);

            float sqrDistXZ = XZDistanceSqr(selectedPos, candidatePos);
            distances.Add(new PlayerDistance(candidate, sqrDistXZ));
        }

        distances.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));
        UpdateLockedNearbyPlayers(distances);

        for (int i = 0; i < nearbyPlayerCount; i++)
        {
            if (_lockedNearbyPlayers[i] != null)
            {
                _trackedPlayers.Add(_lockedNearbyPlayers[i]);
            }
        }

        if (verboseDebug)
        {
            string msg = $"[FutureLine] Selected={selected.name}";
            for (int i = 0; i < nearbyPlayerCount; i++)
            {
                if (_lockedNearbyPlayers[i] == null)
                {
                    msg += $", Nearby[{i}]=null";
                }
                else
                {
                    float d = FindDistance(distances, _lockedNearbyPlayers[i]);
                    msg += $", Nearby[{i}]={_lockedNearbyPlayers[i].name}, distXZ={d:F2}";
                }
            }
            Debug.Log(msg);
        }
    }

    private void UpdateLockedNearbyPlayers(List<PlayerDistance> sortedCandidates)
    {
        EnsureLockArrays();

        HashSet<Transform> alreadyAssigned = new HashSet<Transform>();

        for (int slot = 0; slot < nearbyPlayerCount; slot++)
        {
            Transform current = _lockedNearbyPlayers[slot];
            if (current != null)
            {
                alreadyAssigned.Add(current);
            }
        }

        for (int slot = 0; slot < nearbyPlayerCount; slot++)
        {
            Transform current = _lockedNearbyPlayers[slot];
            float currentDist = FindDistance(sortedCandidates, current);

            if (current != null && currentDist < 0f)
            {
                alreadyAssigned.Remove(current);
                _lockedNearbyPlayers[slot] = null;
                current = null;
            }

            Transform bestCandidate = FindBestUnassignedCandidate(sortedCandidates, alreadyAssigned);

            if (current == null)
            {
                if (bestCandidate != null)
                {
                    _lockedNearbyPlayers[slot] = bestCandidate;
                    _nearbyLockExpireTimes[slot] = Time.time + minHoldSeconds;
                    alreadyAssigned.Add(bestCandidate);
                }
                continue;
            }

            if (Time.time < _nearbyLockExpireTimes[slot])
            {
                continue;
            }

            if (bestCandidate == null)
            {
                alreadyAssigned.Add(current);
                continue;
            }

            if (bestCandidate == current)
            {
                alreadyAssigned.Add(current);
                continue;
            }

            float bestDist = FindDistance(sortedCandidates, bestCandidate);

            if (bestDist >= 0f && currentDist >= 0f && bestDist + switchDistanceMargin < currentDist)
            {
                alreadyAssigned.Remove(current);
                _lockedNearbyPlayers[slot] = bestCandidate;
                _nearbyLockExpireTimes[slot] = Time.time + minHoldSeconds;
                alreadyAssigned.Add(bestCandidate);
            }
            else
            {
                alreadyAssigned.Add(current);
            }
        }
    }

    private Transform FindBestUnassignedCandidate(List<PlayerDistance> sortedCandidates, HashSet<Transform> alreadyAssigned)
    {
        for (int i = 0; i < sortedCandidates.Count; i++)
        {
            Transform p = sortedCandidates[i].player;
            if (p != null && !alreadyAssigned.Contains(p))
            {
                return p;
            }
        }

        return null;
    }

    private float FindDistance(List<PlayerDistance> sortedCandidates, Transform target)
    {
        if (target == null) return -1f;

        for (int i = 0; i < sortedCandidates.Count; i++)
        {
            if (sortedCandidates[i].player == target)
            {
                return Mathf.Sqrt(sortedCandidates[i].sqrDistance);
            }
        }

        return -1f;
    }

    private bool IsExcluded(Transform t)
    {
        if (excludedObjects == null || excludedObjects.Count == 0)
            return false;

        for (int i = 0; i < excludedObjects.Count; i++)
        {
            if (excludedObjects[i] == t)
                return true;
        }

        return false;
    }

    private void RenderTracks(float currentTime)
    {
        if (!showDashedLines)
        {
            HideAllTracks();
            return;
        }

        int count = Mathf.Min(_trackedPlayers.Count, _trackRenderers.Count);

        for (int i = 0; i < count; i++)
        {
            Transform player = _trackedPlayers[i];
            Color color = GetTrackColor(i);

            bool ok = BuildFutureTrajectory(player, currentTime, _samplePoints);
            if (!ok)
            {
                _trackRenderers[i].HideAll();
                continue;
            }

            _trackRenderers[i].SetWidth(lineWidth);
            _trackRenderers[i].DrawDashedPolyline(_samplePoints, color, dashLength, gapLength);
        }

        for (int i = count; i < _trackRenderers.Count; i++)
        {
            _trackRenderers[i].HideAll();
        }
    }

    private void RenderDiscMarkers()
    {
        if (!showDiscMarkers)
        {
            HideAllDiscs();
            return;
        }

        int count = Mathf.Min(_trackedPlayers.Count, _discMarkers.Count);

        for (int i = 0; i < count; i++)
        {
            Transform player = _trackedPlayers[i];
            if (player == null)
            {
                _discMarkers[i].Hide();
                continue;
            }

            Vector3 pos;

            if (useLiveAnchorForDisc)
            {
                pos = GetLiveAnchorForDisc(player);
            }
            else
            {
                float currentTime = playbackController.CurrentTime + syncOffsetSeconds;
                pos = GetInterpolatedPositionFromTimeline(player, currentTime);
                pos = ApplyDiscOffset(pos);
            }

            Color color = GetTrackColor(i);
            color.a = discAlpha;

            _discMarkers[i].Show(pos, discRadius, color, discSegments, discLineWidth);
        }

        for (int i = count; i < _discMarkers.Count; i++)
        {
            _discMarkers[i].Hide();
        }
    }

    private void RenderArrowMarkers()
    {
        if (!showArrowMarkers)
        {
            HideAllArrows();
            return;
        }

        int count = Mathf.Min(_trackedPlayers.Count, _arrowMarkers.Count);

        for (int i = 0; i < count; i++)
        {
            Transform player = _trackedPlayers[i];
            if (player == null)
            {
                _arrowMarkers[i].Hide();
                continue;
            }

            Vector3 center = GetLiveAnchorForArrow(player);
            Vector3 rawForward = GetDisplayForward(player, playbackController.CurrentTime);
            Vector3 smoothForward = GetSmoothedArrowDirection(i, rawForward);

            if (smoothForward.sqrMagnitude < 0.0001f)
            {
                _arrowMarkers[i].Hide();
                continue;
            }

            Color color = GetTrackColor(i);
            color.a = arrowAlpha;

            _arrowMarkers[i].Show(
                center,
                smoothForward.normalized,
                discRadius,
                arrowGap,
                arrowLength,
                arrowWidth,
                arrowBackWidth,
                arrowNotchDepth,
                arrowShoulderRatio,
                color
            );
        }

        for (int i = count; i < _arrowMarkers.Count; i++)
        {
            _arrowMarkers[i].Hide();
        }
    }

    private void HideAllTracks()
    {
        for (int i = 0; i < _trackRenderers.Count; i++)
        {
            _trackRenderers[i].HideAll();
        }
    }

    private void HideAllDiscs()
    {
        for (int i = 0; i < _discMarkers.Count; i++)
        {
            _discMarkers[i].Hide();
        }
    }

    private void HideAllArrows()
    {
        for (int i = 0; i < _arrowMarkers.Count; i++)
        {
            _arrowMarkers[i].Hide();
        }
    }

    private Color GetTrackColor(int trackIndex)
    {
        if (includeSelectedPlayer)
        {
            if (trackIndex == 0) return selectedPlayerColor;
            if (trackIndex == 1) return nearbyPlayerColorA;
            return nearbyPlayerColorB;
        }
        else
        {
            if (trackIndex == 0) return nearbyPlayerColorA;
            return nearbyPlayerColorB;
        }
    }

    private bool BuildFutureTrajectory(Transform player, float startTime, List<Vector3> outPoints)
    {
        outPoints.Clear();

        if (player == null)
            return false;

        if (TimelineCache.Instance == null || !TimelineCache.Instance.IsReady)
            return false;

        int index = TimelineCache.Instance.IndexOf(player);
        if (index < 0)
        {
            LogOnce("TrackMissing_" + player.name, $"[FutureLine] player not found in TimelineCache: {player.name}");
            return false;
        }

        if (useLiveAnchorForTrajectoryStart)
        {
            outPoints.Add(GetLiveAnchorForLine(player));
        }

        int sampleCount = Mathf.Max(2, Mathf.CeilToInt(previewSeconds / sampleInterval) + 1);
        float queryBaseTime = startTime + syncOffsetSeconds;
        int startSampleIndex = useLiveAnchorForTrajectoryStart ? 1 : 0;

        for (int i = startSampleIndex; i < sampleCount; i++)
        {
            float t = queryBaseTime + i * sampleInterval;

            if (!allowWrapAcrossClipEnd)
            {
                if (t > playbackController.clipDuration)
                    break;
            }
            else
            {
                t = NormalizeWrappedTime(t, playbackController.clipDuration);
            }

            Vector3 pos = GetInterpolatedPosition(index, t);
            pos = ApplyVisualOffset(pos);

            if (outPoints.Count > 0)
            {
                float dist = Vector3.Distance(outPoints[outPoints.Count - 1], pos);
                if (dist > maxAllowedSegmentLength)
                {
                    break;
                }
            }

            outPoints.Add(pos);
        }

        return outPoints.Count >= 2;
    }

    private Vector3 GetInterpolatedPositionFromTimeline(Transform target, float time)
    {
        if (TimelineCache.Instance == null || !TimelineCache.Instance.IsReady || target == null)
            return Vector3.zero;

        int index = TimelineCache.Instance.IndexOf(target);
        if (index < 0)
            return Vector3.zero;

        return GetInterpolatedPosition(index, time);
    }

    private Vector3 GetInterpolatedPosition(int objectIndex, float time)
    {
        Vector3[] track = TimelineCache.Instance.GetPositionTrack(objectIndex);
        if (track == null || track.Length == 0)
            return Vector3.zero;

        float duration = TimelineCache.Instance.Duration;
        int frameCount = TimelineCache.Instance.FrameCount;

        if (duration <= 0f || frameCount <= 1)
        {
            return TimelineCache.Instance.GetPosition(objectIndex, time);
        }

        float fps = (frameCount - 1) / duration;
        float frameFloat = Mathf.Clamp(time * fps, 0f, frameCount - 1);

        int frame0 = Mathf.FloorToInt(frameFloat);
        int frame1 = Mathf.Min(frame0 + 1, frameCount - 1);
        float lerpT = frameFloat - frame0;

        return Vector3.Lerp(track[frame0], track[frame1], lerpT);
    }

    private Vector3 GetDisplayForward(Transform player, float currentTime)
    {
        if (player == null)
            return Vector3.forward;

        int index = TimelineCache.Instance.IndexOf(player);
        if (index < 0)
        {
            Vector3 liveFwd = Vector3.ProjectOnPlane(player.forward, Vector3.up);
            return liveFwd.sqrMagnitude < 0.0001f ? Vector3.forward : liveFwd.normalized;
        }

        float t0 = currentTime + syncOffsetSeconds;
        float t1 = t0 + Mathf.Max(sampleInterval, 0.05f);

        if (!allowWrapAcrossClipEnd && t1 <= playbackController.clipDuration)
        {
            Vector3 p0 = GetInterpolatedPosition(index, t0);
            Vector3 p1 = GetInterpolatedPosition(index, t1);
            Vector3 dir = Vector3.ProjectOnPlane(p1 - p0, Vector3.up);
            if (dir.sqrMagnitude > 0.0001f)
                return dir.normalized;
        }
        else if (allowWrapAcrossClipEnd)
        {
            t0 = NormalizeWrappedTime(t0, playbackController.clipDuration);
            t1 = NormalizeWrappedTime(t1, playbackController.clipDuration);
            Vector3 p0 = GetInterpolatedPosition(index, t0);
            Vector3 p1 = GetInterpolatedPosition(index, t1);
            Vector3 dir = Vector3.ProjectOnPlane(p1 - p0, Vector3.up);
            if (dir.sqrMagnitude > 0.0001f)
                return dir.normalized;
        }

        Vector3 cacheFwd = TimelineCache.Instance.GetForward(index, currentTime + syncOffsetSeconds);
        cacheFwd = Vector3.ProjectOnPlane(cacheFwd, Vector3.up);
        if (cacheFwd.sqrMagnitude > 0.0001f)
            return cacheFwd.normalized;

        Vector3 liveForward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
        return liveForward.sqrMagnitude < 0.0001f ? Vector3.forward : liveForward.normalized;
    }

    private Vector3 GetSmoothedArrowDirection(int index, Vector3 rawForward)
    {
        rawForward = Vector3.ProjectOnPlane(rawForward, Vector3.up);

        if (rawForward.sqrMagnitude < 0.0001f)
        {
            if (_smoothedArrowDirections != null &&
                index >= 0 &&
                index < _smoothedArrowDirections.Length &&
                _smoothedArrowDirections[index].sqrMagnitude > 0.0001f)
            {
                return _smoothedArrowDirections[index];
            }

            return Vector3.zero;
        }

        rawForward.Normalize();

        if (_smoothedArrowDirections == null ||
            index < 0 ||
            index >= _smoothedArrowDirections.Length)
        {
            return rawForward;
        }

        if (_smoothedArrowDirections[index].sqrMagnitude < 0.0001f)
        {
            _smoothedArrowDirections[index] = rawForward;
            return rawForward;
        }

        float t = 1f - Mathf.Exp(-arrowDirectionSmooth * Time.deltaTime);
        _smoothedArrowDirections[index] =
            Vector3.Slerp(_smoothedArrowDirections[index], rawForward, t).normalized;

        return _smoothedArrowDirections[index];
    }

    private float NormalizeWrappedTime(float time, float duration)
    {
        if (duration <= 0f)
            return time;

        if (time >= duration)
            time %= duration;
        else if (time < 0f)
            time = (time % duration + duration) % duration;

        return time;
    }

    private float XZDistanceSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private Vector3 ApplyVisualOffset(Vector3 pos)
    {
        pos.y += footOffsetY;

        if (useFixedGroundY)
        {
            pos.y = fixedGroundY + yOffset;
        }
        else
        {
            pos.y += yOffset;
        }

        return pos;
    }

    private Vector3 ApplyDiscOffset(Vector3 pos)
    {
        pos.y += footOffsetY;

        if (useFixedGroundY)
        {
            pos.y = fixedGroundY + discYOffset;
        }
        else
        {
            pos.y += discYOffset;
        }

        return pos;
    }

    private Vector3 GetLiveAnchorForLine(Transform player)
    {
        if (player == null) return Vector3.zero;

        Vector3 pos = player.position;
        pos.y += footOffsetY;

        if (useFixedGroundY)
        {
            pos.y = fixedGroundY + yOffset;
        }
        else
        {
            pos.y += yOffset;
        }

        return pos;
    }

    private Vector3 GetLiveAnchorForDisc(Transform player)
    {
        if (player == null) return Vector3.zero;

        Vector3 pos = player.position;
        pos.y += footOffsetY;

        if (useFixedGroundY)
        {
            pos.y = fixedGroundY + discYOffset;
        }
        else
        {
            pos.y += discYOffset;
        }

        return pos;
    }

    private Vector3 GetLiveAnchorForArrow(Transform player)
    {
        if (player == null) return Vector3.zero;

        Vector3 pos = player.position;
        pos.y += footOffsetY;

        if (useFixedGroundY)
        {
            pos.y = fixedGroundY + arrowYOffset;
        }
        else
        {
            pos.y += arrowYOffset;
        }

        return pos;
    }

    private void CreateSharedMaterials()
    {
        Shader lineShader = Shader.Find("Sprites/Default");
        Shader meshShader = Shader.Find("Sprites/Default");

        if (lineShader == null || meshShader == null)
        {
            Debug.LogError("[FutureLine] required shader not found");
            return;
        }

        _sharedLineMaterial = new Material(lineShader);
        _sharedDiscMaterial = new Material(lineShader);
        _sharedArrowMaterial = new Material(meshShader);
    }

    private void RebuildVisualPools()
    {
        for (int i = 0; i < _trackRenderers.Count; i++)
        {
            _trackRenderers[i].Dispose();
        }
        _trackRenderers.Clear();

        for (int i = 0; i < _discMarkers.Count; i++)
        {
            _discMarkers[i].Dispose();
        }
        _discMarkers.Clear();

        for (int i = 0; i < _arrowMarkers.Count; i++)
        {
            _arrowMarkers[i].Dispose();
        }
        _arrowMarkers.Clear();

        int totalTrackCount = includeSelectedPlayer ? 1 + nearbyPlayerCount : nearbyPlayerCount;

        for (int i = 0; i < totalTrackCount; i++)
        {
            TrackRenderer tr = new TrackRenderer();
            tr.Initialize(transform, "FutureTrack_" + i, _sharedLineMaterial, lineWidth, maxDashSegmentsPerTrack);
            _trackRenderers.Add(tr);

            DiscMarker dm = new DiscMarker();
            dm.Initialize(transform, "DiscMarker_" + i, _sharedDiscMaterial);
            _discMarkers.Add(dm);

            ArrowMarker am = new ArrowMarker();
            am.Initialize(transform, "ArrowMarker_" + i, _sharedArrowMaterial);
            _arrowMarkers.Add(am);
        }

        _smoothedArrowDirections = new Vector3[totalTrackCount];
    }

    private void EnsureLockArrays()
    {
        if (_lockedNearbyPlayers == null || _lockedNearbyPlayers.Length != nearbyPlayerCount)
        {
            _lockedNearbyPlayers = new Transform[nearbyPlayerCount];
        }

        if (_nearbyLockExpireTimes == null || _nearbyLockExpireTimes.Length != nearbyPlayerCount)
        {
            _nearbyLockExpireTimes = new float[nearbyPlayerCount];
        }
    }

    [ContextMenu("Print State")]
    public void PrintState()
    {
        Debug.Log("========== [FutureLine] State ==========");
        Debug.Log($"[FutureLine] gameObject activeInHierarchy = {gameObject.activeInHierarchy}");
        Debug.Log($"[FutureLine] component enabled = {enabled}");
        Debug.Log($"[FutureLine] playbackController null = {playbackController == null}");

        if (playbackController != null)
        {
            Debug.Log($"[FutureLine] CurrentTime = {playbackController.CurrentTime}");
            Debug.Log($"[FutureLine] clipDuration = {playbackController.clipDuration}");
            Debug.Log($"[FutureLine] selectedPlayer null = {playbackController.selectedPlayer == null}");

            if (playbackController.selectedPlayer != null && TimelineCache.Instance != null)
            {
                int idx = TimelineCache.Instance.IndexOf(playbackController.selectedPlayer);
                Debug.Log($"[FutureLine] selectedPlayer name = {playbackController.selectedPlayer.name}, trackedIndex = {idx}");
            }
        }

        Debug.Log($"[FutureLine] TimelineCache.Instance null = {TimelineCache.Instance == null}");

        if (TimelineCache.Instance != null)
        {
            Debug.Log($"[FutureLine] TimelineCache.IsReady = {TimelineCache.Instance.IsReady}");
            Debug.Log($"[FutureLine] TimelineCache.FrameCount = {TimelineCache.Instance.FrameCount}");
            Debug.Log($"[FutureLine] TimelineCache.Duration = {TimelineCache.Instance.Duration}");
        }

        Debug.Log($"[FutureLine] candidatePlayers count = {(candidatePlayers == null ? 0 : candidatePlayers.Count)}");
        Debug.Log("========== [FutureLine] State End ==========");
    }

    private void LogOnce(string key, string message)
    {
        if (!verboseDebug)
            return;

        if (_loggedKeys.Add(key))
        {
            Debug.Log(message);
        }
    }

    private struct PlayerDistance
    {
        public Transform player;
        public float sqrDistance;

        public PlayerDistance(Transform player, float sqrDistance)
        {
            this.player = player;
            this.sqrDistance = sqrDistance;
        }
    }

    private class TrackRenderer
    {
        private readonly List<LineRenderer> _segments = new List<LineRenderer>(128);
        private Transform _root;
        private Material _baseMaterial;
        private float _width;
        private int _maxDashSegments;

        public void Initialize(Transform parent, string name, Material material, float width, int maxDashSegments)
        {
            _baseMaterial = material;
            _width = width;
            _maxDashSegments = Mathf.Max(1, maxDashSegments);

            GameObject rootGo = new GameObject(name);
            _root = rootGo.transform;
            _root.SetParent(parent, false);

            EnsureSegmentCount(_maxDashSegments);
            HideAll();
        }

        public void Dispose()
        {
            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
            }

            _segments.Clear();
        }

        public void SetWidth(float width)
        {
            _width = width;
            for (int i = 0; i < _segments.Count; i++)
            {
                _segments[i].startWidth = _width;
                _segments[i].endWidth = _width;
            }
        }

        private void EnsureSegmentCount(int count)
        {
            while (_segments.Count < count)
            {
                GameObject go = new GameObject("Dash_" + _segments.Count);
                go.transform.SetParent(_root, false);

                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.startWidth = _width;
                lr.endWidth = _width;
                lr.numCapVertices = 2;
                lr.numCornerVertices = 0;
                lr.alignment = LineAlignment.View;
                lr.textureMode = LineTextureMode.Stretch;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.material = new Material(_baseMaterial);
                lr.enabled = false;

                _segments.Add(lr);
            }
        }

        public void HideAll()
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                _segments[i].enabled = false;
            }
        }

        public void DrawDashedPolyline(List<Vector3> points, Color color, float dashLength, float gapLength)
        {
            HideAll();

            if (points == null || points.Count < 2) return;
            if (dashLength <= 0f) return;
            if (gapLength < 0f) return;

            float patternLength = dashLength + gapLength;
            if (patternLength <= 0.0001f) return;

            int segIndex = 0;
            float distanceIntoPattern = 0f;

            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 a = points[i];
                Vector3 b = points[i + 1];

                float segmentLength = Vector3.Distance(a, b);
                if (segmentLength <= 0.0001f) continue;

                float traveled = 0f;

                while (traveled < segmentLength)
                {
                    bool inDash = distanceIntoPattern < dashLength;

                    float remainingPart = inDash
                        ? (dashLength - distanceIntoPattern)
                        : (patternLength - distanceIntoPattern);

                    float step = Mathf.Min(remainingPart, segmentLength - traveled);

                    float t0 = traveled / segmentLength;
                    float t1 = (traveled + step) / segmentLength;

                    Vector3 p0 = Vector3.Lerp(a, b, t0);
                    Vector3 p1 = Vector3.Lerp(a, b, t1);

                    if (inDash)
                    {
                        if (segIndex >= _segments.Count)
                            return;

                        LineRenderer lr = _segments[segIndex];
                        lr.startColor = color;
                        lr.endColor = color;
                        lr.SetPosition(0, p0);
                        lr.SetPosition(1, p1);
                        lr.enabled = true;
                        segIndex++;
                    }

                    traveled += step;
                    distanceIntoPattern += step;

                    if (distanceIntoPattern >= patternLength)
                    {
                        distanceIntoPattern = 0f;
                    }
                }
            }

            for (int i = segIndex; i < _segments.Count; i++)
            {
                _segments[i].enabled = false;
            }
        }
    }

    private class DiscMarker
    {
        private GameObject _root;
        private LineRenderer _lineRenderer;
        private Material _baseMaterial;

        public void Initialize(Transform parent, string name, Material material)
        {
            _baseMaterial = material;

            _root = new GameObject(name);
            _root.transform.SetParent(parent, false);

            _lineRenderer = _root.AddComponent<LineRenderer>();
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.loop = true;
            _lineRenderer.alignment = LineAlignment.View;
            _lineRenderer.textureMode = LineTextureMode.Stretch;
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;
            _lineRenderer.numCapVertices = 2;
            _lineRenderer.numCornerVertices = 2;
            _lineRenderer.material = new Material(_baseMaterial);
            _lineRenderer.enabled = false;
        }

        public void Show(Vector3 center, float radius, Color color, int segments, float width)
        {
            if (_lineRenderer == null) return;

            int count = Mathf.Max(8, segments);
            _lineRenderer.positionCount = count;
            _lineRenderer.startWidth = width;
            _lineRenderer.endWidth = width;
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color;

            float step = Mathf.PI * 2f / count;
            for (int i = 0; i < count; i++)
            {
                float a = step * i;
                Vector3 p = new Vector3(
                    center.x + Mathf.Cos(a) * radius,
                    center.y,
                    center.z + Mathf.Sin(a) * radius
                );
                _lineRenderer.SetPosition(i, p);
            }

            _lineRenderer.enabled = true;
        }

        public void Hide()
        {
            if (_lineRenderer != null)
            {
                _lineRenderer.enabled = false;
            }
        }

        public void Dispose()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }
        }
    }

    private class ArrowMarker
    {
        private GameObject _root;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Material _materialInstance;
        private Mesh _mesh;

        private float _lastLength = -1f;
        private float _lastWidth = -1f;
        private float _lastBackWidth = -1f;
        private float _lastNotchDepth = -1f;
        private float _lastShoulderRatio = -1f;

        public void Initialize(Transform parent, string name, Material material)
        {
            _root = new GameObject(name);
            _root.transform.SetParent(parent, false);

            _meshFilter = _root.AddComponent<MeshFilter>();
            _meshRenderer = _root.AddComponent<MeshRenderer>();

            _materialInstance = new Material(material);
            _meshRenderer.material = _materialInstance;
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;

            _mesh = new Mesh();
            _mesh.name = name + "_Mesh";
            _meshFilter.sharedMesh = _mesh;

            _root.SetActive(false);
        }

        public void Show(
            Vector3 center,
            Vector3 forward,
            float discRadius,
            float arrowGap,
            float arrowLength,
            float arrowWidth,
            float arrowBackWidth,
            float arrowNotchDepth,
            float arrowShoulderRatio,
            Color color)
        {
            if (_root == null || _mesh == null)
                return;

            if (forward.sqrMagnitude < 0.0001f)
            {
                Hide();
                return;
            }

            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.0001f)
            {
                Hide();
                return;
            }

            UpdateMeshIfNeeded(
                arrowLength,
                arrowWidth,
                arrowBackWidth,
                arrowNotchDepth,
                arrowShoulderRatio
            );

            Vector3 anchor = center + forward * (discRadius + arrowGap);

            _root.transform.position = anchor;
            _root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            _materialInstance.color = color;
            _root.SetActive(true);
        }

        private void UpdateMeshIfNeeded(
            float arrowLength,
            float arrowWidth,
            float arrowBackWidth,
            float arrowNotchDepth,
            float arrowShoulderRatio)
        {
            if (Mathf.Approximately(_lastLength, arrowLength) &&
                Mathf.Approximately(_lastWidth, arrowWidth) &&
                Mathf.Approximately(_lastBackWidth, arrowBackWidth) &&
                Mathf.Approximately(_lastNotchDepth, arrowNotchDepth) &&
                Mathf.Approximately(_lastShoulderRatio, arrowShoulderRatio))
            {
                return;
            }

            _lastLength = arrowLength;
            _lastWidth = arrowWidth;
            _lastBackWidth = arrowBackWidth;
            _lastNotchDepth = arrowNotchDepth;
            _lastShoulderRatio = arrowShoulderRatio;

            float shoulderZ = arrowLength * arrowShoulderRatio;

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(0f, 0f, arrowNotchDepth),                // 0: 尾部凹口内点
                new Vector3(arrowBackWidth * 0.5f, 0f, 0f),         // 1: 右后角
                new Vector3(arrowBackWidth * 0.5f, 0f, shoulderZ),  // 2: 右过渡
                new Vector3(arrowWidth * 0.5f, 0f, shoulderZ),      // 3: 右肩
                new Vector3(0f, 0f, arrowLength),                   // 4: 尖端
                new Vector3(-arrowWidth * 0.5f, 0f, shoulderZ),     // 5: 左肩
                new Vector3(-arrowBackWidth * 0.5f, 0f, shoulderZ), // 6: 左过渡
                new Vector3(-arrowBackWidth * 0.5f, 0f, 0f),        // 7: 左后角
            };

            int[] triangles = new int[]
            {
                0, 1, 2,
                0, 2, 3,
                0, 3, 4,
                0, 4, 5,
                0, 5, 6,
                0, 6, 7
            };

            Vector2[] uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                uv[i] = new Vector2(vertices[i].x, vertices[i].z);
            }

            _mesh.Clear();
            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.uv = uv;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        public void Hide()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        public void Dispose()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }
        }
    }
}