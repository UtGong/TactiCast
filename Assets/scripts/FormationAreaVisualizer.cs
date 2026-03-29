using System.Collections.Generic;
using UnityEngine;

public class FormationAreaVisualizer : MonoBehaviour
{
    [Header("Teams")]
    public List<Transform> team1Players = new List<Transform>();
    public List<Transform> team2Players = new List<Transform>();

    [Header("Rendering - Team 1")]
    public Color team1LineColor = new Color(0.2f, 0.6f, 1f, 1f);
    public Color team1FillColor = new Color(0.2f, 0.6f, 1f, 0.2f);
    public float team1LineWidth = 0.03f;

    [Header("Rendering - Team 2")]
    public Color team2LineColor = new Color(1f, 0.25f, 0.25f, 1f);
    public Color team2FillColor = new Color(1f, 0.25f, 0.25f, 0.2f);
    public float team2LineWidth = 0.03f;

    [Header("Edge Alpha by Distance")]
    public float minDistance = 3f;
    public float maxDistance = 20f;
    [Range(0f, 1f)]
    public float minAlpha = 0.15f;

    [Header("Projection Height")]
    public float yOffset = 0.02f;

    private TeamRender _t1, _t2;

    private void Awake()
    {
        _t1 = CreateTeamRender("Team1_Area", team1LineColor, team1FillColor, team1LineWidth);
        _t2 = CreateTeamRender("Team2_Area", team2LineColor, team2FillColor, team2LineWidth);
    }

    private void Update()
    {
        UpdateTeam(_t1, team1Players, team1LineColor, team1LineWidth);
        UpdateTeam(_t2, team2Players, team2LineColor, team2LineWidth);
    }

    // -------------------------------------------------------------------------

    private void UpdateTeam(TeamRender tr, List<Transform> players, Color baseColor, float lineWidth)
    {
        var pts = tr.WorkPoints;
        pts.Clear();

        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] == null) continue;
            Vector3 pos = players[i].position;
            pts.Add(new Vector3(pos.x, pos.y + yOffset, pos.z));
        }

        if (pts.Count < 2) { tr.SetFillVisible(false); tr.HideAllEdges(); return; }
        if (pts.Count < 3)
        {
            tr.SetFillVisible(false);
            tr.EnsureEdgeCount(1, lineWidth);
            SetEdge(tr.EdgeRenderers[0], pts[0], pts[1], baseColor, 1f);
            tr.HideEdgesFrom(1);
            tr.Mesh.Clear();
            return;
        }

        var sorted = new List<Vector3>(pts);
        SortByAngle(sorted);

        List<Vector3> boundary;
        if (!HasSelfIntersection(sorted))
        {
            boundary = sorted;
        }
        else
        {
            var buf = tr.BoundaryBuffer;
            buf.Clear();
            AlphaShape(pts, AutoRadius(pts), buf);
            if (buf.Count < 3) { buf.Clear(); ConvexHull(pts, buf); }
            boundary = buf.Count >= 3 ? buf : sorted;
        }

        int n = boundary.Count;
        tr.SetFillVisible(true);
        FillMesh(tr.Mesh, boundary);
        tr.EnsureEdgeCount(n, lineWidth);

        for (int i = 0; i < n; i++)
        {
            Vector3 a = boundary[i], b = boundary[(i + 1) % n];
            float t = Mathf.InverseLerp(minDistance, maxDistance, XZDist(a, b));
            float alpha = Mathf.Lerp(1f, minAlpha, t);
            SetEdge(tr.EdgeRenderers[i], a, b, baseColor, alpha);
        }
        tr.HideEdgesFrom(n);
    }

    // -------------------------------------------------------------------------
    // Geometry helpers
    // -------------------------------------------------------------------------

    static void SetEdge(LineRenderer lr, Vector3 a, Vector3 b, Color c, float alpha)
    {
        Color col = c; col.a = alpha;
        lr.startColor = col; lr.endColor = col;
        lr.SetPosition(0, a); lr.SetPosition(1, b);
        lr.enabled = true;
    }

    static float XZDist(Vector3 a, Vector3 b)
    { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

    static void FillMesh(Mesh mesh, List<Vector3> pts)
    {
        int n = pts.Count;
        var v = new Vector3[n]; for (int i = 0; i < n; i++) v[i] = pts[i];
        var t = new int[(n - 2) * 3]; int ti = 0;
        for (int i = 1; i < n - 1; i++) { t[ti++] = 0; t[ti++] = i + 1; t[ti++] = i; } // reversed winding = normals up
        mesh.Clear(); mesh.vertices = v; mesh.triangles = t;
        var uv = new Vector2[n]; for (int i = 0; i < n; i++) uv[i] = new Vector2(v[i].x, v[i].z);
        mesh.uv = uv; mesh.RecalculateNormals(); mesh.RecalculateBounds();
    }

    static void SortByAngle(List<Vector3> pts)
    {
        float cx = 0, cz = 0; foreach (var p in pts) { cx += p.x; cz += p.z; }
        cx /= pts.Count; cz /= pts.Count;
        pts.Sort((a, b) => Mathf.Atan2(a.z - cz, a.x - cx).CompareTo(Mathf.Atan2(b.z - cz, b.x - cx)));
    }

    static bool HasSelfIntersection(List<Vector3> pts)
    {
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            var a0 = pts[i]; var a1 = pts[(i + 1) % n];
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                if (SegCross(a0, a1, pts[j], pts[(j + 1) % n])) return true;
            }
        }
        return false;
    }

    static bool SegCross(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
    {
        float d1 = Cross(p3, p4, p1), d2 = Cross(p3, p4, p2);
        float d3 = Cross(p1, p2, p3), d4 = Cross(p1, p2, p4);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    static float Cross(Vector3 o, Vector3 a, Vector3 b)
        => (a.x - o.x) * (b.z - o.z) - (a.z - o.z) * (b.x - o.x);

    static float AutoRadius(List<Vector3> pts)
    {
        int n = pts.Count; if (n <= 1) return 1f;
        float total = 0; int cnt = 0;
        for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) { total += XZDist(pts[i], pts[j]); cnt++; }
        return (total / cnt) * 1.2f;
    }

    static void AlphaShape(List<Vector3> pts, float alpha, List<Vector3> result)
    {
        var tris = Delaunay(pts);
        var kept = new List<Tri>();
        foreach (var t in tris) if (Circumradius(t.a, t.b, t.c) <= alpha) kept.Add(t);
        if (kept.Count == 0) return;
        var ec = new Dictionary<Ed, int>(new EdCmp());
        foreach (var t in kept) { Count(ec, new Ed(t.a, t.b)); Count(ec, new Ed(t.b, t.c)); Count(ec, new Ed(t.c, t.a)); }
        var be = new List<Ed>(); foreach (var kv in ec) if (kv.Value == 1) be.Add(kv.Key);
        if (be.Count == 0) return;
        Chain(be, result);
    }

    static void Count(Dictionary<Ed, int> d, Ed e)
        => d[e] = d.TryGetValue(e, out int c) ? c + 1 : 1;

    static void Chain(List<Ed> edges, List<Vector3> result)
    {
        var adj = new Dictionary<Vector3, List<Vector3>>(new V3Cmp());
        foreach (var e in edges)
        {
            if (!adj.ContainsKey(e.a)) adj[e.a] = new List<Vector3>();
            if (!adj.ContainsKey(e.b)) adj[e.b] = new List<Vector3>();
            adj[e.a].Add(e.b); adj[e.b].Add(e.a);
        }
        var start = edges[0].a; var prev = start; var cur = edges[0].b; result.Add(start);
        int safety = edges.Count * 2 + 4;
        while (safety-- > 0)
        {
            result.Add(cur); if (!adj.ContainsKey(cur)) break;
            Vector3 next = cur; bool found = false;
            foreach (var nb in adj[cur]) if (!V3Eq(nb, prev)) { next = nb; found = true; break; }
            if (!found || V3Eq(next, start)) break;
            prev = cur; cur = next;
        }
    }

    static List<Tri> Delaunay(List<Vector3> pts)
    {
        var tri = new List<Tri>();
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var p in pts) { if (p.x < x0) x0 = p.x; if (p.x > x1) x1 = p.x; if (p.z < z0) z0 = p.z; if (p.z > z1) z1 = p.z; }
        float d = Mathf.Max(x1 - x0, z1 - z0) * 10f, mx = (x0 + x1) * .5f, mz = (z0 + z1) * .5f, y = pts[0].y;
        var s0 = new Vector3(mx - d, y, mz - d); var s1 = new Vector3(mx, y, mz + d); var s2 = new Vector3(mx + d, y, mz - d);
        tri.Add(new Tri(s0, s1, s2));
        var bad = new List<Tri>(); var poly = new List<Ed>();
        foreach (var p in pts)
        {
            bad.Clear(); foreach (var t in tri) if (InCircum(t, p)) bad.Add(t);
            poly.Clear(); foreach (var t in bad) { AddBE(poly, bad, new Ed(t.a, t.b)); AddBE(poly, bad, new Ed(t.b, t.c)); AddBE(poly, bad, new Ed(t.c, t.a)); }
            foreach (var t in bad) tri.Remove(t);
            foreach (var e in poly) tri.Add(new Tri(e.a, e.b, p));
        }
        tri.RemoveAll(t => V3Eq(t.a, s0) || V3Eq(t.a, s1) || V3Eq(t.a, s2) || V3Eq(t.b, s0) || V3Eq(t.b, s1) || V3Eq(t.b, s2) || V3Eq(t.c, s0) || V3Eq(t.c, s1) || V3Eq(t.c, s2));
        return tri;
    }

    static void AddBE(List<Ed> poly, List<Tri> bad, Ed e)
    { int c = 0; foreach (var t in bad) if (HasEd(t, e)) c++; if (c == 1) poly.Add(e); }

    static bool InCircum(Tri t, Vector3 p)
    {
        double ax = t.a.x - p.x, az = t.a.z - p.z, bx = t.b.x - p.x, bz = t.b.z - p.z, cx = t.c.x - p.x, cz = t.c.z - p.z;
        return ax * (bz * (cx * cx + cz * cz) - cz * (bx * bx + bz * bz)) - az * (bx * (cx * cx + cz * cz) - cx * (bx * bx + bz * bz)) + (ax * ax + az * az) * (bx * cz - bz * cx) > 0;
    }

    static float Circumradius(Vector3 a, Vector3 b, Vector3 c)
    {
        float ax = b.x - a.x, az = b.z - a.z, bx = c.x - a.x, bz = c.z - a.z, D = 2f * (ax * bz - az * bx);
        if (Mathf.Abs(D) < 1e-6f) return float.MaxValue;
        float ux = (bz * (ax * ax + az * az) - az * (bx * bx + bz * bz)) / D, uz = (ax * (bx * bx + bz * bz) - bx * (ax * ax + az * az)) / D;
        return Mathf.Sqrt(ux * ux + uz * uz);
    }

    static bool HasEd(Tri t, Ed e) =>
        (V3Eq(t.a, e.a) && V3Eq(t.b, e.b)) || (V3Eq(t.b, e.a) && V3Eq(t.a, e.b)) ||
        (V3Eq(t.b, e.a) && V3Eq(t.c, e.b)) || (V3Eq(t.c, e.a) && V3Eq(t.b, e.b)) ||
        (V3Eq(t.c, e.a) && V3Eq(t.a, e.b)) || (V3Eq(t.a, e.a) && V3Eq(t.c, e.b));

    static void ConvexHull(List<Vector3> pts, List<Vector3> hull)
    {
        var s = new List<Vector3>(pts); s.Sort((a, b) => { int c = a.x.CompareTo(b.x); return c != 0 ? c : a.z.CompareTo(b.z); });
        var lo = new List<Vector3>(); foreach (var p in s) { while (lo.Count >= 2 && Cross(lo[lo.Count - 2], lo[lo.Count - 1], p) <= 0) lo.RemoveAt(lo.Count - 1); lo.Add(p); }
        var hi = new List<Vector3>(); for (int i = s.Count - 1; i >= 0; i--) { var p = s[i]; while (hi.Count >= 2 && Cross(hi[hi.Count - 2], hi[hi.Count - 1], p) <= 0) hi.RemoveAt(hi.Count - 1); hi.Add(p); }
        for (int i = 0; i < lo.Count - 1; i++) hull.Add(lo[i]);
        for (int i = 0; i < hi.Count - 1; i++) hull.Add(hi[i]);
    }

    static bool V3Eq(Vector3 a, Vector3 b, float eps = 0.001f)
        => (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z) < eps * eps;
    static int HXZ(Vector3 v)
        => Mathf.RoundToInt(v.x * 100) * 73856093 ^ Mathf.RoundToInt(v.z * 100) * 19349663;

    struct Tri { public Vector3 a, b, c; public Tri(Vector3 a, Vector3 b, Vector3 c) { this.a = a; this.b = b; this.c = c; } }
    struct Ed { public Vector3 a, b; public Ed(Vector3 a, Vector3 b) { this.a = a; this.b = b; } }
    class EdCmp : IEqualityComparer<Ed>
    {
        public bool Equals(Ed x, Ed y) => (V3Eq(x.a, y.a) && V3Eq(x.b, y.b)) || (V3Eq(x.a, y.b) && V3Eq(x.b, y.a));
        public int GetHashCode(Ed e) { int h0 = HXZ(e.a), h1 = HXZ(e.b); return h0 < h1 ? h0 ^ (h1 * 397) : h1 ^ (h0 * 397); }
    }
    class V3Cmp : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y) => V3Eq(x, y);
        public int GetHashCode(Vector3 v) => HXZ(v);
    }

    // -------------------------------------------------------------------------
    // TeamRender
    // -------------------------------------------------------------------------

    class TeamRender
    {
        public Mesh Mesh;
        public readonly List<Vector3> WorkPoints = new List<Vector3>(16);
        public readonly List<Vector3> BoundaryBuffer = new List<Vector3>(32);
        public readonly List<LineRenderer> EdgeRenderers = new List<LineRenderer>();
        private GameObject _edgeRoot, _fillGo;
        private Material _lineMat;

        public void Init(GameObject er, GameObject fg, Material lm)
        { _edgeRoot = er; _fillGo = fg; _lineMat = lm; }

        public void EnsureEdgeCount(int count, float width)
        {
            while (EdgeRenderers.Count < count)
            {
                var go = new GameObject("Edge_" + EdgeRenderers.Count);
                go.transform.SetParent(_edgeRoot.transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true; lr.positionCount = 2; lr.numCapVertices = 2;
                lr.material = new Material(_lineMat); lr.enabled = false;
                EdgeRenderers.Add(lr);
            }
            foreach (var lr in EdgeRenderers) { lr.startWidth = width; lr.endWidth = width; }
        }

        public void HideAllEdges()
        { foreach (var lr in EdgeRenderers) lr.enabled = false; }

        public void HideEdgesFrom(int idx)
        { for (int i = idx; i < EdgeRenderers.Count; i++) EdgeRenderers[i].enabled = false; }

        public void SetFillVisible(bool v)
        { if (_fillGo) _fillGo.GetComponent<MeshRenderer>().enabled = v; }
    }

    static Material CreateTransparentMaterial(Color color)
    {
        var mat = new Material(Shader.Find("Standard"));
        mat.SetFloat("_Mode", 3);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_Cull", 0); // disable backface culling ¡ª show both sides
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        mat.color = color;
        return mat;
    }

    TeamRender CreateTeamRender(string name, Color lineColor, Color fillColor, float lineWidth)
    {
        var tr = new TeamRender();
        var root = new GameObject(name); root.transform.SetParent(this.transform, false);
        var er = new GameObject("Edges"); er.transform.SetParent(root.transform, false);
        var fg = new GameObject("FillMesh"); fg.transform.SetParent(root.transform, false);
        var mf = fg.AddComponent<MeshFilter>();
        var mr = fg.AddComponent<MeshRenderer>();
        tr.Mesh = new Mesh { name = name + "_Mesh" }; mf.sharedMesh = tr.Mesh;
        var fm = CreateTransparentMaterial(fillColor);
        mr.sharedMaterial = fm;
        var lm = new Material(Shader.Find("Sprites/Default")); lm.color = lineColor;
        tr.Init(er, fg, lm);
        return tr;
    }
}