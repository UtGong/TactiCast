using UnityEngine;

public class FollowPointPath : MonoBehaviour
{
    [Header("路径设置")]
    public MeshFilter pathMesh;           // 拖入你的点组成的Mesh
    public bool loop = true;              // 是否循环
    public float speed = 2f;              // 基础速度
    public AnimationCurve speedCurve;     // 可选速度曲线 (0~1 时间映射速度倍率)

    private Vector3[] points;
    private int currentIndex = 0;
    private float t = 0f;                 // 当前段插值进度

    void Start()
    {
        if (pathMesh == null)
        {
            Debug.LogError("Path Mesh 未设置！");
            enabled = false;
            return;
        }

        // 获取顶点并转换为世界坐标
        points = pathMesh.sharedMesh.vertices;
        if (points.Length < 2)
        {
            Debug.LogError("路径顶点数量不足！");
            enabled = false;
            return;
        }

        for (int i = 0; i < points.Length; i++)
        {
            points[i] = pathMesh.transform.TransformPoint(points[i]);
        }

        // 把球放到路径起点
        transform.position = points[0];

        // 如果速度曲线没设置，创建一个默认匀速
        if (speedCurve == null || speedCurve.length == 0)
        {
            speedCurve = AnimationCurve.Linear(0, 1, 1, 1);
        }
    }

    void Update()
    {
        if (points.Length < 2) return;

        Vector3 start = points[currentIndex];
        Vector3 end = points[currentIndex + 1];

        // 速度调节
        float segmentSpeed = speed * speedCurve.Evaluate(t);

        // 沿当前段移动
        t += (segmentSpeed / Vector3.Distance(start, end)) * Time.deltaTime;
        t = Mathf.Clamp01(t);

        transform.position = Vector3.Lerp(start, end, t);

        // 自动朝向前进方向
        Vector3 dir = end - transform.position;
        if (dir != Vector3.zero)
        {
            transform.forward = dir.normalized;
        }

        // 到达当前段终点
        if (t >= 1f)
        {
            t = 0f;
            currentIndex++;

            if (currentIndex >= points.Length - 1)
            {
                if (loop)
                    currentIndex = 0;
                else
                    enabled = false; // 到终点停止
            }
        }
    }
}