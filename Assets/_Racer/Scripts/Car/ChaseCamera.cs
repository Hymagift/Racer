using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Caméra toujours DERRIÈRE la voiture, même quand la route part en travers ou fait demi-tour (épingles, lacets).
///
/// La caméra Cinemachine de la scène suit une cible invisible ("CameraTarget") placée sur la voiture et tournée dans le sens
/// où l'on avance : surtout le sens de la route devant la voiture, un peu le sens de la glisse. On n'utilise pas l'orientation
/// de la voiture elle-même : en drift, son nez part de 40° sur le côté et la caméra tournerait sans arrêt.
/// La caméra (CinemachineFollow) doit être en Binding Mode "Lock To Target With World Up" : son décalage (derrière, en hauteur)
/// tourne avec la cible, et son Rotation Composer vise la cible.
///
/// Ajouté tout seul sur la voiture par DriftCar. La voiture ne bouge jamais en Z (le décor défile) :
/// le sens de déplacement est donc calculé par rapport à la route (X de la physique, Z du défilement).
/// </summary>
[DefaultExecutionOrder(-50)]
public class ChaseCamera : MonoBehaviour
{
    [Tooltip("Caméra Cinemachine à piloter. Vide = celle qui est sous la voiture, sinon la première de la scène")]
    public CinemachineCamera cinemachineCamera;
    [Range(0f, 1f)]
    [Tooltip("Part du sens de la route dans l'orientation de la caméra (le reste = sens de déplacement de la voiture)")]
    public float roadWeight = 0.65f;
    [Tooltip("Distance (m) devant la voiture où l'on lit le sens de la route : la caméra commence à tourner un peu avant le virage")]
    public float roadLookAhead = 4f;
    [Tooltip("Temps (s) pour que la caméra rattrape le cap visé. Plus petit = plus nerveux")]
    public float turnSmoothTime = 0.35f;

    DriftCar car;
    Rigidbody rb;
    Transform target;
    float yaw, yawVelocity;

    void Start()
    {
        car = GetComponent<DriftCar>();
        rb = GetComponent<Rigidbody>();

        if (cinemachineCamera == null) cinemachineCamera = GetComponentInChildren<CinemachineCamera>(true);
        if (cinemachineCamera == null) cinemachineCamera = FindFirstObjectByType<CinemachineCamera>();
        if (cinemachineCamera == null) { enabled = false; return; }

        var go = new GameObject("CameraTarget");
        SceneManager.MoveGameObjectToScene(go, gameObject.scene); // reste dans la scène de jeu (menu chargé en additif)
        target = go.transform;
        yaw = transform.eulerAngles.y;
        target.SetPositionAndRotation(transform.position, Quaternion.Euler(0f, yaw, 0f));
        cinemachineCamera.Follow = target;
    }

    void OnDestroy()
    {
        if (target != null) Destroy(target.gameObject);
    }

    void LateUpdate()
    {
        if (target == null) return;

        float wanted = yaw;
        ZigzagRoadGenerator road = car != null ? car.road : null;
        if (road != null)
        {
            float roadYaw = road.PointAt(road.TotalDistance + roadLookAhead).HeadingDeg;
            wanted = roadYaw;

            // Sens de déplacement (seulement si la voiture roule vraiment)
            Vector2 v = new Vector2(rb != null ? rb.linearVelocity.x : 0f, road.ScrollSpeed);
            if (v.sqrMagnitude > 4f)
            {
                float moveYaw = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
                wanted = roadYaw + Mathf.DeltaAngle(roadYaw, moveYaw) * (1f - roadWeight);
            }
        }

        yaw = Mathf.SmoothDampAngle(yaw, wanted, ref yawVelocity, Mathf.Max(0.01f, turnSmoothTime));
        target.SetPositionAndRotation(transform.position, Quaternion.Euler(0f, yaw, 0f));
    }
}
