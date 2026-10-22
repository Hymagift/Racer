using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// DA de la vraie partie seulement (pas dans la démo derrière le menu, ni dans le menu ou le garage) :
/// - route avec lignes blanches (bords pleins + pointillés au centre) : Resources/InGame/RoadLines ;
/// - toutes les voitures en blanc, contour noir de la silhouette et traits noirs sur les arêtes.
///
/// Rien à brancher : s'active tout seul à chaque chargement d'une scène de jeu (ZigzagRoadGenerator, DriftCar),
/// juste après les Awake (le modèle de la voiture équipée est déjà en place) et avant les Start
/// (la route n'a pas encore pris son material).
///
/// Arêtes : calculées une seule fois par modèle (mises en cache) depuis le mesh : arêtes vives (angle > creaseAngle),
/// bords ouverts et frontières entre materials (vitres, phares...). Elles sont dessinées par un 2e mesh de quads
/// très fins (Racer/CarEdges, 1 draw call) qui suit les os des roues. Les FBX des voitures doivent avoir
/// Read/Write coché ; sinon la voiture est seulement blanche avec le contour.
/// </summary>
public static class InGameLook
{
    const string Folder = "InGame/";
    const string EdgesName = "Arêtes";
    const float CreaseAngle = 35f; // angle entre 2 faces à partir duquel l'arête est tracée

    static Material roadMat, carMat, edgeMat;
    static readonly Dictionary<Mesh, Mesh> bodyMeshes = new();
    static readonly Dictionary<Mesh, Mesh> edgeMeshes = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        ClearCache(bodyMeshes);
        ClearCache(edgeMeshes);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void ClearCache(Dictionary<Mesh, Mesh> cache)
    {
        foreach (var m in cache.Values)
            if (m != null) Object.Destroy(m);
        cache.Clear();
    }

    // Appelé après les Awake de la scène chargée, avant leurs Start
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Démo du menu (chargée en additif par MenuBackground) : on garde la DA du menu.
        // La vraie partie est chargée en Single : on ne se fie pas seulement à DemoRunning,
        // qui peut encore être vrai le temps que la scène du menu soit détruite.
        if (mode == LoadSceneMode.Additive && MenuBackground.DemoRunning) return;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var road in root.GetComponentsInChildren<ZigzagRoadGenerator>(true))
            {
                if (roadMat == null) roadMat = Resources.Load<Material>(Folder + "RoadLines");
                if (roadMat != null) road.roadMaterial = roadMat;
            }
            foreach (var car in root.GetComponentsInChildren<DriftCar>(true))
                ApplyCar(car);
        }
    }

    /// <summary>Passe le modèle actuel de la voiture en blanc + contour + arêtes noires.</summary>
    public static void ApplyCar(DriftCar car)
    {
        if (car == null || car.body == null) return;
        if (carMat == null) carMat = Resources.Load<Material>(Folder + "CarWhite");
        if (edgeMat == null) edgeMat = Resources.Load<Material>(Folder + "CarEdges");
        if (carMat == null) return;

        foreach (var r in car.body.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name == EdgesName) continue;

            Mesh src;
            var smr = r as SkinnedMeshRenderer;
            MeshFilter mf = null;
            if (smr != null) src = smr.sharedMesh;
            else if (r is MeshRenderer && r.TryGetComponent(out mf)) src = mf.sharedMesh;
            else continue;
            if (src == null) continue;

            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = carMat;
            r.sharedMaterials = mats;

            if (!src.isReadable) continue; // contour avec les normales d'origine, pas d'arêtes

            if (smr != null) smr.sharedMesh = BodyMesh(src);
            else mf.sharedMesh = BodyMesh(src);

            Mesh edges = EdgeMesh(src);
            if (edges == null || edgeMat == null) continue;

            var go = new GameObject(EdgesName);
            go.transform.SetParent(r.transform, false);
            Renderer er;
            if (smr != null)
            {
                var esmr = go.AddComponent<SkinnedMeshRenderer>();
                esmr.sharedMesh = edges;
                esmr.bones = smr.bones;
                esmr.rootBone = smr.rootBone;
                esmr.localBounds = smr.localBounds;
                esmr.updateWhenOffscreen = smr.updateWhenOffscreen;
                esmr.quality = smr.quality;
                er = esmr;
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = edges;
                er = go.AddComponent<MeshRenderer>();
            }
            er.sharedMaterial = edgeMat;
            er.shadowCastingMode = ShadowCastingMode.Off;
            er.receiveShadows = false;
            er.lightProbeUsage = LightProbeUsage.Off;
            er.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    // ---------- Meshs (calculés une fois par modèle) ----------

    // Regroupe les sommets à la même position (les modèles low poly dupliquent les sommets aux arêtes vives)
    static int[] Weld(Vector3[] verts, out int count)
    {
        var ids = new int[verts.Length];
        var map = new Dictionary<Vector3Int, int>(verts.Length);
        Bounds b = new Bounds(verts.Length > 0 ? verts[0] : Vector3.zero, Vector3.zero);
        foreach (var v in verts) b.Encapsulate(v);
        float q = 1f / Mathf.Max(1e-6f, b.size.magnitude * 1e-5f);

        count = 0;
        for (int i = 0; i < verts.Length; i++)
        {
            var key = Vector3Int.RoundToInt(verts[i] * q);
            if (!map.TryGetValue(key, out int id)) { id = count++; map.Add(key, id); }
            ids[i] = id;
        }
        return ids;
    }

    // Copie du mesh avec des normales lissées rangées dans les tangentes (w = 2) pour le contour de Racer/CarWhite
    static Mesh BodyMesh(Mesh src)
    {
        if (bodyMeshes.TryGetValue(src, out var cached) && cached != null) return cached;

        var verts = src.vertices;
        var tris = src.triangles;
        int[] weld = Weld(verts, out int count);
        var smooth = new Vector3[count];
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
            Vector3 n = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]);
            if (n.sqrMagnitude < 1e-20f) continue;
            n.Normalize();
            smooth[weld[i0]] += n;
            smooth[weld[i1]] += n;
            smooth[weld[i2]] += n;
        }

        var normals = src.normals;
        var tangents = new Vector4[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 n = smooth[weld[i]];
            if (n.sqrMagnitude < 1e-12f) n = i < normals.Length ? normals[i] : Vector3.up;
            n.Normalize();
            tangents[i] = new Vector4(n.x, n.y, n.z, 2f);
        }

        var mesh = Object.Instantiate(src);
        mesh.name = src.name + " (contour)";
        mesh.tangents = tangents;
        mesh.hideFlags = HideFlags.DontUnloadUnusedAsset; // gardé d'une partie à l'autre
        bodyMeshes[src] = mesh;
        return mesh;
    }

    class EdgeInfo
    {
        public int a, b;          // sommets d'origine (pour la position et les poids des os)
        public Vector3 normal;    // normale de la 1re face
        public int sub, count;
        public bool crease, opposite;
    }

    // Mesh de quads posés sur les arêtes à tracer (voir Racer/CarEdges). Null s'il n'y en a aucune.
    static Mesh EdgeMesh(Mesh src)
    {
        if (edgeMeshes.TryGetValue(src, out var cached)) return cached;

        var verts = src.vertices;
        int[] weld = Weld(verts, out int count);
        float cosCrease = Mathf.Cos(CreaseAngle * Mathf.Deg2Rad);
        var edges = new Dictionary<long, EdgeInfo>();
        var order = new List<EdgeInfo>();

        for (int s = 0; s < src.subMeshCount; s++)
        {
            var tris = src.GetTriangles(s);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                Vector3 n = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]);
                if (n.sqrMagnitude < 1e-20f) continue;
                n.Normalize();
                AddEdge(i0, i1, n, s);
                AddEdge(i1, i2, n, s);
                AddEdge(i2, i0, n, s);
            }
        }

        void AddEdge(int ia, int ib, Vector3 n, int sub)
        {
            int wa = weld[ia], wb = weld[ib];
            if (wa == wb) return;
            long key = (long)Mathf.Min(wa, wb) * count + Mathf.Max(wa, wb);
            if (!edges.TryGetValue(key, out var e))
            {
                e = new EdgeInfo { a = ia, b = ib, normal = n, sub = sub, count = 1 };
                edges.Add(key, e);
                order.Add(e);
                return;
            }
            e.count++;
            float d = Vector3.Dot(n, e.normal);
            if (d <= -0.99f) e.opposite = true;              // face double (dos à dos)
            else if (d < cosCrease || sub != e.sub) e.crease = true;
        }

        var bw = src.boneWeights;
        bool skinned = bw.Length == verts.Length && src.bindposes.Length > 0;
        var pos = new List<Vector3>();
        var tan = new List<Vector4>();
        var nrm = new List<Vector3>();
        var uv = new List<Vector2>();
        var weights = new List<BoneWeight>();
        var idx = new List<int>();

        foreach (var e in order)
        {
            // Arête vive, frontière entre materials, bord ouvert, ou bord d'une face double
            bool draw = e.crease || e.count == 1 || (e.count == 2 && e.opposite);
            if (!draw) continue;
            Vector3 A = verts[e.a], B = verts[e.b];
            Vector3 dir = B - A;
            if (dir.sqrMagnitude < 1e-12f) continue;
            dir.Normalize();
            var t4 = new Vector4(dir.x, dir.y, dir.z, 1f);

            int v0 = pos.Count;
            for (int k = 0; k < 4; k++)
            {
                bool end = k >= 2;
                pos.Add(end ? B : A);
                tan.Add(t4);
                nrm.Add(e.normal);
                uv.Add(new Vector2(end ? 1f : -1f, (k & 1) == 0 ? -1f : 1f));
                if (skinned) weights.Add(bw[end ? e.b : e.a]);
            }
            idx.Add(v0); idx.Add(v0 + 1); idx.Add(v0 + 2);
            idx.Add(v0 + 2); idx.Add(v0 + 1); idx.Add(v0 + 3);
        }

        Mesh mesh = null;
        if (idx.Count > 0)
        {
            mesh = new Mesh { name = src.name + " (arêtes)" };
            if (pos.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetTangents(tan);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(idx, 0);
            if (skinned)
            {
                mesh.boneWeights = weights.ToArray();
                mesh.bindposes = src.bindposes;
            }
            mesh.bounds = src.bounds;
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        edgeMeshes[src] = mesh;
        return mesh;
    }
}
