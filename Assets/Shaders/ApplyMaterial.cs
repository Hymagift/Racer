using UnityEngine;

/// <summary>
/// Remplace au lancement le material de tous les modèles enfants (ex : la voiture importée sans material).
/// Utile pour les FBX qui n'ont pas de material : impossible de le choisir dans l'import.
/// Le garage peut toujours teinter la voiture par-dessus (sa teinte est passée en gris par le shader Racer/ShadedBW).
/// </summary>
public class ApplyMaterial : MonoBehaviour
{
    [Tooltip("Material appliqué à tous les MeshRenderer / SkinnedMeshRenderer enfants")]
    public Material material;

    void Awake()
    {
        if (material == null) return;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
        }
    }
}
