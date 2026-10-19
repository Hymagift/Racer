using TMPro;
using UnityEngine;

/// <summary>
/// Shop simple : affiche les objets du catalogue, gère achat et équipement.
/// À mettre sur le panneau "ShopPanel" (la liste se reconstruit à chaque ouverture).
/// </summary>
public class ShopManager : MonoBehaviour
{
    public ShopCatalog catalog;
    [Tooltip("Conteneur des lignes (avec un Vertical Layout Group)")]
    public Transform container;
    public ShopItemUI itemPrefab;
    public TMP_Text coinsText;

    void OnEnable()
    {
        PlayerData.OnChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        PlayerData.OnChanged -= Refresh;
    }

    void Refresh()
    {
        if (coinsText != null) coinsText.text = PlayerData.Coins.ToString();
        if (catalog == null || container == null || itemPrefab == null) return;

        // On vide la liste puis on la reconstruit
        foreach (Transform child in container)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        var equipped = catalog.Equipped;

        foreach (var item in catalog.items)
        {
            var it = item; // copie pour la fonction du bouton
            var ui = Instantiate(itemPrefab, container);

            bool owned = it.IsOwned;
            bool isEquipped = equipped == it;

            string priceLabel = owned ? "" : it.price.ToString();
            string buttonLabel = isEquipped ? "Équipé" : (owned ? "Équiper" : "Acheter");
            bool interactable = !isEquipped && (owned || PlayerData.Coins >= it.price);

            ui.Setup(it.displayName, priceLabel, buttonLabel, interactable, () => OnItemClicked(it));
        }
    }

    void OnItemClicked(ShopCatalog.Item item)
    {
        if (!item.IsOwned)
        {
            if (!PlayerData.TrySpend(item.price)) return;
            PlayerData.SetOwned(item.id);
        }
        PlayerData.EquippedId = item.id;
    }

    /// <summary>Pour tester : branche-la sur un bouton temporaire (+100 pièces).</summary>
    public void AddTestCoins() => PlayerData.AddCoins(100);
}