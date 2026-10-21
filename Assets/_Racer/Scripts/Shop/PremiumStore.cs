using System;
using UnityEngine;

/// <summary>
/// Achats avec de l'argent réel (rubis, pack de démarrage) et prix en rubis des objets du jeu.
///
/// ÉTAT ACTUEL : MODE TEST. Dans l'éditeur et les builds de développement, un achat est simulé
/// (l'UI affiche clairement "Achat de test, aucun paiement"). Dans un build de production, les achats
/// sont refusés tant que le vrai paiement n'est pas branché.
///
/// Pour vendre pour de vrai : installe le package Unity IAP (com.unity.purchasing), déclare les mêmes
/// identifiants de produits dans la Play Console / App Store Connect, puis remplace le contenu de Buy()
/// par l'appel au store, et appelle Grant() quand le store confirme le paiement. Les prix affichés
/// doivent alors venir du store (prix localisé) au lieu de fallbackPrice.
/// </summary>
public static class PremiumStore
{
    public class Product
    {
        public string id;              // identifiant du produit dans les stores
        public string title;
        public int rubis;
        public int bonusPercent;       // rubis en plus par rapport au plus petit pack (calculé, pas inventé)
        public int autopilots;
        public int coins;
        public string fallbackPrice;   // prix affiché tant que le store n'est pas branché
        public bool oneTime;           // achat unique (pack de démarrage)
        public string badge;           // étiquette affichée sur la carte (vide = aucune)
        public string carId;           // voiture débloquée (vide = aucune)
        public int carDuplicateRubis;  // rubis donnés à la place si la voiture est déjà débloquée
        public string boosterId;       // boosters offerts, à ouvrir dans les Invocations
        public int boosterCount;
    }

    /// <summary>Le pack de démarrage (achat unique).</summary>
    public static readonly Product StarterPack = new Product
    {
        id = "racer_starter_pack", title = "Pack de démarrage", rubis = 300, autopilots = 3, coins = 2000,
        fallbackPrice = "2,99 €", oneTime = true, badge = "UNE SEULE FOIS",
    };

    /// <summary>Pack pilote (achat unique) : une voiture épique, des boosters Or et des rubis.</summary>
    public static readonly Product CarPack = new Product
    {
        id = "racer_pilot_pack", title = "Pack Pilote", rubis = 500, carId = "raijin", carDuplicateRubis = 250,
        boosterId = "or", boosterCount = 3, fallbackPrice = "7,99 €", oneTime = true, badge = "UNE SEULE FOIS",
    };

    /// <summary>Packs de rubis, du plus petit au plus grand.</summary>
    public static readonly Product[] RubisPacks =
    {
        new Product { id = "racer_rubis_80", title = "Poignée de rubis", rubis = 80, fallbackPrice = "0,99 €" },
        new Product { id = "racer_rubis_500", title = "Sac de rubis", rubis = 500, bonusPercent = 25, fallbackPrice = "4,99 €" },
        new Product { id = "racer_rubis_1200", title = "Coffre de rubis", rubis = 1200, bonusPercent = 50, fallbackPrice = "9,99 €", badge = "MEILLEURE VALEUR" },
    };

    // ---------- Objets achetés avec des rubis ----------

    public struct RubisOffer
    {
        public int amount;      // pilotes auto ou pièces obtenus
        public int price;       // en rubis
        public int discount;    // % de réduction par rapport à l'offre de base (calculé)
    }

    /// <summary>Prix d'un pilote auto à l'unité (aussi utilisé en jeu quand la réserve est vide).</summary>
    public const int AutopilotPrice = 20;

    public static readonly RubisOffer[] AutopilotOffers =
    {
        new RubisOffer { amount = 1, price = AutopilotPrice },
        new RubisOffer { amount = 5, price = 80, discount = 20 },
        new RubisOffer { amount = 15, price = 200, discount = 33 },
    };

    public static readonly RubisOffer[] CoinOffers =
    {
        new RubisOffer { amount = 1000, price = 40 },
        new RubisOffer { amount = 6000, price = 200, discount = 17 },
    };

    // ---------- Achat en argent réel ----------

    /// <summary>Vrai tant que les achats sont simulés (éditeur, build de développement).</summary>
    public static bool TestMode => Application.isEditor || Debug.isDebugBuild;

    public static string PriceOf(Product p) => p.fallbackPrice;

    public static bool IsSoldOut(Product p) =>
        p.oneTime && (p == StarterPack ? PlayerData.StarterPackBought : PlayerData.IsBought(p.id));

    /// <summary>
    /// Lance l'achat. done(true, null) quand le produit est livré, done(false, message) sinon.
    /// En mode test, le produit est livré tout de suite (aucun paiement).
    /// </summary>
    public static void Buy(Product p, Action<bool, string> done)
    {
        if (IsSoldOut(p)) { done?.Invoke(false, "Déjà acheté"); return; }

        if (!TestMode)
        {
            // TODO : appeler Unity IAP ici, puis Grant(p) dans le callback de paiement confirmé
            done?.Invoke(false, "Les achats ne sont pas encore disponibles");
            return;
        }

        Grant(p);
        done?.Invoke(true, null);
    }

    /// <summary>Livre le contenu d'un produit payé (à appeler par le store quand le paiement est confirmé).</summary>
    public static void Grant(Product p)
    {
        if (p.rubis > 0) PlayerData.AddRubis(p.rubis, save: false);
        if (p.autopilots > 0) PlayerData.AddAutopilots(p.autopilots, save: false);
        if (p.coins > 0) PlayerData.AddCoins(p.coins, save: false);
        if (!string.IsNullOrEmpty(p.carId))
        {
            var car = CarCatalog.Load().Find(p.carId);
            if (car != null && !car.IsUnlocked) PlayerData.SetCarUnlocked(car.id, save: false);
            else PlayerData.AddRubis(p.carDuplicateRubis, save: false); // déjà à toi : compensé en rubis
        }
        if (!string.IsNullOrEmpty(p.boosterId) && p.boosterCount > 0)
            PlayerData.AddFreeBoosters(p.boosterId, p.boosterCount, save: false);
        if (p.oneTime) PlayerData.SetBought(p.id, save: false);
        if (p == StarterPack) PlayerData.StarterPackBought = true; // sauvegarde tout
        else PlayerData.SaveAndNotify();
    }

    /// <summary>Valeur du contenu d'un produit en rubis (pour montrer ce que contient le pack de démarrage).</summary>
    public static int ValueInRubis(Product p)
    {
        float coinRate = CoinOffers[0].price / (float)CoinOffers[0].amount; // rubis par pièce, au prix de base
        return p.rubis + p.autopilots * AutopilotPrice + Mathf.RoundToInt(p.coins * coinRate);
    }
}
