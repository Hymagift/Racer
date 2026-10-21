using UnityEngine;

/// <summary>
/// Réglages de performance appliqués au lancement, avant la première scène (rien à mettre dans les scènes).
/// - 60 images par seconde : sur Android, Unity limite à 30 par défaut (le jeu paraît saccadé même si le téléphone suit).
/// - L'écran ne se met pas en veille pendant le jeu.
/// </summary>
public static class PerformanceSettings
{
    /// <summary>Images par seconde visées sur téléphone. 60 = fluide ; 30 = économise la batterie et la chauffe.</summary>
    public const int TargetFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        QualitySettings.vSyncCount = 0;            // sur mobile, c'est targetFrameRate qui compte
        Application.targetFrameRate = TargetFrameRate;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }
}
