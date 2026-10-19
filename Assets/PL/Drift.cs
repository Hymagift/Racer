using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DriftCar : MonoBehaviour
{
    public float forwardSpeed = 15f;   // vitesse d'avancée
    public float turnSpeed = 90f;      // degrés par seconde
    public float steerSmooth = 8f;     // douceur du volant
    [Range(0f, 1f)]
    public float grip = 0.1f;          // 1 = pas de drift, 0 = glace

    Rigidbody rb;
    float steer, targetSteer;

    void Awake() => rb = GetComponent<Rigidbody>();

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
        Vector3 v = rb.linearVelocity;   // "rb.velocity" avant Unity 6
        Vector3 target = transform.forward * forwardSpeed;
        Vector3 newVel = Vector3.Lerp(v, target, grip);
        newVel.y = v.y;
        rb.linearVelocity = newVel;
    }
}
