using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Route infinie de type TOUGE, générée par "chunks" devant le joueur.
/// Le tracé (épingles, épingles à 180° et lacets, enchaînements en S, virages serrés, courbes, lignes droites) vient de TougeLayout,
/// le relief (montées, descentes, replats) d'ElevationLayout. La route est une courbe paramétrée par sa longueur s :
/// elle peut rouler en travers et faire de vrais demi-tours, mais ne recule jamais en Z (cap entre -90° et +90°).
/// Le côté du vide (falaise à gauche ou à droite) et les ponts qui passent de l'un à l'autre viennent de CliffLayout.
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
///
/// Pour lire la route : PointAt(s) (centre, sens, largeur, pente à une distance s) et Locate(position monde)
/// (point de la route le plus proche, et écart latéral). Les positions des RoadPoint sont locales au TrackRoot.
/// </summary>
public class ZigzagRoadGenerator : MonoBehaviour
{
    [Header("Références")]
    public Transform player;
    public Material roadMaterial;
    [Tooltip("Terrain low poly (facultatif, lié automatiquement par le terrain). Il suit le relief de la route.")]
    public LowPolyTerrainGenerator terrain;

    [Header("Forme de la route")]
    [Tooltip("Largeur de la route au départ (mesurée perpendiculairement à la route, même dans les diagonales)")]
    public float roadWidth = 9f;
    [Tooltip("Largeur de la route à la difficulté max : elle rétrécit petit à petit")]
    public float roadWidthEnd = 6f;

    [Header("Tracé touge")]
    [Tooltip("Portion de départ (m), en courbes douces : il n'y a jamais de ligne droite")]
    public float straightStart = 50f;
    [Tooltip("Angle max de la route (degrés) au début. Plus grand = virages plus prononcés")]
    [Range(10f, 70f)] public float maxAngleStart = 40f;
    [Tooltip("Angle max de la route (degrés) à la difficulté max. Une épingle fait 2x cet angle (60 -> 120°)")]
    [Range(10f, 70f)] public float maxAngleEnd = 66f;
    [Tooltip("Distance parcourue pour atteindre la difficulté max (angles max, épingles et S fréquents)")]
    public float distanceToMaxDifficulty = 900f;
    [Tooltip("Forme de la montée de difficulté : 1 = régulière, plus petit = difficile plus tôt")]
    [Range(0.3f, 1.5f)] public float difficultyCurve = 0.75f;
    [Tooltip("Accélération latérale visée dans les virages (m/s²). Rayon = vitesse² / cornerGrip. Plus grand = virages plus serrés (plus dur)")]
    public float cornerGrip = 6.5f;
    [Tooltip("Même chose à la difficulté max : les virages se resserrent petit à petit (au-delà de ~11, injouable)")]
    public float cornerGripEnd = 10.5f;
    [Tooltip("Rayon de virage minimum (m)")]
    public float minCornerRadius = 8f;
    [Tooltip("Au-delà de cet écart latéral (m), les virages ramènent la route vers le centre")]
    public float recenterDistance = 40f;
    [Tooltip("Une courte portion de répit (courbes douces) toutes les N figures environ (0 = jamais). Plus grand = plus dur")]
    public int figuresBetweenBreaks = 5;
    [Tooltip("Même chose à la difficulté max : les lignes droites deviennent rares (et plus courtes)")]
    public int figuresBetweenBreaksEnd = 18;
    [Tooltip("Variété des rayons : 0 = chaque virage au rayon juste faisable, 1 = beaucoup de virages plus larges, de toutes tailles " +
             "(jamais plus serrés que faisable). Avec l'anti-répétition des figures, évite de ressentir les mêmes virages en boucle")]
    [Range(0f, 1f)] public float radiusVariety = 0.6f;

    [Header("Épingles à 180° et lacets")]
    [Tooltip("Fréquence des épingles à 180° (demi-tours) et des lacets (plusieurs épingles enchaînées). 0 = jamais")]
    [Range(0f, 3f)] public float hairpinFrequency = 1f;
    [Tooltip("Pas d'épingle à 180° avant cette distance (m)")]
    public float hairpinStartDistance = 250f;
    [Tooltip("Cap des branches en travers (degrés) : 90 = vrai demi-tour de 180°, 75 = épingle de 150°")]
    [Range(45f, 90f)] public float hairpinHeading = 90f;
    [Tooltip("Accélération latérale visée dans une épingle (m/s²). Rayon = vitesse² / hairpinGrip. Plus grand = épingles plus serrées (plus dur)")]
    public float hairpinGrip = 7f;
    [Tooltip("Rayon minimum d'une épingle (m, au centre de la route). Jamais moins que la demi-largeur + 3 m, ni que le braquage de la voiture")]
    public float minHairpinRadius = 10f;
    [Tooltip("Vitesse visée dans une épingle (x la vitesse normale) : la voiture lève le pied avant et réaccélère après. " +
             "1 = pas de ralentissement (les épingles deviennent très larges)")]
    [Range(0.4f, 1f)] public float hairpinSlowdown = 0.7f;
    [Tooltip("Probabilité (à la difficulté max, x0.4 au début) qu'une épingle soit SERRÉE, type demi-tour de montagne : " +
             "gros ralentissement et rayon très court. 0 = jamais")]
    [Range(0f, 1f)] public float tightHairpinChance = 0.35f;
    [Tooltip("Rayon minimum d'une épingle serrée (m, au centre de la route). Jamais moins que le braquage de la voiture")]
    public float tightHairpinRadius = 8f;
    [Tooltip("Vitesse visée dans une épingle serrée (x la vitesse normale)")]
    [Range(0.2f, 1f)] public float tightHairpinSlowdown = 0.4f;
    [Tooltip("Accélération latérale visée dans une épingle serrée (m/s²). Plus grand = encore plus serré")]
    public float tightHairpinGrip = 7.5f;

    [Header("Épingle de drift (grand U régulier)")]
    [Tooltip("Une épingle de drift (longues courbes douces, demi-tour à 180° régulier assez large pour tenir un gros drift, " +
             "longues courbes douces en sortie) revient tous les min à max mètres. Max = 0 : jamais")]
    public float driftHairpinEveryMin = 450f;
    public float driftHairpinEveryMax = 800f;
    [Tooltip("Distance de la première épingle de drift (m)")]
    public float driftHairpinFirst = 180f;
    [Tooltip("Vitesse visée dans l'épingle de drift (x la vitesse normale) : on lève à peine le pied")]
    [Range(0.4f, 1f)] public float driftHairpinSlowdown = 0.85f;
    [Tooltip("Accélération latérale visée dans le U (m/s²). Rayon = vitesse² / driftHairpinGrip. Plus grand = U plus serré")]
    public float driftHairpinGrip = 9f;
    [Tooltip("Rayon minimum du U (m, au centre de la route)")]
    public float driftHairpinMinRadius = 14f;
    [Tooltip("Longueur des portions d'entrée et de sortie du U, en courbes douces (m, au hasard entre min et max)")]
    public float driftHairpinLegMin = 40f;
    public float driftHairpinLegMax = 70f;

    [Header("Jamais de ligne droite")]
    [Tooltip("Rayon des courbes douces qui remplacent toutes les lignes droites (m, au hasard entre min et max). " +
             "Plus petit = on tourne plus fort entre deux virages")]
    public float wiggleRadiusMin = 45f;
    public float wiggleRadiusMax = 80f;
    [Tooltip("Longueur max d'une vague (une courbe qui s'écarte, une qui revient) entre deux virages (m)")]
    public float wiggleLength = 24f;
    [Tooltip("Longueur des branches en travers entre deux épingles (m, au hasard entre min et max)")]
    public float legLengthMin = 22f;
    public float legLengthMax = 45f;
    [Tooltip("Nombre max d'épingles enchaînées dans des lacets")]
    [Range(2, 6)] public int maxLacets = 4;

    [Header("Relief : montées et descentes")]
    [Tooltip("Distance plate au départ (m)")]
    public float elevationStartFlat = 80f;
    [Tooltip("Pente max au début (0.1 = 10 %)")]
    [Range(0f, 0.25f)] public float maxGrade = 0.07f;
    [Tooltip("Pente max à la difficulté max")]
    [Range(0f, 0.25f)] public float maxGradeEnd = 0.14f;
    [Tooltip("L'altitude de la route reste entre -amplitude et +amplitude (m)")]
    public float elevationAmplitude = 35f;
    [Tooltip("Longueur d'une montée / descente (m, au hasard entre min et max)")]
    public float minSlopeLength = 60f;
    public float maxSlopeLength = 180f;
    [Tooltip("Longueur du raccord progressif entre deux pentes (m). Plus court = bosses plus marquées (la voiture peut décoller)")]
    public float slopeTransition = 45f;
    [Tooltip("Probabilité qu'un tronçon soit un replat")]
    [Range(0f, 1f)] public float flatChance = 0.2f;

    [Header("Touge : côté du vide et ponts")]
    [Tooltip("Côté du vide (falaise) au départ : -1 = à gauche (montagne à droite), 1 = à droite")]
    public int cliffSide = -1;
    [Tooltip("Le vide change de côté de temps en temps, avec un pont au-dessus d'une gorge entre les deux")]
    public bool switchSides = true;
    [Tooltip("Longueur (m) de la première zone avant le premier pont")]
    public float firstSideLength = 450f;
    [Tooltip("Longueur (m) d'une zone entre deux ponts (au hasard entre min et max)")]
    public float minSideLength = 350f;
    public float maxSideLength = 800f;
    [Tooltip("Longueur (m) d'un pont (au hasard entre min et max)")]
    public float minBridgeLength = 50f;
    public float maxBridgeLength = 90f;
    [Tooltip("Distance (m) sur laquelle la montagne s'enfonce dans la gorge avant le pont, puis remonte après")]
    public float sideRampLength = 30f;
    [Tooltip("Profondeur de la gorge sous un pont (m)")]
    public float gorgeDepth = 45f;
    [Tooltip("Épaisseur du tablier du pont (m)")]
    public float deckThickness = 1.4f;
    [Tooltip("Distance (m) entre deux piles du pont")]
    public float pillarSpacing = 18f;
    [Tooltip("Épaisseur d'une pile du pont dans le sens de la route (m)")]
    public float pillarDepth = 2f;

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

    /// <summary>
    /// Distance parcourue le long de la route depuis le départ (à utiliser pour le score) : la plus grande distance s
    /// atteinte par la voiture sur la route. Elle avance aussi quand la route roule en travers (le décor ne défile pas en Z).
    /// </summary>
    public double TotalDistance => progressS;

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
    TougeLayout layout;          // tracé de la route (créé à la première utilisation)
    ElevationLayout elevation;   // relief de la route (créé à la première utilisation)
    CliffLayout cliffs;          // côté du vide et ponts (créé à la première utilisation)
    readonly Queue<(GameObject go, double endS)> chunks = new Queue<(GameObject, double)>();
    double nextChunkS;           // distance (le long de la route) du début du prochain chunk
    double originZ;              // décalage cumulé des rebases : z absolu = originZ + z local
    double progressS;            // plus grande distance atteinte par la voiture sur la route
    float scrollSpeed;
    bool scrollWasSet, warnedNoScroll;

    Transform trackRoot;
    Rigidbody trackRb;
    PhysicsMaterial roadPhysics;
    Material roadMat, wallMat, bridgeMat;
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

        nextChunkS = -ChunkLength; // un chunk derrière le départ

        roadMat = roadMaterial != null ? roadMaterial : CreateFallbackMaterial(new Color(0.22f, 0.22f, 0.25f));
        if (showWalls)
            wallMat = wallMaterial != null ? wallMaterial : CreateFallbackMaterial(new Color(0.9f, 0.9f, 0.92f));
        bridgeMat = wallMaterial != null ? wallMaterial : CreateFallbackMaterial(new Color(0.72f, 0.7f, 0.68f)); // béton du pont

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
        if (wallMaterial == null && bridgeMat != null) Destroy(bridgeMat);
    }

    // ---------- Défilement du décor ----------

    void FixedUpdate()
    {
        if (trackRb == null) return;

        // Défilement signé : la route ne recule jamais en Z, mais en glisse dans une branche en travers
        // la voiture peut repartir un peu vers -Z
        float dz = scrollSpeed * Time.fixedDeltaTime;

        if (Mathf.Abs(trackRb.position.z - dz) > rebaseDistance)
            Rebase();

        Vector3 p = trackRb.position;
        p.z -= dz;
        trackRb.MovePosition(p);
    }

    // Ramène le TrackRoot à 0 et décale les chunks de l'inverse : la position monde de chaque chunk est inchangée.
    void Rebase()
    {
        float s = -trackRb.position.z;
        if (Mathf.Abs(s) < 0.001f) return;

        // On coupe l'interpolation le temps de la téléportation pour éviter un à-coup visuel
        trackRb.interpolation = RigidbodyInterpolation.None;
        trackRb.position = Vector3.zero;
        trackRoot.position = Vector3.zero;

        foreach (var c in chunks)
            c.go.transform.localPosition -= new Vector3(0f, 0f, s);
        originZ += s;

        Physics.SyncTransforms();
        trackRb.interpolation = RigidbodyInterpolation.Interpolate;

        OnTrackRebased?.Invoke(s);
    }

    /// <summary>
    /// Décale tout le décor en Z (téléportation invisible pour les objets qui défilent) : sert à remettre un point de la route
    /// en face de la voiture, qui ne bouge jamais en Z (voir DriftGameManager, "Continuer").
    /// </summary>
    public void ShiftTrackZ(float delta)
    {
        EnsureRoot();
        trackRb.interpolation = RigidbodyInterpolation.None;
        Vector3 p = trackRb.position + new Vector3(0f, 0f, delta);
        trackRb.position = p;
        trackRoot.position = p;
        Physics.SyncTransforms();
        trackRb.interpolation = RigidbodyInterpolation.Interpolate;
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

        UpdateProgress();

        if (debugLogs && Time.time >= nextLogTime)
        {
            nextLogTime = Time.time + 1f;
            Debug.Log($"[ZigzagRoad] scroll={scrollSpeed:F1} m/s | rootZ={RootZ:F1} | progress={progressS:F1} | nextChunkS={nextChunkS:F0} | chunks={chunks.Count} | vitesse visée x{SpeedFactorAt(progressS):F2}", this);
        }

        // Génère devant : un seul morceau par image (sinon le jeu saccade quand plusieurs arrivent ensemble),
        // sauf si on est très en retard (au lancement ou après un respawn)
        int budget = nextChunkS < progressS + viewDistance * 0.5f ? 16 : 1;
        while (budget-- > 0 && nextChunkS < progressS + viewDistance)
            SpawnChunk();

        // Détruit derrière
        while (chunks.Count > 0 && chunks.Peek().endS < progressS - despawnDistance)
            DestroyChunk(chunks.Dequeue().go);
    }

    // Avance la distance parcourue : on cherche la voiture près de là où elle était (elle ne saute pas d'une branche
    // de lacet à l'autre à travers les murs), et seulement quand elle est sur la route.
    void UpdateProgress()
    {
        if (player == null) return;
        RoadPoint rp = Locate(player.position, progressS, out float lateral);
        if (Mathf.Abs(lateral) <= rp.HalfWidth + 2f && rp.s > progressS)
            progressS = rp.s;
    }

    // ---------- Tracé de la route (fonction déterministe de la distance s) ----------

    TougeLayout Layout
    {
        get
        {
            if (layout != null) return layout;

            // Les rayons des virages dépendent de la vitesse et du braquage de la voiture à chaque endroit (pour rester faisables)
            DriftCar car = player != null ? player.GetComponentInParent<DriftCar>() : null;
            if (car == null) car = FindFirstObjectByType<DriftCar>();

            layout = new TougeLayout(seed)
            {
                straightStart = straightStart,
                maxAngleStart = maxAngleStart,
                maxAngleEnd = maxAngleEnd,
                distanceToMaxDifficulty = distanceToMaxDifficulty,
                difficultyCurve = difficultyCurve,
                cornerGrip = cornerGrip,
                cornerGripEnd = cornerGripEnd,
                minRadius = minCornerRadius,
                recenterDistance = recenterDistance,
                figuresBetweenBreaks = figuresBetweenBreaks,
                figuresBetweenBreaksEnd = figuresBetweenBreaksEnd,
                radiusVariety = radiusVariety,
                roadWidthStart = roadWidth,
                roadWidthEnd = roadWidthEnd,
                hairpinFrequency = hairpinFrequency,
                hairpinStartDistance = hairpinStartDistance,
                hairpinHeading = hairpinHeading,
                hairpinGrip = hairpinGrip,
                minHairpinRadius = minHairpinRadius,
                hairpinSlowdown = hairpinSlowdown,
                tightHairpinChance = tightHairpinChance,
                tightHairpinRadius = tightHairpinRadius,
                tightHairpinSlowdown = tightHairpinSlowdown,
                tightHairpinGrip = tightHairpinGrip,
                driftHairpinEveryMin = driftHairpinEveryMin,
                driftHairpinEveryMax = driftHairpinEveryMax,
                driftHairpinFirst = driftHairpinFirst,
                driftHairpinSlowdown = driftHairpinSlowdown,
                driftHairpinGrip = driftHairpinGrip,
                driftHairpinMinRadius = driftHairpinMinRadius,
                driftHairpinLegMin = driftHairpinLegMin,
                driftHairpinLegMax = driftHairpinLegMax,
                wiggleRadiusMin = wiggleRadiusMin,
                wiggleRadiusMax = Mathf.Max(wiggleRadiusMin, wiggleRadiusMax),
                wiggleLength = wiggleLength,
                legLengthMin = legLengthMin,
                legLengthMax = Mathf.Max(legLengthMin, legLengthMax),
                maxLacets = maxLacets,
                brakeDecel = car != null ? car.hairpinBrake * 0.6f : 2.5f,
                speedAt = car == null ? null : (Func<double, float>)(s =>
                    Mathf.Lerp(car.startSpeed, car.maxSpeed, (float)(s / Math.Max(1f, car.speedRampDistance)))),
                minTurnRadiusAt = car == null ? null : (Func<float, float>)car.MinTurnRadius,
            };
            return layout;
        }
    }

    ElevationLayout Elevation
    {
        get
        {
            if (elevation != null) return elevation;
            elevation = new ElevationLayout(seed)
            {
                startFlat = elevationStartFlat,
                maxGradeStart = maxGrade,
                maxGradeEnd = maxGradeEnd,
                maxHeight = elevationAmplitude,
                minSectionLength = minSlopeLength,
                maxSectionLength = Mathf.Max(minSlopeLength, maxSlopeLength),
                transitionLength = slopeTransition,
                flatChance = flatChance,
                difficultyAt = s => Layout.DifficultyAt(s),
            };
            return elevation;
        }
    }

    CliffLayout Cliffs
    {
        get
        {
            if (cliffs != null) return cliffs;
            cliffs = new CliffLayout(seed)
            {
                startSide = cliffSide >= 0 ? 1 : -1,
                switchSides = switchSides,
                firstZoneLength = firstSideLength,
                minZoneLength = minSideLength,
                maxZoneLength = Mathf.Max(minSideLength, maxSideLength),
                minBridgeLength = minBridgeLength,
                maxBridgeLength = Mathf.Max(minBridgeLength, maxBridgeLength),
                rampLength = sideRampLength,
            };
            return cliffs;
        }
    }

    // ---------- Construction des chunks ----------

    void SpawnChunk()
    {
        double s0 = nextChunkS;
        float chunkZ = PointAt(s0).position.z; // le chunk est placé au z de son début, ses sommets sont relatifs
        GameObject go = new GameObject("RoadChunk");
        go.transform.SetParent(trackRoot, false);
        go.transform.localPosition = new Vector3(0f, 0f, chunkZ);

        BuildMeshes(s0, chunkZ, out Mesh visualMesh, out Mesh collisionMesh, out Mesh railsMesh, out Mesh bridgeMesh);

        go.AddComponent<MeshFilter>().sharedMesh = visualMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = roadMat;
        mr.shadowCastingMode = ShadowCastingMode.Off; // route plate : elle reçoit l'ombre de la voiture mais n'en projette pas

        if (collisionMesh != null)
        {
            var mc = go.AddComponent<MeshCollider>();
            // Préparation du collider plus rapide (moins de saccade à chaque nouveau morceau de route)
            mc.cookingOptions = MeshColliderCookingOptions.UseFastMidphase | MeshColliderCookingOptions.CookForFasterSimulation
                              | MeshColliderCookingOptions.WeldColocatedVertices;
            mc.sharedMesh = collisionMesh;
            if (roadPhysics != null) mc.sharedMaterial = roadPhysics;
        }

        if (railsMesh != null) AddPart(go.transform, "Rails", railsMesh, wallMat, false);
        if (bridgeMesh != null) AddPart(go.transform, "Pont", bridgeMesh, bridgeMat, true);

        chunks.Enqueue((go, s0 + ChunkLength));
        nextChunkS += ChunkLength;
    }

    static void AddPart(Transform parent, string name, Mesh mesh, Material material, bool shadows)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = part.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.receiveShadows = shadows;
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

    void BuildMeshes(double startS, float chunkZ, out Mesh visualMesh, out Mesh collisionMesh, out Mesh railsMesh, out Mesh bridgeMesh)
    {
        int rows = segmentsPerChunk + 1;
        var left = new Vector3[rows];
        var right = new Vector3[rows];
        var rightDir = new Vector3[rows];
        var uvs = new Vector2[rows * 2];
        var uvs2 = new Vector2[rows * 2]; // x = largeur de la route (m) : le shader RoadLines place ses lignes avec
        var voidL = new float[rows];
        var voidR = new float[rows];
        var bridge = new float[rows];

        // v = distance le long de la route / uvTileLength. On retire un multiple de uvTileLength (le même pour tout le chunk,
        // il ne change que tous les ~1 km) pour garder des petits nombres : le motif reste continu d'un chunk à l'autre.
        double uvPeriod = uvTileLength * 128.0;
        double uvBase = Math.Floor(startS / uvPeriod) * uvPeriod;
        Vector3 origin = new Vector3(0f, 0f, chunkZ);
        for (int r = 0; r < rows; r++)
        {
            double s = startS + r * segmentLength;
            RoadPoint p = PointAt(s);
            float hw = p.HalfWidth;

            // Les bords sont décalés perpendiculairement à la route (largeur exacte même dans les épingles) ;
            // la route est plate dans sa largeur et suit le relief dans sa longueur.
            // Le terrain utilise exactement les mêmes points : roche et falaise se raccordent au bord de la route.
            Vector3 c = p.position - origin;
            rightDir[r] = p.right;
            left[r] = c - p.right * hw;
            right[r] = c + p.right * hw;
            SideAt(s, out voidL[r], out voidR[r], out bridge[r]);

            float v = (float)((s - uvBase) / uvTileLength);
            uvs[r * 2] = new Vector2(0f, v);
            uvs[r * 2 + 1] = new Vector2(1f, v);
            uvs2[r * 2] = uvs2[r * 2 + 1] = new Vector2(p.width, 0f);
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
        visualMesh.uv2 = uvs2;
        visualMesh.triangles = triangles;
        visualMesh.RecalculateNormals();
        visualMesh.RecalculateBounds();

        collisionMesh = addCollider ? BuildCollisionMesh(left, right, rightDir, voidL, voidR) : null;
        railsMesh = showWalls ? BuildRailsMesh(left, right, rightDir, voidL, voidR) : null;
        bridgeMesh = BuildBridgeMesh(startS, chunkZ, left, right, rightDir, bridge);
    }

    /// <summary>
    /// Mesh de collision "fermé" : dalle épaisse + murs latéraux optionnels.
    /// Un volume fermé est beaucoup plus fiable qu'un plan sans épaisseur (pas de traversée à haute vitesse).
    /// Côté vide : glissière basse ; côté montagne : mur invisible plus haut (la roche est dessinée par le terrain).
    /// </summary>
    Mesh BuildCollisionMesh(Vector3[] left, Vector3[] right, Vector3[] rightDir, float[] voidL, float[] voidR)
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
            var hL = new float[left.Length];
            var hR = new float[left.Length];
            for (int r = 0; r < left.Length; r++)
            {
                hL[r] = Mathf.Lerp(rockWallColliderHeight, wallHeight, voidL[r]);
                hR[r] = Mathf.Lerp(rockWallColliderHeight, wallHeight, voidR[r]);
            }

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

    /// <summary>Glissières visibles, seulement là où il y a du vide (des deux côtés sur un pont).</summary>
    Mesh BuildRailsMesh(Vector3[] left, Vector3[] right, Vector3[] rightDir, float[] voidL, float[] voidR)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float h = wallHeight;
        float wt = Mathf.Max(0.1f, wallThickness);
        const float low = -0.4f; // descend un peu sous la route pour se raccorder au sol

        foreach (var (a, b) in Runs(voidL, 0.5f))
        {
            var edge = Slice(left, a, b);
            var dir = Slice(rightDir, a, b);
            Vector3[] liT = ShiftAlong(edge, dir, 0f, h);
            Vector3[] loT = ShiftAlong(edge, dir, -wt, h);
            Vector3[] loB = ShiftAlong(edge, dir, -wt, low);
            AddStrip(verts, tris, liT, edge);
            AddStrip(verts, tris, loT, liT);
            AddStrip(verts, tris, loB, loT);
        }

        foreach (var (a, b) in Runs(voidR, 0.5f))
        {
            var edge = Slice(right, a, b);
            var dir = Slice(rightDir, a, b);
            Vector3[] riT = ShiftAlong(edge, dir, 0f, h);
            Vector3[] roT = ShiftAlong(edge, dir, wt, h);
            Vector3[] roB = ShiftAlong(edge, dir, wt, low);
            AddStrip(verts, tris, edge, riT);
            AddStrip(verts, tris, riT, roT);
            AddStrip(verts, tris, roT, roB);
        }

        if (verts.Count == 0) return null;
        var mesh = new Mesh { name = "RoadChunkRails" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Pont : tablier épais sous la route et piles qui descendent au fond de la gorge.</summary>
    Mesh BuildBridgeMesh(double startS, float chunkZ, Vector3[] left, Vector3[] right, Vector3[] rightDir, float[] bridge)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float wt = Mathf.Max(0.1f, wallThickness);
        float deck = Mathf.Max(0.3f, deckThickness);

        // Tablier : flancs et dessous, dans le prolongement des glissières
        foreach (var (a, b) in Runs(bridge, 0.001f, extend: 1))
        {
            var l = Slice(left, a, b);
            var rr = Slice(right, a, b);
            var dir = Slice(rightDir, a, b);
            Vector3[] lTop = ShiftAlong(l, dir, -wt, -0.4f);
            Vector3[] lBot = ShiftAlong(l, dir, -wt, -deck);
            Vector3[] rTop = ShiftAlong(rr, dir, wt, -0.4f);
            Vector3[] rBot = ShiftAlong(rr, dir, wt, -deck);
            AddStrip(verts, tris, lBot, lTop);  // flanc gauche
            AddStrip(verts, tris, rTop, rBot);  // flanc droit
            AddStrip(verts, tris, rBot, lBot);  // dessous
        }

        // Piles : une tous les pillarSpacing mètres (en distance s, donc toujours aux mêmes endroits)
        float spacing = Mathf.Max(4f, pillarSpacing);
        double first = Math.Ceiling(startS / spacing) * spacing;
        for (double s = first; s < startS + ChunkLength; s += spacing)
        {
            SideAt(s, out _, out _, out float b);
            if (b < 0.99f) continue;

            RoadPoint p = PointAt(s);
            Vector3 top = p.position - new Vector3(0f, deck, chunkZ);
            AddPillar(verts, tris, top, p.right, p.forward, p.width * 0.3f, Mathf.Max(0.5f, pillarDepth) * 0.5f, gorgeDepth + 15f);
        }

        if (verts.Count == 0) return null;
        var mesh = new Mesh { name = "RoadChunkBridge" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Pile : boîte orientée selon la route (4 faces, le dessus est caché par le tablier, le bas dans la gorge)
    static void AddPillar(List<Vector3> verts, List<int> tris, Vector3 top, Vector3 side, Vector3 forward,
                          float halfWidth, float halfDepth, float height)
    {
        Vector3 s = side * halfWidth, f = forward * halfDepth, down = Vector3.down * height;
        Vector3 a = top - s - f, b = top + s - f, c = top + s + f, d = top - s + f; // coins du haut
        Quad(verts, tris, a + down, a, b, b + down);   // face avant (vers -forward)
        Quad(verts, tris, c + down, c, d, d + down);   // face arrière
        Quad(verts, tris, d + down, d, a, a + down);   // face gauche
        Quad(verts, tris, b + down, b, c, c + down);   // face droite
    }

    // Quadrilatère p0 (bas gauche), p1 (haut gauche), p2 (haut droit), p3 (bas droit) vu de l'extérieur
    static void Quad(List<Vector3> verts, List<int> tris, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        int i = verts.Count;
        verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
    }

    // Suites de lignes consécutives où values >= threshold (au moins 2 lignes pour faire une bande)
    static IEnumerable<(int start, int end)> Runs(float[] values, float threshold, int extend = 0)
    {
        int n = values.Length;
        int i = 0;
        while (i < n)
        {
            if (values[i] < threshold) { i++; continue; }
            int a = i;
            while (i < n && values[i] >= threshold) i++;
            int b = i - 1;
            a = Mathf.Max(0, a - extend);
            b = Mathf.Min(n - 1, b + extend);
            if (b > a) yield return (a, b);
        }
    }

    static Vector3[] Slice(Vector3[] src, int start, int end)
    {
        var res = new Vector3[end - start + 1];
        Array.Copy(src, start, res, 0, res.Length);
        return res;
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

    // Même chose avec une hauteur différente à chaque ligne
    static Vector3[] ShiftAlong(Vector3[] src, Vector3[] dirs, float dist, float[] dy)
    {
        var res = new Vector3[src.Length];
        for (int i = 0; i < src.Length; i++) res[i] = src[i] + dirs[i] * dist + Vector3.up * dy[i];
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

    /// <summary>Convertit un Z monde en Z "piste" (coordonnées locales du TrackRoot).</summary>
    public float TrackZ(float worldZ) => worldZ - RootZ;

    /// <summary>Position monde -> coordonnées locales du TrackRoot (le X et le Y ne défilent pas).</summary>
    public Vector3 WorldToTrack(Vector3 world) => new Vector3(world.x, world.y, world.z - RootZ);

    /// <summary>Coordonnées locales du TrackRoot -> position monde.</summary>
    public Vector3 TrackToWorld(Vector3 local) => new Vector3(local.x, local.y, local.z + RootZ);

    /// <summary>Le point de la route à la distance s (centre, sens, largeur, pente...). Position locale au TrackRoot.</summary>
    public RoadPoint PointAt(double s)
    {
        Layout.Sample(s, out double x, out double z, out float heading);
        var fwd = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
        return new RoadPoint
        {
            s = s,
            position = new Vector3((float)x, Elevation.Height(s), (float)(z - originZ)),
            forward = fwd,
            right = new Vector3(fwd.z, 0f, -fwd.x),
            width = WidthAt(s),
            grade = Elevation.Grade(s),
            curvature = Layout.Curvature(s),
        };
    }

    /// <summary>
    /// Point de la route le plus proche d'une position MONDE (en plan, sans tenir compte de la hauteur).
    /// lateral = écart signé au centre de la route (+ = à droite dans le sens de la route).
    /// </summary>
    public RoadPoint Locate(Vector3 worldPos, out float lateral)
    {
        double ax = worldPos.x, az = originZ + TrackZ(worldPos.z);
        double s = Layout.NearestByZ(ax, az, 40.0, out double d2);
        if (double.IsInfinity(d2) || d2 > 40.0 * 40.0) s = Layout.NearestByZ(ax, az, 250.0, out _);
        return Finish(s, worldPos, out lateral);
    }

    /// <summary>
    /// Même chose, en cherchant seulement entre sHint - 40 m et sHint + 80 m : pour suivre un objet qui avance sur la route
    /// (la voiture) sans le confondre avec une autre branche de lacets toute proche. Si rien n'est assez proche, recherche complète.
    /// </summary>
    public RoadPoint Locate(Vector3 worldPos, double sHint, out float lateral)
    {
        double ax = worldPos.x, az = originZ + TrackZ(worldPos.z);
        double s = Layout.NearestInRange(ax, az, sHint - 40.0, sHint + 80.0, out double d2);
        float hw = WidthAt(s) * 0.5f + 12f;
        if (d2 > hw * hw) return Locate(worldPos, out lateral);
        return Finish(s, worldPos, out lateral);
    }

    RoadPoint Finish(double s, Vector3 worldPos, out float lateral)
    {
        RoadPoint p = PointAt(s);
        Vector3 d = WorldToTrack(worldPos) - p.position;
        lateral = d.x * p.right.x + d.z * p.right.z;
        return p;
    }

    /// <summary>Largeur de la route (perpendiculairement) à la distance s : de roadWidth au départ à roadWidthEnd à la difficulté max.</summary>
    public float WidthAt(double s) => Mathf.Lerp(roadWidth, roadWidthEnd, Layout.DifficultyAt(s));

    /// <summary>Difficulté de la route (0 = départ, 1 = max) à la distance s.</summary>
    public float DifficultyAt(double s) => Layout.DifficultyAt(s);

    /// <summary>Vitesse visée à la distance s, en fraction de la vitesse normale (moins de 1 avant et dans une épingle à 180°).</summary>
    public float SpeedFactorAt(double s) => Layout.SpeedFactorAt(s);

    /// <summary>
    /// Côté du vide à la distance s : voidLeft / voidRight = 0 (montagne contre la route) à 1 (falaise, vide),
    /// bridge = 1 sur un pont (vide des deux côtés, gorge de gorgeDepth mètres), 0 ailleurs.
    /// </summary>
    public void SideAt(double s, out float voidLeft, out float voidRight, out float bridge) =>
        Cliffs.Sample(s, out voidLeft, out voidRight, out bridge);

    /// <summary>De quel côté est le vide : -1 = à gauche, 1 = à droite, 0 = des deux côtés (pont) ou d'aucun.</summary>
    public float CliffBiasAt(double s)
    {
        SideAt(s, out float l, out float r, out _);
        return r - l;
    }
}

/// <summary>Un point du centre de la route (voir ZigzagRoadGenerator.PointAt / Locate).</summary>
public struct RoadPoint
{
    /// <summary>Distance le long de la route depuis le départ.</summary>
    public double s;
    /// <summary>Centre de la route, en coordonnées locales du TrackRoot.</summary>
    public Vector3 position;
    /// <summary>Sens de la route (horizontal, normalisé).</summary>
    public Vector3 forward;
    /// <summary>Vers la droite de la route (horizontal, normalisé).</summary>
    public Vector3 right;
    /// <summary>Largeur de la route (m).</summary>
    public float width;
    /// <summary>Pente (montée de la route par mètre parcouru, 0.1 = 10 %).</summary>
    public float grade;
    /// <summary>Courbure (1 / rayon, + = virage à droite, 0 seulement à la jonction de deux courbes : la route n'a jamais de ligne droite).</summary>
    public float curvature;

    public float HalfWidth => width * 0.5f;
    /// <summary>Cap de la route en degrés (0 = vers +Z, + = vers la droite).</summary>
    public float HeadingDeg => Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    /// <summary>Sens de la route en 3D (avec la pente).</summary>
    public Vector3 Forward3D => (forward + Vector3.up * grade).normalized;
}
