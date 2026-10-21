using System;
using System.Collections.Generic;
using UnityEngine;

public enum CarRarity { Commune, Rare, Epique, Legendaire }

/// <summary>Noms et couleurs des raretés (utilisés par toute l'UI).</summary>
public static class RarityInfo
{
    public const int Count = 4;

    public static string Name(CarRarity r)
    {
        switch (r)
        {
            case CarRarity.Rare: return "Rare";
            case CarRarity.Epique: return "Épique";
            case CarRarity.Legendaire: return "Légendaire";
            default: return "Commune";
        }
    }

    public static Color Color(CarRarity r)
    {
        switch (r)
        {
            case CarRarity.Rare: return new Color(0.25f, 0.6f, 1f);
            case CarRarity.Epique: return new Color(0.72f, 0.35f, 1f);
            case CarRarity.Legendaire: return new Color(1f, 0.76f, 0.15f);
            default: return new Color(0.78f, 0.78f, 0.8f);
        }
    }
}

/// <summary>
/// Catalogue des voitures à débloquer et des boosters du shop.
///
/// Les boosters donnent des cartes "blueprint" : pour chaque carte, une rareté est tirée selon les probabilités
/// du booster, puis une voiture de cette rareté au hasard. Une voiture est débloquée quand on a assez de blueprints.
/// Un blueprint d'une voiture déjà débloquée est converti en pièces (doublon).
///
/// Sans asset, un catalogue par défaut est utilisé. Pour le modifier : menu Drift > Créer le catalogue de voitures,
/// puis édite Assets/_Racer/Resources/CarCatalog.asset dans l'Inspector.
/// </summary>
[CreateAssetMenu(menuName = "Drift/Car Catalog", fileName = "CarCatalog")]
public class CarCatalog : ScriptableObject
{
    [Serializable]
    public class Car
    {
        [Tooltip("Identifiant unique sauvegardé sur le téléphone. Ne plus le changer une fois le jeu publié.")]
        public string id;
        public string displayName;
        public CarRarity rarity;
        [Tooltip("Nombre de blueprints nécessaires pour débloquer la voiture")]
        public int blueprintsToUnlock = 4;
        [Tooltip("Voiture possédée dès le départ (elle ne sort jamais des boosters)")]
        public bool unlockedByDefault;
        [Tooltip("Couleur de la carrosserie (pastille de la carte dans le Garage)")]
        public Color color = Color.white;
        [Tooltip("Optionnel : matériau de carrosserie (seulement si la voiture n'a pas de modèle 3D)")]
        public Material material;
        [Tooltip("Modèle 3D de la voiture (un .fbx de Models/Cars). Il remplace celui de la scène quand la voiture est équipée")]
        public GameObject model;
        [Tooltip("Rotation du modèle (degrés) si elle est à l'envers ou de travers : 180 = demi-tour")]
        public float modelYaw;
        [Tooltip("Taille du modèle : 1 = même longueur que la boîte de collision de la voiture")]
        public float modelScale = 1f;
        [Tooltip("Conduite propre à cette voiture : multiplicateurs des réglages du DriftCar de la scène (1 = réglage de la scène)")]
        public CarHandling handling = new CarHandling();

        public bool IsUnlocked => unlockedByDefault || PlayerData.IsCarUnlocked(id);
        public int Blueprints => IsUnlocked ? blueprintsToUnlock : Mathf.Min(PlayerData.GetBlueprints(id), blueprintsToUnlock);
        public int Missing => Mathf.Max(0, blueprintsToUnlock - Blueprints);
    }

    [Serializable]
    public class Booster
    {
        public string id;
        public string displayName;
        public int price = 100;
        [Tooltip("Nombre de blueprints dans le booster")]
        public int cards = 3;
        [Tooltip("Probabilités (en %) de chaque rareté pour chaque carte : Commune, Rare, Épique, Légendaire")]
        public float[] odds = { 70f, 24f, 5f, 1f };
        public Color color = Color.white;
        [Tooltip("Phrase affichée sous le booster dans le shop")]
        public string tagline = "";

        public float Odds(CarRarity r)
        {
            int i = (int)r;
            return odds != null && i < odds.Length ? Mathf.Max(0f, odds[i]) : 0f;
        }

        /// <summary>Probabilité en % (les valeurs sont normalisées si leur somme n'est pas 100).</summary>
        public float Percent(CarRarity r)
        {
            float total = 0f;
            for (int i = 0; i < RarityInfo.Count; i++) total += Odds((CarRarity)i);
            return total > 0f ? Odds(r) / total * 100f : 0f;
        }
    }

    public List<Car> cars = new List<Car>();
    public List<Booster> boosters = new List<Booster>();
    [Tooltip("Pièces rendues pour un doublon (voiture déjà débloquée) : Commune, Rare, Épique, Légendaire")]
    public int[] duplicateCoins = { 10, 25, 60, 150 };

    // ---------- Accès ----------

    static CarCatalog loaded;

    /// <summary>Catalogue du jeu : Resources/CarCatalog.asset s'il existe, sinon le catalogue par défaut.</summary>
    public static CarCatalog Load()
    {
        if (loaded != null) return loaded;
        loaded = Resources.Load<CarCatalog>("CarCatalog");
        if (loaded == null) loaded = CreateDefault();
        return loaded;
    }

    public Car Find(string id)
    {
        foreach (var c in cars)
            if (c.id == id) return c;
        return null;
    }

    /// <summary>Voiture équipée (la première voiture débloquée si rien n'est choisi).</summary>
    public Car Equipped
    {
        get
        {
            var c = Find(PlayerData.EquippedCarId);
            if (c != null && c.IsUnlocked) return c;
            foreach (var car in cars)
                if (car.IsUnlocked) return car;
            return cars.Count > 0 ? cars[0] : null;
        }
    }

    public int UnlockedCount
    {
        get
        {
            int n = 0;
            foreach (var c in cars) if (c.IsUnlocked) n++;
            return n;
        }
    }

    public int DuplicateCoins(CarRarity r)
    {
        int i = (int)r;
        return duplicateCoins != null && i < duplicateCoins.Length ? duplicateCoins[i] : 0;
    }

    // ---------- Ouverture d'un booster ----------

    /// <summary>Résultat d'une carte du booster.</summary>
    public struct Pull
    {
        public Car car;
        public CarRarity rarity;
        /// <summary>Blueprints de la voiture après cette carte.</summary>
        public int blueprints;
        /// <summary>Vrai si cette carte a débloqué la voiture.</summary>
        public bool unlockedNow;
        /// <summary>Pièces rendues si la voiture était déjà débloquée (doublon), sinon 0.</summary>
        public int duplicateCoins;
    }

    /// <summary>
    /// Achète et ouvre un booster : retire les pièces, tire les cartes, sauvegarde blueprints / déblocages / doublons.
    /// Renvoie null si le joueur n'a pas assez de pièces.
    /// </summary>
    public List<Pull> OpenBooster(Booster booster, System.Random rng = null)
    {
        // Achat et résultat sont sauvegardés ensemble à la fin (rien n'est perdu si l'appli est coupée entre les deux)
        // Un booster offert (pack de la boutique) passe avant les pièces
        if (booster == null) return null;
        if (!PlayerData.TryUseFreeBooster(booster.id, save: false) && !PlayerData.TrySpend(booster.price, save: false)) return null;
        rng ??= new System.Random();

        var pulls = new List<Pull>();
        int refund = 0;

        for (int i = 0; i < Mathf.Max(1, booster.cards); i++)
        {
            CarRarity rarity = RollRarity(booster, rng);
            Car car = PickCar(rarity, rng);
            if (car == null) continue;

            var pull = new Pull { car = car, rarity = car.rarity };
            if (car.IsUnlocked)
            {
                pull.duplicateCoins = DuplicateCoins(car.rarity);
                pull.blueprints = car.blueprintsToUnlock;
                refund += pull.duplicateCoins;
            }
            else
            {
                int n = PlayerData.GetBlueprints(car.id) + 1;
                PlayerData.SetBlueprints(car.id, n, save: false);
                if (n >= car.blueprintsToUnlock)
                {
                    PlayerData.SetCarUnlocked(car.id, save: false);
                    pull.unlockedNow = true;
                }
                pull.blueprints = Mathf.Min(n, car.blueprintsToUnlock);
            }
            pulls.Add(pull);
        }

        if (refund > 0) PlayerData.AddCoins(refund, save: false);
        PlayerData.SaveAndNotify();
        return pulls;
    }

    static CarRarity RollRarity(Booster b, System.Random rng)
    {
        float total = 0f;
        for (int i = 0; i < RarityInfo.Count; i++) total += b.Odds((CarRarity)i);
        if (total <= 0f) return CarRarity.Commune;

        double t = rng.NextDouble() * total;
        for (int i = 0; i < RarityInfo.Count; i++)
        {
            t -= b.Odds((CarRarity)i);
            if (t < 0.0) return (CarRarity)i;
        }
        return CarRarity.Legendaire;
    }

    // Voiture au hasard de la rareté tirée (hors voitures de départ). S'il n'y en a pas : rareté inférieure, puis supérieure.
    Car PickCar(CarRarity rarity, System.Random rng)
    {
        var pool = new List<Car>();
        for (int step = 0; step < RarityInfo.Count * 2 && pool.Count == 0; step++)
        {
            int r = step < RarityInfo.Count ? (int)rarity - step : (int)rarity + step - RarityInfo.Count + 1;
            if (r < 0 || r >= RarityInfo.Count) continue;
            foreach (var c in cars)
                if (!c.unlockedByDefault && (int)c.rarity == r) pool.Add(c);
        }
        return pool.Count > 0 ? pool[rng.Next(pool.Count)] : null;
    }

    // ---------- Contenu par défaut ----------

    public static CarCatalog CreateDefault()
    {
        var cat = CreateInstance<CarCatalog>();
        cat.name = "CarCatalog (défaut)";

        void Add(string id, string name, CarRarity r, Color color, CarHandling handling, bool owned = false)
        {
            int[] needed = { 3, 4, 5, 6 };
            cat.cars.Add(new Car { id = id, displayName = name, rarity = r, blueprintsToUnlock = needed[(int)r], color = color, unlockedByDefault = owned, handling = handling });
        }

        // Mêmes voitures (et mêmes conduites) que Assets/_Racer/Resources/CarCatalog.asset (qui, lui, a les modèles 3D)
        Add("first", "1st Car", CarRarity.Commune, new Color(0.85f, 0.85f, 0.85f), owned: true, handling: new CarHandling
        {
            // Voiture de départ : saine et rassurante, glisse plus courte et plus aidée
            speed = 0.97f, acceleration = 0.9f, steering = 0.95f, steerResponse = 0.9f, rotationInertia = 1.05f, countersteer = 1.15f,
            grip = 1f, driftGrip = 1.08f, driftAngle = 0.85f, driftThreshold = 1.05f, driftEngage = 0.85f, driftRecover = 1.15f,
            driftSpeedKeep = 0.95f, bodyRoll = 1f,
        });
        Add("twingy", "Neegy Twingy", CarRarity.Commune, new Color(1f, 0.6f, 0.75f), new CarHandling
        {
            // Petite citadine : légère et vive, tourne sec, drifts courts et nerveux, perd de la vitesse en glisse
            speed = 0.95f, acceleration = 1.05f, steering = 1.15f, steerResponse = 1.25f, rotationInertia = 0.75f, countersteer = 1f,
            grip = 1f, driftGrip = 1.05f, driftAngle = 0.75f, driftThreshold = 1f, driftEngage = 1.2f, driftRecover = 1.35f,
            driftSpeedKeep = 0.88f, bodyRoll = 1.3f,
        });
        Add("camion", "Mini Camion", CarRarity.Commune, new Color(0.35f, 0.75f, 0.4f), new CarHandling
        {
            // Camion : lourd, lent à tourner et à pivoter, glisse peu (et pas loin), penche beaucoup
            speed = 0.9f, acceleration = 0.65f, steering = 0.8f, steerResponse = 0.6f, rotationInertia = 1.6f, countersteer = 1.2f,
            grip = 1f, driftGrip = 1.3f, driftAngle = 0.55f, driftThreshold = 1.2f, driftEngage = 0.6f, driftRecover = 1.4f,
            driftSpeedKeep = 0.85f, bodyRoll = 1.8f,
        });
        Add("vroom", "Vroom", CarRarity.Rare, new Color(0.2f, 0.5f, 1f), new CarHandling
        {
            // Muscle car : beaucoup de moteur, l'arrière décroche facilement et se rattrape lentement, moins précise
            speed = 1f, acceleration = 1.2f, steering = 0.95f, steerResponse = 0.9f, rotationInertia = 1.1f, countersteer = 0.85f,
            grip = 0.97f, driftGrip = 0.92f, driftAngle = 0.95f, driftThreshold = 0.9f, driftEngage = 1.15f, driftRecover = 0.8f,
            driftSpeedKeep = 0.92f, bodyRoll = 1.2f,
        });
        Add("raijin", "Raijin R", CarRarity.Epique, new Color(0.6f, 0.25f, 0.95f), new CarHandling
        {
            // Sportive "grip" : précise et réactive, plus d'adhérence, drifts plus serrés qui se referment vite
            speed = 1.03f, acceleration = 1.15f, steering = 1.05f, steerResponse = 1.15f, rotationInertia = 0.9f, countersteer = 0.9f,
            grip = 1.05f, driftGrip = 1.12f, driftAngle = 0.85f, driftThreshold = 1.08f, driftEngage = 0.9f, driftRecover = 1.25f,
            driftSpeedKeep = 1f, bodyRoll = 0.8f,
        });
        // Voiture de drift : la référence, exactement les réglages du DriftCar de la scène (le meilleur feeling)
        Add("ryujin", "Ryujin GT-X", CarRarity.Legendaire, new Color(1f, 0.75f, 0.1f), new CarHandling());

        cat.boosters.Add(new Booster { id = "bronze", displayName = "Booster Bronze", price = 100, cards = 3, odds = new[] { 70f, 24f, 5f, 1f }, tagline = "Le meilleur rapport pour les Rares", color = new Color(0.8f, 0.5f, 0.25f) });
        cat.boosters.Add(new Booster { id = "or", displayName = "Booster Or", price = 750, cards = 5, odds = new[] { 15f, 45f, 28f, 12f }, tagline = "Plus d'une chance sur quatre d'avoir une Épique", color = new Color(1f, 0.8f, 0.2f) });
        cat.boosters.Add(new Booster { id = "diamant", displayName = "Booster Diamant", price = 1500, cards = 5, odds = new[] { 0f, 15f, 45f, 40f }, tagline = "40 % de Légendaire, zéro Commune", color = new Color(0.45f, 0.9f, 1f) });
        return cat;
    }
}
