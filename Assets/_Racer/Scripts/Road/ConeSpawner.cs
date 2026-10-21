using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Place des cônes le long de la route infinie (ZigzagRoadGenerator), au fur et à mesure qu'elle est générée.
/// Ne modifie pas la route : on lit simplement son tracé (CenterXAt, RoadHeightAt) comme le fait le terrain.
///
/// Les cônes sont enfants du TrackRoot de la route : ils défilent avec elle.
/// Ils sont réutilisés (pool) : Cone.OnEnable les remet "neufs", donc chaque cône peut redonner du multiplicateur.
/// Un cône touché quitte le TrackRoot le temps de son vol (voir Cone), puis y revient une fois posé.
///
/// Mise en place : GameObject vide "ConeSpawner" avec ce script, et assigne le prefab Cone (Assets/_Racer/Prefabs/Cone.prefab).
/// La route est trouvée automatiquement. Sans prefab, un cube orange avec le script Cone est créé.
/// Assigne aussi "Hit Effect" (ConeHit.vfx) pour l'effet d'impact (voir ConeHitFX).
/// </summary>
public class ConeSpawner : MonoBehaviour
{
    [Header("Références")]
    public ZigzagRoadGenerator road;
    [Tooltip("Prefab du cône (doit avoir le script Cone). Facultatif : un cube est créé sinon.")]
    public Cone conePrefab;
    [Tooltip("Effet VFX Graph joué quand la voiture tape un cône (ConeHit.vfx, événement OnConeHit). Facultatif.")]
    public VisualEffectAsset hitEffect;

    [Header("Placement")]
    [Tooltip("Pas de cône avant cette distance (laisse le temps au joueur de démarrer)")]
    public float startDistance = 40f;
    [Tooltip("Écart minimum entre deux groupes de cônes (en m, le long de la route)")]
    public float minGap = 15f;
    [Tooltip("Écart maximum entre deux groupes de cônes")]
    public float maxGap = 35f;
    [Range(1, 5)]
    [Tooltip("Nombre max de cônes alignés dans un même groupe (en travers de la route)")]
    public int maxConesPerGroup = 2;
    [Tooltip("Distance minimale entre un cône et le bord de la route")]
    public float edgeMargin = 1f;
    [Tooltip("Hauteur du centre du cône au-dessus de la route (0.5 pour un cube de 1 m)")]
    public float heightOffset = 0.5f;
    [Tooltip("Le cône est tourné dans le sens de la route")]
    public bool alignWithRoad = true;

    [Header("Génération")]
    [Tooltip("Distance générée devant le joueur (garder <= viewDistance de la route)")]
    public float viewDistance = 150f;
    [Tooltip("Distance gardée derrière le joueur avant de recycler un cône")]
    public float despawnDistance = 20f;
    public int seed = 0;                      // 0 = aléatoire à chaque partie

    readonly List<Cone> active = new List<Cone>();
    readonly Stack<Cone> pool = new Stack<Cone>();
    float nextZ;                 // position Z (locale au TrackRoot) du prochain groupe
    System.Random rng;
    Material fallbackMat;

    void Awake()
    {
        if (road == null) road = FindFirstObjectByType<ZigzagRoadGenerator>();
        if (road == null)
        {
            Debug.LogError("[ConeSpawner] Aucune ZigzagRoadGenerator trouvée dans la scène.", this);
            enabled = false;
            return;
        }

        rng = seed == 0 ? new System.Random() : new System.Random(seed);
    }

    void Start()
    {
        nextZ = startDistance;
        road.OnTrackRebased += HandleTrackRebased;
        if (hitEffect != null) ConeHitFX.Create(road, hitEffect);
    }

    void OnDestroy()
    {
        if (road != null) road.OnTrackRebased -= HandleTrackRebased;
        if (fallbackMat != null) Destroy(fallbackMat);
    }

    void Update()
    {
        // Le tracé de la route n'est prêt qu'une fois la route démarrée (elle se désactive sans joueur)
        if (road == null || !road.enabled || road.player == null) return;
        float pz = road.TrackZ(road.player.position.z);

        while (nextZ < pz + viewDistance)
        {
            SpawnGroup(nextZ);
            nextZ += Mathf.Lerp(minGap, Mathf.Max(minGap, maxGap), (float)rng.NextDouble());
        }

        // Recycle les cônes passés derrière le joueur
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Cone c = active[i];
            if (c == null) { active.RemoveAt(i); continue; }
            // Position monde convertie en Z piste : marche aussi pour un cône en plein vol (sorti du TrackRoot)
            if (road.TrackZ(c.transform.position.z) < pz - despawnDistance)
            {
                c.gameObject.SetActive(false);
                active.RemoveAt(i);
                pool.Push(c);
            }
        }
    }

    // La route a ramené le TrackRoot à 0 : on décale nos cônes (invisible, même position monde)
    void HandleTrackRebased(float shift)
    {
        // Un cône en plein vol n'est plus enfant du TrackRoot : sa position monde ne change pas, on n'y touche pas
        foreach (Cone c in active)
            if (c != null && c.transform.parent == road.TrackRoot) c.transform.localPosition -= new Vector3(0f, 0f, shift);
        nextZ -= shift;
    }

    void SpawnGroup(float z)
    {
        // Demi-largeur mesurée en X (plus grande dans les diagonales), marge comprise
        float halfX = road.HalfWidthXAt(z);
        float halfUsable = Mathf.Max(0f, halfX - edgeMargin * halfX / (road.WidthAt(z) * 0.5f));
        int count = rng.Next(1, Mathf.Max(1, maxConesPerGroup) + 1);

        float centerX = road.CenterXAt(z);
        float y = road.RoadHeightAt(z) + heightOffset;
        Quaternion rot = Quaternion.identity;
        if (alignWithRoad)
        {
            float dx = road.CenterXAt(z + 1f) - road.CenterXAt(z - 1f);
            rot = Quaternion.Euler(0f, Mathf.Atan2(dx, 2f) * Mathf.Rad2Deg, 0f);
        }

        // Les cônes d'un groupe sont répartis en travers de la route, chacun dans sa propre "voie"
        float laneWidth = 2f * halfUsable / count;
        for (int i = 0; i < count; i++)
        {
            float laneStart = -halfUsable + i * laneWidth;
            float offset = laneStart + (float)rng.NextDouble() * laneWidth;

            Cone c = GetCone();
            c.transform.localPosition = new Vector3(centerX + offset, y, z);
            c.transform.localRotation = rot;
            c.gameObject.SetActive(true);
            active.Add(c);
        }
    }

    Cone GetCone()
    {
        while (pool.Count > 0)
        {
            Cone pooled = pool.Pop();
            if (pooled == null) continue;
            // Recyclé en plein vol : on le remet dans le TrackRoot avant de le replacer
            if (pooled.transform.parent != road.TrackRoot) pooled.transform.SetParent(road.TrackRoot, false);
            return pooled;
        }

        Cone c = conePrefab != null ? Instantiate(conePrefab) : CreateFallbackCone();
        c.gameObject.SetActive(false);
        c.transform.SetParent(road.TrackRoot, false);
        return c;
    }

    Cone CreateFallbackCone()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); // a déjà un BoxCollider
        go.name = "Cone";
        if (fallbackMat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            fallbackMat = new Material(sh) { color = new Color(1f, 0.45f, 0f) };
        }
        go.GetComponent<MeshRenderer>().sharedMaterial = fallbackMat;
        return go.AddComponent<Cone>();
    }
}
