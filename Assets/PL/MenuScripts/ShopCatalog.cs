using System;
using UnityEngine;

/// <summary>
/// Liste des objets du shop. Crée-la avec : clic droit dans Project > Create > Drift > Shop Catalog.
/// Le premier objet de la liste est équipé par défaut ; un objet à prix 0 est gratuit.
/// </summary>
[CreateAssetMenu(menuName = "Drift/Shop Catalog", fileName = "ShopCatalog")]
public class ShopCatalog : ScriptableObject
{
    [Serializable]
    public class Item
    {
        [Tooltip("Identifiant unique (ex : red, blue, gold). Ne pas changer après publication.")]
        public string id;
        public string displayName;
        public int price;
        [Tooltip("Matériau appliqué à la voiture quand l'objet est équipé")]
        public Material material;

        public bool IsOwned => price <= 0 || PlayerData.IsOwned(id);
    }

    public Item[] items;

    public Item Find(string id)
    {
        if (items == null) return null;
        foreach (var it in items)
            if (it.id == id) return it;
        return null;
    }

    /// <summary>Objet équipé (le premier de la liste si rien n'est choisi).</summary>
    public Item Equipped
    {
        get
        {
            var it = Find(PlayerData.EquippedId);
            if (it == null || !it.IsOwned) it = (items != null && items.Length > 0) ? items[0] : null;
            return it;
        }
    }
}