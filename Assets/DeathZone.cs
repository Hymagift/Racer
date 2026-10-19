using UnityEngine;

/// <summary>
/// Zone mortelle : la partie s'arrête dès que la voiture entre dans ce collider.
/// À mettre sur un obstacle, un trou, etc. Le collider est automatiquement passé en "Is Trigger".
///
/// Si la zone doit défiler avec la route, place-la en enfant de road.TrackRoot
/// (la death zone de chute dans le vide est déjà gérée automatiquement par DriftGameManager).
/// </summary>
[RequireComponent(typeof(Collider))]
public class DeathZone : MonoBehaviour
{
    DriftGameManager manager;

    void Reset() => GetComponent<Collider>().isTrigger = true;

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        manager = FindFirstObjectByType<DriftGameManager>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (manager == null) return;
        if (other.GetComponentInParent<DriftCar>() != null)
            manager.Die();
    }
}