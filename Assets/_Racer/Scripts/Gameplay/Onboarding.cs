using UnityEngine;
using UnityEngine.UI;
using static MenuStyle;

/// <summary>
/// Petit tutoriel du tout début : "Glisse ton doigt de gauche à droite pour tourner", avec un doigt qui fait le geste.
///
/// Visible pendant le compte à rebours et le début de la partie, il disparaît dès que le joueur a vraiment tourné
/// (un peu plus d'une seconde de braquage cumulé) ou après quelques secondes de course.
/// Une fois réussi, il ne revient plus (sauvegardé sur le téléphone). Rien pendant la démo du menu ni en pilote auto.
/// Ajouté automatiquement par DriftGameManager. L'UI est créée par code.
/// </summary>
public class Onboarding : MonoBehaviour
{
    const string DoneKey = "Onboarding.Done";

    [Tooltip("Texte affiché")]
    public string message = "Glisse ton doigt de gauche à droite pour tourner";
    [Tooltip("Hauteur du texte à l'écran (0 = bas, 1 = haut)")]
    [Range(0f, 1f)] public float verticalPosition = 0.32f;
    [Tooltip("Secondes de braquage (cumulées) pour considérer que le joueur a compris")]
    public float steerTimeToLearn = 1.2f;
    [Tooltip("Durée max d'affichage après le départ (secondes)")]
    public float maxTimeAfterStart = 8f;
    [Tooltip("Toujours afficher le tutoriel, même s'il a déjà été réussi (pour tester)")]
    public bool alwaysShow;

    DriftCar car;
    Canvas canvas;
    CanvasGroup group;
    RectTransform finger, root;
    float steerTime, runTime, alpha, appear;
    bool leaving;

    /// <summary>Remet le tutoriel à zéro (il s'affichera à la prochaine partie).</summary>
    public static void ResetTutorial() => PlayerPrefs.DeleteKey(DoneKey);

    void Start()
    {
        if (MenuBackground.DemoRunning || (!alwaysShow && PlayerPrefs.GetInt(DoneKey, 0) == 1))
        {
            enabled = false;
            return;
        }
        car = FindFirstObjectByType<DriftCar>();
        if (car == null) { enabled = false; return; }
        Build();
    }

    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        bool autopilot = car.useExternalInput;

        // Le joueur tourne lui-même (pas le pilote auto) : il a compris
        if (Time.timeScale > 0f && !autopilot)
        {
            runTime += Time.deltaTime;
            if (Mathf.Abs(car.SteerAngle) > 4f) steerTime += Time.deltaTime;
        }
        if (!leaving && steerTime >= steerTimeToLearn)
        {
            leaving = true;
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();
            MenuFx.Play(MenuFx.Sfx.Pop, 1.3f, 0.5f);
        }
        if (runTime > maxTimeAfterStart) leaving = true; // pas réussi : il reviendra à la prochaine partie

        // Apparition / disparition en fondu (caché aussi en pilote auto)
        float target = leaving || autopilot ? 0f : 1f;
        alpha = Mathf.MoveTowards(alpha, target, dt * (target > alpha ? 3f : 2.5f));
        group.alpha = alpha;
        if (leaving && alpha <= 0f) { Destroy(canvas.gameObject); enabled = false; return; }

        // Petit rebond à l'arrivée, puis le doigt glisse de gauche à droite
        appear = Mathf.MoveTowards(appear, 1f, dt * 2.5f);
        float pop = 1f + Mathf.Sin(appear * Mathf.PI) * 0.15f;
        root.localScale = Vector3.one * pop;
        float t = Time.unscaledTime;
        finger.anchoredPosition = new Vector2(Mathf.Sin(t * 2.4f) * 230f, -150f);
    }

    void Build()
    {
        var go = new GameObject("Onboarding", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15; // au-dessus du HUD, sous le compte à rebours
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false; // ne gêne jamais le doigt
        group.interactable = false;

        root = Node("Tutoriel", go.transform);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, verticalPosition);
        root.sizeDelta = new Vector2(940f, 200f);

        // Bandeau sombre derrière le texte
        var panel = Node("Fond", root);
        Stretch(panel);
        Rounded(panel, WithAlpha(Navy, 0.75f), 60f);
        var text = FitLabel(panel, message, 54, 28, TextAnchor.MiddleCenter, Color.white, 3f);
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(40f, 16f);
        text.rectTransform.offsetMax = new Vector2(-40f, -16f);

        // Rail du geste avec des flèches aux deux bouts, et le doigt qui glisse dessus
        var track = Node("Rail", root);
        track.anchorMin = track.anchorMax = new Vector2(0.5f, 0.5f);
        track.sizeDelta = new Vector2(560f, 14f);
        track.anchoredPosition = new Vector2(0f, -150f);
        Rounded(track, WithAlpha(Color.white, 0.35f), 7f);
        foreach (bool right in new[] { false, true })
        {
            var arrow = Label(root, right ? "»" : "«", 80, TextAnchor.MiddleCenter, Color.white, 3f);
            arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            arrow.rectTransform.sizeDelta = new Vector2(100f, 100f);
            arrow.rectTransform.anchoredPosition = new Vector2(right ? 340f : -340f, -152f);
        }

        finger = Node("Doigt", root);
        finger.anchorMin = finger.anchorMax = new Vector2(0.5f, 0.5f);
        finger.sizeDelta = new Vector2(96f, 96f);
        var ring = Fill(finger, WithAlpha(Color.white, 0.35f));
        ring.sprite = Round;
        var dot = Node("Point", finger);
        Area(dot, 0.2f, 0.2f, 0.8f, 0.8f);
        Fill(dot, Color.white).sprite = Round;
    }
}
