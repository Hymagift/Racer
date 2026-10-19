using System;
using UnityEngine;

/// <summary>
/// Données du joueur sauvegardées sur le téléphone (PlayerPrefs) : pièces, objets achetés,
/// objet équipé, volume. Classe statique : pas besoin de la mettre dans la scène.
/// </summary>
public static class PlayerData
{
    const string CoinsKey = "DriftCoins";
    const string EquippedKey = "DriftEquipped";
    const string VolumeKey = "DriftVolume";
    const string OwnedPrefix = "DriftOwned_";
    const string BestScoreKey = "DriftBestScore"; // même clé que DriftGameManager

    /// <summary>Déclenché dès que les pièces, achats ou l'objet équipé changent.</summary>
    public static event Action OnChanged;

    public static int BestScore => PlayerPrefs.GetInt(BestScoreKey, 0);
    public static int Coins => PlayerPrefs.GetInt(CoinsKey, 0);

    public static void AddCoins(int amount)
    {
        PlayerPrefs.SetInt(CoinsKey, Mathf.Max(0, Coins + amount));
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }

    public static bool TrySpend(int amount)
    {
        if (Coins < amount) return false;
        AddCoins(-amount);
        return true;
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