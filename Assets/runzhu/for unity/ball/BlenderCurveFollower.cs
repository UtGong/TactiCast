// BlenderCurveFollower.cs
// 让物体跟随Blender导入的曲线运动，支持速度曲线控制

using UnityEngine;
using System.Collections.Generic;

public class BlenderCurveFollower : MonoBehaviour
{
    [Header("曲线设置")]
    public Transform curveObject;           // Blender导入的曲线对象（FBX）
    public bool loop = true;                 // 是否循环
    public bool reverse = false;              // 是否反向运动
    
    [Header("速度控制")]
    public AnimationCurve speedCurve = AnimationCurve.Linear(0, 1, 10, 1);  // 横轴：时间（秒）
    public float baseSpeed = 1f;               // 基础速度
    public bool enableCurveControl = true;      // 是否启用曲线控制
    
    [Header("运动状态")]
    [Range(0, 1)]
    public float debugProgress = 0f;            // 调试用进度
    
    [Header("调试")]
    public bool showDebugLog = false;
    public bool drawGizmos = true;               // 是否绘制路径Gizmos
    
    private List<Vector3> curvePoints = new List<Vector3>();  // 曲线上的点
    private float totalLength = 0f;               // 曲线总长度
    private float currentDistance = 0f;            // 当前走过的距离
    private float currentTime = 0f;                 // 当前时间
    
    void Start()
    {
        InitializeCurve();
    }
    
    void InitializeCurve()
    {
        if (curveObject == null)
        {
            Debug.LogError("请指定曲线对象！");
            return;
        }
        
        // 获取曲线上的所有点
        GetCurvePoints();
        
        if (curvePoints.Count < 2)
        {
            Debug.LogError("曲线点不足，请检查曲线是否有效");
            return;
        }
        
        // 计算曲线总长度
        CalculateTotalLength();
        
        Debug.Log($"曲线初始化完成：{curvePoints.Count}个点，总长度：{totalLength:F2}");
    }
    
    void GetCurvePoints()
    {
        curvePoints.Clear();
        
        // 方法1：尝试获取LineRenderer组件（FBX导入曲线通常会有）
        LineRenderer lineRenderer = curveObject.GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            Vector3[] positions = new Vector3[lineRenderer.positionCount];
            lineRenderer.GetPositions(positions);
            
            // 转换到世界坐标
            foreach (Vector3 localPos in positions)
            {
                curvePoints.Add(curveObject.TransformPoint(localPos));
            }
            return;
        }
        
        // 方法2：尝试获取Mesh（如果是管道形状）
        MeshFilter meshFilter = curveObject.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.mesh != null)
        {
            // 用顶点近似曲线
            Vector3[] vertices = meshFilter.mesh.vertices;
            if (vertices.Length > 0)
            {
                // 按顺序排列顶点（假设Blender导出的曲线顶点是有序的）
                foreach (Vector3 localPos in vertices)
                {
                    curvePoints.Add(curveObject.TransformPoint(localPos));
                }
                return;
            }
        }
        
        // 方法3：尝试获取所有子物体（如果是空物体组成的路径）
        foreach (Transform child in curveObject)
        {
            curvePoints.Add(child.position);
        }
        
        if (curvePoints.Count == 0)
        {
            // 方法4：手动采样曲线（如果上面都不行）
            SampleCurveManually();
        }
    }
    
    void SampleCurveManually()
    {
        // 尝试通过LineRenderer组件手动创建
        LineRenderer lr = curveObject.GetComponent<LineRenderer>();
        if (lr == null)
        {
            lr = curveObject.gameObject.AddComponent<LineRenderer>();
        }
        
        // 默认创建100个点
        int sampleCount = 100;
        lr.positionCount = sampleCount;
        
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / (sampleCount - 1);
            // 这里需要根据你的曲线类型调整
            // 如果是贝塞尔曲线，需要更复杂的采样
            Vector3 point = curveObject.position + new Vector3(t * 10f, Mathf.Sin(t * Mathf.PI * 2), 0);
            lr.SetPosition(i, point);
            curvePoints.Add(curveObject.TransformPoint(point));
        }
    }
    
    void CalculateTotalLength()
    {
        totalLength = 0f;
        for (int i = 0; i < curvePoints.Count - 1; i++)
        {
            totalLength += Vector3.Distance(curvePoints[i], curvePoints[i + 1]);
        }
    }
    
    void Update()
    {
        if (curvePoints.Count < 2) return;
        
        // 更新时间（基于速度曲线）
        float speedMultiplier = enableCurveControl ? speedCurve.Evaluate(currentTime) : 1f;
        float deltaDistance = baseSpeed * speedMultiplier * Time.deltaTime;
        
        // 更新当前距离
        currentDistance += reverse ? -deltaDistance : deltaDistance;
        
        // 处理循环/边界
        if (loop)
        {
            if (currentDistance > totalLength)
            {
                currentDistance -= totalLength;
                currentTime = 0f;  // 重置时间
            }
            else if (currentDistance < 0)
            {
                currentDistance += totalLength;
                currentTime = 0f;
            }
        }
        else
        {
            currentDistance = Mathf.Clamp(currentDistance, 0, totalLength);
        }
        
        // 更新时间（用于速度曲线采样）
        currentTime += Time.deltaTime;
        
        // 根据距离获取位置
        Vector3 targetPosition = GetPositionAtDistance(currentDistance);
        transform.position = targetPosition;
        
        // 可选：自动旋转以面向运动方向
        AutoRotate();
        
        // 更新调试进度
        debugProgress = currentDistance / totalLength;
        
        if (showDebugLog)
        {
            Debug.Log($"时间: {currentTime:F2}, 速度倍数: {speedMultiplier:F2}, 进度: {debugProgress:P2}");
        }
    }
    
    Vector3 GetPositionAtDistance(float distance)
    {
        if (distance <= 0) return curvePoints[0];
        if (distance >= totalLength) return curvePoints[curvePoints.Count - 1];
        
        float accumulatedDistance = 0f;
        
        for (int i = 0; i < curvePoints.Count - 1; i++)
        {
            float segmentLength = Vector3.Distance(curvePoints[i], curvePoints[i + 1]);
            
            if (distance <= accumulatedDistance + segmentLength)
            {
                float t = (distance - accumulatedDistance) / segmentLength;
                return Vector3.Lerp(curvePoints[i], curvePoints[i + 1], t);
            }
            
            accumulatedDistance += segmentLength;
        }
        
        return curvePoints[curvePoints.Count - 1];
    }
    
    void AutoRotate()
    {
        if (!enableCurveControl) return;
        
        // 获取运动方向
        Vector3 direction = GetDirectionAtDistance(currentDistance);
        if (direction.magnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }
    }
    
    Vector3 GetDirectionAtDistance(float distance)
    {
        float epsilon = 0.1f;
        Vector3 currentPos = GetPositionAtDistance(distance);
        
        float nextDistance = distance + epsilon;
        if (nextDistance > totalLength)
        {
            nextDistance = loop ? nextDistance - totalLength : totalLength;
        }
        
        Vector3 nextPos = GetPositionAtDistance(nextDistance);
        return (nextPos - currentPos).normalized;
    }
    
    // 重置位置到起点
    public void ResetToStart()
    {
        currentDistance = 0f;
        currentTime = 0f;
        transform.position = curvePoints[0];
    }
    
    // 设置进度（0-1）
    public void SetProgress(float progress)
    {
        progress = Mathf.Clamp01(progress);
        currentDistance = progress * totalLength;
    }
    
    // 获取当前进度
    public float GetProgress()
    {
        return currentDistance / totalLength;
    }
    
    // 在Scene视图中绘制路径
    void OnDrawGizmos()
    {
        if (!drawGizmos || curvePoints.Count < 2) return;
        
        Gizmos.color = Color.yellow;
        for (int i = 0; i < curvePoints.Count - 1; i++)
        {
            Gizmos.DrawLine(curvePoints[i], curvePoints[i + 1]);
        }
        
        // 绘制控制点
        Gizmos.color = Color.red;
        foreach (Vector3 point in curvePoints)
        {
            Gizmos.DrawSphere(point, 0.1f);
        }
        
        // 绘制当前位置
        if (Application.isPlaying)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(transform.position, 0.2f);
        }
    }
}