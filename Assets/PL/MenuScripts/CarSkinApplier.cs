using UnityEngine;

/// <summary>
/// Applique le matériau de l'objet équipé dans le shop au modèle de la voiture.
/// À mettre sur la voiture (scène de jeu ET scène du menu pour voir le skin en fond).
/// </summary>
public class CarSkinApplier : MonoBehaviour
{
    public ShopCatalog catalog;
    [Tooltip("Renderers à modifier. Vide = tous ceux de la voiture.")]
    public Renderer[] targets;
    [Tooltip("Numéro du matériau à remplacer sur chaque renderer (0 = le premier : carrosserie en général)")]
    public int materialSlot = 0;

    void Start() => Apply();

    public void Apply()
    {
        if (catalog == null) return;
        var item = catalog.Equipped;
        if (item == null || item.material == null) return;

        var renderers = (targets != null && targets.Length > 0) ? targets : GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            var mats = r.sharedMaterials;
            if (materialSlot < 0 || materialSlot >= mats.Length) continue;
            mats[materialSlot] = item.material;
            r.sharedMaterials = mats;
        }
    }
}