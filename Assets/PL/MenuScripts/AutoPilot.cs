using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pilote automatique pour la partie qui tourne en fond du menu.
/// À mettre sur la voiture de la scène du menu : elle suit la route toute seule en driftant.
/// Nécessite les 2 champs "useExternalInput" / "externalInput" dans DriftCar.
/// Si la voiture tombe malgré tout, la scène du menu est rechargée.
/// </summary>
[RequireComponent(typeof(DriftCar))]
public class DriftAutoPilot : MonoBehaviour
{
    public ZigzagRoadGenerator road;

    [Tooltip("Distance (m) devant la voiture où l'on regarde le centre de la route")]
    public float lookAhead = 12f;
    [Tooltip("Anticipation : on tient compte de la vitesse latérale sur ce délai (s)")]
    public float predictTime = 0.5f;
    [Tooltip("Écart toléré (m) avant de braquer. Plus petit = drift plus nerveux")]
    public float deadZone = 0.6f;
    [Tooltip("Si la voiture tourne du mauvais côté, coche ça")]
    public bool invertSteering = false;
    [Tooltip("Hauteur de chute (m) sous la route avant de recharger le menu")]
    public float fallDepth = 6f;

    DriftCar car;
    Rigidbody rb;
    float dirSign = 1f;
    bool calibrated;

    void Awake()
    {
        car = GetComponent<DriftCar>();
        rb = GetComponent<Rigidbody>();
        car.useExternalInput = true;
        car.debugHUD = false;
        if (road == null) road = FindFirstObjectByType<ZigzagRoadGenerator>();
    }

    // Détecte dans quel sens "LateralOffset" augmente par rapport à l'axe X du monde
    void Calibrate()
    {
        Vector3 p = transform.position + Vector3.forward * lookAhead;
        float a = road.LateralOffset(p);
        float b = road.LateralOffset(p + Vector3.right);
        dirSign = (b - a) >= 0f ? 1f : -1f;
        calibrated = true;
    }

    void Update()
    {
        if (road == null) return;
        if (!calibrated) Calibrate();

        Vector3 p = transform.position;

        // Position de la voiture par rapport au centre de la route plus loin devant (>0 = trop à droite)
        float off = road.LateralOffset(p + Vector3.forward * lookAhead) * dirSign;
        float predicted = off + rb.linearVelocity.x * predictTime;

        float input = 0f;
        if (Mathf.Abs(predicted) > deadZone) input = predicted > 0f ? -1f : 1f;
        if (invertSteering) input = -input;
        car.externalInput = input;

        // Filet de sécurité : chute -> on relance le menu
        if (p.y < road.RoadHeightAt(road.TrackZ(p.z)) - fallDepth)
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}