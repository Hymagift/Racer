using System;
using UnityEngine;

/// <summary>
/// Données du joueur sauvegardées sur le téléphone (PlayerPrefs) : pièces, objets achetés,
/// objet équipé, volume, blueprints et voitures du garage. Classe statique : pas besoin de la mettre dans la scène.
/// </summary>
public static class PlayerData
{
    const string CoinsKey = "DriftCoins";
    const string EquippedKey = "DriftEquipped";
    const string VolumeKey = "DriftVolume";
    const string OwnedPrefix = "DriftOwned_";

    /// <summary>Déclenché dès que les pièces, achats ou l'objet équipé changent.</summary>
    public static event Action OnChanged;

    /// <summary>Meilleur score (enregistré par le ScoreManager).</summary>
    public static int BestScore => PlayerPrefs.GetInt(ScoreManager.BestScoreKey, 0);
    public static int Coins => PlayerPrefs.GetInt(CoinsKey, 0);

    /// <summary>Ajoute des pièces. save = false : à toi d'appeler SaveAndNotify() après plusieurs modifications.</summary>
    public static void AddCoins(int amount, bool save = true)
    {
        PlayerPrefs.SetInt(CoinsKey, Mathf.Max(0, Coins + amount));
        if (save) SaveAndNotify();
    }

    public static bool TrySpend(int amount, bool save = true)
    {
        if (Coins < amount) return false;
        AddCoins(-amount, save);
        return true;
    }

    /// <summary>Écrit les PlayerPrefs sur le téléphone et prévient l'UI.</summary>
    public static void SaveAndNotify()
    {
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }

    // ---------- Garage : blueprints et voitures (voir CarCatalog) ----------

    const string BlueprintPrefix = "DriftBlueprints_";
    const string CarUnlockedPrefix = "DriftCarUnlocked_";
    const string EquippedCarKey = "DriftEquippedCar";

    public static int GetBlueprints(string carId) => PlayerPrefs.GetInt(BlueprintPrefix + carId, 0);

    public static void SetBlueprints(string carId, int count, bool save = true)
    {
        PlayerPrefs.SetInt(BlueprintPrefix + carId, Mathf.Max(0, count));
        if (save) SaveAndNotify();
    }

    public static bool IsCarUnlocked(string carId) => PlayerPrefs.GetInt(CarUnlockedPrefix + carId, 0) == 1;

    public static void SetCarUnlocked(string carId, bool save = true)
    {
        PlayerPrefs.SetInt(CarUnlockedPrefix + carId, 1);
        if (save) SaveAndNotify();
    }

    /// <summary>Efface blueprints et déblocage d'une voiture (pour les tests).</summary>
    public static void ResetCar(string carId, bool save = true)
    {
        PlayerPrefs.DeleteKey(BlueprintPrefix + carId);
        PlayerPrefs.DeleteKey(CarUnlockedPrefix + carId);
        if (save) SaveAndNotify();
    }

    /// <summary>Voiture choisie dans le garage ("" = la voiture de départ).</summary>
    public static string EquippedCarId
    {
        get => PlayerPrefs.GetString(EquippedCarKey, "");
        set
        {
            PlayerPrefs.SetString(EquippedCarKey, value);
            SaveAndNotify();
        }
    }

    public static bool IsOwned(string id) => PlayerPrefs.GetInt(OwnedPrefix + id, 0) == 1;

    public static void SetOwned(string id)
    {
        PlayerPrefs.SetInt(OwnedPrefix + id, 1);
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }

    public static string EquippedId
    {
        get => PlayerPrefs.GetString(EquippedKey, "");
        set
        {
            PlayerPrefs.SetString(EquippedKey, value);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }
    }

    public static float Volume
    {
        get => PlayerPrefs.GetFloat(VolumeKey, 1f);
        set
        {
            PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>Efface toute la progression (pièces, achats, record, volume).</summary>
    public static void ResetAll()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }
}