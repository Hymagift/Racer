using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// À mettre sur chaque cône.
/// Avant d'être touché, le Collider est en "Is Trigger" : la voiture ne rebondit pas dessus et ne perd pas de vitesse.
/// Quand la voiture le touche : multiplicateur au ScoreManager, puis le cône est ÉJECTÉ avec une vraie physique
/// (Rigidbody ajouté, il vole, tourne, rebondit sur la route et les murs, puis se pose).
///
/// Pendant le vol, le cône quitte le TrackRoot (un Rigidbody dynamique ne doit pas être enfant d'un Rigidbody qui bouge)
/// et suit lui-même le défilement de la route. Une fois posé, il redevient enfant du TrackRoot et défile avec elle.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Cone : MonoBehaviour
{
    [Header("Apparence")]
    [Tooltip("Matériau appliqué au modèle du cône (TrafficCone.mat). Vide : on garde celui du modèle.")]
    public Material bodyMaterial;

    [Header("Éjection")]
    [Tooltip("Vitesse du cône vers l'avant, en multiple de la vitesse de la voiture")]
    public float forwardKick = 1.2f;
    [Tooltip("Vitesse vers le haut (m/s) à l'arrêt")]
    public float upKickMin = 3f;
    [Tooltip("Vitesse vers le haut (m/s) en plus par m/s de la voiture")]
    public float upKickPerSpeed = 0.35f;
    [Tooltip("Poussée sur le côté (m/s), à l'opposé du centre de la voiture")]
    public float sideKick = 3f;
    [Tooltip("Rotation aléatoire max (rad/s)")]
    public float maxSpin = 15f;
    public float mass = 4f;

    [Header("Freinage (par rapport à la route)")]
    [Tooltip("Ralentissement dans l'air")]
    public float airDrag = 0.3f;
    [Tooltip("Ralentissement quand il glisse sur la route (la route peut être sans friction)")]
    public float groundDrag = 3f;

    [Header("Fin du vol")]
    [Tooltip("Durée max du vol (s) avant que le cône soit reposé sur la route")]
    public float maxFlightTime = 5f;
    [Tooltip("Sous cette vitesse (par rapport à la route), le cône est considéré comme posé")]
    public float restSpeed = 0.5f;
    [Tooltip("Tombé de plus de cette hauteur (hors de la route) : le cône disparaît")]
    public float fallLimit = 15f;

    bool hit;
    bool flying;
    float flightTime;
    float startY;
    bool touching;
    Collider col;
    Renderer[] renderers;      // mis en cache : OnEnable est rappelé à chaque réutilisation du cône
    Rigidbody rb;
    Transform home;            // le TrackRoot (parent d'origine)
    ZigzagRoadGenerator road;
    Collider[] carColliders;
    ScoreManager score;

    void Awake()
    {
        col = GetComponent<Collider>();
        col.isTrigger = true;

        renderers = GetComponentsInChildren<Renderer>(true);
        if (bodyMaterial != null)
            foreach (var r in renderers) r.sharedMaterial = bodyMaterial;
    }

    // Si le générateur de route réutilise les cônes, ils redeviennent actifs et "neufs"
    void OnEnable()
    {
        hit = false;
        StopFlight(false); // le ConeSpawner l'a déjà remis dans le TrackRoot
        foreach (var r in renderers) r.enabled = true;
        col.enabled = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (flying) return;
        DriftCar car = other.GetComponentInParent<DriftCar>();
        if (car == null) return;

        // Le multiplicateur n'est donné qu'une fois, mais un cône renversé peut être retapé
        if (!hit)
        {
            hit = true;
            if (score == null) score = ScoreManager.Instance != null ? ScoreManager.Instance : FindFirstObjectByType<ScoreManager>();
            if (score != null) score.OnConeHit();
        }

        Launch(car, other.attachedRigidbody, col.ClosestPoint(car.transform.position));
    }

    void Launch(DriftCar car, Rigidbody carRb, Vector3 hitPoint)
    {
        road = car.road;
        home = transform.parent;

        // Vitesse de la route dans le monde (elle défile vers -Z) et vitesse de la voiture PAR RAPPORT à la route
        Vector3 roadVel = RoadVelocity();
        Vector3 carVel = new Vector3(carRb != null ? carRb.linearVelocity.x : 0f, 0f, 0f) - roadVel;
        float speed = carVel.magnitude;

        Vector3 away = transform.position - car.transform.position;
        away.y = 0f;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.zero;

        ConeHitFX.Play(hitPoint, carVel); // petit effet jaune (Particle System) au point d'impact

        Vector3 kick = carVel * forwardKick
                     + away * sideKick
                     + Vector3.up * (upKickMin + speed * upKickPerSpeed);

        // Le cône quitte le TrackRoot le temps du vol (même position dans le monde)
        transform.SetParent(null, true);
        if (home != null) SceneManager.MoveGameObjectToScene(gameObject, home.gameObject.scene); // reste dans la scène de la route (menu chargé en additif)

        col.isTrigger = false;
        carColliders = car.GetComponentsInChildren<Collider>();
        foreach (Collider c in carColliders)
            if (c != null) Physics.IgnoreCollision(col, c, true); // la voiture traverse, seul le cône est éjecté

        if (rb == null) rb = gameObject.AddComponent<Rigidbody>(); // gardé (cinématique) après le 1er vol
        rb.isKinematic = false;
        rb.mass = mass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = roadVel + kick;
        rb.angularVelocity = Random.insideUnitSphere * maxSpin;

        flying = true;
        flightTime = 0f;
        startY = transform.position.y;
    }

    void FixedUpdate()
    {
        if (!flying) return;

        float dt = Time.fixedDeltaTime;
        flightTime += dt;

        // Freinage calculé par rapport à la route (qui bouge) : le cône finit par rester sur place sur la route
        Vector3 roadVel = RoadVelocity();
        Vector3 rel = rb.linearVelocity - roadVel;
        float drag = touching ? groundDrag : airDrag;
        float k = Mathf.Clamp01(1f - drag * dt);
        rel.x *= k;
        rel.z *= k;
        rb.linearVelocity = roadVel + rel;
        if (touching) rb.angularVelocity *= Mathf.Clamp01(1f - drag * dt);

        if (transform.position.y < startY - fallLimit)
        {
            // Tombé hors de la route : il disparaît (comme avant)
            foreach (var r in renderers) r.enabled = false;
            col.enabled = false;
            StopFlight(true);
        }
        else if ((touching && flightTime > 0.5f && rel.magnitude < restSpeed) || flightTime > maxFlightTime)
        {
            StopFlight(true); // posé : il redevient un décor qui défile
        }

        touching = false; // remis à vrai par OnCollisionStay pendant ce pas de physique
    }

    void OnCollisionStay(Collision collision) => touching = true;

    void StopFlight(bool backToTrack)
    {
        // Le Rigidbody reste mais devient cinématique : le cône suit à nouveau le TrackRoot sans physique
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;
        }

        if (carColliders != null)
        {
            foreach (Collider c in carColliders)
                if (c != null && col != null) Physics.IgnoreCollision(col, c, false);
            carColliders = null;
        }

        if (backToTrack && home != null && transform.parent != home) transform.SetParent(home, true);
        if (col != null) col.isTrigger = true;
        flying = false;
        touching = false;
    }

    Vector3 RoadVelocity() => road != null ? new Vector3(0f, 0f, -road.ScrollSpeed) : Vector3.zero;
}
