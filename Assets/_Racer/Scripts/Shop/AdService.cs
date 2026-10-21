using System;
using UnityEngine;

/// <summary>
/// Publicités "récompensées" (le joueur regarde une pub en échange d'un avantage, ex : continuer après la mort).
///
/// ÉTAT ACTUEL : MODE TEST. Dans l'éditeur et les builds de développement, la pub est simulée par ContinueOffer
/// (écran "Publicité (test)" de quelques secondes). Dans un build de production, aucune pub n'est proposée
/// tant qu'un vrai SDK n'est pas branché.
///
/// Pour de vraies pubs : installe un SDK (Unity Ads / LevelPlay, AdMob...), puis remplis RewardedAvailable
/// et ShowRewarded ci-dessous : done(true) seulement quand le SDK confirme que la pub a été vue jusqu'au bout.
/// </summary>
public static class AdService
{
    /// <summary>Vrai tant que les pubs sont simulées (éditeur, build de développement).</summary>
    public static bool TestMode => PremiumStore.TestMode;

    /// <summary>Une pub récompensée peut être montrée maintenant.</summary>
    public static bool RewardedAvailable => TestMode; // TODO : demander au SDK si une pub est chargée

    /// <summary>Montre une vraie pub (hors mode test). done(true) = vue en entier, le joueur a droit à sa récompense.</summary>
    public static void ShowRewarded(Action<bool> done)
    {
        // TODO : appeler le SDK de pubs ici
        done?.Invoke(false);
    }
}
