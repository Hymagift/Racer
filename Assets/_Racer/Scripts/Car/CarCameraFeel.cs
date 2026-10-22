using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// "Ressenti" caméra : petits mouvements ajoutés PAR-DESSUS la caméra Cinemachine existante (elle n'est pas remplacée),
/// pour que le joueur sente ce que vit la voiture.
///
/// - Vitesse : le champ de vision s'élargit, la caméra se rapproche un peu et vibre légèrement.
/// - Virages / drift : la caméra s'incline dans le virage, se décale du côté où la voiture glisse et vibre en drift.
/// - Chocs : secousse (cônes, murs, chute) et "coup" de zoom (cônes, nouveau palier du multiplicateur).
///
/// À mettre sur le GameObject de la CinemachineCamera (déjà fait dans la scène PL). S'il manque, il est ajouté
/// automatiquement au chargement de la scène. Tous les réglages sont modifiables en jeu dans l'Inspector ;
/// "Intensité" règle tout d'un coup (0 = désactivé). Rien pendant la démo du menu.
/// </summary>
[AddComponentMenu("Racer/Car Camera Feel")]
public class CarCameraFeel : CinemachineExtension
{
    [Header("Global")]
    [Tooltip("Multiplie tous les effets (0 = caméra d'origine, 1 = normal, 2 = très fort)")]
    [Range(0f, 2f)] public float intensity = 1f;

    [Header("Vitesse")]
    [Tooltip("Degrés de champ de vision ajoutés à vitesse max")]
    public float speedFov = 6f;
    [Tooltip("Mètres dont la caméra se rapproche de la voiture à vitesse max")]
    public float speedDolly = 1f;
    [Tooltip("Vibration permanente à vitesse max (degrés)")]
    public float speedRumble = 0.12f;

    [Header("Virages / drift")]
    [Tooltip("Inclinaison de la caméra dans les virages (degrés). Négatif = dans l'autre sens")]
    public float turnRoll = 2.5f;
    [Tooltip("Inclinaison en plus quand la voiture drifte à fond (degrés)")]
    public float driftRoll = 2f;
    [Tooltip("Vitesse de rotation de la voiture (degrés/s) qui donne l'inclinaison maximale")]
    public float yawRateForMaxRoll = 90f;
    [Tooltip("Décalage de la caméra du côté où la voiture se déplace (mètres) : laisse voir plus loin devant")]
    public float lateralLead = 0.8f;
    [Tooltip("Vitesse latérale de la voiture (m/s) qui donne le décalage maximal")]
    public float leadSpeed = 10f;
    [Tooltip("Degrés de champ de vision ajoutés pendant un drift à fond")]
    public float driftFov = 2.5f;
    [Tooltip("Vibration pendant un drift à fond (degrés), comme les pneus qui accrochent")]
    public float driftRumble = 0.25f;
    [Tooltip("Douceur de l'inclinaison et du décalage (plus grand = plus réactif)")]
    public float smoothing = 4f;

    [Header("Chocs")]
    [Tooltip("Secousse quand on touche un cône (0 à 1)")]
    [Range(0f, 1f)] public float coneShake = 0.25f;
    [Tooltip("Coup de zoom quand on touche un cône (degrés de champ de vision)")]
    public float coneFovKick = 2f;
    [Tooltip("Secousse quand on tape un mur (0 à 1)")]
    [Range(0f, 1f)] public float wallShake = 0.6f;
    [Tooltip("Coup d'inclinaison quand on tape un mur (degrés)")]
    public float wallRollKick = 4f;
    [Tooltip("Secousse à la mort / chute (0 à 1)")]
    [Range(0f, 1f)] public float deathShake = 0.8f;
    [Tooltip("Coup de zoom à chaque nouveau palier du multiplicateur (degrés), un peu plus fort à chaque palier")]
    public float levelUpFovKick = 2.5f;
    [Tooltip("Secousse à chaque nouveau palier du multiplicateur (0 à 1)")]
    [Range(0f, 1f)] public float levelUpShake = 0.12f;

    [Header("Secousse")]
    [Tooltip("Rotation maximale d'une secousse à fond (degrés)")]
    public float maxShakeAngle = 2.2f;
    [Tooltip("Déplacement maximal d'une secousse à fond (mètres)")]
    public float maxShakeOffset = 0.3f;
    [Tooltip("Fréquence de la secousse (plus grand = plus nerveux)")]
    public float shakeFrequency = 22f;
    [Tooltip("Vitesse à laquelle la secousse s'arrête (par seconde)")]
    public float shakeDecay = 1.8f;

    /// <summary>Dernière caméra active (pour les appels depuis d'autres scripts).</summary>
    public static CarCameraFeel Instance { get; private set; }

    DriftCar car;
    ScoreManager score;
    float trauma;              // 0..1, la secousse vaut trauma² (les petits chocs restent petits)
    float roll, lead, speedK, driftK, live;
    float fovKick, fovKickVel, rollKick, rollKickVel;
    float lastYaw, lastX, time;
    bool hasLast;

    // Résultat appliqué à la caméra par Cinemachine
    Vector3 outOffset;
    Vector3 outEuler;
    float outFov;

    // ---------- API ----------

    /// <summary>Ajoute une secousse (0 à 1, cumulable).</summary>
    public static void Shake(float amount)
    {
        if (Instance != null) Instance.trauma = Mathf.Clamp01(Instance.trauma + amount);
    }

    /// <summary>Coup de zoom (degrés de champ de vision ; positif = la vue s'élargit puis revient).</summary>
    public static void FovKick(float degrees)
    {
        if (Instance != null) Instance.fovKickVel += degrees * 30f;
    }

    /// <summary>Nouveau palier du multiplicateur (appelé par le ComboHUD).</summary>
    public static void LevelUp(int level)
    {
        if (Instance == null) return;
        float k = 1f + 0.15f * Mathf.Max(0, level - 2);
        FovKick(Instance.levelUpFovKick * k);
        Shake(Instance.levelUpShake * k);
    }

    // ---------- Installation ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AutoInstall();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => AutoInstall();

    // Si la scène a une voiture et une CinemachineCamera sans ce composant, on l'ajoute
    static void AutoInstall()
    {
        if (FindFirstObjectByType<DriftCar>() == null) return;
        foreach (var vcam in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            if (vcam.GetComponent<CarCameraFeel>() == null) vcam.gameObject.AddComponent<CarCameraFeel>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Instance = this;
        hasLast = false;
    }

    protected override void OnDestroy()
    {
        Unhook();
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    void Hook()
    {
        if (car == null)
        {
            car = FindFirstObjectByType<DriftCar>();
            if (car != null) car.OnWallHit += HandleWallHit;
        }
        if (score == null)
        {
            score = ScoreManager.Instance;
            if (score != null) score.OnCone += HandleCone;
        }
    }

    void Unhook()
    {
        if (car != null) car.OnWallHit -= HandleWallHit;
        if (score != null) score.OnCone -= HandleCone;
        car = null;
        score = null;
    }

    void HandleCone()
    {
        Shake(coneShake);
        FovKick(coneFovKick);
    }

    void HandleWallHit()
    {
        Shake(wallShake);
        // Coup d'inclinaison du côté opposé au virage en cours (comme si la caméra encaissait le choc)
        rollKickVel += (roll >= 0f ? -1f : 1f) * wallRollKick * 30f;
    }

    /// <summary>Appelé par le DriftGameManager à la mort.</summary>
    public static void Death() => Shake(Instance != null ? Instance.deathShake : 0f);

    // ---------- Calcul (une fois par image) ----------

    void Update()
    {
        if (Instance != this && isActiveAndEnabled) Instance = this;
        Hook();

        // Les chocs se calment même pendant la pause (temps réel), le reste suit le temps du jeu
        float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float dt = Time.deltaTime;

        bool running = car != null && car.isActiveAndEnabled && Time.timeScale > 0f && !MenuBackground.DemoRunning
                       && (score == null || score.IsRunning);
        live = Mathf.MoveTowards(live, running ? 1f : 0f, udt * 2f);

        float yawRate = 0f, latSpeed = 0f;
        if (car != null && dt > 0f)
        {
            float yaw = car.transform.eulerAngles.y, x = car.transform.position.x;
            if (hasLast)
            {
                yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
                latSpeed = (x - lastX) / dt;
            }
            lastYaw = yaw;
            lastX = x;
            hasLast = true;
        }

        float s = 1f - Mathf.Exp(-smoothing * udt);
        if (car != null && running)
        {
            float maxSpeed = Mathf.Max(1f, car.maxSpeed);
            speedK = Mathf.Lerp(speedK, Mathf.Clamp01(car.Speed / maxSpeed), s);
            float dk = car.IsDrifting ? Mathf.Clamp01(Mathf.Abs(car.DriftAngle) / Mathf.Max(1f, car.maxDriftAngle)) : 0f;
            driftK = Mathf.Lerp(driftK, dk, s);
            float turn = Mathf.Clamp(yawRate / Mathf.Max(1f, yawRateForMaxRoll), -1f, 1f);
            // Tourner à droite (yaw qui augmente) = la caméra penche à droite (roll négatif)
            roll = Mathf.Lerp(roll, -turn * (turnRoll + driftRoll * driftK), s);
            lead = Mathf.Lerp(lead, Mathf.Clamp(latSpeed / Mathf.Max(0.1f, leadSpeed), -1f, 1f) * lateralLead, s);
        }
        else
        {
            speedK = Mathf.Lerp(speedK, 0f, s);
            driftK = Mathf.Lerp(driftK, 0f, s);
            roll = Mathf.Lerp(roll, 0f, s);
            lead = Mathf.Lerp(lead, 0f, s);
        }

        // Ressorts (coups de zoom et d'inclinaison) : un léger rebond avant de revenir
        Spring(ref fovKick, ref fovKickVel, 140f, 0.45f, udt);
        Spring(ref rollKick, ref rollKickVel, 120f, 0.4f, udt);

        trauma = Mathf.MoveTowards(trauma, 0f, shakeDecay * udt);
        time += udt * shakeFrequency;

        float rumble = (speedRumble * speedK + driftRumble * driftK) * live;
        float shake = trauma * trauma;
        float ang = shake * maxShakeAngle + rumble;
        float off = shake * maxShakeOffset;
        float k = MenuBackground.DemoRunning ? 0f : intensity; // caméra d'origine derrière le menu

        outEuler = new Vector3(
            Noise(1) * ang,
            Noise(2) * ang * 0.6f,
            Noise(3) * ang + (roll * live + rollKick)) * k;
        outOffset = new Vector3(
            Noise(4) * off + lead * live,
            Noise(5) * off,
            speedDolly * speedK * live) * k;
        outFov = ((speedFov * speedK + driftFov * driftK) * live + fovKick) * k;
    }

    float Noise(int axis) => Mathf.PerlinNoise(axis * 17.13f, time) * 2f - 1f;

    static void Spring(ref float x, ref float v, float stiffness, float dampingRatio, float dt)
    {
        float w = Mathf.Sqrt(stiffness);
        v += (-stiffness * x - 2f * dampingRatio * w * v) * dt;
        x += v * dt;
        if (Mathf.Abs(x) < 0.001f && Mathf.Abs(v) < 0.01f) x = v = 0f;
    }

    // ---------- Application à la caméra ----------

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
                                                      ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finish || intensity <= 0f) return;

        Quaternion rot = state.RawOrientation;
        state.PositionCorrection += rot * outOffset;
        state.OrientationCorrection = state.OrientationCorrection * Quaternion.Euler(outEuler);
        if (!state.Lens.Orthographic)
            state.Lens.FieldOfView = Mathf.Clamp(state.Lens.FieldOfView + outFov, 1f, 179f);
    }
}
