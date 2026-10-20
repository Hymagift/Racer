using UnityEngine;

/// <summary>
/// Pilote automatique pour la partie qui tourne en fond du menu.
/// Ajouté tout seul sur la voiture par MenuBackground : elle suit la route en driftant.
/// Nécessite les 2 champs "useExternalInput" / "externalInput" dans DriftCar.
///
/// Principe : on vise un point au centre de la route devant la voiture (plus loin quand on va vite),
/// on en déduit la vitesse latérale voulue et on braque en proportion de l'écart
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

    [Header("Sécurité")]
    [Tooltip("Hauteur de chute (m) sous la route avant de terminer la partie")]
    public float fallDepth = 6f;

    DriftCar car;
    Rigidbody rb;
    DriftGameManager manager;
    float lastVx, accelX;
    bool hasLastVx;

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
        hasLastVx = false;
    }

    void OnDisable()
    {
        if (car == null) return;
        car.useExternalInput = false;
        car.externalInput = 0f;
    }

    void FixedUpdate()
    {
        if (road == null || !car.enabled) return;

        float dt = Time.fixedDeltaTime;
        Vector3 p = transform.position;
        float z = road.TrackZ(p.z);
        float vx = rb.linearVelocity.x;
        float vz = Mathf.Max(road.ScrollSpeed, 1f); // la voiture avance via le défilement de la route

        // Accélération latérale (lissée) : sert à anticiper où ira la voiture
        if (hasLastVx) accelX = Mathf.Lerp(accelX, (vx - lastVx) / dt, 1f - Mathf.Exp(-12f * dt));
        lastVx = vx;
        hasLastVx = true;

        float bias = -road.CliffBiasAt(z) * cliffBias; // vers la montagne (0 sur un pont : vide des deux côtés)
        float look = PickLookAhead(p.x, z, Mathf.Clamp(car.Speed * lookAheadTime, minLookAhead, maxLookAhead), bias);
        float targetX = road.CenterXAt(z + look) + bias;

        // Vitesse latérale qui amène la voiture sur le point visé quand elle y arrive
        float wantedVx = (targetX - p.x) * vz / look;
        float error = wantedVx - (vx + accelX * predictTime);

        float input = Mathf.Abs(error) > deadZone ? Mathf.Clamp(error * steerGain, -1f, 1f) : 0f;

        // Urgence : trop près du bord et on s'en approche encore -> braquage à fond vers le centre
        float off = p.x - road.CenterXAt(z);
        float half = road.HalfWidthXAt(z);
        if (Mathf.Abs(off) > half - edgeMargin && vx * Mathf.Sign(off) > 0f)
            input = -Mathf.Sign(off);

        if (invertSteering) input = -input;
        car.externalInput = input;

        // Filet de sécurité : chute -> fin de la partie (le menu fait le fondu et relance)
        if (p.y < road.RoadHeightAt(z) - fallDepth)
        {
            if (manager == null) manager = FindFirstObjectByType<DriftGameManager>();
            if (manager != null) manager.Die();
            else MenuBackground.NotifyRunEnded();
        }
    }

    // Distance du point visé : on la réduit tant que la ligne droite voiture -> point sort de la route
    float PickLookAhead(float x, float z, float look, float bias)
    {
        const int samples = 6;
        while (look > minLookAhead)
        {
            float tx = road.CenterXAt(z + look) + bias;
            bool inside = true;
            for (int i = 1; i < samples && inside; i++)
            {
                float f = i / (float)samples;
                float zi = z + look * f;
                float lineX = Mathf.Lerp(x, tx, f);
                inside = Mathf.Abs(lineX - road.CenterXAt(zi)) < road.HalfWidthXAt(zi) - cornerMargin;
            }
            if (inside) break;
            look *= 0.8f;
        }
        return Mathf.Max(look, minLookAhead);
    }
}
