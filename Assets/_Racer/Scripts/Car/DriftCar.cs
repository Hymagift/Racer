using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Voiture de drift "arcade mais physique" pour mobile, compatible avec le décor qui défile (ZigzagRoadGenerator).
///
/// MODÈLE PHYSIQUE (vue de dessus, "bicycle model") :
/// - Chaque essieu génère une force latérale selon l'angle de glissement de ses pneus, limitée par son adhérence max.
/// - Traction AVANT : le moteur pousse dans la direction des roues avant.
/// - Quand le joueur braque fort, l'adhérence ARRIÈRE chute : l'arrière décroche et la voiture part en glisse.
/// - Quand il relâche, l'arrière retrouve de l'adhérence et la voiture se redresse.
///
/// Le modèle est calculé en plusieurs sous-pas par pas de physique (plus fluide et plus stable),
/// et c'est lui qui décide seul de l'orientation de la voiture (MoveRotation) : rotation et glisse restent synchronisées.
///
/// La voiture ne bouge pas en Z : sa vitesse vers l'avant fait défiler la route.
/// CONTRÔLES : le braquage suit la position HORIZONTALE du doigt (souris dans l'éditeur) :
/// doigt au centre = roues droites, plus il s'écarte du centre, plus on braque. Flèches = braquage à fond.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DriftCar : MonoBehaviour
{
    [Header("Références")]
    public ZigzagRoadGenerator road;
    [Tooltip("Optionnel : le modèle 3D de la voiture (ENFANT). Il penche dans les virages et suit la pente.")]
    public Transform body;
    [Tooltip("Optionnel : fumée des pneus arrière")]
    public ParticleSystem[] tireSmoke;

    [Header("Vitesse")]
    public float startSpeed = 8f;            // m/s au début (~29 km/h)
    public float maxSpeed = 16f;              // m/s à la fin (~58 km/h)
    [Tooltip("Distance pour passer de startSpeed à maxSpeed")]
    public float speedRampDistance = 4000f;
    [Tooltip("Accélération max du moteur (m/s²)")]
    public float engineAccel = 5f;
    [Range(0f, 1f)]
    [Tooltip("Puissance moteur en plus pendant un drift (garde de la vitesse en glisse)")]
    public float driftBoost = 0.3f;
    [Range(0f, 1f)]
    [Tooltip("Part de la vitesse perdue à cause de l'angle de drift qui est rendue (0 = la voiture ralentit beaucoup en glisse, 1 = presque pas). " +
             "Ne s'applique pas au pilote auto (il garde une conduite sûre).")]
    public float driftSpeedKeep = 0.85f;

    [Header("Châssis")]
    public float mass = 1200f;
    public float yawInertia = 2200f;
    [Tooltip("Distance centre de gravité -> essieu avant")]
    public float frontAxle = 1.2f;
    [Tooltip("Distance centre de gravité -> essieu arrière")]
    public float rearAxle = 1.3f;

    [Header("Pneus")]
    [Tooltip("Raideur latérale (N/rad) : plus haut = réponse plus vive")]
    public float frontStiffness = 42000f;
    public float rearStiffness = 48000f;
    [Tooltip("Adhérence avant (coefficient de friction)")]
    public float frontGrip = 1.15f;
    [Tooltip("Adhérence arrière normale")]
    public float rearGrip = 1.18f;
    [Tooltip("Adhérence arrière en drift. Plus bas = l'arrière glisse plus.")]
    public float rearDriftGrip = 0.47f;

    [Header("Direction")]
    [Tooltip("Angle de braquage max à basse vitesse (degrés)")]
    public float maxSteerLow = 26f;
    [Tooltip("Angle de braquage max à haute vitesse (degrés)")]
    public float maxSteerHigh = 14f;
    [Tooltip("Réactivité du volant : plus haut = les roues suivent le doigt plus vite (lissage exponentiel, par seconde)")]
    public float steerFollowSpeed = 12f;
    [Tooltip("Vitesse max de rotation du volant (de -1 à 1 en 2/x secondes). Évite les à-coups si le doigt saute.")]
    public float steerMaxRate = 6f;
    [Tooltip("Vitesse à laquelle le volant revient au centre quand on lâche l'écran (par seconde)")]
    public float steerReturnSpeed = 5f;

    [Header("Doigt / souris")]
    [Tooltip("PositionEcran : l'angle dépend de la position du doigt par rapport au centre de l'écran.\n" +
             "Glissement : l'angle dépend de la distance glissée depuis l'endroit où le doigt s'est posé.")]
    public SteerMode steerMode = SteerMode.PositionEcran;
    [Range(0f, 0.5f)]
    [Tooltip("Zone morte au centre (fraction de la demi-largeur d'écran) : en dessous, roues droites")]
    public float touchDeadZone = 0.08f;
    [Range(0.1f, 1f)]
    [Tooltip("Mode PositionEcran : fraction de la demi-largeur d'écran où l'on atteint le braquage max (1 = bord de l'écran)")]
    public float fullSteerScreenFraction = 0.75f;
    [Range(0.05f, 1f)]
    [Tooltip("Mode Glissement : distance à glisser pour braquer à fond (fraction de la demi-largeur d'écran)")]
    public float fullSteerDragFraction = 0.35f;
    [Range(1f, 3f)]
    [Tooltip("Courbe de réponse : 1 = linéaire, plus haut = plus de précision près du centre, braquage fort réservé aux bords")]
    public float steerCurve = 1.4f;

    [Header("Aides au pilotage")]
    [Range(0f, 1.5f)]
    [Tooltip("Contre-braquage automatique en drift (0 = aucun, 1 = les roues suivent la glisse)")]
    public float countersteerAssist = 0.7f;
    [Tooltip("Contre-braquage automatique max (degrés). Il plafonne en douceur : plus de bascule d'un coup en contre-braquage complet.")]
    public float maxCountersteer = 18f;
    [Tooltip("Angle de drift max (degrés). Au-delà, la rotation est freinée en douceur.")]
    public float maxDriftAngle = 44f;
    public float angleLimitStrength = 8f;
    [Tooltip("Vitesse à partir de laquelle braquer fort déclenche le drift")]
    public float minDriftSpeed = 7f;
    [Range(0f, 1f)]
    [Tooltip("Braquage (0..1) à partir duquel l'arrière commence à décrocher")]
    public float driftStartSteer = 0.38f;
    [Range(0f, 1f)]
    [Tooltip("Braquage (0..1) pour un drift complet. Entre les deux, l'arrière décroche en proportion du braquage.")]
    public float driftFullSteer = 0.95f;
    [Tooltip("Vitesse à laquelle l'arrière décroche quand on braque")]
    public float driftEngageRate = 1.7f;
    [Tooltip("Vitesse à laquelle l'arrière se raccroche quand on relâche")]
    public float driftReleaseRate = 1.4f;
    [Tooltip("Stabilise la rotation quand on ne touche pas l'écran")]
    public float yawDampingNoInput = 3f;
    [Tooltip("Rotation max (degrés/s)")]
    public float maxYawRate = 120f;

    [Header("Simulation")]
    [Range(1, 8)]
    [Tooltip("Sous-pas de calcul par pas de physique : plus = plus fluide")]
    public int substeps = 4;

    [Header("Murs")]
    [Range(0f, 1f)]
    [Tooltip("Vitesse conservée après un choc contre un mur")]
    public float wallSpeedKeep = 0.85f;
    [Tooltip("Délai minimum entre deux chocs (évite que les frottements répétés arrêtent la voiture)")]
    public float wallHitCooldown = 0.6f;
    [Tooltip("Vitesse latérale min contre le mur pour compter comme un choc (m/s)")]
    public float wallHitMinSpeed = 3f;

    [Header("Visuel")]
    public float bodyRollMax = 5f;            // degrés d'inclinaison en virage (vers l'extérieur)
    [Tooltip("Inclinaison max du modèle pour suivre la pente (degrés)")]
    public float maxGroundTilt = 20f;
    public float smokeMaxRate = 60f;

    [Header("Debug")]
    [Tooltip("Affiche les valeurs de pilotage en haut à gauche de l'écran")]
    public bool debugHUD = false;

    [Header("Menu / autopilote")]
    [Tooltip("Pendant le pilote auto : l'arrière ne décroche pas et accroche au moins autant que rearGrip de pilote auto (son drift est visuel, voir styleDrift). Il reste sûr même sur la route la plus dure.")]
    public bool autopilotGrip = true;
    public float autopilotRearGrip = 1.3f;
    public bool useExternalInput;
    public float externalInput;   // -1 gauche, 0 rien, 1 droite (valeurs intermédiaires = braquage partiel)

    public enum SteerMode { PositionEcran, Glissement }
    // ---------- Infos pour le score / l'UI ----------

    /// <summary>Vitesse en m/s.</summary>
    public float Speed => vel.magnitude;
    /// <summary>Vitesse dans l'axe de la voiture en m/s (sert à faire tourner les roues).</summary>
    public float ForwardSpeed => Vector2.Dot(vel, new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)));
    /// <summary>Angle de braquage des roues avant (degrés, positif = à droite), contre-braquage compris.</summary>
    public float SteerAngle => lastSteerDeg - styleShown * 0.6f; // + contre-braquage visuel du drift de style
    /// <summary>Angle entre le nez de la voiture et sa direction de déplacement (degrés, signé).</summary>
    /// <remarks>Pendant le pilote auto, c'est la plus forte entre la glisse réelle et le drift "de style" (styleDrift).</remarks>
    public float DriftAngle => Mathf.Abs(physicalDriftAngle) >= Mathf.Abs(styleShown) ? physicalDriftAngle : -styleShown;
    /// <summary>Glisse réelle de la voiture (degrés, signé), sans le drift de style.</summary>
    public float PhysicalDriftAngle => physicalDriftAngle;

    /// <summary>
    /// Drift "de style" voulu par le pilote auto (degrés, + = nez vers la droite) : la carrosserie pivote,
    /// les roues contre-braquent, les pneus fument et les points de drift comptent, sans changer la trajectoire.
    /// </summary>
    [System.NonSerialized] public float styleDrift;
    float physicalDriftAngle, styleShown;
    /// <summary>Vrai quand la voiture glisse franchement.</summary>
    public bool IsDrifting { get; private set; }
    /// <summary>Points du drift en cours (augmente avec l'angle et la vitesse).</summary>
    public float CurrentDriftPoints { get; private set; }
    /// <summary>Appelé à la fin d'un drift réussi, avec ses points.</summary>
    public event Action<float> OnDriftEnded;
    /// <summary>Appelé quand la voiture touche un mur (le drift en cours est perdu).</summary>
    public event Action OnWallHit;

    Rigidbody rb;
    Vector2 vel;          // vitesse horizontale "réelle" (x = monde X, y = monde Z)
    float yaw;            // orientation (rad), seule source de vérité
    float yawRate;        // rad/s, positif = vers la droite
    float steerInput;     // -1..1, lissé
    float driftFactor;    // 0 = arrière accroché, 1 = arrière décroché
    float lateralAccel;
    float driftTime;
    float rawInput, lastSteerDeg;
    bool pointerWasDown;
    float pointerStartX;  // mode Glissement : où le doigt s'est posé
    int wallContacts;
    string inputError;
    float lastWallHitTime = -10f;

    Quaternion bodyBaseLocalRot = Quaternion.identity;   // rotation d'origine du modèle (ex : -90° des modèles Blender)
    Vector3 bodyBaseLocalPos;                            // position d'origine du modèle (compense un pivot mal placé)
    Quaternion bodyTilt = Quaternion.identity;
    float smoothedLatAccel;
    readonly RaycastHit[] groundHits = new RaycastHit[8];

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezePositionZ
                       | RigidbodyConstraints.FreezeRotationX
                       | RigidbodyConstraints.FreezeRotationZ;
        rb.useGravity = true;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
        transform.rotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);

        if (road == null) road = FindFirstObjectByType<ZigzagRoadGenerator>();
        if (road == null)
            Debug.LogError("[DriftCar] Aucune ZigzagRoadGenerator trouvée : le décor ne défilera pas.", this);

        if (body == transform)
        {
            Debug.LogWarning("[DriftCar] 'Body' doit être un ENFANT (le modèle 3D), pas la voiture elle-même. Champ ignoré.", this);
            body = null;
        }

        // Modèle 3D de la voiture équipée dans le Garage (remplace celui de la scène, voir CarModelSwap)
        CarModelSwap.ApplyEquipped(this);
        if (GetComponent<BlobShadow>() == null) gameObject.AddComponent<BlobShadow>(); // ombre simple (pas d'ombres en temps réel)

        if (body != null)
        {
            bodyBaseLocalRot = body.localRotation;
            bodyBaseLocalPos = body.localPosition;
        }
    }

    /// <summary>Remet la voiture en route (après "Continuer") : position, cap (degrés), vitesse de départ, glisse remise à zéro.</summary>
    public void Respawn(Vector3 position, float headingDeg)
    {
        yaw = headingDeg * Mathf.Deg2Rad;
        yawRate = 0f;
        steerInput = 0f;
        driftFactor = 0f;
        driftTime = 0f;
        CurrentDriftPoints = 0f;
        IsDrifting = false;
        float speed = TargetSpeed();
        vel = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * speed;

        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, headingDeg, 0f));
        if (rb != null)
        {
            rb.position = position;
            rb.rotation = transform.rotation;
            rb.linearVelocity = new Vector3(vel.x, 0f, 0f);
            rb.angularVelocity = Vector3.zero;
        }
        if (road != null) road.ScrollSpeed = Mathf.Max(0f, vel.y);
    }

    /// <summary>Change le modèle 3D affiché (appelé par CarModelSwap). Le nouveau modèle garde la pose donnée.</summary>
    public void SetBody(Transform newBody)
    {
        body = newBody;
        bodyTilt = Quaternion.identity;
        if (body == null) return;
        bodyBaseLocalRot = body.localRotation;
        bodyBaseLocalPos = body.localPosition;
    }

    void Start()
    {
        // La fumée doit défiler avec le décor, sinon elle reste "collée" à la voiture
        if (tireSmoke != null && road != null)
            foreach (var ps in tireSmoke)
            {
                if (ps == null) continue;
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                main.customSimulationSpace = road.TrackRoot;
            }
    }

    void OnDisable()
    {
        if (road != null) road.ScrollSpeed = 0f; // voiture désactivée (game over) : le décor s'arrête
    }

    // ---------- Entrées ----------

    void Update()
    {
        rawInput = ReadInput();

        // Drift de style (pilote auto) : entre et sort en douceur, comme une vraie glisse
        styleShown = Mathf.MoveTowards(styleShown, styleDrift, 75f * Time.deltaTime);

        // Volant : suit le doigt avec un léger lissage (réactif, mais sans à-coup si le doigt saute),
        // et revient au centre à vitesse fixe quand on lâche l'écran
        float dt = Time.deltaTime;
        if (Mathf.Abs(rawInput) > 0.001f)
        {
            float smoothed = Mathf.Lerp(steerInput, rawInput, 1f - Mathf.Exp(-steerFollowSpeed * dt));
            steerInput = Mathf.MoveTowards(steerInput, smoothed, steerMaxRate * dt);
        }
        else
            steerInput = Mathf.MoveTowards(steerInput, 0f, steerReturnSpeed * dt);

        UpdateVisuals();
    }

    // Lit le doigt / la souris / le clavier, avec l'ancien ET le nouveau système d'Input de Unity.
    // Renvoie -1..1 : pour le doigt et la souris, c'est proportionnel à leur position horizontale.
    float ReadInput()
    {
        if (useExternalInput) return externalInput;

        bool down = false;
        float x = 0f;
        float key = 0f;

#if ENABLE_INPUT_SYSTEM
        try
        {
            var ts = Touchscreen.current;
            var mouse = Mouse.current;
            if (ts != null && ts.primaryTouch.press.isPressed)
            {
                down = true;
                x = ts.primaryTouch.position.ReadValue().x;
            }
            else if (mouse != null && mouse.leftButton.isPressed)
            {
                down = true;
                x = mouse.position.ReadValue().x;
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                bool l = kb.leftArrowKey.isPressed || kb.aKey.isPressed || kb.qKey.isPressed;
                bool r = kb.rightArrowKey.isPressed || kb.dKey.isPressed;
                if (l != r) key = l ? -1f : 1f;
            }
        }
        catch (Exception e) { inputError = "InputSystem: " + e.Message; }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        try
        {
            if (!down && Input.touchCount > 0)
            {
                down = true;
                x = Input.GetTouch(0).position.x;
            }
            else if (!down && Input.GetMouseButton(0))
            {
                down = true;
                x = Input.mousePosition.x;
            }

            if (key == 0f)
            {
                bool l = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q);
                bool r = Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D);
                if (l != r) key = l ? -1f : 1f;
            }
        }
        catch (Exception e) { inputError = "Input: " + e.Message; }
#endif

        if (!down)
        {
            pointerWasDown = false;
            return key;
        }

        // Le doigt vient de se poser : point de départ du mode Glissement
        if (!pointerWasDown) pointerStartX = x;
        pointerWasDown = true;

        float half = Mathf.Max(1f, Screen.width * 0.5f);
        float offset = steerMode == SteerMode.Glissement
            ? (x - pointerStartX) / (half * fullSteerDragFraction)
            : (x - half) / (half * fullSteerScreenFraction);
        return ShapeInput(offset);
    }

    // Zone morte au centre, saturation à ±1, puis courbe de réponse (précision près du centre)
    float ShapeInput(float v)
    {
        float a = Mathf.Abs(v);
        float dz = Mathf.Clamp(touchDeadZone, 0f, 0.9f);
        if (a <= dz) return 0f;
        a = Mathf.Clamp01((a - dz) / (1f - dz));
        return Mathf.Sign(v) * Mathf.Pow(a, Mathf.Max(1f, steerCurve));
    }

    // ---------- Physique ----------

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // La physique Unity a pu modifier X (collisions) : on repart de sa valeur
        vel.x = rb.linearVelocity.x;

        // Déclenchement du drift : l'arrière décroche EN PROPORTION du braquage (à vitesse suffisante),
        // au lieu de tout ou rien : un petit coup de doigt = légère glisse, doigt au bord = gros drift
        float driftTarget = 0f;
        if (vel.magnitude > minDriftSpeed && !(useExternalInput && autopilotGrip))
        {
            float t = Mathf.InverseLerp(driftStartSteer, Mathf.Max(driftStartSteer + 0.01f, driftFullSteer), Mathf.Abs(steerInput));
            driftTarget = t * t * (3f - 2f * t); // départ et arrivée en douceur
        }
        float driftRate = driftTarget > driftFactor ? driftEngageRate : driftReleaseRate;
        driftFactor = Mathf.MoveTowards(driftFactor, driftTarget, driftRate * dt);

        // Modèle de voiture en plusieurs sous-pas (fluide et stable)
        int n = Mathf.Max(1, substeps);
        float h = dt / n;
        for (int i = 0; i < n; i++)
            SimulateStep(h);

        // Application : orientation pilotée par le modèle, X par la physique, Z par le défilement du décor
        rb.angularVelocity = Vector3.zero;
        rb.MoveRotation(Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f));
        rb.linearVelocity = new Vector3(vel.x, rb.linearVelocity.y, 0f);

        if (road != null) road.ScrollSpeed = Mathf.Max(0f, vel.y);

        UpdateDriftScore(dt, vel.magnitude);
    }

    void SimulateStep(float h)
    {
        float speedBefore = vel.magnitude;
        Vector2 fwd = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
        Vector2 right = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));

        float vLong = Vector2.Dot(vel, fwd);
        float vLat = Vector2.Dot(vel, right);
        float speed = vel.magnitude;
        float vRef = Mathf.Max(Mathf.Abs(vLong), 4f); // évite l'instabilité à très basse vitesse

        // Angle de glisse de la caisse (positif = la voiture se déplace vers sa droite)
        float beta = Mathf.Atan2(vLat, vRef);
        physicalDriftAngle = beta * Mathf.Rad2Deg;

        // --- Direction : braquage joueur (réduit avec la vitesse) + contre-braquage auto (surtout en drift)
        float speed01 = Mathf.InverseLerp(5f, 25f, speed);
        float maxSteer = Mathf.Lerp(maxSteerLow, maxSteerHigh, speed01) * Mathf.Deg2Rad;
        float assist = countersteerAssist * Mathf.Lerp(0.3f, 1f, driftFactor);
        float counter = SoftClamp(assist * beta, maxCountersteer * Mathf.Deg2Rad); // plafonne en douceur
        float steer = steerInput * maxSteer + counter;
        steer = Mathf.Clamp(steer, -50f * Mathf.Deg2Rad, 50f * Mathf.Deg2Rad);
        lastSteerDeg = steer * Mathf.Rad2Deg;

        // --- Charge sur chaque essieu
        float L = frontAxle + rearAxle;
        const float g = 9.81f;
        float maxF = frontGrip * mass * g * rearAxle / L;
        float baseRear = useExternalInput && autopilotGrip ? Mathf.Max(rearGrip, autopilotRearGrip) : rearGrip;
        float maxR = Mathf.Lerp(baseRear, rearDriftGrip, driftFactor) * mass * g * frontAxle / L;

        // --- Forces latérales des pneus (saturation douce : pas de cassure brutale quand ça décroche)
        float slipF = Mathf.Atan2(vLat + yawRate * frontAxle, vRef) - steer;
        float slipR = Mathf.Atan2(vLat - yawRate * rearAxle, vRef);
        float Fyf = SoftClamp(-frontStiffness * slipF, maxF);
        float Fyr = SoftClamp(-rearStiffness * slipR, maxR);

        // --- Traction avant : vise la vitesse cible, avec un boost en drift
        float target = TargetSpeed();
        float throttle = Mathf.Clamp01((target - vLong) / 3f) + driftBoost * driftFactor;
        float T = Mathf.Clamp01(throttle) * engineAccel * mass;
        if (vLong > target + 2f) T = -0.5f * engineAccel * mass * Mathf.Clamp01((vLong - target - 2f) / 5f); // frein moteur

        // Cercle d'adhérence avant : traction + force latérale <= adhérence max
        float combined = Mathf.Sqrt(T * T + Fyf * Fyf);
        if (combined > maxF)
        {
            float k = maxF / combined;
            T *= k;
            Fyf *= k;
        }

        // --- Forces dans le repère de la voiture
        float cs = Mathf.Cos(steer), sn = Mathf.Sin(steer);
        float frontLong = T * cs - Fyf * sn;
        float frontLat = T * sn + Fyf * cs;
        float drag = 0.4f * speed * speed + 30f * speed;

        Vector2 force = fwd * frontLong + right * (frontLat + Fyr);
        if (speed > 0.01f) force -= vel / speed * drag;

        // --- Intégration
        vel += force / mass * h;
        KeepDriftSpeed(speedBefore, Mathf.Abs(beta));
        lateralAccel = (frontLat + Fyr) / mass;

        float torque = frontAxle * frontLat - rearAxle * Fyr;
        yawRate += torque / yawInertia * h;

        // Aide : au-delà de l'angle max, on freine en douceur la rotation qui l'aggrave (pas de tête-à-queue)
        float maxBeta = maxDriftAngle * Mathf.Deg2Rad;
        float excess = Mathf.Abs(beta) - maxBeta;
        if (excess > 0f)
        {
            float s = Mathf.Sign(beta);
            float outward = -yawRate * s;                 // > 0 : la rotation augmente l'angle
            if (outward > 0f)
                yawRate += s * outward * Mathf.Clamp01(excess * angleLimitStrength * 20f * h);
            yawRate += s * excess * angleLimitStrength * h;
        }

        // Aide : stabilisation quand on ne touche pas l'écran
        if (Mathf.Abs(steerInput) < 0.05f)
            yawRate *= 1f - Mathf.Clamp01(yawDampingNoInput * h);

        float maxRate = maxYawRate * Mathf.Deg2Rad;
        yawRate = Mathf.Clamp(yawRate, -maxRate, maxRate);

        yaw += yawRate * h;
    }

    // En glisse, les pneus freinent la voiture : on rend une partie de la vitesse perdue (sans dépasser la vitesse visée).
    // L'angle, la rotation et la trajectoire ne changent pas, seulement la vitesse.
    void KeepDriftSpeed(float speedBefore, float absBeta)
    {
        if (driftSpeedKeep <= 0f || useExternalInput) return;
        float weight = Mathf.InverseLerp(5f * Mathf.Deg2Rad, 20f * Mathf.Deg2Rad, absBeta);
        float speed = vel.magnitude;
        float target = TargetSpeed();
        if (weight <= 0f || speed >= speedBefore || speed >= target || speed < 0.1f) return;
        float restored = (Mathf.Min(speedBefore, target) - speed) * driftSpeedKeep * weight;
        vel *= 1f + restored / speed;
    }

    // Saturation progressive (tanh) : la force plafonne en douceur au lieu de "casser" d'un coup
    static float SoftClamp(float f, float max)
    {
        if (max <= 0f) return 0f;
        float x = f / max;
        float e = Mathf.Exp(2f * Mathf.Clamp(x, -10f, 10f));
        return max * (e - 1f) / (e + 1f);
    }

    float TargetSpeed()
    {
        float d = road != null ? (float)road.TotalDistance : 0f;
        return Mathf.Lerp(startSpeed, maxSpeed, Mathf.Clamp01(d / speedRampDistance));
    }

    // ---------- Murs ----------

    // Choc : perte de vitesse, drift annulé (un vrai impact, pas un simple frottement)
    void OnCollisionEnter(Collision collision)
    {
        if (!TryGetWallNormal(collision, out Vector2 n2)) return;

        float into = Vector2.Dot(vel, n2);
        if (into < -wallHitMinSpeed && Time.time - lastWallHitTime > wallHitCooldown)
        {
            lastWallHitTime = Time.time;
            vel -= n2 * into * 1.2f;   // petit rebond
            vel *= wallSpeedKeep;
            yawRate *= 0.5f;
            driftFactor = 0f;
            CurrentDriftPoints = 0f;   // drift raté
            OnWallHit?.Invoke();
        }
    }

    // Frottement contre le mur : on retire seulement la vitesse qui rentre dedans
    void OnCollisionStay(Collision collision)
    {
        if (!TryGetWallNormal(collision, out Vector2 n2)) return;
        wallContacts++;

        float into = Vector2.Dot(vel, n2);
        if (into < 0f) vel -= n2 * into;
    }

    // Normale du mur calculée à partir du TRACÉ de la route (et non de la normale de contact de Unity,
    // qui peut pointer vers l'avant aux jonctions entre chunks et "arrêter" la voiture).
    bool TryGetWallNormal(Collision collision, out Vector2 normal)
    {
        normal = Vector2.zero;
        int found = 0;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint c = collision.GetContact(i);
            if (c.normal.y > 0.5f) continue; // c'est le sol

            if (road == null)
            {
                Vector2 raw = new Vector2(c.normal.x, c.normal.z);
                if (raw.sqrMagnitude > 0.0001f) { normal += raw.normalized; found++; }
                continue;
            }

            float tz = road.TrackZ(c.point.z);
            float cx = road.CenterXAt(tz);
            float side = c.point.x - cx;

            // Contact loin du bord de la route = jonction interne ou autre : on l'ignore
            if (Mathf.Abs(side) < road.HalfWidthXAt(tz) - 1f) continue;

            // Direction de la route à cet endroit, puis normale qui pointe vers le centre de la route
            float dx = (road.CenterXAt(tz + 1f) - road.CenterXAt(tz - 1f)) * 0.5f;
            Vector2 tangent = new Vector2(dx, 1f).normalized;
            Vector2 rightN = new Vector2(tangent.y, -tangent.x);
            normal += side > 0f ? -rightN : rightN;
            found++;
        }

        if (found == 0 || normal.sqrMagnitude < 0.0001f) return false;
        normal.Normalize();
        return true;
    }

    // ---------- Score de drift ----------

    void UpdateDriftScore(float dt, float speed)
    {
        bool drifting = Mathf.Abs(DriftAngle) > 12f && speed > minDriftSpeed * 0.8f;

        if (drifting)
        {
            driftTime += dt;
            CurrentDriftPoints += Mathf.Abs(DriftAngle) * speed * dt * 0.1f;
        }
        else if (IsDrifting)
        {
            if (driftTime > 0.4f && CurrentDriftPoints > 0f)
                OnDriftEnded?.Invoke(CurrentDriftPoints);
            CurrentDriftPoints = 0f;
            driftTime = 0f;
        }

        IsDrifting = drifting;
    }

    // ---------- Visuel ----------

    void UpdateVisuals()
    {
        if (body != null)
        {
            // Pente du sol, en ignorant les colliders de la voiture elle-même
            Vector3 normal = Vector3.up;
            int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * 2f, Vector3.down, groundHits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.rigidbody == rb || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.normal.y < 0.5f) continue;
                if (hit.distance < best) { best = hit.distance; normal = hit.normal; }
            }

            Quaternion ground = Quaternion.FromToRotation(Vector3.up, normal);
            ground = Quaternion.RotateTowards(Quaternion.identity, ground, maxGroundTilt);

            // Roulis vers l'EXTÉRIEUR du virage (lissé pour éviter les tremblements)
            smoothedLatAccel = Mathf.Lerp(smoothedLatAccel, lateralAccel, 6f * Time.deltaTime);
            float roll = Mathf.Clamp(smoothedLatAccel / 9.81f, -1f, 1f) * bodyRollMax;
            Quaternion worldTilt = ground * Quaternion.AngleAxis(roll, transform.forward)
                                 * Quaternion.AngleAxis(styleShown, Vector3.up); // drift de style : le nez rentre dans le virage

            bodyTilt = Quaternion.Slerp(bodyTilt, worldTilt, 10f * Time.deltaTime);

            // Inclinaison appliquée PAR-DESSUS la rotation d'origine du modèle
            Transform parent = body.parent != null ? body.parent : transform;
            Quaternion localTilt = Quaternion.Inverse(parent.rotation) * bodyTilt * parent.rotation;
            body.localRotation = localTilt * bodyBaseLocalRot;

            // Le modèle pivote autour du centre de la voiture (son parent), pas autour de son propre pivot :
            // si le pivot du mesh est loin de la carrosserie (origine Blender mal placée), le mesh reste sur la route.
            body.localPosition = localTilt * bodyBaseLocalPos;
        }

        if (tireSmoke != null)
        {
            float intensity = Mathf.InverseLerp(10f, 40f, Mathf.Abs(DriftAngle)) * Mathf.InverseLerp(5f, 15f, Speed);
            foreach (var ps in tireSmoke)
            {
                if (ps == null) continue;
                var em = ps.emission;
                em.rateOverTime = intensity * smokeMaxRate;
            }
        }
    }

    // ---------- Debug ----------

    // OnGUI est très lent sur téléphone : il n'existe que dans l'éditeur et les builds de développement
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void OnGUI()
    {
        if (!debugHUD) return;

        var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 45) };
        string txt =
            $"Entrée : {rawInput:F2}   Volant : {steerInput:F2}   Braquage : {lastSteerDeg:F0}°\n" +
            $"Rotation : {yawRate * Mathf.Rad2Deg:F0}°/s   Vitesse : {Speed * 3.6f:F0} km/h\n" +
            $"Drift : {driftFactor:F2}   Angle : {DriftAngle:F0}°   Contacts mur : {wallContacts}";
        if (!string.IsNullOrEmpty(inputError)) txt += "\nERREUR : " + inputError;

        GUI.Box(new Rect(10, 10, style.fontSize * 30, style.fontSize * 5f), GUIContent.none);
        GUI.Label(new Rect(20, 15, style.fontSize * 29, style.fontSize * 5f), txt, style);
    }
#endif

    void LateUpdate() => wallContacts = 0;
}