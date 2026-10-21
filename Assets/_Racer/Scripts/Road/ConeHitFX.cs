using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Petit effet d'impact (VFX Graph) quand la voiture tape un cône.
///
/// Un seul VisualEffect pour toute la route, enfant du TrackRoot : ses particules (simulées en espace LOCAL dans le graph)
/// défilent donc avec le décor, comme les cônes. Chaque impact envoie l'événement "OnConeHit" avec deux attributs :
///   - position : le point d'impact (en local du TrackRoot)
///   - velocity : la vitesse de la voiture par rapport à la route (vers l'avant) x velocityScale
/// Le graph lit ces attributs avec "Inherit Source Position" / "Inherit Source Velocity" (voir Docs/ConeHitVFX.md).
///
/// Mise en place : rien à poser à la main, assigne le .vfx dans "Hit Effect" du ConeSpawner, qui crée cet objet.
/// </summary>
public class ConeHitFX : MonoBehaviour
{
    [Tooltip("Nom de l'événement du graph (contexte Event relié au Start du Spawn)")]
    public string eventName = "OnConeHit";
    [Tooltip("Part de la vitesse de la voiture donnée aux particules (0 = elles partent sur place)")]
    public float velocityScale = 0.35f;

    static ConeHitFX instance;
    static readonly int PositionId = Shader.PropertyToID("position");
    static readonly int VelocityId = Shader.PropertyToID("velocity");

    ZigzagRoadGenerator road;
    VisualEffect vfx;
    VFXEventAttribute attr;
    int eventId;

    /// <summary>Crée l'effet sous le TrackRoot de la route (appelé par le ConeSpawner).</summary>
    public static ConeHitFX Create(ZigzagRoadGenerator road, VisualEffectAsset asset)
    {
        var go = new GameObject("ConeHitFX");
        go.transform.SetParent(road.TrackRoot, false);
        go.SetActive(false); // Awake/OnEnable après la config
        var vfx = go.AddComponent<VisualEffect>();
        vfx.visualEffectAsset = asset;
        var fx = go.AddComponent<ConeHitFX>();
        fx.road = road;
        fx.vfx = vfx;
        go.SetActive(true);
        return fx;
    }

    void OnEnable()
    {
        if (vfx == null) vfx = GetComponent<VisualEffect>();
        if (road == null) road = FindFirstObjectByType<ZigzagRoadGenerator>();
        if (vfx == null) return;

        attr = vfx.CreateVFXEventAttribute();
        eventId = Shader.PropertyToID(eventName);
        instance = this;
        if (road != null) road.OnTrackRebased += HandleTrackRebased;
    }

    void OnDisable()
    {
        if (instance == this) instance = null;
        if (road != null) road.OnTrackRebased -= HandleTrackRebased;
    }

    // Le TrackRoot est ramené à 0 : on garde la même position monde, sinon les particules sautent
    void HandleTrackRebased(float shift) => transform.localPosition -= new Vector3(0f, 0f, shift);

    /// <summary>Joue l'impact au point donné (monde). roadVelocity : vitesse de la voiture par rapport à la route (monde).</summary>
    public static void Play(Vector3 worldPoint, Vector3 roadVelocity)
    {
        if (instance == null || instance.vfx == null) return;
        instance.Emit(worldPoint, roadVelocity);
    }

    void Emit(Vector3 worldPoint, Vector3 roadVelocity)
    {
        attr.SetVector3(PositionId, transform.InverseTransformPoint(worldPoint));
        attr.SetVector3(VelocityId, transform.InverseTransformDirection(roadVelocity * velocityScale));
        vfx.SendEvent(eventId, attr);
    }
}
