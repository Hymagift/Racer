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

    [Header("Barre du haut")]
    [Tooltip("Cadre du record (titre RECORD compris)")]
    public Sprite recordBox;
    [Tooltip("Compteur de rubis (rubis et + compris)")]
    public Sprite rubisCounter;
    [Tooltip("Compteur de pièces (pièce comprise)")]
    public Sprite coinCounter;

    [Header("Onglets du bas")]
    public Sprite tabPlay;
    public Sprite tabInvocations;
    public Sprite tabGarage;
    public Sprite tabOptions;

    [Header("Page Jouer")]
    public Sprite playButton;
    [Tooltip("Logo RACER")]
    public Sprite logo;
    [Tooltip("\"Glisse pour les invocations >>\"")]
    public Sprite swipeHint;

    [Header("Pages")]
    [Tooltip("Panneau sans titre (Garage, Options) : l'onglet de la page sert de titre par-dessus")]
    public Sprite emptyPanel;

    [Header("Options")]
    public Sprite volumeTitle;
    [Tooltip("Barre du curseur de volume (9-slice)")]
    public Sprite sliderBar;
    public Sprite sliderKnob;
    public Sprite resetButton;
    public Sprite quitButton;

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
    [Tooltip("Rubis possédés (haut de la boutique)")]
    public Sprite rubisBalance;
    [Tooltip("\"Réserve :\" des pilotes auto (haut de la boutique)")]
    public Sprite reserveBalance;
    public Sprite starterPackCard;
    public Sprite carPackCard;
    public Sprite rubisTitle;
    public Sprite autopilotTitle;
    public Sprite coinsTitle;
    [Tooltip("Cartes des packs de rubis, retrouvées par le nombre de rubis (80, 500...)")]
    public PriceSprite[] rubisCards;
    [Tooltip("Cartes des pilotes auto, retrouvées par la quantité (1, 5...), dans l'ordre des offres")]
    public PriceSprite[] autopilotCards;
    [Tooltip("Bandeau \"30s de conduite parfaite\" au-dessus des cartes des pilotes auto")]
    public Sprite autopilotBanner;
    [Tooltip("Cartes des pièces, retrouvées par la quantité (1000, 6000...)")]
    public PriceSprite[] coinCards;

    [Header("En jeu")]
    [Tooltip("Compteur du pilote auto, sans le temps ni le remplissage de la barre (ajoutés par le jeu)")]
    public Sprite autopilotHud;
    [Tooltip("Bouton ACTIVER du pilote auto au départ")]
    public Sprite autopilotActivate;
    [Tooltip("Carte du pilote auto au départ, sans sous-titre ni barre (écrits par le jeu)")]
    public Sprite autopilotCard;
    [Tooltip("Boutons \"continuer\" en rubis, par prix (le prix monte à chaque mort)")]
    public PriceSprite[] continuePrices;
    [Tooltip("\"Non merci\" (abandonner)")]
    public Sprite continueGiveUp;

    [Header("Garage")]
    public Sprite rarityCommune;
    public Sprite rarityRare;
    public Sprite rarityEpique;
    public Sprite rarityLegendaire;
    [Tooltip("\"???\" : nom d'une voiture verrouillée")]
    public Sprite unknownName;
    public Sprite equipButton;
    [Tooltip("\"Equipée\" : affiché à la place du bouton sur la voiture équipée")]
    public Sprite equippedButton;
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
