using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class VRPipController : MonoBehaviour
{
    [Header("核心引用")]
    [SerializeField] private Transform viewer;             // VR 相机 / HMD
    [SerializeField] private Transform targetBall;         // 需要指向的 ball
    [SerializeField] private RectTransform pipRoot;        
    [SerializeField] private RectTransform pipFrame;       
    [SerializeField] private RectTransform circleMask;     
    [SerializeField] private RectTransform rawImageRect;   
    [SerializeField] private RectTransform arrowPivot;     

    [Header("PIP 是否始终朝向玩家")]
    [SerializeField] private bool keepFacingViewer = true;
    [SerializeField] private bool yawOnly = false;         // true: 只绕Y轴朝向玩家；false: 完整朝向玩家

    [Header("CircleMask 锁定参数")]
    [SerializeField] private Vector2 maskSize = new Vector2(180f, 180f);
    [SerializeField] private Vector2 maskOffset = Vector2.zero;
    [SerializeField] private float maskLocalZ = 0f;       

    [Header("Arrow 旋转参数")]
    [SerializeField] private float arrowAngleOffset = 0f;  
                                                           
                                                           
    private void Reset()
    {
        if (viewer == null && Camera.main != null)
        {
            viewer = Camera.main.transform;
        }

        if (pipRoot == null)
        {
            pipRoot = transform as RectTransform;
        }
    }

    private void LateUpdate()
    {
        // 运行时自动补主相机，避免忘记拖引用
        if (viewer == null && Camera.main != null)
        {
            viewer = Camera.main.transform;
        }

        // 1. 让整个 PIP 面板在 VR 中保持“正面”
        if (keepFacingViewer)
        {
            UpdateBillboard();
        }

        // 2. 强制锁死 CircleMask 的位置、旋转、缩放
        LockCircleMaskToFrame();

        // 3. 强制锁死 RawImage，避免内部纹理容器乱偏
        LockRawImageToMask();

        // 4. 只旋转箭头，不旋转整个 PipFrame
        UpdateArrowDirection();
    }

    /// <summary>
    /// 让整个 PIP 面板朝向玩家，避免上层 Canvas 旋转导致它歪掉。
    /// </summary>
    private void UpdateBillboard()
    {
        if (pipRoot == null || viewer == null) return;

        Vector3 toViewer = viewer.position - pipRoot.position;
        if (toViewer.sqrMagnitude < 0.0001f) return;

        if (yawOnly)
        {
            toViewer.y = 0f;
            if (toViewer.sqrMagnitude < 0.0001f) return;

            pipRoot.rotation = Quaternion.LookRotation(toViewer.normalized, Vector3.up);
        }
        else
        {
            // 完整朝向玩家
            pipRoot.rotation = Quaternion.LookRotation(toViewer.normalized, viewer.up);
        }
    }

    /// <summary>
    /// 把 CircleMask 锁死在 PipFrame 正中央，并保持与其平行贴合。
    /// </summary>
    private void LockCircleMaskToFrame()
    {
        if (pipFrame == null || circleMask == null) return;

        // 如果层级错了，自动纠正成 PipFrame 的子物体
        if (circleMask.parent != pipFrame)
        {
            circleMask.SetParent(pipFrame, false);
        }

        // 中心锚点，避免父物体旋转时产生奇怪偏移
        circleMask.anchorMin = new Vector2(0.5f, 0.5f);
        circleMask.anchorMax = new Vector2(0.5f, 0.5f);
        circleMask.pivot = new Vector2(0.5f, 0.5f);

        // 大小、位置、旋转、缩放全部锁定
        //circleMask.sizeDelta = maskSize;
        circleMask.anchoredPosition = maskOffset;
        circleMask.localRotation = Quaternion.identity;
        circleMask.localScale = Vector3.one;
        circleMask.anchoredPosition3D = new Vector3(maskOffset.x, maskOffset.y, maskLocalZ);
    }

    /// <summary>
    /// 让 RawImage 永远填满 CircleMask。
    /// </summary>
    private void LockRawImageToMask()
    {
        if (circleMask == null || rawImageRect == null) return;

        if (rawImageRect.parent != circleMask)
        {
            rawImageRect.SetParent(circleMask, false);
        }

        rawImageRect.anchorMin = Vector2.zero;
        rawImageRect.anchorMax = Vector2.one;
        rawImageRect.pivot = new Vector2(0.5f, 0.5f);
        rawImageRect.offsetMin = Vector2.zero;
        rawImageRect.offsetMax = Vector2.zero;
        rawImageRect.localRotation = Quaternion.identity;
        rawImageRect.localScale = Vector3.one;
        rawImageRect.anchoredPosition3D = Vector3.zero;
    }

    /// <summary>
    /// 根据 ball 相对 PipFrame 的方向，只旋转箭头。
    /// 这里是在 PipFrame 的局部平面（XY 平面）里算方向角。
    /// </summary>
    private void UpdateArrowDirection()
    {
        if (targetBall == null || pipFrame == null || arrowPivot == null) return;

        Vector3 worldDir = targetBall.position - pipFrame.position;
        if (worldDir.sqrMagnitude < 0.0001f) return;

        // 转成 PipFrame 的局部方向
        Vector3 localDir = pipFrame.InverseTransformDirection(worldDir.normalized);

        // UI 平面默认看 XY，Z 垂直屏幕
        Vector2 dir2D = new Vector2(localDir.x, localDir.y);
        if (dir2D.sqrMagnitude < 0.0001f) return;

        float angle = Mathf.Atan2(dir2D.y, dir2D.x) * Mathf.Rad2Deg;

        // 只转 Z 轴，让箭头在平面内旋转
        arrowPivot.localRotation = Quaternion.Euler(0f, 0f, angle + arrowAngleOffset);
    }
}