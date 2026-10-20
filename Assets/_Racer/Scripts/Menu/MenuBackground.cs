using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Partie de démo qui tourne derrière le menu principal.
/// À mettre sur l'objet "MainMenu" de la scène du menu (la scène du menu n'a ni caméra ni lumière :
/// ce sont celles de la scène de jeu qui servent).
///
/// La scène de jeu est chargée par-dessus le menu (en additif), la voiture reçoit le DriftAutoPilot
/// et l'UI de la partie est cachée. Quand la voiture meurt (ou après maxRunTime), fondu au noir,
/// la scène de jeu est rechargée, puis retour de l'image.
/// Le noir de ces fondus passe sous le menu : le menu reste affiché et cliquable pendant tout ce temps.
/// Pendant la démo, DriftGameManager ne donne ni pièces ni record.
/// </summary>
public class MenuBackground : MonoBehaviour
{
    /// <summary>Vrai tant que la scène de jeu tourne en fond du menu.</summary>
    public static bool DemoRunning { get; private set; }

    static bool runEnded;

    [Tooltip("Nom exact de la scène de jeu (elle doit être dans Build Profiles > Scene List)")]
    public string gameSceneName = "PL";
    [Tooltip("Durée max d'une partie de démo avant de relancer (secondes, 0 = jusqu'à la mort)")]
    public float maxRunTime = 60f;
    [Tooltip("Durée du fondu au noir entre deux parties")]
    public float fadeDuration = 0.8f;
    [Tooltip("Temps passé dans le noir entre deux parties")]
    public float blackTime = 0.3f;

    Scene demoScene;

    /// <summary>Appelé par DriftGameManager quand la voiture de la démo meurt.</summary>
    public static void NotifyRunEnded()
    {
        if (DemoRunning) runEnded = true;
    }

    IEnumerator Start()
    {
        var fader = ScreenFader.Instance;
        fader.SetBelowUI(true); // le menu reste visible et cliquable pendant les fondus de la démo
        fader.SetAlpha(1f);

        while (true)
        {
            yield return LoadDemo();
            if (!demoScene.IsValid()) yield break;

            yield return fader.Fade(0f, fadeDuration);

            float t = 0f;
            while (!runEnded && (maxRunTime <= 0f || t < maxRunTime))
            {
                t += Time.deltaTime;
                yield return null;
            }

            yield return fader.Fade(1f, fadeDuration);
            yield return SceneManager.UnloadSceneAsync(demoScene);
            yield return new WaitForSecondsRealtime(blackTime);
        }
    }

    /// <summary>Arrête la boucle de démo (appelé quand on lance une vraie partie).</summary>
    public void StopDemo()
    {
        StopAllCoroutines();
        ScreenFader.Instance.SetBelowUI(false); // le fondu vers la vraie partie recouvre tout, menu compris
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        DemoRunning = false;
        if (ScreenFader.HasInstance) ScreenFader.Instance.SetBelowUI(false);
    }

    IEnumerator LoadDemo()
    {
        DemoRunning = true;
        runEnded = false;
        demoScene = default;

        SceneManager.sceneLoaded += OnSceneLoaded;
        var op = SceneManager.LoadSceneAsync(gameSceneName, LoadSceneMode.Additive);
        if (op == null)
        {
            Debug.LogError($"[MenuBackground] Impossible de charger la scène '{gameSceneName}' : ajoute-la dans Build Profiles > Scene List.", this);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            yield break;
        }
        yield return op;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // Appelé juste après les Awake de la scène de jeu, avant leurs Start
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Additive || scene.name != gameSceneName) return;

        demoScene = scene;
        SceneManager.SetActiveScene(scene); // lumière / ciel de la scène de jeu, objets créés dedans

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var car in root.GetComponentsInChildren<DriftCar>(true))
                if (car.GetComponent<DriftAutoPilot>() == null) car.gameObject.AddComponent<DriftAutoPilot>();

            foreach (var score in root.GetComponentsInChildren<ScoreManager>(true))
                score.showHUD = false;

            // L'UI de la partie (HUD, écran de fin) et son EventSystem ne servent pas dans le menu
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                canvas.gameObject.SetActive(false);
            foreach (var es in root.GetComponentsInChildren<EventSystem>(true))
                es.gameObject.SetActive(false);
        }
    }
}
