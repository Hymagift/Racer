using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Terrain "ultra low poly" de type TOUGE : la route longe une falaise d'un côté et une paroi de montagne de l'autre.
/// Le côté de la falaise change de temps en temps (ZigzagRoadGenerator.SideAt) : la montagne s'enfonce dans une gorge,
/// la route la franchit sur un pont, puis la montagne remonte de l'autre côté.
///
/// Le terrain est maillé "à partir de la route" : chaque ligne du maillage est un point de la route (tous les segmentLength
/// mètres le long de la route), et les colonnes sont des distances perpendiculaires à partir du bord de la route.
/// La première colonne est exactement le bord de la route, donc la falaise et la paroi rocheuse s'y raccordent sans trou.
/// L'altitude suit celle de la route (montées / descentes, voir ZigzagRoadGenerator) : le profil en travers s'y ajoute.
///
/// ÉPINGLES ET LACETS : à l'intérieur d'un virage, le terrain s'arrête avant le centre du virage (sinon il se replierait),
/// et entre deux branches de route proches (lacets, épingle), chaque côté s'arrête à mi-chemin et rejoint l'altitude
/// moyenne des deux routes : le terrain d'une branche ne recouvre jamais l'autre.
/// - Près de la route : colonnes serrées, une ligne tous les segments de la route (précis).
/// - Au loin : colonnes espacées (cellSize), sommets interpolés sur les lignes intermédiaires (aspect low poly).
///
/// Les chunks sont enfants du TrackRoot de la route : ils défilent avec elle.
/// Mise en place : GameObject vide "Terrain" avec ce script. La route est trouvée et liée automatiquement.
/// Pour un build mobile, assigne plutôt un Material URP/Lit (ou Standard) pour être sûr que le shader est inclus.
/// </summary>
public class LowPolyTerrainGenerator : MonoBehaviour
{
    [Header("Références")]
    public ZigzagRoadGenerator road;
    [Tooltip("Optionnel. Sa texture principale sera remplacée par le dégradé de couleurs ci-dessous.")]
    public Material terrainMaterial;

    [Header("Maillage")]
    [Tooltip("Distances (en m, à partir du bord de la route) des colonnes proches. La première doit rester 0.")]
    public float[] nearOffsets = { 0f, 2.5f, 6f, 11f };
    [Tooltip("Espacement des colonnes lointaines. Plus grand = plus low poly.")]
    public float cellSize = 14f;
    [Tooltip("Le terrain s'étend jusqu'à cette distance du bord de la route, de chaque côté")]
    public float halfWidth = 200f;
    [Tooltip("Au loin, un sommet toutes les N lignes (la route a une ligne tous les 2 m). Doit diviser Segments Per Chunk (15) : 1, 3, 5, 15.")]
    public int coarseRowStride = 3;
    public float viewDistance = 260f;
    public float despawnDistance = 60f;
    [Tooltip("Collider sur le terrain (inutile : la route a ses propres murs de collision). Coûteux sur mobile.")]
    public bool addCollider = false;

    [Header("Relief")]
    public int seed = 0;                       // 0 = aléatoire à chaque partie
    [Tooltip("Distance de départ où tout est plat au niveau de la route (falaise et montagne apparaissent progressivement). " +
             "Les montées / descentes de la route se règlent sur la route (ZigzagRoadGenerator, Relief)")]
    public float startFlatDistance = 80f;

    [Header("Paroi de montagne (côté opposé à la falaise)")]
    [Tooltip("Hauteur de la paroi rocheuse juste contre la route")]
    public float wallHeight = 22f;
    [Tooltip("Largeur (en plan) de la paroi. Petit = paroi quasi verticale.")]
    public float wallRun = 10f;

    [Header("Falaise")]
    public float cliffDepth = 25f;
    [Tooltip("Largeur (en plan) de la falaise avant le fond du ravin")]
    public float cliffRun = 12f;

    [Header("Montagnes au loin")]
    public float sideRelief = 45f;
    [Tooltip("Distance entre les crêtes. Plus petit = plus de pics")]
    public float sideReliefWavelength = 90f;
    [Tooltip("Distance pour atteindre le relief complet")]
    public float sideReliefRamp = 40f;

    [Header("Raccord à la route")]
    [Tooltip("Le sol au bord de la route est abaissé de cette valeur (évite les accrochages visuels)")]
    public float edgeSink = 0.05f;

    [Header("Couleurs (du plus bas au plus haut)")]
    public Color[] heightColors =
    {
        new Color(0.22f, 0.42f, 0.22f),
        new Color(0.33f, 0.55f, 0.26f),
        new Color(0.50f, 0.50f, 0.30f),
        new Color(0.45f, 0.40f, 0.36f),
        new Color(0.62f, 0.60f, 0.58f),
        new Color(0.93f, 0.95f, 0.97f)
    };

    float ChunkLength => road.segmentLength * road.segmentsPerChunk;

    readonly Queue<(GameObject go, double endS)> chunks = new Queue<(GameObject, double)>();
    readonly List<float> offsets = new List<float>();
    readonly List<RoadPoint> samples = new List<RoadPoint>();
    int nearLast;     // index de la dernière colonne "fine"
    double nextS;     // distance (le long de la route) du prochain chunk
    int noiseSeed;
    Material material;
    Texture2D colorTexture;

    void Awake()
    {
        if (road == null) road = FindFirstObjectByType<ZigzagRoadGenerator>();
        if (road == null)
        {
            Debug.LogError("[LowPolyTerrain] Aucune ZigzagRoadGenerator trouvée dans la scène.", this);
            enabled = false;
            return;
        }

        // Lie la route à ce terrain
        if (road.terrain == null) road.terrain = this;

        noiseSeed = seed == 0 ? UnityEngine.Random.Range(1, 100000) : seed;
    }

    void Start()
    {
        BuildMaterial();
        BuildOffsets();
        nextS = -ChunkLength;
        road.OnTrackRebased += HandleTrackRebased;
    }

    void OnDestroy()
    {
        if (road != null) road.OnTrackRebased -= HandleTrackRebased;
        if (material != null) Destroy(material);
        if (colorTexture != null) Destroy(colorTexture);
    }

    void BuildOffsets()
    {
        offsets.Clear();
        if (nearOffsets == null || nearOffsets.Length < 2 || nearOffsets[0] != 0f)
            nearOffsets = new[] { 0f, 2.5f, 6f, 11f };

        offsets.AddRange(nearOffsets);
        nearLast = offsets.Count - 1;

        float last = offsets[offsets.Count - 1];
        float step = Mathf.Max(2f, cellSize);
        while (last < halfWidth)
        {
            last += step;
            offsets.Add(last);
        }
    }

    void Update()
    {
        if (road == null || road.player == null || !road.enabled) return;
        double ps = road.TotalDistance;

        // Un seul morceau par image, sauf si on est très en retard (au lancement)
        int budget = nextS < ps + viewDistance * 0.5f ? 16 : 1;
        while (budget-- > 0 && nextS < ps + viewDistance)
            SpawnChunk();

        while (chunks.Count > 0 && chunks.Peek().endS < ps - despawnDistance)
            DestroyChunk(chunks.Dequeue().go);
    }

    // La route a ramené le TrackRoot à 0 : on décale nos chunks (invisible, même position monde)
    void HandleTrackRebased(float shift)
    {
        foreach (var c in chunks)
            c.go.transform.localPosition -= new Vector3(0f, 0f, shift);
    }

    // ---------- Fonctions de hauteur ----------

    // Profil en travers : décalage d'altitude par rapport à la route, à la distance e du bord de la route.
    // voidness : 0 = paroi de montagne contre la route, 1 = falaise (vide) ; entre les deux, la montagne s'enfonce.
    // bridge : 1 sur un pont, la gorge (plus profonde) commence dès le bord de la route : la route est dans le vide.
    float Profile(float e, float voidness, float bridge, float x, double az)
    {
        float ridged = Ridged(x, az, sideReliefWavelength, noiseSeed + 7);

        float run = Mathf.Max(0.1f, cliffRun);
        float depth = Mathf.Lerp(cliffDepth, road.gorgeDepth, bridge);
        float drop = depth * EaseOut((e + bridge * run) / run);
        float far = ridged * sideRelief * 0.6f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cliffRun, cliffRun + sideReliefRamp, e));
        float cliff = -drop + far;
        if (voidness >= 1f) return cliff;

        float wall = wallHeight * EaseOut(e / Mathf.Max(0.1f, wallRun));
        float far2 = ridged * sideRelief * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(wallRun * 0.5f, wallRun * 0.5f + sideReliefRamp, e));
        float mountain = wall + far2;
        return Mathf.Lerp(mountain, cliff, voidness);
    }

    // Raide au départ, qui s'aplatit ensuite : paroi / falaise quasi verticale contre la route
    static float EaseOut(float t)
    {
        t = Mathf.Clamp01(t);
        float u = 1f - t;
        return 1f - u * u;
    }

    // Crêtes de montagne (0..1) : les lignes où le bruit vaut 0.5 deviennent des arêtes, le reste des vallées
    static float Ridged(double x, double z, float wavelength, int seed)
    {
        float a = 1f - Mathf.Abs(2f * ValueNoise(x, z, wavelength, seed) - 1f);
        float b = 1f - Mathf.Abs(2f * ValueNoise(x, z, wavelength * 0.4f, seed + 1) - 1f);
        float r = 0.75f * a + 0.25f * b;
        return r * r;
    }

    // Bruit de valeur sur grille entière : précis même à des distances énormes (pas de float géant)
    static float ValueNoise(double x, double z, float wavelength, int seed)
    {
        double gx = x / wavelength, gz = z / wavelength;
        long ix = (long)Math.Floor(gx), iz = (long)Math.Floor(gz);
        float fx = (float)(gx - ix), fz = (float)(gz - iz);
        fx = fx * fx * (3f - 2f * fx);
        fz = fz * fz * (3f - 2f * fz);

        float a = Hash(ix, iz, seed);
        float b = Hash(ix + 1, iz, seed);
        float c = Hash(ix, iz + 1, seed);
        float d = Hash(ix + 1, iz + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
    }

    static float Hash(long x, long z, int seed)
    {
        unchecked
        {
            ulong h = (ulong)(x * 73856093L) ^ (ulong)(z * 19349663L) ^ (ulong)(seed * 83492791L);
            h ^= h >> 33; h *= 0xff51afd7ed558ccdUL;
            h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL;
            h ^= h >> 33;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    // ---------- Matériau (dégradé de couleurs par altitude) ----------

    void BuildMaterial()
    {
        int n = Mathf.Max(1, heightColors.Length);
        colorTexture = new Texture2D(n, 1, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        for (int i = 0; i < n; i++)
            colorTexture.SetPixel(i, 0, i < heightColors.Length ? heightColors[i] : Color.white);
        colorTexture.Apply();

        if (terrainMaterial != null)
        {
            material = new Material(terrainMaterial);
        }
        else
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            material = new Material(sh);
        }

        material.mainTexture = colorTexture;
        material.color = Color.white;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
    }

    // ---------- Chunks ----------

    void SpawnChunk()
    {
        double s0 = nextS;
        float chunkZ = road.PointAt(s0).position.z;
        GameObject go = new GameObject("TerrainChunk");
        go.transform.SetParent(road.TrackRoot, false);
        go.transform.localPosition = new Vector3(0f, 0f, chunkZ);

        Mesh mesh = BuildChunkMesh(s0, chunkZ);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;

        if (addCollider)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.cookingOptions = MeshColliderCookingOptions.UseFastMidphase | MeshColliderCookingOptions.CookForFasterSimulation
                              | MeshColliderCookingOptions.WeldColocatedVertices;
            mc.sharedMesh = mesh;
            if (road.RoadPhysics != null) mc.sharedMaterial = road.RoadPhysics;
        }

        chunks.Enqueue((go, s0 + ChunkLength));
        nextS += ChunkLength;
    }

    void DestroyChunk(GameObject chunk)
    {
        var mf = chunk.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        Destroy(chunk);
    }

    // Points de la route tous les RoomStep mètres (sur une grille fixe de distances s : deux chunks voisins
    // trouvent exactement les mêmes limites sur leur ligne commune, donc pas de trou entre eux)
    const float RoomStep = 4f;
    const double RoomWindow = 300.0;

    /// <summary>
    /// Distance max (depuis le centre de la route) jusqu'où le terrain peut aller de ce côté (sgn = -1 gauche, 1 droite) :
    /// avant le centre du virage à l'intérieur d'une courbe, et avant la mi-distance avec une autre partie de la route
    /// qui revient (épingle, lacets). Dans ce cas other = vrai et otherY = altitude moyenne des deux routes.
    /// </summary>
    float Room(in RoadPoint p, int sgn, out bool other, out float otherY)
    {
        float hw = p.HalfWidth;
        float tMax = hw + halfWidth + cellSize;
        other = false;
        otherY = p.position.y;
        Vector2 pos = new Vector2(p.position.x, p.position.z);
        Vector2 n = new Vector2(p.right.x, p.right.z) * sgn;

        foreach (RoadPoint q in samples)
        {
            double ds = Math.Abs(q.s - p.s);

            // Intérieur d'un virage tout proche : on reste avant son centre (limite qui varie en douceur le long de la route)
            if (ds <= 80.0 && q.curvature * sgn > 0.0001f)
                tMax = Mathf.Min(tMax, 0.95f / Mathf.Abs(q.curvature) + (float)ds);

            // Autre partie de la route qui revient près d'ici (beaucoup plus près en ligne droite que le long de la route)
            Vector2 d = new Vector2(q.position.x, q.position.z) - pos;
            float dist = d.magnitude;
            if (ds < hw * 2f || dist >= 0.75f * ds) continue;
            float along = Vector2.Dot(d, n);
            if (along <= 0.1f) continue;
            float t = dist * dist / (2f * along) + 1f; // croisement avec la médiatrice (+1 m de recouvrement)
            if (t < tMax)
            {
                tMax = t;
                other = true;
                otherY = (p.position.y + q.position.y) * 0.5f;
            }
        }
        return Mathf.Max(tMax, hw + 0.5f);
    }

    Mesh BuildChunkMesh(double startS, float chunkZ)
    {
        int rows = Mathf.Max(1, road.segmentsPerChunk);
        float ds = road.segmentLength;
        int stride = Mathf.Max(1, coarseRowStride);
        while (stride > 1 && rows % stride != 0) stride--;

        int cols = offsets.Count;
        // Points de la route à chaque ligne (mêmes valeurs que celles utilisées par le mesh de la route)
        var pts = new RoadPoint[rows + 1];
        var voids = new float[2, rows + 1];  // vide à gauche (0) et à droite (1) à chaque ligne
        var bridges = new float[rows + 1];
        for (int r = 0; r <= rows; r++)
        {
            double s = startS + r * ds;
            pts[r] = road.PointAt(s);
            road.SideAt(s, out voids[0, r], out voids[1, r], out bridges[r]);
        }

        // Points de la route autour du chunk, pour savoir jusqu'où le terrain peut aller de chaque côté
        samples.Clear();
        double g0 = Math.Floor((startS - RoomWindow) / RoomStep) * RoomStep;
        for (double g = g0; g <= startS + ChunkLength + RoomWindow; g += RoomStep)
            samples.Add(road.PointAt(g));

        var room = new float[2, rows + 1];
        var hasOther = new bool[2, rows + 1];
        var otherY = new float[2, rows + 1];
        for (int si = 0; si < 2; si++)
            for (int r = 0; r <= rows; r++)
                room[si, r] = Room(pts[r], si == 0 ? -1 : 1, out hasOther[si, r], out otherY[si, r]);

        // pos[côté, colonne, ligne] : côté 0 = gauche, 1 = droite (dans le sens de la route)
        var pos = new Vector3[2, cols, rows + 1];
        Vector3 origin = new Vector3(0f, 0f, chunkZ);
        for (int si = 0; si < 2; si++)
        {
            int sgn = si == 0 ? -1 : 1;

            for (int j = 0; j < cols; j++)
            {
                bool fine = j <= nearLast;
                float e = offsets[j];

                for (int r = 0; r <= rows; r++)
                {
                    if (!fine && r % stride != 0) continue; // interpolé plus bas

                    RoadPoint p = pts[r];
                    float tMax = room[si, r];
                    float t = Mathf.Min(p.HalfWidth + e, tMax);
                    Vector3 v = p.position + p.right * (sgn * t);
                    double az = road.OriginZ + v.z;
                    float fade = Mathf.SmoothStep(0f, 1f, (float)(p.s / Math.Max(1f, startFlatDistance)));

                    float y = p.position.y + Profile(t - p.HalfWidth, voids[si, r], bridges[r], v.x, az) * fade;
                    // Près d'une autre branche de la route : on rejoint l'altitude moyenne des deux routes à mi-chemin
                    if (hasOther[si, r] && j > 0)
                        y = Mathf.Lerp(y, otherY[si, r], Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f * tMax, tMax, t)));
                    if (j == 0) y -= edgeSink;

                    v.y = y;
                    pos[si, j, r] = v - origin;
                }

                if (!fine && stride > 1)
                {
                    for (int r = 0; r <= rows; r++)
                    {
                        if (r % stride == 0) continue;
                        int r0 = (r / stride) * stride;
                        int r1 = r0 + stride;
                        float t = (r - r0) / (float)stride;
                        pos[si, j, r] = Vector3.Lerp(pos[si, j, r0], pos[si, j, r1], t);
                    }
                }
            }
        }

        float colorMin = -(road.elevationAmplitude + Mathf.Max(cliffDepth, road.gorgeDepth) + 1f);
        float colorMax = Mathf.Max(0.1f, road.elevationAmplitude + wallHeight + sideRelief);

        var verts = new List<Vector3>(2 * (cols - 1) * rows * 6);
        var uvs = new List<Vector2>(verts.Capacity);
        var tris = new List<int>(verts.Capacity);

        float U(float y) => Mathf.InverseLerp(colorMin, colorMax, y);

        // Ajoute un triangle orienté vers le haut (l'orientation ne dépend que de la projection en XZ)
        void Tri(Vector3 a, Vector3 b, Vector3 c, float u)
        {
            if (Vector3.Cross(b - a, c - a).y < 0f) { Vector3 tmp = b; b = c; c = tmp; }
            var uv = new Vector2(u, 0.5f);
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        for (int s = 0; s < 2; s++)
        {
            for (int j = 0; j < cols - 1; j++)
            {
                for (int r = 0; r < rows; r++)
                {
                    Vector3 a = pos[s, j, r];         // intérieur (côté route), ligne r
                    Vector3 b = pos[s, j + 1, r];     // extérieur, ligne r
                    Vector3 c = pos[s, j, r + 1];     // intérieur, ligne r+1
                    Vector3 d = pos[s, j + 1, r + 1]; // extérieur, ligne r+1

                    bool coarse = j >= nearLast;
                    float uFlat = 0f;
                    if (coarse)
                    {
                        // Une seule couleur par grande case lointaine (même teinte sur toutes ses sous-lignes)
                        int r0 = (r / stride) * stride;
                        int r1 = r0 + stride;
                        float avg = (pos[s, j, r0].y + pos[s, j + 1, r0].y + pos[s, j, r1].y + pos[s, j + 1, r1].y) * 0.25f;
                        uFlat = U(avg);
                    }

                    // On alterne la diagonale pour éviter un effet "quadrillé" trop régulier
                    if (((j + r) & 1) == 0)
                    {
                        Tri(a, c, b, coarse ? uFlat : U((a.y + c.y + b.y) / 3f));
                        Tri(b, c, d, coarse ? uFlat : U((b.y + c.y + d.y) / 3f));
                    }
                    else
                    {
                        Tri(a, c, d, coarse ? uFlat : U((a.y + c.y + d.y) / 3f));
                        Tri(a, d, b, coarse ? uFlat : U((a.y + d.y + b.y) / 3f));
                    }
                }
            }
        }

        var mesh = new Mesh { name = "TerrainChunkMesh" };
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
