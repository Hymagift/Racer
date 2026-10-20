using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fondu au noir entre les scènes et entre les parties du menu.
/// Créé automatiquement au premier appel de ScreenFader.Instance et gardé entre les scènes :
/// pas besoin de le mettre dans une scène.
///
/// Après un changement de scène (LoadScene normal), l'écran revient tout seul du noir.
/// </summary>
public class ScreenFader : MonoBehaviour
{
    static ScreenFader instance;

    /// <summary>Le fader (créé s'il n'existe pas encore).</summary>
    public static ScreenFader Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("ScreenFader");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<ScreenFader>();
            }
            return instance;
        }
    }

    [Tooltip("Durée d'un fondu par défaut (secondes, temps réel)")]
    public float defaultDuration = 0.6f;

    Image image;
    int fadeId; // chaque nouveau fondu annule le précédent

    /// <summary>Opacité actuelle du noir (0 = transparent, 1 = noir).</summary>
    public float Alpha => image.color.a;

    void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // au-dessus de toute l'UI
        gameObject.AddComponent<GraphicRaycaster>();

        var rt = new GameObject("Black", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        image = rt.gameObject.AddComponent<Image>();
        Apply(0f);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single && Alpha > 0f) Fade(0f);
    }

    /// <summary>Met le noir à une opacité donnée tout de suite (annule le fondu en cours).</summary>
    public void SetAlpha(float alpha)
    {
        fadeId++;
        Apply(alpha);
    }

    /// <summary>Fondu vers l'opacité voulue (1 = noir). À utiliser avec "yield return" pour attendre la fin.</summary>
    public Coroutine Fade(float target, float duration = -1f)
    {
        return StartCoroutine(FadeRoutine(target, duration < 0f ? defaultDuration : duration, ++fadeId));
    }

    IEnumerator FadeRoutine(float target, float duration, int id)
    {
        float start = Alpha;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            if (id != fadeId) yield break;
            Apply(Mathf.Lerp(start, target, t / duration));
            yield return null;
        }
        if (id == fadeId) Apply(target);
    }

    void Apply(float alpha)
    {
        image.color = new Color(0f, 0f, 0f, alpha);
        image.raycastTarget = alpha > 0.01f; // bloque les clics pendant le fondu
    }
}
