using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Route infinie de type TOUGE, générée par "chunks" devant le joueur.
/// Le tracé (épingles, enchaînements en S, virages serrés, courbes, lignes droites) vient de TougeLayout.
///
/// MODE DÉFILEMENT : la voiture ne bouge pas en Z, c'est le décor (la route + le terrain) qui défile sous elle.
/// Tous les chunks sont enfants d'un objet "TrackRoot" (Rigidbody cinématique) déplacé en continu vers -Z.
/// De temps en temps le TrackRoot est ramené à 0 en décalant les chunks en sens inverse : invisible.
///
/// Mise en place :
/// 1. GameObject vide "Road" avec ce script. Assigne "Player" (la voiture) et un Material de route.
/// 2. Sur la voiture, utilise le script DriftCar fourni : il doit pointer vers cette route (champ "Road").
/// 3. Ajoute LowPolyTerrainGenerator sur un autre objet : il se lie tout seul.
/// 4. Tout objet de jeu qui doit défiler (obstacles, pièces...) doit être enfant de road.TrackRoot.
/// </summary>
public class ZigzagRoadGenerator : MonoBehaviour
{
    [Header("Références")]
    public Transform player;
    public Material roadMaterial;
    [Tooltip("Terrain low poly (facultatif, lié automatiquement par le terrain). Si présent, la route suit son relief.")]
    public LowPolyTerrainGenerator terrain;

    [Header("Forme de la route")]
    [Tooltip("Largeur de la route (mesurée perpendiculairement à la route, même dans les diagonales)")]
    public float roadWidth = 8f;

    [Header("Tracé touge")]
    [Tooltip("Ligne droite au départ (m)")]
    public float straightStart = 50f;
    [Tooltip("Angle max de la route (degrés) au début. Plus grand = virages plus prononcés")]
    [Range(10f, 70f)] public float maxAngleStart = 40f;
    [Tooltip("Angle max de la route (degrés) à la difficulté max. Une épingle fait 2x cet angle (60 -> 120°)")]
    [Range(10f, 70f)] public float maxAngleEnd = 60f;
    [Tooltip("Distance parcourue pour atteindre la difficulté max (angles max, épingles et S fréquents)")]
    public float distanceToMaxDifficulty = 1500f;
    [Tooltip("Accélération latérale visée dans les virages (m/s²). Rayon = vitesse² / cornerGrip. Plus grand = virages plus serrés (plus dur)")]
    public float cornerGrip = 8f;
    [Tooltip("Rayon de virage minimum (m)")]
    public float minCornerRadius = 8f;
    [Tooltip("Au-delà de cet écart latéral (m), les virages ramènent la route vers le centre")]
    public float recenterDistance = 40f;
    [Tooltip("Une courte ligne droite de répit toutes les N figures environ (0 = jamais). Plus grand = plus dur")]
    public int figuresBetweenBreaks = 8;

    [Header("Touge")]
    [Tooltip("-1 = falaise à gauche (montagne à droite), 1 = falaise à droite, 0 = montagne des deux côtés")]
    public int cliffSide = -1;

    [Header("Génération")]
    public float segmentLength = 2f;          // finesse du mesh
    public int segmentsPerChunk = 15;         // taille d'un chunk = segmentLength * segmentsPerChunk
    public float viewDistance = 150f;         // distance générée devant le joueur
    public float despawnDistance = 40f;       // distance gardée derrière le joueur
    public float uvTileLength = 8f;           // longueur de route pour un tour de texture
    public int seed = 0;                      // 0 = aléatoire à chaque partie

    [Header("Collisions")]
    public bool addCollider = true;
    [Tooltip("Épaisseur du sol de collision (évite que la voiture traverse la route)")]
    public float colliderThickness = 1f;
    [Tooltip("Murs de collision sur les bords de la route (glissière côté falaise, paroi côté montagne)")]
    public bool addSideWalls = true;
    [Tooltip("Hauteur de la glissière (côté falaise)")]
    public float wallHeight = 1.2f;
    [Tooltip("Hauteur du mur de collision invisible côté montagne (la roche est dessinée par le terrain)")]
    public float rockWallColliderHeight = 4f;
    public float wallThickness = 0.5f;
    [Tooltip("Sol sans friction : le sol défile sous la voiture, la friction n'a pas de sens ici")]
    public bool frictionlessRoad = true;

    [Header("Glissières visibles (côté falaise)")]
    public bool showWalls = true;
    [Tooltip("Optionnel. Sinon un material gris clair est créé.")]
    public Material wallMaterial;

    [Header("Défilement du décor")]
    [Tooltip("Quand le TrackRoot a défilé de plus que cette distance, il est ramené à 0 (invisible)")]
    public float rebaseDistance = 500f;

    [Header("Debug")]
    public bool debugLogs = false;

    /// <summary>Vitesse de défilement du décor vers l'arrière (m/s). Renseignée par la voiture (DriftCar).</summary>
    public float ScrollSpeed
    {
        get => scrollSpeed;
        set { scrollSpeed = value; scrollWasSet = true; }
    }

    /// <summary>Distance totale parcourue depuis le départ (à utiliser pour le score).</summary>
    public double TotalDistance => distanceScrolled;

    /// <summary>Distance totale ajoutée par les rebases (utilisée par le terrain pour rester cohérent).</summary>
    public double OriginZ => originZ;

    /// <summary>Parent de tout ce qui doit défiler avec la route (obstacles, pièces...).</summary>
    public Transform TrackRoot { get { EnsureRoot(); return trackRoot; } }

    /// <summary>Matériau physique sans friction (null si désactivé), réutilisable par le terrain.</summary>
    public PhysicsMaterial RoadPhysics => roadPhysics;

    /// <summary>
    /// Appelé quand le TrackRoot est ramené à 0, avec la distance retirée. Tout objet enfant du TrackRoot
    /// doit alors faire localPosition.z -= shift (les chunks de la route et du terrain le font déjà).
    /// </summary>
    public event Action<float> OnTrackRebased;

    float ChunkLength => segmentLength * segmentsPerChunk;
    float RootZ => trackRb != null ? trackRb.position.z : 0f;
    bool LeftHasRail => cliffSide <= 0;
    bool RightHasRail => cliffSide >= 0;

    TougeLayout layout;          // tracé de la route (créé à la première utilisation)
    readonly Queue<GameObject> chunks = new Queue<GameObject>();
    float nextChunkZ;            // position Z (locale au TrackRoot) du prochain chunk
    double originZ;              // décalage cumulé des rebases : z absolu = originZ + z local
    double distanceScrolled;
    float scrollSpeed;
    bool scrollWasSet, warnedNoScroll;

    Transform trackRoot;
    Rigidbody trackRb;
    PhysicsMaterial roadPhysics;
    Material roadMat, wallMat;
    float nextLogTime;

    void Awake()
    {
        EnsureRoot();
    }

    void EnsureRoot()
    {
        if (trackRoot != null) return;

        var go = new GameObject("TrackRoot");
        trackRoot = go.transform;
        trackRoot.position = Vector3.zero;

        trackRb = go.AddComponent<Rigidbody>();
        trackRb.isKinematic = true;
        trackRb.useGravity = false;
        trackRb.interpolation = RigidbodyInterpolation.Interpolate; // défilement fluide malgré le pas fixe de la physique
    }

    void Start()
    {
        // Chargée en additif (fond du menu) : le TrackRoot créé dans Awake peut atterrir dans la scène active
        if (trackRoot.gameObject.scene != gameObject.scene)
            SceneManager.MoveGameObjectToScene(trackRoot.gameObject, gameObject.scene);

        if (player == null)
        {
            Debug.LogError("[ZigzagRoad] Le champ 'Player' n'est pas assigné.", this);
            enabled = false;
            return;
        }

        nextChunkZ = -ChunkLength; // un chunk derrière le départ

        roadMat = roadMaterial != null ? roadMaterial : CreateFallbackMaterial(new Color(0.22f, 0.22f, 0.25f));
        if (showWalls)
            wallMat = wallMaterial != null ? wallMaterial : CreateFallbackMaterial(new Color(0.9f, 0.9f, 0.92f));

        if (frictionlessRoad)
        {
            roadPhysics = new PhysicsMaterial("RoadNoFriction")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }
    }

    void OnDestroy()
    {
        if (roadMaterial == null && roadMat != null) Destroy(roadMat);
        if (wallMaterial == null && wallMat != null) Destroy(wallMat);
    }

    // ---------- Défilement du décor ----------

    void FixedUpdate()
    {
        if (trackRb == null) return;

        float dz = Mathf.Max(0f, scrollSpeed) * Time.fixedDeltaTime;

        if (trackRb.position.z - dz < -rebaseDistance)
            Rebase();

        distanceScrolled += dz;

        Vector3 p = trackRb.position;
        p.z -= dz;
        trackRb.MovePosition(p);
    }

    // Ramène le TrackRoot à 0 et décale les chunks de l'inverse : la position monde de chaque chunk est inchangée.
    void Rebase()
    {
        float s = -trackRb.position.z;
        if (s <= 0f) return;

        // On coupe l'interpolation le temps de la téléportation pour éviter un à-coup visuel
        trackRb.interpolation = RigidbodyInterpolation.None;
        trackRb.position = Vector3.zero;
        trackRoot.position = Vector3.zero;

        foreach (GameObject c in chunks)
            c.transform.localPosition -= new Vector3(0f, 0f, s);
        nextChunkZ -= s;
        originZ += s;

        Physics.SyncTransforms();
        trackRb.interpolation = RigidbodyInterpolation.Interpolate;

        OnTrackRebased?.Invoke(s);
    }

    void Update()
    {
        // Si personne ne pilote le défilement, on le dit clairement
        if (!scrollWasSet && !warnedNoScroll && Time.timeSinceLevelLoad > 1.5f)
        {
            warnedNoScroll = true;
            Debug.LogWarning("[ZigzagRoad] Personne n'a défini ScrollSpeed : le décor ne défilera pas. " +
                             "Vérifie que ta voiture utilise le script DriftCar fourni et que son champ 'Road' pointe vers cette route.", this);
        }

        float playerTrackZ = TrackZ(player.position.z);

        if (debugLogs && Time.time >= nextLogTime)
        {
            nextLogTime = Time.time + 1f;
            Debug.Log($"[ZigzagRoad] scroll={scrollSpeed:F1} m/s | rootZ={RootZ:F1} | playerTrackZ={playerTrackZ:F1} | nextChunkZ={nextChunkZ:F0} | chunks={chunks.Count} | distance={distanceScrolled:F0}", this);
        }

        // Génère devant
        while (nextChunkZ < playerTrackZ + viewDistance)
            SpawnChunk();

        // Détruit derrière
        while (chunks.Count > 0 && chunks.Peek().transform.localPosition.z + ChunkLength < playerTrackZ - despawnDistance)
            DestroyChunk(chunks.Dequeue());
    }

    // ---------- Tracé de la route (fonction déterministe de la position) ----------

    TougeLayout Layout
    {
        get
        {
            if (layout != null) return layout;

            // Les rayons des virages dépendent de la vitesse de la voiture à chaque endroit (pour rester faisables)
            DriftCar car = player != null ? player.GetComponentInParent<DriftCar>() : null;
            if (car == null) car = FindFirstObjectByType<DriftCar>();

            layout = new TougeLayout(seed)
            {
                straightStart = straightStart,
                maxAngleStart = maxAngleStart,
                maxAngleEnd = maxAngleEnd,
                distanceToMaxDifficulty = distanceToMaxDifficulty,
                cornerGrip = cornerGrip,
                minRadius = minCornerRadius,
                recenterDistance = recenterDistance,
                figuresBetweenBreaks = figuresBetweenBreaks,
                speedAt = car == null ? null : (Func<double, float>)(z =>
                    Mathf.Lerp(car.startSpeed, car.maxSpeed, (float)(z / Math.Max(1f, car.speedRampDistance)))),
            };
            return layout;
        }
    }

    float GetCenterX(float z) => Layout.CenterX(originZ + z);

    // Les bords sont décalés en X pur : dans une diagonale il faut élargir en X pour garder roadWidth perpendiculairement
    float WidthScale(float z)
    {
        float s = Layout.Slope(originZ + z);
        return Mathf.Sqrt(1f + s * s);
    }

    // ---------- Construction des chunks ----------

    void SpawnChunk()
    {
        GameObject go = new GameObject("RoadChunk");
        go.transform.SetParent(trackRoot, false);
        go.transform.localPosition = new Vector3(0f, 0f, nextChunkZ);

        BuildMeshes(nextChunkZ, out Mesh visualMesh, out Mesh collisionMesh, out Mesh railsMesh);

        go.AddComponent<MeshFilter>().sharedMesh = visualMesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = roadMat;

        if (collisionMesh != null)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = collisionMesh;
            if (roadPhysics != null) mc.sharedMaterial = roadPhysics;
        }

        if (railsMesh != null)
        {
            var rails = new GameObject("Rails");
            rails.transform.SetParent(go.transform, false);
            rails.AddComponent<MeshFilter>().sharedMesh = railsMesh;
            var mr = rails.AddComponent<MeshRenderer>();
            mr.sharedMaterial = wallMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        chunks.Enqueue(go);
        nextChunkZ += ChunkLength;
    }

    void DestroyChunk(GameObject chunk)
    {
        // Les meshes créés par code ne sont pas libérés avec le GameObject : on les détruit nous-mêmes
        foreach (var mf in chunk.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
        var mc = chunk.GetComponent<MeshCollider>();
        if (mc != null && mc.sharedMesh != null) Destroy(mc.sharedMesh);
        Destroy(chunk);
    }

    void BuildMeshes(float startZ, out Mesh visualMesh, out Mesh collisionMesh, out Mesh railsMesh)
    {
        int rows = segmentsPerChunk + 1;
        var left = new Vector3[rows];
        var right = new Vector3[rows];
        var rightDir = new Vector3[rows];
        var uvs = new Vector2[rows * 2];

        // Décalage d'UV pour que la texture reste continue malgré les rebases (calculé en double)
        float uvOffset = (float)(originZ % uvTileLength);
        for (int r = 0; r < rows; r++)
        {
            float z = startZ + r * segmentLength;
            float x = GetCenterX(z);
            float y = RoadHeightAt(z); // la route suit le relief (plate dans la largeur)
            float k = WidthScale(z);
            float hw = roadWidth * 0.5f * k;

            // Les bords de la route sont décalés en X pur : le terrain utilise exactement les mêmes points,
            // donc la roche et la falaise se raccordent parfaitement au bord de la route.
            // (élargi de k en X pour que la largeur et l'épaisseur des murs restent constantes dans les diagonales)
            rightDir[r] = Vector3.right * k;
            left[r] = new Vector3(x - hw, y, z - startZ);
            right[r] = new Vector3(x + hw, y, z - startZ);

            float v = (z + uvOffset) / uvTileLength;
            uvs[r * 2] = new Vector2(0f, v);
            uvs[r * 2 + 1] = new Vector2(1f, v);
        }

        // ----- Mesh visuel : une simple bande (gauche/droite) -----
        var vertices = new Vector3[rows * 2];
        var triangles = new int[segmentsPerChunk * 6];
        for (int r = 0; r < rows; r++)
        {
            vertices[r * 2] = left[r];
            vertices[r * 2 + 1] = right[r];
        }
        for (int s = 0; s < segmentsPerChunk; s++)
        {
            int l0 = s * 2, r0 = s * 2 + 1, l1 = (s + 1) * 2, r1 = (s + 1) * 2 + 1;
            int t = s * 6;
            triangles[t] = l0; triangles[t + 1] = l1; triangles[t + 2] = r0;
            triangles[t + 3] = r0; triangles[t + 4] = l1; triangles[t + 5] = r1;
        }

        visualMesh = new Mesh { name = "RoadChunkMesh" };
        visualMesh.vertices = vertices;
        visualMesh.uv = uvs;
        visualMesh.triangles = triangles;
        visualMesh.RecalculateNormals();
        visualMesh.RecalculateBounds();

        collisionMesh = addCollider ? BuildCollisionMesh(left, right, rightDir) : null;
        railsMesh = showWalls ? BuildRailsMesh(left, right, rightDir) : null;
    }

    /// <summary>
    /// Mesh de collision "fermé" : dalle épaisse + murs latéraux optionnels.
    /// Un volume fermé est beaucoup plus fiable qu'un plan sans épaisseur (pas de traversée à haute vitesse).
    /// </summary>
    Mesh BuildCollisionMesh(Vector3[] left, Vector3[] right, Vector3[] rightDir)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float th = Mathf.Max(0.1f, colliderThickness);

        Vector3[] leftBottom = Shift(left, Vector3.down * th);
        Vector3[] rightBottom = Shift(right, Vector3.down * th);

        // Dalle : dessus, dessous, côtés
        AddStrip(verts, tris, left, right);               // dessus (normale vers le haut)
        AddStrip(verts, tris, rightBottom, leftBottom);   // dessous
        AddStrip(verts, tris, leftBottom, left);          // flanc gauche
        AddStrip(verts, tris, right, rightBottom);        // flanc droit

        if (addSideWalls)
        {
            float wt = Mathf.Max(0.1f, wallThickness);
            float hL = LeftHasRail ? wallHeight : rockWallColliderHeight;
            float hR = RightHasRail ? wallHeight : rockWallColliderHeight;

            // Mur gauche (le bord intérieur est le bord de la route)
            Vector3[] liT = ShiftAlong(left, rightDir, 0f, hL);
            Vector3[] loT = ShiftAlong(left, rightDir, -wt, hL);
            Vector3[] loB = ShiftAlong(left, rightDir, -wt, -th);
            AddStrip(verts, tris, liT, leftBottom);       // face intérieure
            AddStrip(verts, tris, loT, liT);              // dessus
            AddStrip(verts, tris, loB, loT);              // face extérieure
            AddStrip(verts, tris, leftBottom, loB);       // dessous

            // Mur droit
            Vector3[] riT = ShiftAlong(right, rightDir, 0f, hR);
            Vector3[] roT = ShiftAlong(right, rightDir, wt, hR);
            Vector3[] roB = ShiftAlong(right, rightDir, wt, -th);
            AddStrip(verts, tris, rightBottom, riT);      // face intérieure
            AddStrip(verts, tris, riT, roT);              // dessus
            AddStrip(verts, tris, roT, roB);              // face extérieure
            AddStrip(verts, tris, roB, rightBottom);      // dessous
        }

        var mesh = new Mesh { name = "RoadChunkCollision" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Glissières visibles, uniquement du côté falaise (ou des deux côtés si cliffSide = 0).</summary>
    Mesh BuildRailsMesh(Vector3[] left, Vector3[] right, Vector3[] rightDir)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float h = wallHeight;
        float wt = Mathf.Max(0.1f, wallThickness);
        const float low = -0.4f; // descend un peu sous la route pour se raccorder au sol

        if (LeftHasRail)
        {
            Vector3[] liT = ShiftAlong(left, rightDir, 0f, h);
            Vector3[] loT = ShiftAlong(left, rightDir, -wt, h);
            Vector3[] loB = ShiftAlong(left, rightDir, -wt, low);
            AddStrip(verts, tris, liT, left);
            AddStrip(verts, tris, loT, liT);
            AddStrip(verts, tris, loB, loT);
        }

        if (RightHasRail)
        {
            Vector3[] riT = ShiftAlong(right, rightDir, 0f, h);
            Vector3[] roT = ShiftAlong(right, rightDir, wt, h);
            Vector3[] roB = ShiftAlong(right, rightDir, wt, low);
            AddStrip(verts, tris, right, riT);
            AddStrip(verts, tris, riT, roT);
            AddStrip(verts, tris, roT, roB);
        }

        var mesh = new Mesh { name = "RoadChunkRails" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Ajoute une bande entre deux lignes de points (a puis b, ligne par ligne).
    // La normale est dirigée vers cross(sens de la route, a -> b).
    static void AddStrip(List<Vector3> verts, List<int> tris, Vector3[] a, Vector3[] b)
    {
        int baseIndex = verts.Count;
        for (int r = 0; r < a.Length; r++)
        {
            verts.Add(a[r]);
            verts.Add(b[r]);
        }
        for (int s = 0; s < a.Length - 1; s++)
        {
            int a0 = baseIndex + s * 2, b0 = a0 + 1, a1 = baseIndex + (s + 1) * 2, b1 = a1 + 1;
            tris.Add(a0); tris.Add(a1); tris.Add(b0);
            tris.Add(b0); tris.Add(a1); tris.Add(b1);
        }
    }

    static Vector3[] Shift(Vector3[] src, Vector3 delta)
    {
        var res = new Vector3[src.Length];
        for (int i = 0; i < src.Length; i++) res[i] = src[i] + delta;
        return res;
    }

    static Vector3[] ShiftAlong(Vector3[] src, Vector3[] dirs, float dist, float dy)
    {
        var res = new Vector3[src.Length];
        for (int i = 0; i < src.Length; i++) res[i] = src[i] + dirs[i] * dist + Vector3.up * dy;
        return res;
    }

    static Material CreateFallbackMaterial(Color color)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        var m = new Material(sh) { color = color };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        return m;
    }

    // ---------- Utilitaires ----------

    /// <summary>Convertit un Z monde en Z "piste" (coordonnées locales du TrackRoot, utilisées pour générer la route).</summary>
    public float TrackZ(float worldZ) => worldZ - RootZ;

    /// <summary>Position X du centre de la route à un Z "piste" (le X monde = le X piste, rien ne défile en X).</summary>
    public float CenterXAt(float trackZ) => GetCenterX(trackZ);

    /// <summary>
    /// Demi-largeur de la route mesurée en X à un Z "piste". Égale à roadWidth / 2 en ligne droite,
    /// plus grande dans les diagonales (les bords sont décalés en X pur). À utiliser à la place de roadWidth * 0.5f.
    /// </summary>
    public float HalfWidthXAt(float trackZ) => roadWidth * 0.5f * WidthScale(trackZ);

    /// <summary>Altitude de la route à un Z "piste". 0 s'il n'y a pas de terrain.</summary>
    public float RoadHeightAt(float trackZ) => terrain != null ? terrain.BaseHeight(GetCenterX(trackZ), trackZ) : 0f;

    /// <summary>Distance latérale entre un point monde (la voiture) et le centre de la route (pour détecter une sortie de route).</summary>
    public float LateralOffset(Vector3 worldPos) => worldPos.x - GetCenterX(TrackZ(worldPos.z));
}