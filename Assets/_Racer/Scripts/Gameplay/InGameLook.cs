using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// DA de la vraie partie (la démo derrière le menu n'a que la voiture en blanc et la route à lignes blanches) :
/// - route avec lignes blanches (bords pleins + pointillés au centre) : Resources/InGame/RoadLines ;
/// - voitures blanches, contour et arêtes noirs : Resources/InGame/CarWhite (aussi sur les photos du garage) ;
/// - décor (DecorSpawner) noir, contour et arêtes blancs : Resources/InGame/DecorBlack ;
/// - terrain blanc à facettes grises : Resources/InGame/TerrainWhite ;
/// - fond (ciel) blanc uni, et au loin tout se fond dans ce blanc (global _RacerDistanceFade).
///
/// Rien à brancher : s'active tout seul à chaque chargement d'une scène de jeu (ZigzagRoadGenerator),
/// juste après les Awake (le modèle de la voiture équipée est déjà en place) et avant les Start
/// (la route n'a pas encore pris son material).
///
/// Arêtes : calculées une seule fois par modèle (mises en cache) depuis le mesh : arêtes vives (angle > CreaseAngle),
/// bords ouverts et frontières entre materials (vitres, phares...). Elles sont ajoutées au mesh lui-même
/// sous forme de quads très fins (voir Racer/Outlined) : pas de renderer ni de draw call en plus.
/// Les FBX doivent avoir Read/Write coché ; sinon l'objet garde seulement l'aplat et le contour.
/// </summary>
public static class InGameLook
{
    const string Folder = "InGame/";
    const float CreaseAngle = 35f; // angle entre 2 faces à partir duquel l'arête est tracée

    /// <summary>Vrai dans une vraie partie : DecorSpawner passe alors le décor en noir à traits blancs.</summary>
    public static bool DecorActive { get; private set; }

    static readonly int DistanceFade = Shader.PropertyToID("_RacerDistanceFade");
    static readonly Vector4 FadeInGame = new Vector4(80f, 170f, 1f, 0f); // début, fin (m), force

    static Material roadMat, carMat, decorMat, terrainMat;
    static readonly Dictionary<Mesh, Mesh> meshes = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        foreach (var m in meshes.Values)
            if (m != null) Object.Destroy(m);
        meshes.Clear();
        DecorActive = false;
        Shader.SetGlobalVector(DistanceFade, Vector4.zero);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Appelé après les Awake de la scène chargée, avant leurs Start
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var roots = scene.GetRootGameObjects();
        bool gameScene = false;
        foreach (var root in roots)
            if (root.GetComponentInChildren<ZigzagRoadGenerator>(true) != null) { gameScene = true; break; }
        if (!gameScene)
        {
            if (mode == LoadSceneMode.Single) Shader.SetGlobalVector(DistanceFade, Vector4.zero); // menu, garage
            return;
        }

        // Démo du menu (chargée en additif par MenuBackground) : on garde la DA du menu.
        // La vraie partie est chargée en Single : on ne se fie pas seulement à DemoRunning,
        // qui peut encore être vrai le temps que la scène du menu soit détruite.
        bool demo = mode == LoadSceneMode.Additive && MenuBackground.DemoRunning;
        DecorActive = !demo;
        Shader.SetGlobalVector(DistanceFade, demo ? Vector4.zero : FadeInGame);
        if (demo)
        {
            // La voiture et la route de la démo (autopilote) ont quand même la DA du jeu ; le reste garde celle du menu
            foreach (var root in roots)
            {
                ApplyRoads(root);
                foreach (var car in root.GetComponentsInChildren<DriftCar>(true))
                    ApplyCar(car);
            }
            return;
        }

        foreach (var root in roots)
        {
            ApplyRoads(root);
            foreach (var terrain in root.GetComponentsInChildren<LowPolyTerrainGenerator>(true))
            {
                if (terrainMat == null) terrainMat = Resources.Load<Material>(Folder + "TerrainWhite");
                if (terrainMat != null) terrain.terrainMaterial = terrainMat; // copié dans son Start
            }
            foreach (var car in root.GetComponentsInChildren<DriftCar>(true))
                ApplyCar(car);
            foreach (var cam in root.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.white;
            }
        }
    }

    // Route avec lignes blanches (bords pleins + pointillés au centre)
    static void ApplyRoads(GameObject root)
    {
        foreach (var road in root.GetComponentsInChildren<ZigzagRoadGenerator>(true))
        {
            if (roadMat == null) roadMat = Resources.Load<Material>(Folder + "RoadLines");
            if (roadMat != null) road.roadMaterial = roadMat;
        }
    }

    /// <summary>Passe le modèle actuel de la voiture en blanc + contour + arêtes noirs.</summary>
    public static void ApplyCar(DriftCar car)
    {
        if (car != null && car.body != null) ApplyCarModel(car.body.gameObject);
    }

    /// <summary>Même chose sur n'importe quel modèle de voiture (ex : photos du garage).</summary>
    public static void ApplyCarModel(GameObject model)
    {
        if (carMat == null) carMat = Resources.Load<Material>(Folder + "CarWhite");
        if (carMat == null || model == null) return;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            Style(r, carMat);
    }

    /// <summary>Objet du décor : noir, contour et arêtes blancs (appelé par DecorSpawner).</summary>
    public static void ApplyDecor(Renderer r)
    {
        if (decorMat == null) decorMat = Resources.Load<Material>(Folder + "DecorBlack");
        if (decorMat != null) Style(r, decorMat);
    }

    static void Style(Renderer r, Material mat)
    {
        Mesh src;
        var smr = r as SkinnedMeshRenderer;
        MeshFilter mf = null;
        if (smr != null) src = smr.sharedMesh;
        else if (r is MeshRenderer && r.TryGetComponent(out mf)) src = mf.sharedMesh;
        else return;
        if (src == null) return;

        var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
        r.sharedMaterials = mats;

        Mesh outlined = OutlinedMesh(src);
        if (outlined == null) return; // pas lisible : contour selon les normales d'origine, pas d'arêtes
        if (smr != null) smr.sharedMesh = outlined;
        else mf.sharedMesh = outlined;
    }

    // ---------- Mesh préparé (calculé une fois par modèle) ----------

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

    class EdgeInfo
    {
        public int a, b;          // sommets d'origine (position et poids des os)
        public Vector3 normal;    // normale de la 1re face
        public int sub, count;
        public bool crease, opposite;
    }

    /// <summary>
    /// Copie du mesh pour Racer/Outlined : normales lissées dans les tangentes (w = 2) pour le contour,
    /// puis quads d'arêtes ajoutés au 1er sous-mesh (uv3.x = 1). Null si le mesh n'est pas lisible.
    /// </summary>
    static Mesh OutlinedMesh(Mesh src)
    {
        if (meshes.TryGetValue(src, out var cached) && cached != null) return cached;
        if (!src.isReadable) return null;

        var verts = src.vertices;
        var srcNormals = src.normals;
        int n = verts.Length;
        int[] weld = Weld(verts, out int count);
        float cosCrease = Mathf.Cos(CreaseAngle * Mathf.Deg2Rad);

        var smooth = new Vector3[count];
        var edges = new Dictionary<long, EdgeInfo>();
        var order = new List<EdgeInfo>();
        var subTris = new List<int>[src.subMeshCount];

        for (int s = 0; s < src.subMeshCount; s++)
        {
            var tris = src.GetTriangles(s);
            subTris[s] = new List<int>(tris);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                Vector3 fn = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]);
                if (fn.sqrMagnitude < 1e-20f) continue;
                fn.Normalize();
                smooth[weld[i0]] += fn;
                smooth[weld[i1]] += fn;
                smooth[weld[i2]] += fn;
                AddEdge(i0, i1, fn, s);
                AddEdge(i1, i2, fn, s);
                AddEdge(i2, i0, fn, s);
            }
        }

        void AddEdge(int ia, int ib, Vector3 fn, int sub)
        {
            int wa = weld[ia], wb = weld[ib];
            if (wa == wb) return;
            long key = (long)Mathf.Min(wa, wb) * count + Mathf.Max(wa, wb);
            if (!edges.TryGetValue(key, out var e))
            {
                e = new EdgeInfo { a = ia, b = ib, normal = fn, sub = sub, count = 1 };
                edges.Add(key, e);
                order.Add(e);
                return;
            }
            e.count++;
            float d = Vector3.Dot(fn, e.normal);
            if (d <= -0.99f) e.opposite = true;              // face double (dos à dos)
            else if (d < cosCrease || sub != e.sub) e.crease = true;
        }

        var pos = new List<Vector3>(verts);
        var nrm = new List<Vector3>(n);
        var tan = new List<Vector4>(n);
        var flags = new List<Vector4>(n);
        for (int i = 0; i < n; i++)
        {
            Vector3 sn = smooth[weld[i]];
            Vector3 vn = i < srcNormals.Length ? srcNormals[i] : Vector3.up;
            if (sn.sqrMagnitude < 1e-12f) sn = vn;
            sn.Normalize();
            nrm.Add(vn);
            tan.Add(new Vector4(sn.x, sn.y, sn.z, 2f));
            flags.Add(Vector4.zero);
        }

        var bw = src.boneWeights;
        bool skinned = bw.Length == n && src.bindposes.Length > 0;
        var weights = skinned ? new List<BoneWeight>(bw) : null;
        var edgeTris = subTris.Length > 0 ? subTris[0] : null;

        foreach (var e in order)
        {
            if (edgeTris == null) break;
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
                nrm.Add(e.normal);
                tan.Add(t4);
                flags.Add(new Vector4(1f, end ? 1f : -1f, (k & 1) == 0 ? -1f : 1f, 0f));
                if (skinned) weights.Add(bw[end ? e.b : e.a]);
            }
            // Les 2 sens de rotation : le quad est visible quel que soit son sens à l'écran
            edgeTris.Add(v0); edgeTris.Add(v0 + 1); edgeTris.Add(v0 + 2);
            edgeTris.Add(v0 + 2); edgeTris.Add(v0 + 1); edgeTris.Add(v0 + 3);
            edgeTris.Add(v0); edgeTris.Add(v0 + 2); edgeTris.Add(v0 + 1);
            edgeTris.Add(v0 + 2); edgeTris.Add(v0 + 3); edgeTris.Add(v0 + 1);
        }

        var mesh = new Mesh { name = src.name + " (DA)" };
        if (pos.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(pos);
        mesh.SetNormals(nrm);
        mesh.SetTangents(tan);
        mesh.SetUVs(3, flags);
        mesh.subMeshCount = subTris.Length;
        for (int s = 0; s < subTris.Length; s++) mesh.SetTriangles(subTris[s], s, false);
        if (skinned)
        {
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = src.bindposes;
        }
        mesh.bounds = src.bounds;
        mesh.hideFlags = HideFlags.DontUnloadUnusedAsset; // gardé d'une partie à l'autre
        meshes[src] = mesh;
        return mesh;
    }
}
