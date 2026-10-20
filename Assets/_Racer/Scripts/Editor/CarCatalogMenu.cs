using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Menu Drift > Créer le catalogue de voitures : crée Assets/_Racer/Resources/CarCatalog.asset avec le contenu par défaut.</summary>
public static class CarCatalogMenu
{
    const string Path = "Assets/_Racer/Resources/CarCatalog.asset";

    [MenuItem("Drift/Créer le catalogue de voitures")]
    static void Create()
    {
        var existing = AssetDatabase.LoadAssetAtPath<CarCatalog>(Path);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("[CarCatalog] Le catalogue existe déjà : " + Path);
            return;
        }

        Directory.CreateDirectory("Assets/_Racer/Resources");
        var catalog = CarCatalog.CreateDefault();
        catalog.name = "CarCatalog";
        AssetDatabase.CreateAsset(catalog, Path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);
        Debug.Log("[CarCatalog] Catalogue créé : " + Path + " (voitures, boosters, prix et probabilités modifiables dans l'Inspector)");
    }

    [MenuItem("Drift/Donner 2000 pièces (test)")]
    static void GiveCoins() => PlayerData.AddCoins(2000);

    [MenuItem("Drift/Réinitialiser le garage (test)")]
    static void ResetGarage()
    {
        foreach (var car in CarCatalog.Load().cars)
            PlayerData.ResetCar(car.id, save: false);
        PlayerData.EquippedCarId = ""; // sauvegarde aussi
        Debug.Log("[CarCatalog] Blueprints et voitures remis à zéro");
    }
}
