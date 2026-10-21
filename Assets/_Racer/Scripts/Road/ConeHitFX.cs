using UnityEngine;

/// <summary>
/// Petit effet d'impact jaune (Particle System) quand la voiture tape un cône. Tout est créé par code, rien à monter.
///
/// Deux systèmes : des bouffées rondes qui gonflent puis rétrécissent, et des étincelles étirées qui giclent plus loin.
/// Ils simulent dans l'espace du TrackRoot (simulation space Custom) : les particules défilent avec la route,
/// et déplacer l'émetteur au point d'impact suivant ne traîne pas celles déjà émises.
///
/// Mise en place : le ConeSpawner le crée tout seul (case "Hit Particles"), avec le matériau ConeHitParticle.
/// </summary>
public class ConeHitFX : MonoBehaviour
{
    [Header("Couleur")]
    public Color color = new Color(1f, 0.85f, 0.1f, 1f);

    [Header("Bouffées")]
    public int puffCount = 8;
    public Vector2 puffSize = new Vector2(0.25f, 0.45f);
    public Vector2 puffSpeed = new Vector2(1.5f, 3.5f);
    public Vector2 puffLifetime = new Vector2(0.3f, 0.45f);

    [Header("Étincelles")]
    public int sparkCount = 12;
    public Vector2 sparkSize = new Vector2(0.06f, 0.1f);
    public Vector2 sparkSpeed = new Vector2(5f, 9f);
    public Vector2 sparkLifetime = new Vector2(0.2f, 0.35f);

    [Header("Direction")]
    [Tooltip("Penche le jet vers l'avant de la voiture (0 = droit vers le haut, 1 = 45°)")]
    public float forwardTilt = 0.6f;
    [Tooltip("Ouverture du jet (degrés)")]
    public float spreadAngle = 55f;

    static ConeHitFX instance;

    ZigzagRoadGenerator road;
    ParticleSystem puffs;
    ParticleSystem sparks;
    ParticleSystem.Particle[] buffer;

    /// <summary>Crée l'effet sous le TrackRoot de la route (appelé par le ConeSpawner).</summary>
    public static ConeHitFX Create(ZigzagRoadGenerator road, Material material)
    {
        var go = new GameObject("ConeHitFX");
        go.transform.SetParent(road.TrackRoot, false);
        go.SetActive(false); // OnEnable après la config
        var fx = go.AddComponent<ConeHitFX>();
        fx.road = road;
        fx.Build(material);
        go.SetActive(true);
        return fx;
    }

    void Build(Material material)
    {
        if (material == null)
        {
            Shader sh = Shader.Find("Racer/ParticleSoft");
            if (sh != null) material = new Material(sh);
        }

        puffs = MakeSystem("Puffs", material, false);
        sparks = MakeSystem("Sparks", material, true);
    }

    ParticleSystem MakeSystem(string name, Material material, bool stretched)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.maxParticles = 128;
        main.simulationSpace = ParticleSystemSimulationSpace.Custom;
        main.customSimulationSpace = road.TrackRoot;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startColor = color;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = stretched ? 1.5f : 0.3f;
        Vector2 size = stretched ? sparkSize : puffSize;
        Vector2 speed = stretched ? sparkSpeed : puffSpeed;
        Vector2 life = stretched ? sparkLifetime : puffLifetime;
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);

        var emission = ps.emission;
        emission.enabled = false; // on émet à la main (Emit)

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone; // ouvert vers +Z de l'émetteur
        shape.angle = spreadAngle;
        shape.radius = 0.15f;

        // Les bouffées gonflent vite puis fondent, les étincelles rétrécissent
        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        AnimationCurve curve = stretched
            ? AnimationCurve.Linear(0f, 1f, 1f, 0f)
            : new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f));
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = gradient;

        // Freinage : effet de souffle, puis ça retombe
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 1000f; // pas de plafond, seulement le drag
        limit.drag = stretched ? 2f : 5f;

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (stretched)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 1.5f;
        }
        else
        {
            r.renderMode = ParticleSystemRenderMode.Billboard;
        }
        return ps;
    }

    void OnEnable()
    {
        instance = this;
        if (road != null) road.OnTrackRebased += HandleTrackRebased;
    }

    void OnDisable()
    {
        if (instance == this) instance = null;
        if (road != null) road.OnTrackRebased -= HandleTrackRebased;
    }

    // Le TrackRoot est ramené à 0 : on décale l'émetteur et les particules vivantes (même position monde)
    void HandleTrackRebased(float shift)
    {
        transform.localPosition -= new Vector3(0f, 0f, shift);
        Shift(puffs, shift);
        Shift(sparks, shift);
    }

    void Shift(ParticleSystem ps, float shift)
    {
        if (ps == null) return;
        if (buffer == null || buffer.Length < ps.main.maxParticles) buffer = new ParticleSystem.Particle[ps.main.maxParticles];
        int n = ps.GetParticles(buffer);
        for (int i = 0; i < n; i++) buffer[i].position -= new Vector3(0f, 0f, shift);
        ps.SetParticles(buffer, n);
    }

    /// <summary>Joue l'impact au point donné (monde). roadVelocity : vitesse de la voiture par rapport à la route (monde).</summary>
    public static void Play(Vector3 worldPoint, Vector3 roadVelocity)
    {
        if (instance == null) return;
        instance.Emit(worldPoint, roadVelocity);
    }

    void Emit(Vector3 worldPoint, Vector3 roadVelocity)
    {
        // Le jet part vers le haut, penché dans le sens où va la voiture
        Vector3 forward = roadVelocity;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.zero;
        Vector3 dir = (Vector3.up + forward * forwardTilt).normalized;

        Vector3 upHint = forward.sqrMagnitude > 0f ? -forward : Vector3.forward; // jamais parallèle à dir
        transform.SetPositionAndRotation(worldPoint, Quaternion.LookRotation(dir, upHint));
        if (puffs != null) puffs.Emit(puffCount);
        if (sparks != null) sparks.Emit(sparkCount);
    }
}
