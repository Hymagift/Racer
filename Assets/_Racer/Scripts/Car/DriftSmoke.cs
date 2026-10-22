using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fumée de drift STYLISÉE sous les roues ARRIÈRE (technique "Stylized Smoke" de Gabriel Aguiar,
/// refaite avec un Particle System pour rester léger sur mobile) :
/// - chaque bouffée est une petite boule 3D bosselée (mesh généré ici), opaque, éclairée en 2 tons blanc / gris ;
/// - elle gonfle vite, monte un peu, puis disparaît rongée par un bruit (érosion, shader Racer/StylizedSmoke).
///
/// Mise en place : rien à faire, le script est déjà sur le prefab Car (à côté de DriftCar).
/// Au lancement il crée deux Particle Systems au ras du sol sous les roues arrière (os "wheel_back.L" / "wheel_back.R"
/// de 1stCar, ou n'importe quelle roue dont le nom contient "back", "rear" ou "arr"), replacés si le modèle de la voiture
/// change (Garage), puis les donne à DriftCar (champ "Tire Smoke") qui s'occupe déjà :
/// - de les faire défiler avec le décor (TrackRoot), sinon la fumée resterait collée à la voiture ;
/// - de régler leur débit selon l'angle de drift et la vitesse (max = "Smoke Max Rate" sur DriftCar).
///
/// Si "Tire Smoke" est déjà rempli à la main sur DriftCar, ce script ne fait rien.
/// </summary>
public class DriftSmoke : MonoBehaviour
{
    [Header("Références")]
    [Tooltip("Trouvé automatiquement sur ce GameObject ou ses parents")]
    public DriftCar car;
    [Tooltip("Matériau des boules de fumée (shader Racer/StylizedSmoke). Fourni : Materials/DriftSmoke")]
    public Material smokeMaterial;
    [Tooltip("Vide = recherche automatique des roues arrière par nom")]
    public Transform[] rearWheels;

    [Header("Recherche automatique")]
    [Tooltip("Mots cherchés dans le nom des roues (minuscules)")]
    public string[] wheelKeywords = { "wheel", "roue", "tire", "pneu" };
    [Tooltip("Mots qui désignent l'arrière (minuscules)")]
    public string[] rearKeywords = { "back", "rear", "arr" };

    [Header("Look")]
    [Tooltip("Teinte des boules (aléatoire entre A et B). L'ombre et la lumière sont réglées sur le matériau.")]
    public Color colorA = Color.white;
    public Color colorB = new Color(0.85f, 0.85f, 0.85f, 1f);
    [Tooltip("Rayon des boules une fois gonflées (m)")]
    public Vector2 startSize = new Vector2(0.3f, 0.55f);
    [Tooltip("Durée de vie (s)")]
    public Vector2 lifetime = new Vector2(0.7f, 1.1f);
    [Tooltip("Moment de la vie (0..1) où l'érosion commence à ronger la boule")]
    [Range(0f, 0.9f)] public float erosionStart = 0.3f;
    [Tooltip("Hauteur de l'émetteur au-dessus du bas de la voiture (m)")]
    public float groundOffset = 0.15f;
    public int maxParticles = 80;
    [Tooltip("Finesse des boules : 1 = 80 triangles (léger, mobile), 2 = 320 triangles (plus rondes, 4x plus de triangles)")]
    [Range(1, 2)] public int puffDetail = 1;

    static readonly Mesh[] puffMeshes = new Mesh[3];

    readonly List<ParticleSystem> systems = new List<ParticleSystem>();
    Transform placedFor;   // modèle pour lequel les émetteurs ont été placés

    void Awake()
    {
        if (car == null) car = GetComponentInParent<DriftCar>();
        if (car == null)
        {
            Debug.LogError("[DriftSmoke] Aucun DriftCar trouvé sur la voiture.", this);
            enabled = false;
            return;
        }
        // Fumée déjà réglée à la main : on n'y touche pas
        if (car.tireSmoke != null && car.tireSmoke.Length > 0) { enabled = false; return; }

        // Créée dès Awake : DriftCar (Start) la met dans le repère du décor et règle son débit
        systems.Add(CreateSmoke("DriftSmoke_L", Vector3.zero));
        systems.Add(CreateSmoke("DriftSmoke_R", Vector3.zero));
        car.tireSmoke = systems.ToArray();
    }

    // Le modèle peut être remplacé (CarModelSwap, Garage) : on place les émetteurs une fois tous les Awake passés,
    // puis à nouveau à chaque changement de modèle.
    void Start() => PlaceEmitters();

    void LateUpdate()
    {
        if (car.body != placedFor) PlaceEmitters();
    }

    void PlaceEmitters()
    {
        placedFor = car.body;
        Transform carT = car.transform;
        Transform model = car.body != null ? car.body : carT;
        float bottomY = CarBottomY(model);

        Transform[] wheels = rearWheels != null && rearWheels.Length > 0 ? rearWheels : FindRearWheels(model);
        var points = new List<Vector3>();
        foreach (Transform w in wheels)
        {
            if (w == null) continue;
            Vector3 p = w.position;
            p.y = bottomY + groundOffset;
            points.Add(carT.InverseTransformPoint(p));
        }
        if (points.Count == 0)
        {
            // Pas de roue trouvée : deux points sous l'essieu arrière
            Debug.LogWarning("[DriftSmoke] Aucune roue arrière trouvée : fumée placée sous l'essieu arrière.", this);
            float y = carT.InverseTransformPoint(new Vector3(0f, bottomY + groundOffset, 0f)).y;
            points.Add(new Vector3(-0.7f, y, -car.rearAxle));
            points.Add(new Vector3(0.7f, y, -car.rearAxle));
        }
        // Gauche puis droite
        points.Sort((u, v) => u.x.CompareTo(v.x));

        for (int i = 0; i < systems.Count; i++)
            systems[i].transform.localPosition = points[Mathf.Min(i, points.Count - 1)];
    }

    // Roues dont le nom contient un mot "roue" + un mot "arrière" ; sinon les roues les plus en arrière
    Transform[] FindRearWheels(Transform model)
    {
        var wheels = new List<Transform>();
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (t != car.transform && Contains(t.name, wheelKeywords) && !t.name.ToLowerInvariant().Contains("_end"))
                wheels.Add(t);

        var rear = wheels.FindAll(t => Contains(t.name, rearKeywords));
        if (rear.Count == 0 && wheels.Count >= 4)
        {
            float minZ = float.MaxValue;
            foreach (Transform t in wheels) minZ = Mathf.Min(minZ, car.transform.InverseTransformPoint(t.position).z);
            rear = wheels.FindAll(t => car.transform.InverseTransformPoint(t.position).z < minZ + 0.2f);
        }

        // Un seul émetteur par côté (évite les doublons os parent / os enfant)
        var result = new List<Transform>();
        foreach (Transform t in rear)
        {
            bool dup = false;
            foreach (Transform r in result)
                if (Vector3.Distance(r.position, t.position) < 0.3f) { dup = true; break; }
            if (!dup) result.Add(t);
        }

        foreach (Transform t in result) Debug.Log($"[DriftSmoke] Fumée sur : {t.name}", t);
        return result.ToArray();
    }

    static bool Contains(string objName, string[] keywords)
    {
        string n = objName.ToLowerInvariant();
        foreach (string k in keywords)
            if (!string.IsNullOrEmpty(k) && n.Contains(k)) return true;
        return false;
    }

    float CarBottomY(Transform model)
    {
        float y = float.MaxValue;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            if (!(r is ParticleSystemRenderer)) y = Mathf.Min(y, r.bounds.min.y);
        return y == float.MaxValue ? car.transform.position.y : y;
    }

    ParticleSystem CreateSmoke(string objName, Vector3 localPos)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(car.transform, false);
        go.transform.localPosition = localPos;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // obligatoire avant de changer la durée

        var main = ps.main;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(startSize.x, startSize.y);
        // Rotation 3D aléatoire : chaque boule montre une autre face du bruit d'érosion
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.gravityModifier = -0.12f;           // la fumée monte un peu
        main.maxParticles = maxParticles;
        main.playOnAwake = true;

        var emission = ps.emission;
        emission.rateOverTime = 0f;              // piloté par DriftCar selon le drift

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.2f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // demi-sphère tournée vers le haut

        // Freine vite après la sortie de la roue (effet "bouffée")
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.3f;
        limit.dampen = 0.15f;

        // Gonfle très vite au début puis grossit encore un peu
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        var grow = new AnimationCurve(new Keyframe(0f, 0.3f, 0f, 6f), new Keyframe(0.2f, 1f, 1f, 0.3f), new Keyframe(1f, 1.2f));
        size.size = new ParticleSystem.MinMaxCurve(1f, grow);

        // L'alpha ne sert PAS de transparence : le shader s'en sert pour l'érosion (1 = pleine, 0 = rongée)
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var erosion = new Gradient();
        erosion.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, erosionStart), new GradientAlphaKey(0f, 1f) });
        col.color = erosion;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-1f, 1f); // rad/s
        rot.y = new ParticleSystem.MinMaxCurve(-1f, 1f);
        rot.z = new ParticleSystem.MinMaxCurve(-1f, 1f);

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Mesh;
        rend.mesh = PuffMesh(puffDetail);
        rend.alignment = ParticleSystemRenderSpace.World;
        rend.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color
        });
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        if (smokeMaterial != null) rend.sharedMaterial = smokeMaterial;
        else Debug.LogWarning("[DriftSmoke] 'Smoke Material' vide : les particules seront roses.", this);

        ps.Play();
        return ps;
    }

    // ---------- Mesh d'une bouffée : icosphère (rayon ~1) déformée par quelques bosses ----------

    static Mesh PuffMesh(int detail)
    {
        detail = Mathf.Clamp(detail, 1, 2);
        if (puffMeshes[detail] != null) return puffMeshes[detail];

        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var verts = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
        var tris = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        // 1 subdivision : 80 triangles (2 × 80 boules max = 12 800 triangles) ; 2 subdivisions : 320 triangles, plus rond
        for (int s = 0; s < detail; s++)
        {
            var mid = new Dictionary<long, int>();
            var next = new List<int>(tris.Count * 4);
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(verts, mid, a, b), bc = Mid(verts, mid, b, c), ca = Mid(verts, mid, c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tris = next;
        }

        // Bosses : la boule ressemble à un petit nuage
        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 d = verts[i];
            float bump = 0.18f * Mathf.Sin(3.1f * d.x + 1.3f) * Mathf.Sin(2.7f * d.y + 0.4f) * Mathf.Sin(3.3f * d.z + 2.1f)
                       + 0.08f * Mathf.Sin(6.2f * d.y + 2.9f * d.x);
            verts[i] = d * (1f + bump);
        }

        var puffMesh = new Mesh { name = "DriftSmokePuff" };
        puffMesh.SetVertices(verts);
        puffMesh.SetTriangles(tris, 0);
        puffMesh.RecalculateNormals();
        puffMesh.RecalculateBounds();
        puffMeshes[detail] = puffMesh;
        return puffMesh;
    }

    static int Mid(List<Vector3> verts, Dictionary<long, int> cache, int a, int b)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (cache.TryGetValue(key, out int index)) return index;
        verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
        cache[key] = verts.Count - 1;
        return verts.Count - 1;
    }
}
