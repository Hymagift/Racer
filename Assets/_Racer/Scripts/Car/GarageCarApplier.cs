using UnityEngine;

/// <summary>
/// Applique la voiture équipée dans le Garage (CarCatalog) au modèle de la voiture :
/// son matériau s'il en a un, sinon sa couleur (teinte de la carrosserie).
/// À mettre sur la voiture, dans la scène de jeu (elle sert aussi pour la démo du menu).
/// </summary>
public class GarageCarApplier : MonoBehaviour
{
    [Tooltip("Vide = Assets/_Racer/Resources/CarCatalog.asset, ou le catalogue par défaut")]
    public CarCatalog catalog;
    [Tooltip("Renderers de la carrosserie. Vide = tous ceux de la voiture.")]
    public Renderer[] targets;
    [Tooltip("Numéro du matériau de carrosserie sur chaque renderer (0 = le premier)")]
    public int materialSlot = 0;

    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int LegacyColor = Shader.PropertyToID("_Color");

    void Start() => Apply();

    public void Apply()
    {
        if (catalog == null) catalog = CarCatalog.Load();
        var car = catalog.Equipped;
        if (car == null) return;

        var renderers = (targets != null && targets.Length > 0) ? targets : GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer) continue;
            if (materialSlot < 0 || materialSlot >= r.sharedMaterials.Length) continue;

            if (car.material != null)
            {
                var mats = r.sharedMaterials;
                mats[materialSlot] = car.material;
                r.sharedMaterials = mats;
            }
            else
            {
                // Teinte sans créer de copie du matériau
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block, materialSlot);
                block.SetColor(BaseColor, car.color);
                block.SetColor(LegacyColor, car.color);
                r.SetPropertyBlock(block, materialSlot);
            }
        }
    }
}
