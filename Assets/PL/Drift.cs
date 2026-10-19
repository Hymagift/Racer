using UnityEngine;

/// <summary>
/// Voiture de drift en mode "décor qui défile" :
/// la voiture ne bouge pas en Z, sa vitesse vers l'avant est transmise à la route, qui défile sous elle.
/// En X (gauche/droite) et en Y (pentes, bosses), c'est la vraie physique : les glissières bloquent toujours la voiture.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Drift : MonoBehaviour
{
    public ZigzagRoadGenerator road;   // la route à faire défiler

    public float forwardSpeed = 15f;   // vitesse d'avancée
    public float turnSpeed = 90f;      // degrés par seconde
    public float steerSmooth = 8f;     // douceur du volant
    [Range(0f, 1f)]
    public float grip = 0.1f;          // 1 = pas de drift, 0 = glace

    Rigidbody rb;
    float steer, targetSteer;
    float forwardVel;                  // vitesse "virtuelle" vers l'avant (la voiture, elle, reste immobile en Z)

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints |= RigidbodyConstraints.FreezePositionZ; // la voiture ne bouge jamais en Z

        // Si le champ n'est pas assigné dans l'inspecteur, on cherche la route dans la scène
        if (road == null) road = FindFirstObjectByType<ZigzagRoadGenerator>();
        if (road == null)
            Debug.LogError("[DriftCar] Aucune ZigzagRoadGenerator trouvée : le décor ne défilera pas.", this);
    }

    void OnDisable()
    {
        if (road != null) road.ScrollSpeed = 0f; // voiture désactivée (game over) : le décor s'arrête
    }

    void Update()
    {
        targetSteer = 0f;

        // Fonctionne au doigt sur téléphone ET à la souris dans l'éditeur
        if (Input.GetMouseButton(0))
            targetSteer = Input.mousePosition.x < Screen.width / 2f ? -1f : 1f;

        steer = Mathf.Lerp(steer, targetSteer, steerSmooth * Time.deltaTime);
    }

    void FixedUpdate()
    {
        // 1. Rotation de la voiture
        float angle = steer * turnSpeed * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, angle, 0f));

        // 2. Vitesse : on tire doucement la vélocité vers l'avant de la voiture.
        //    Un grip faible = la voiture glisse = drift.
        Vector3 v = rb.linearVelocity;   // X et Y : physique réelle (collisions, pentes) — "rb.velocity" avant Unity 6
        v.z = forwardVel;                // Z : vitesse virtuelle, le décor défile à cette vitesse
        Vector3 target = transform.forward * forwardSpeed;
        Vector3 newVel = Vector3.Lerp(v, target, grip);
        newVel.y = v.y;

        forwardVel = newVel.z;
        newVel.z = 0f;                   // la voiture reste en place en Z
        rb.linearVelocity = newVel;

        // 3. Le décor défile à la vitesse avant (jamais à reculons)
        if (road != null) road.ScrollSpeed = Mathf.Max(0f, forwardVel);
    }
}