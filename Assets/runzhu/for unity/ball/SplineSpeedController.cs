// SplineSpeedController.cs
// Unity 中使用曲线控制 Spline Animate 速度（横轴为时间）

using UnityEngine;
using UnityEngine.Splines;

[RequireComponent(typeof(SplineAnimate))]
public class SplineSpeedController : MonoBehaviour
{
    private SplineAnimate splineAnimate;
    private float totalDuration;
    
    [Header("速度曲线设置")]
    public AnimationCurve speedCurve = AnimationCurve.Linear(0, 1, 5, 1);  // 横轴为时间（秒）
    public bool enableCurve = true;
    public float baseSpeed = 1f;
    
    [Header("调试")]
    public bool showDebugLog = false;
    
    void Start()
    {
        splineAnimate = GetComponent<SplineAnimate>();
        
        // 获取动画总时长
        if (splineAnimate != null)
        {
            totalDuration = splineAnimate.Duration;  // Duration 是大写 D
            Debug.Log($"动画总时长: {totalDuration} 秒");
        }
    }
    
    void Update()
    {
        if (splineAnimate == null || !enableCurve) return;
        
        // 获取当前动画播放的时间（秒）- 使用 ElapsedTime 而不是 CurrentTime
        float currentTime = splineAnimate.ElapsedTime;  // ✅ 正确的属性名
        
        // 从曲线获取当前时间对应的速度倍数
        float speedMultiplier = speedCurve.Evaluate(currentTime);
        
        // 应用速度 - MaxSpeed 是大写 M
        splineAnimate.MaxSpeed = baseSpeed * speedMultiplier;
        
        if (showDebugLog)
        {
            Debug.Log($"当前时间: {currentTime:F2}秒, 速度倍数: {speedMultiplier:F2}, 当前速度: {splineAnimate.MaxSpeed:F2}");
        }
    }
    
    // 添加速度控制点
    public void AddSpeedPoint(float timeInSeconds, float speedValue)
    {
        speedCurve.AddKey(timeInSeconds, speedValue);
        Debug.Log($"添加速度点: 时间={timeInSeconds}秒, 速度倍数={speedValue}");
    }
    
    // 重置为匀速
    public void ResetToConstant(float constantSpeed = 1f)
    {
        speedCurve = AnimationCurve.Linear(0, constantSpeed, totalDuration, constantSpeed);
    }
    
    // 启用/禁用
    public void ToggleCurve(bool enable)
    {
        enableCurve = enable;
    }
    
    // 获取当前速度倍数
    public float GetCurrentSpeedMultiplier()
    {
        if (splineAnimate == null) return 1f;
        return speedCurve.Evaluate(splineAnimate.ElapsedTime);
    }
}