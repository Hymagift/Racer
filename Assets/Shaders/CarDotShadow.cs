using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ombre en points blancs sous la voiture (shader Racer/DotShadow).
///
/// Mise en place : ajoute ce script sur la voiture et assigne le material "DotShadow".
/// Un Quad est créé tout seul. À chaque image il est posé sur le sol sous la voiture (raycast),
/// orienté comme la voiture et incliné comme la route, et il s'efface quand la voiture décolle.
/// L'ombre classique de la voiture est coupée (option "Disable Car Shadows") pour ne garder que les points.
/// </summary>
public class CarDotShadow : MonoBehaviour
{
    [Tooltip("Material qui utilise le shader Racer/DotShadow")]
    public Material shadowMaterial;
    [Tooltip("Taille de l'ombre au sol (m) : x = largeur, y = longueur")]
    public Vector2 size = new Vector2(2.4f, 4.6f);
    [Tooltip("Décalage de l'ombre par rapport au centre de la voiture (m, repère de la voiture : x = côté, y = avant)")]
    public Vector2 offset = Vector2.zero;
    [Tooltip("Hauteur au-dessus du sol pour éviter de rentrer dans la route")]
    public float groundOffset = 0.03f;
    [Tooltip("Hauteur de la voiture au-dessus du sol à partir de laquelle l'ombre a disparu")]
    public float fadeHeight = 3f;
    [Tooltip("Couches considérées comme le sol")]
    public LayerMask groundMask = ~0;
    [Tooltip("Coupe l'ombre portée classique des modèles de la voiture")]
    public bool disableCarShadows = true;

    static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    Transform quad;
    MeshRenderer quadRenderer;
    MaterialPropertyBlock block;
    float baseOpacity = 1f;
    readonly RaycastHit[] hits = new RaycastHit[8];

    void Start()
    {
        if (shadowMaterial == null)
        {
            Debug.LogError("[CarDotShadow] Assigne un material qui utilise le shader Racer/DotShadow.", this);
            enabled = false;
            return;
        }

        if (disableCarShadows)
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = ShadowCastingMode.Off;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "DotShadow";
        Destroy(go.GetComponent<Collider>()); // pas de collision : la voiture ne doit pas rouler dessus
        go.transform.SetParent(transform, false);
        quad = go.transform;

        quadRenderer = go.GetComponent<MeshRenderer>();
        quadRenderer.sharedMaterial = shadowMaterial;
        quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;

        block = new MaterialPropertyBlock();
        if (shadowMaterial.HasProperty(OpacityId)) baseOpacity = shadowMaterial.GetFloat(OpacityId);
    }

    void LateUpdate()
    {
        Vector3 origin = transform.position + transform.right * offset.x + transform.forward * offset.y + Vector3.up * 1.5f;

        // On cherche le sol le plus proche sous la voiture, en ignorant ses propres colliders
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 1.5f + fadeHeight, groundMask, QueryTriggerInteraction.Ignore);
        bool found = false;
        RaycastHit best = default;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform)) continue;
            if (!found || hits[i].distance < best.distance) { best = hits[i]; found = true; }
        }

        quadRenderer.enabled = found;
        if (!found) return;

        // Quad couché sur le sol, l'avant de la voiture le long de son axe Y
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, best.normal).normalized;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(Vector3.forward, best.normal).normalized;
        quad.SetPositionAndRotation(best.point + best.normal * groundOffset, Quaternion.LookRotation(-best.normal, forward));
        Vector3 parentScale = transform.lossyScale;
        quad.localScale = new Vector3(size.x / Mathf.Max(parentScale.x, 1e-4f), size.y / Mathf.Max(parentScale.z, 1e-4f), 1f);

        // Plus la voiture est haute, plus l'ombre s'efface
        float height = Mathf.Max(0f, best.distance - 1.5f);
        float fade = 1f - Mathf.Clamp01(height / Mathf.Max(fadeHeight, 0.01f));
        quadRenderer.GetPropertyBlock(block);
        block.SetFloat(OpacityId, baseOpacity * fade);
        quadRenderer.SetPropertyBlock(block);
    }
}
