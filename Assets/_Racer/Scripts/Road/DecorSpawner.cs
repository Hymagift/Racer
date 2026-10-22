using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Décor 3D (sapins, palmiers, rochers) posé sur le terrain de part et d'autre de la route infinie, au fur et à mesure
/// qu'elle est générée. Ne modifie ni la route ni le terrain : on lit leur tracé (CenterXAt, SideAt, GroundHeight).
///
/// Le décor est enfant du TrackRoot de la route : il défile avec elle, et il est réutilisé (pool) une fois passé derrière.
/// Rien n'est jamais posé sur la route : chaque objet garde au moins "roadClearance" mètres (plus son propre rayon)
/// entre lui et le bord de la route, mesurés en vrai dans le plan (les épingles et virages serrés sont pris en compte).
/// Les objets n'ont pas de collider (ils sont retirés à la création) : le décor ne gêne jamais la voiture.
///
/// Chaque "couche" (forêt en contrebas, pins sur la montagne, rochers...) a ses réglages dans l'Inspector :
/// côté (vide / montagne), distance au bord de la route, densité, regroupement en bosquets, taille, altitude.
/// La taille est donnée en mètres : l'échelle d'origine du FBX n'a pas d'importance.
///
/// Mise en place : GameObject vide "DecorSpawner" avec ce script. La route et le terrain sont trouvés automatiquement.
/// En éditeur, les listes "Prefabs" vides sont remplies avec les FBX de Assets/_Racer/Models nommés dans "Model Names".
/// Sauvegarde la scène ensuite pour que les références soient gardées dans un build.
/// </summary>
public class DecorSpawner : MonoBehaviour
{
    public enum Placement
    {
        [InspectorName("Côté du vide (falaise, gorge sous les ponts)")] VoidSide,
        [InspectorName("Côté montagne")] MountainSide,
        [InspectorName("Les deux côtés")] BothSides
    }

    [Serializable]
    public class DecorLayer
    {
        public string name = "Couche";
        public bool enabled = true;
        [Tooltip("Variantes possibles (tirées au hasard). Vide = remplie en éditeur à partir de Model Names.")]
        public GameObject[] prefabs = new GameObject[0];
        [Tooltip("Noms des FBX (sans extension) dans Assets/_Racer/Models, utilisés en éditeur si Prefabs est vide")]
        public string[] modelNames = new string[0];

        [Header("Où")]
        public Placement placement = Placement.BothSides;
        [Tooltip("Distance minimale au bord de la route (m)")]
        public float minDistance = 10f;
        [Tooltip("Distance maximale au bord de la route (m)")]
        public float maxDistance = 120f;
        [Tooltip("Altitude du sol par rapport à la route : en dessous de cette valeur, pas d'objet (m)")]
        public float minRelativeHeight = -999f;
        [Tooltip("Altitude du sol par rapport à la route : au dessus de cette valeur, pas d'objet (ex. -6 = seulement en contrebas)")]
        public float maxRelativeHeight = 999f;

        [Header("Combien")]
        [Tooltip("Nombre d'essais par tranche de 100 m de route, de chaque côté autorisé (avant le regroupement). Un essai pose un amas entier.")]
        public float density = 20f;
        [Tooltip("Taille des bosquets (m). 0 = répartition uniforme, sans regroupement.")]
        public float clusterSize = 60f;
        [Range(0f, 1f)]
        [Tooltip("Part du terrain laissée vide entre les bosquets (0 = partout, 0.8 = petits bosquets isolés)")]
        public float clusterGaps = 0.4f;

        [Header("Amas")]
        [Tooltip("Nombre minimum d'objets posés ensemble à chaque essai (1 = objets isolés)")]
        public int groupMin = 1;
        [Tooltip("Nombre maximum d'objets posés ensemble à chaque essai")]
        public int groupMax = 1;
        [Tooltip("Rayon de l'amas autour du plus gros objet (m)")]
        public float groupRadius = 4f;
        [Range(0.1f, 1f)]
        [Tooltip("Taille des petits objets de l'amas, en part de la taille du plus gros (minimum)")]
        public float groupSmallest = 0.35f;

        [Header("Taille et orientation")]
        [Tooltip("Hauteur minimale de l'objet (m), quelle que soit l'échelle du FBX")]
        public float minHeight = 6f;
        [Tooltip("Hauteur maximale de l'objet (m)")]
        public float maxHeight = 10f;
        [Tooltip("Inclinaison aléatoire max (degrés). Bien pour les rochers, 0 pour les arbres.")]
        public float maxTilt = 0f;
        [Tooltip("Enfonce l'objet dans le sol, en part de sa hauteur (évite qu'il flotte sur une pente)")]
        [Range(0f, 0.5f)] public float sink = 0.05f;
        [Tooltip("Optionnel : remplace les matériaux du FBX (couleur unie par exemple)")]
        public Material overrideMaterial;
    }

    [Header("Références")]
    public ZigzagRoadGenerator road;
    [Tooltip("Lié automatiquement. Sans terrain, le décor est posé à l'altitude de la route.")]
    public LowPolyTerrainGenerator terrain;

    [Header("Sécurité")]
    [Tooltip("Espace libre minimum entre le bord de la route et l'objet (en plus de son rayon), en m")]
    public float roadClearance = 2.5f;
    [Tooltip("Pas de décor avant cette distance (m)")]
    public float startDistance = -30f;

    [Header("Génération")]
    [Tooltip("Distance générée devant le joueur. Restera sous la viewDistance du terrain pour ne pas voir d'arbres flotter.")]
    public float viewDistance = 230f;
    [Tooltip("Distance gardée derrière le joueur avant de recycler un objet")]
    public float despawnDistance = 40f;
    [Tooltip("Longueur de route traitée d'un coup (m). Plus petit = génération plus lissée.")]
    public float sliceLength = 4f;
    [Tooltip("Ombres portées par le décor (coûteux sur mobile)")]
    public bool castShadows = false;
    public int seed = 0;                      // 0 = aléatoire à chaque partie

    [Header("Couches de décor")]
    public DecorLayer[] layers = DefaultLayers();

    const string ModelsFolder = "Assets/_Racer/Models/";

    // Taille d'un prefab à l'échelle 1, dans l'espace de son conteneur (mesurée une fois par prefab)
    struct PrefabInfo
    {
        public float height;     // hauteur totale
        public float bottom;     // y du point le plus bas par rapport au pivot
        public float radius;     // rayon max dans le plan XZ autour du pivot
    }

    class Item
    {
        public Transform t;
        public GameObject prefab;
        public float z;           // Z piste
    }

    readonly List<Item> active = new List<Item>();
    readonly Dictionary<GameObject, Stack<Item>> pools = new Dictionary<GameObject, Stack<Item>>();
    readonly Dictionary<GameObject, PrefabInfo> infos = new Dictionary<GameObject, PrefabInfo>();
    float nextZ;
    bool started;
    System.Random rng;
    float noiseOffset;

    static DecorLayer[] DefaultLayers() => new[]
    {
        new DecorLayer
        {
            name = "Forêt en contrebas", modelNames = new[] { "pinetree" },
            placement = Placement.VoidSide, minDistance = 12f, maxDistance = 150f, maxRelativeHeight = -5f,
            density = 70f, clusterSize = 55f, clusterGaps = 0.35f, minHeight = 7f, maxHeight = 13f
        },
        new DecorLayer
        {
            name = "Pins sur la montagne", modelNames = new[] { "pinetree" },
            placement = Placement.MountainSide, minDistance = 9f, maxDistance = 130f, minRelativeHeight = 3f,
            density = 20f, clusterSize = 40f, clusterGaps = 0.55f, minHeight = 5f, maxHeight = 10f
        },
        new DecorLayer
        {
            name = "Palmiers au fond de la vallée", modelNames = new[] { "palmier_1", "palmier_2" },
            placement = Placement.VoidSide, minDistance = 30f, maxDistance = 150f, maxRelativeHeight = -18f,
            density = 6f, clusterSize = 70f, clusterGaps = 0.6f, minHeight = 7f, maxHeight = 11f
        },
        new DecorLayer
        {
            name = "Amas de rochers près de la route", modelNames = new[] { "pierre_ronde", "pierre_large", "pierre_haute" },
            placement = Placement.BothSides, minDistance = 3f, maxDistance = 25f,
            density = 1.2f, clusterSize = 0f, minHeight = 2f, maxHeight = 4f, maxTilt = 20f, sink = 0.15f,
            groupMin = 3, groupMax = 7, groupRadius = 4f, groupSmallest = 0.3f
        },
        new DecorLayer
        {
            name = "Amas de gros rochers au loin", modelNames = new[] { "pierre_haute", "pierre_large", "pierre_ronde" },
            placement = Placement.BothSides, minDistance = 20f, maxDistance = 160f,
            density = 2f, clusterSize = 0f, minHeight = 6f, maxHeight = 14f, maxTilt = 12f, sink = 0.2f,
            groupMin = 3, groupMax = 8, groupRadius = 10f, groupSmallest = 0.25f
        },
        new DecorLayer
        {
            name = "Rochers isolés", modelNames = new[] { "pierre_ronde", "pierre_large", "pierre_haute" },
            placement = Placement.BothSides, minDistance = 1.5f, maxDistance = 140f,
            density = 6f, clusterSize = 0f, minHeight = 0.8f, maxHeight = 5f, maxTilt = 20f, sink = 0.15f
        },
    };

    void Awake()
    {
        if (road == null) road = FindFirstObjectByType<ZigzagRoadGenerator>();
        if (road == null)
        {
            Debug.LogError("[DecorSpawner] Aucune ZigzagRoadGenerator trouvée dans la scène.", this);
            enabled = false;
            return;
        }
        if (terrain == null) terrain = road.terrain != null ? road.terrain : FindFirstObjectByType<LowPolyTerrainGenerator>();

#if UNITY_EDITOR
        FillPrefabsFromModels();
#endif
        rng = seed == 0 ? new System.Random() : new System.Random(seed);
        noiseOffset = (float)rng.NextDouble() * 10000f;
    }

    void Start()
    {
        road.OnTrackRebased += HandleTrackRebased;
        foreach (DecorLayer layer in layers)
            if (layer != null && layer.enabled && (layer.prefabs == null || layer.prefabs.Length == 0))
                Debug.LogWarning($"[DecorSpawner] La couche \"{layer.name}\" n'a aucun prefab : elle est ignorée.", this);
    }

    void OnDestroy()
    {
        if (road != null) road.OnTrackRebased -= HandleTrackRebased;
    }

#if UNITY_EDITOR
    void OnValidate() => FillPrefabsFromModels();

    // Remplit les listes Prefabs vides avec les FBX nommés dans Model Names (éditeur seulement)
    void FillPrefabsFromModels()
    {
        if (layers == null) return;
        foreach (DecorLayer layer in layers)
        {
            if (layer == null || (layer.prefabs != null && layer.prefabs.Length > 0) || layer.modelNames == null) continue;
            var found = new List<GameObject>();
            foreach (string n in layer.modelNames)
            {
                if (string.IsNullOrEmpty(n)) continue;
                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ModelsFolder + n + ".fbx");
                if (go != null) found.Add(go);
            }
            if (found.Count > 0) layer.prefabs = found.ToArray();
        }
    }
#endif

    void Update()
    {
        // Le tracé de la route n'est prêt qu'une fois la route démarrée (elle se désactive sans joueur)
        if (road == null || !road.enabled || road.player == null) return;
        float pz = road.TrackZ(road.player.position.z);

        if (!started)
        {
            started = true;
            nextZ = Mathf.Max(startDistance, pz - despawnDistance);
        }

        // Pas plus loin que le terrain, sinon les objets apparaissent avant le sol
        float ahead = terrain != null ? Mathf.Min(viewDistance, terrain.viewDistance - 10f) : viewDistance;
        float step = Mathf.Max(0.5f, sliceLength);

        // Quelques tranches par image, sauf si on est très en retard (au lancement)
        int budget = nextZ < pz + ahead * 0.5f ? 1000 : 4;
        while (budget-- > 0 && nextZ < pz + ahead)
        {
            SpawnSlice(nextZ, step);
            nextZ += step;
        }

        // Recycle les objets passés derrière le joueur
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Item it = active[i];
            if (it.t == null) { active.RemoveAt(i); continue; }
            if (it.z < pz - despawnDistance)
            {
                it.t.gameObject.SetActive(false);
                active.RemoveAt(i);
                pools[it.prefab].Push(it);
            }
        }
    }

    // La route a ramené le TrackRoot à 0 : on décale nos objets (invisible, même position monde)
    void HandleTrackRebased(float shift)
    {
        foreach (Item it in active)
        {
            if (it.t != null) it.t.localPosition -= new Vector3(0f, 0f, shift);
            it.z -= shift;
        }
        nextZ -= shift;
    }

    void SpawnSlice(float z0, float length)
    {
        road.SideAt(z0 + length * 0.5f, out float voidLeft, out float voidRight, out _);

        foreach (DecorLayer layer in layers)
        {
            if (layer == null || !layer.enabled || layer.prefabs == null || layer.prefabs.Length == 0) continue;

            for (int s = -1; s <= 1; s += 2)
            {
                bool isVoid = (s < 0 ? voidLeft : voidRight) > 0.5f; // sur un pont : vide des deux côtés
                if (layer.placement == Placement.VoidSide && !isVoid) continue;
                if (layer.placement == Placement.MountainSide && isVoid) continue;

                // Nombre d'essais : partie entière + une chance pour la partie décimale
                float expected = Mathf.Max(0f, layer.density) * length / 100f;
                int count = (int)expected + (rng.NextDouble() < expected - (int)expected ? 1 : 0);
                for (int k = 0; k < count; k++)
                    TryPlace(layer, s, z0 + (float)rng.NextDouble() * length);
            }
        }
    }

    void TryPlace(DecorLayer layer, int side, float z)
    {
        float minD = Mathf.Max(0f, layer.minDistance);
        float e = Mathf.Lerp(minD, Mathf.Max(minD, layer.maxDistance), (float)rng.NextDouble());
        float x = road.CenterXAt(z) + side * (road.HalfWidthXAt(z) + e);

        // Bosquets : un bruit à grande échelle laisse des clairières entre les groupes d'objets
        if (layer.clusterSize > 0.01f)
        {
            float az = (float)((road.OriginZ + z) % 100000.0);
            float n = Mathf.PerlinNoise(noiseOffset + x / layer.clusterSize, noiseOffset + az / layer.clusterSize);
            if (n < layer.clusterGaps) return;
        }

        // Amas : le premier objet est le plus gros, les autres plus petits tout autour
        int min = Mathf.Max(1, layer.groupMin);
        int count = rng.Next(min, Mathf.Max(min, layer.groupMax) + 1);
        float height = Mathf.Lerp(layer.minHeight, Mathf.Max(layer.minHeight, layer.maxHeight), (float)rng.NextDouble());
        for (int i = 0; i < count; i++)
        {
            if (i == 0) { PlaceOne(layer, side, z, e, height); continue; }
            float a = Rand(0f, Mathf.PI * 2f);
            float d = Mathf.Max(0f, layer.groupRadius) * Mathf.Sqrt((float)rng.NextDouble());
            float me = e + Mathf.Cos(a) * d;
            if (me < 0f) continue; // côté route : on ne déborde pas vers elle
            PlaceOne(layer, side, z + Mathf.Sin(a) * d, me, height * Rand(layer.groupSmallest, 0.85f));
        }
    }

    void PlaceOne(DecorLayer layer, int side, float z, float e, float height)
    {
        float x = road.CenterXAt(z) + side * (road.HalfWidthXAt(z) + e);
        float ground = terrain != null ? terrain.GroundHeight(z, side, e) : road.RoadHeightAt(z);
        float rel = ground - road.RoadHeightAt(z);
        if (rel < layer.minRelativeHeight || rel > layer.maxRelativeHeight) return;

        GameObject prefab = layer.prefabs[rng.Next(layer.prefabs.Length)];
        if (prefab == null) return;
        PrefabInfo info = GetInfo(prefab);

        float scale = height / info.height;
        if (!IsClearOfRoad(x, z, info.radius * scale + roadClearance)) return;

        Item it = GetItem(prefab, layer.overrideMaterial);
        float tilt = layer.maxTilt;
        it.t.localRotation = Quaternion.Euler(Rand(-tilt, tilt), Rand(0f, 360f), Rand(-tilt, tilt));
        it.t.localScale = Vector3.one * scale;
        it.t.localPosition = new Vector3(x, ground - (info.bottom + info.height * layer.sink) * scale, z);
        it.z = z;
        it.t.gameObject.SetActive(true);
        active.Add(it);
    }

    float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();

    // Vrai si le cercle (x, z, radius) ne touche pas la route. La route est une fonction x = centre(z) :
    // un point de la route à dz d'écart en Z est au moins à |dz| de distance, donc on ne teste que |dz| <= radius.
    bool IsClearOfRoad(float x, float z, float radius)
    {
        const float step = 1f;
        for (float dz = -radius; dz <= radius + 0.001f; dz += step)
        {
            float rz = z + dz;
            float dx = Mathf.Max(0f, Mathf.Abs(x - road.CenterXAt(rz)) - road.HalfWidthXAt(rz));
            if (dx * dx + dz * dz < radius * radius) return false;
        }
        return true;
    }

    // ---------- Pool ----------

    Item GetItem(GameObject prefab, Material overrideMaterial)
    {
        if (!pools.TryGetValue(prefab, out Stack<Item> pool))
            pools[prefab] = pool = new Stack<Item>();

        while (pool.Count > 0)
        {
            Item pooled = pool.Pop();
            if (pooled.t != null) return pooled;
        }

        // Conteneur (position, rotation, échelle) qui garde le transform d'origine du FBX (rotation d'import Blender...)
        var holder = new GameObject(prefab.name);
        holder.SetActive(false);
        holder.transform.SetParent(road.TrackRoot, false);
        GameObject model = Instantiate(prefab, holder.transform, false);

        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = castShadows;
            if (overrideMaterial != null)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = overrideMaterial;
                r.sharedMaterials = mats;
            }
        }

        return new Item { t = holder.transform, prefab = prefab };
    }

    // Mesure le prefab à partir de ses meshes (pas besoin de l'instancier ni de l'activer)
    PrefabInfo GetInfo(GameObject prefab)
    {
        if (infos.TryGetValue(prefab, out PrefabInfo cached)) return cached;

        bool any = false;
        Vector3 min = Vector3.zero, max = Vector3.zero;
        float radius = 0f;
        foreach (Component comp in prefab.GetComponentsInChildren<Component>(true))
        {
            Mesh mesh = comp is MeshFilter mf ? mf.sharedMesh : comp is SkinnedMeshRenderer smr ? smr.sharedMesh : null;
            if (mesh == null) continue;

            // Matrice du mesh dans l'espace du conteneur (la racine du prefab garde son transform local)
            Matrix4x4 m = Matrix4x4.TRS(prefab.transform.localPosition, prefab.transform.localRotation, prefab.transform.localScale)
                        * prefab.transform.worldToLocalMatrix * comp.transform.localToWorldMatrix;
            Bounds b = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (!any) { min = max = p; any = true; }
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                radius = Mathf.Max(radius, new Vector2(p.x, p.z).magnitude);
            }
        }

        var info = any && max.y - min.y > 0.001f
            ? new PrefabInfo { height = max.y - min.y, bottom = min.y, radius = radius }
            : new PrefabInfo { height = 1f, bottom = 0f, radius = 0.5f };
        infos[prefab] = info;
        return info;
    }
}
