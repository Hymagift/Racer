using UnityEngine;

/// <summary>
/// À mettre sur chaque cône (pour l'instant un cube Unity).
/// Le Collider doit être en "Is Trigger" : la voiture traverse le cône sans rebondir,
/// et le cône donne un multiplicateur au ScoreManager.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Cone : MonoBehaviour
{
    bool hit;
    ScoreManager score;

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    // Si le générateur de route réutilise les cônes, ils redeviennent actifs
    void OnEnable()
    {
        hit = false;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
        GetComponent<Collider>().enabled = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (hit) return;
        if (other.GetComponentInParent<DriftCar>() == null) return;

        hit = true;

        if (score == null) score = FindFirstObjectByType<ScoreManager>();
        if (score != null) score.OnConeHit();

        // Le cône disparaît (plus tard : animation de cône renversé)
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        GetComponent<Collider>().enabled = false;
    }
}