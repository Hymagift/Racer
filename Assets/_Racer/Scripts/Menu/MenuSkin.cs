using System;
using UnityEngine;

/// <summary>
/// Images de l'UI (dossier Assets/_Racer/UI) utilisées par le menu à la place des boutons dessinés par code.
/// Asset : Assets/_Racer/Resources/MenuSkin.asset. Une case vide = l'ancien bouton dessiné par code.
/// Les textes sont dans les images : les boutons de prix sont retrouvés par leur prix (chiffres seulement : "2,99 €" = "299").
/// </summary>
[CreateAssetMenu(menuName = "Drift/Menu Skin", fileName = "MenuSkin")]
public class MenuSkin : ScriptableObject
{
    [Serializable]
    public class PriceSprite
    {
        [Tooltip("Prix, chiffres seulement : 2,99 € = 299, 20 rubis = 20")]
        public string price;
        public Sprite sprite;
    }

    [Header("Onglets du bas")]
    public Sprite tabPlay;
    public Sprite tabInvocations;
    public Sprite tabGarage;
    public Sprite tabOptions;

    [Header("Page Jouer")]
    public Sprite playButton;

    [Header("Invocations")]
    [Tooltip("Panneau de la page Invocations (titre compris)")]
    public Sprite invocationsPanel;
    public Sprite tabBoosters;
    public Sprite tabBoutique;
    [Tooltip("Carte de chaque booster, dans l'ordre du catalogue (Bronze, Or, Diamant)")]
    public Sprite[] boosterCards;
    [Tooltip("Bouton OUVRIR de chaque booster, dans l'ordre du catalogue")]
    public Sprite[] boosterOpenButtons;

    [Header("Boutique")]
    [Tooltip("Boutons de prix en euros (packs et rubis)")]
    public PriceSprite[] euroPrices;
    [Tooltip("Boutons de prix en rubis des pilotes auto")]
    public PriceSprite[] autopilotPrices;
    [Tooltip("Boutons de prix en rubis des pièces")]
    public PriceSprite[] coinPrices;

    [Header("Garage")]
    public Sprite rarityCommune;
    public Sprite rarityRare;
    public Sprite rarityEpique;
    public Sprite rarityLegendaire;
    [Tooltip("\"???\" : nom d'une voiture verrouillée")]
    public Sprite unknownName;
    public Sprite equipButton;
    [Tooltip("Image avec les deux flèches (gauche au début, droite à la fin)")]
    public Sprite arrows;

    static MenuSkin loaded;
    static bool searched;

    /// <summary>Skin du jeu (Resources/MenuSkin), ou null s'il n'existe pas.</summary>
    public static MenuSkin Current
    {
        get
        {
            if (!searched) { loaded = Resources.Load<MenuSkin>("MenuSkin"); searched = true; }
            return loaded;
        }
    }

    public static Sprite Rarity(CarRarity r)
    {
        var s = Current;
        if (s == null) return null;
        switch (r)
        {
            case CarRarity.Rare: return s.rarityRare;
            case CarRarity.Epique: return s.rarityEpique;
            case CarRarity.Legendaire: return s.rarityLegendaire;
            default: return s.rarityCommune;
        }
    }

    public static Sprite At(Sprite[] list, int index) => list != null && index >= 0 && index < list.Length ? list[index] : null;

    /// <summary>Image du bouton de ce prix ("2,99 €", "20"...), ou null.</summary>
    public static Sprite Price(PriceSprite[] list, string price)
    {
        if (list == null || string.IsNullOrEmpty(price)) return null;
        string key = Digits(price);
        foreach (var p in list)
            if (p != null && p.sprite != null && Digits(p.price) == key) return p.sprite;
        return null;
    }

    static string Digits(string s)
    {
        if (s == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char c in s) if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    static Sprite arrowLeft, arrowRight;

    /// <summary>Flèche gauche ou droite, découpée dans l'image des deux flèches.</summary>
    public static Sprite Arrow(bool right)
    {
        var s = Current;
        if (s == null || s.arrows == null) return null;
        if (arrowLeft == null)
        {
            Rect r = s.arrows.rect;
            float w = Mathf.Min(r.height, r.width * 0.5f); // chaque flèche est à peu près carrée, collée au bord
            arrowLeft = Sprite.Create(s.arrows.texture, new Rect(r.x, r.y, w, r.height), new Vector2(0.5f, 0.5f), 100f);
            arrowRight = Sprite.Create(s.arrows.texture, new Rect(r.xMax - w, r.y, w, r.height), new Vector2(0.5f, 0.5f), 100f);
        }
        return right ? arrowRight : arrowLeft;
    }
}
