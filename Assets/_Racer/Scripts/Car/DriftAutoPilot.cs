using UnityEngine;

/// <summary>
/// Pilote automatique : partie de démo derrière le menu, et pilote auto acheté (AutopilotBoost) pendant une partie.
/// Il suit une trajectoire sûre et ajoute un drift "de style" dans les virages (DriftCar.styleDrift).
/// Ajouté tout seul sur la voiture par MenuBackground : elle suit la route en driftant.
/// Nécessite les 2 champs "useExternalInput" / "externalInput" dans DriftCar.
///
/// Principe : on vise un point au centre de la route devant la voiture (plus loin quand on va vite, mesuré le long de la route),
/// on en déduit la direction voulue et on braque en proportion de l'écart de vitesse sur le côté de la voiture
/// (avec anticipation, pour ne pas zigzaguer). Le point visé est rapproché si la ligne droite
/// pour l'atteindre sort de la route (épingles), et la voiture se recentre d'urgence près du bord.
/// Si la voiture tombe malgré tout, la partie se termine et le menu relance une démo.
/// </summary>
[RequireComponent(typeof(DriftCar))]
public class DriftAutoPilot : MonoBehaviour
{
    public ZigzagRoadGenerator road;

    [Header("Trajectoire")]
    [Tooltip("Temps (s) d'avance du point visé : distance visée = vitesse x ce temps")]
    public float lookAheadTime = 0.9f;
    [Tooltip("Distance visée minimum (m)")]
    public float minLookAhead = 6f;
    [Tooltip("Distance visée maximum (m)")]
    public float maxLookAhead = 22f;
    [Tooltip("Marge (m) à garder avec le bord quand on coupe un virage")]
    public float cornerMargin = 1.3f;
    [Tooltip("Décalage (m) vers la montagne, pour rester loin de la falaise")]
    public float cliffBias = 0.6f;

    [Header("Volant")]
    [Tooltip("Braquage par m/s d'écart de vitesse latérale. Plus grand = plus nerveux (drifts plus fréquents)")]
    public float steerGain = 0.45f;
    [Tooltip("Anticipation (s) : on tient compte de l'accélération latérale sur ce délai (évite les zigzags)")]
    public float predictTime = 0.3f;
    [Tooltip("Écart (m/s) toléré avant de braquer")]
    public float deadZone = 0.15f;
    [Tooltip("Distance (m) au bord à partir de laquelle on braque à fond pour revenir")]
    public float edgeMargin = 1.2f;
    [Tooltip("Si la voiture tourne du mauvais côté, coche ça")]
    public bool invertSteering = false;

    [Header("Style : drift")]
    [Tooltip("La voiture drifte dans les virages : le nez rentre dans le virage, les roues contre-braquent, les pneus fument,\n" +
             "et les points de drift comptent. La trajectoire reste celle d'une conduite sûre (pas de choc contre les murs).")]
    public bool styleDrift = true;
    [Tooltip("Angle de drift (degrés) au début d'un virage")]
    public float minStyleAngle = 20f;
    [Tooltip("Angle de drift max (degrés), dans les virages les plus serrés")]
    public float maxStyleAngle = 38f;
    [Tooltip("Degrés de drift en plus par degré/seconde de virage")]
    public float styleGain = 1.3f;
    [Tooltip("En dessous de cette vitesse de virage (degrés/seconde), pas de drift : la voiture va tout droit")]
    public float styleDeadZone = 18f;
    [Tooltip("Anticipation (s) : le drift commence un peu avant le virage")]
    public float styleLookTime = 0.4f;

    [Header("Sécurité")]
    [Tooltip("Hauteur de chute (m) sous la route avant de terminer la partie")]
    public float fallDepth = 6f;

    DriftCar car;
    Rigidbody rb;
    DriftGameManager manager;
    Vector2 lastV, accel;
    bool hasLastV;

    void Awake()
    {
        car = GetComponent<DriftCar>();
        rb = GetComponent<Rigidbody>();
        if (MenuBackground.DemoRunning) car.debugHUD = false;
        if (road == null) road = FindFirstObjectByType<ZigzagRoadGenerator>();
    }

    // Activé / désactivé en cours de partie par AutopilotBoost : on prend puis on rend le volant au joueur
    void OnEnable()
    {
        if (car == null) car = GetComponent<DriftCar>();
        car.useExternalInput = true;
        hasLastV = false;
    }

    void OnDisable()
    {
        if (car == null) return;
        car.useExternalInput = false;
        car.externalInput = 0f;
        car.styleDrift = 0f; // fin du pilote auto : la carrosserie se remet droite en douceur
    }

    // Drift de style : plus la route tourne vite devant la voiture, plus le nez rentre dans le virage
    float StyleAngle(double s, float speed)
    {
        if (!styleDrift || car.Speed < car.minDriftSpeed) return 0f;
        double ahead = s + Mathf.Max(3f, car.Speed * styleLookTime);
        // Courbure moyenne sur 4 m (degrés par mètre) x vitesse = degrés par seconde
        float k = (road.PointAt(ahead - 2.0).curvature + road.PointAt(ahead).curvature + road.PointAt(ahead + 2.0).curvature) / 3f;
        float rate = k * Mathf.Rad2Deg * speed;
        if (Mathf.Abs(rate) < styleDeadZone) return 0f;
        float angle = minStyleAngle + (Mathf.Abs(rate) - styleDeadZone) * styleGain;
        return Mathf.Sign(rate) * Mathf.Min(angle, maxStyleAngle);
    }

    void FixedUpdate()
    {
        if (road == null || !car.enabled) return;

        float dt = Time.fixedDeltaTime;
        Vector3 p = transform.position;
        Vector3 local = road.WorldToTrack(p);
        RoadPoint here = road.Locate(p, road.TotalDistance, out float off);

        // Vitesse de la voiture par rapport à la route : X par la physique, Z par le défilement du décor
        Vector2 v = new Vector2(rb.linearVelocity.x, road.ScrollSpeed);
        float speed = Mathf.Max(v.magnitude, 1f);

        // Accélération (lissée) : sert à anticiper où ira la voiture
        if (hasLastV) accel = Vector2.Lerp(accel, (v - lastV) / dt, 1f - Mathf.Exp(-12f * dt));
        lastV = v;
        hasLastV = true;

        float bias = -road.CliffBiasAt(here.s) * cliffBias; // vers la montagne (0 sur un pont : vide des deux côtés)
        float look = PickLookAhead(local, here.s, Mathf.Clamp(car.Speed * lookAheadTime, minLookAhead, maxLookAhead), bias);
        RoadPoint aim = road.PointAt(here.s + look);
        Vector3 target = aim.position + aim.right * bias;

        // Vitesse voulue : vers le point visé, à la vitesse actuelle. On braque selon l'écart, mesuré sur la droite de la voiture
        Vector2 toTarget = new Vector2(target.x - local.x, target.z - local.z);
        Vector2 wanted = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized * speed : v;
        Vector2 predicted = v + accel * predictTime;
        float yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
        Vector2 carRight = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
        float error = Vector2.Dot(wanted - predicted, carRight);

        float input = Mathf.Abs(error) > deadZone ? Mathf.Clamp(error * steerGain, -1f, 1f) : 0f;

        // Urgence : trop près du bord et on s'en approche encore -> braquage à fond vers le centre
        float vLat = v.x * here.right.x + v.y * here.right.z;
        if (Mathf.Abs(off) > here.HalfWidth - edgeMargin && vLat * Mathf.Sign(off) > 0f)
            input = -Mathf.Sign(off);

        if (invertSteering) input = -input;
        car.externalInput = input;
        car.styleDrift = StyleAngle(here.s, speed);

        // Filet de sécurité : chute -> fin de la partie (le menu fait le fondu et relance)
        if (local.y < here.position.y - fallDepth)
        {
            if (manager == null) manager = FindFirstObjectByType<DriftGameManager>();
            if (manager != null) manager.Die();
            else MenuBackground.NotifyRunEnded();
        }
    }

    // Distance du point visé : on la réduit tant que la ligne droite voiture -> point sort de la route (épingles)
    float PickLookAhead(Vector3 local, double s, float look, float bias)
    {
        const int samples = 6;
        while (look > minLookAhead)
        {
            RoadPoint aim = road.PointAt(s + look);
            Vector3 target = aim.position + aim.right * bias;
            bool inside = true;
            for (int i = 1; i < samples && inside; i++)
            {
                float f = i / (float)samples;
                Vector3 pt = Vector3.Lerp(local, target, f);
                // Écart de ce point au point de la route le plus proche (cherché à partir de la voiture)
                inside = LateralInside(pt, s);
            }
            if (inside) break;
            look *= 0.8f;
        }
        return Mathf.Max(look, minLookAhead);
    }

    bool LateralInside(Vector3 local, double sMin)
    {
        Vector3 world = road.TrackToWorld(local);
        RoadPoint rp = road.Locate(world, sMin, out float lat);
        return Mathf.Abs(lat) < rp.HalfWidth - cornerMargin;
    }
}
