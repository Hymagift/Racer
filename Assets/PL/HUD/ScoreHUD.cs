using UnityEngine;
using UnityEngine.UI;
using static MenuStyle;

/// <summary>
/// HUD de la partie dans la DA du menu (rouge / blanc / bleu, pastilles penchées, textes gras italiques).
/// Tout est créé par code, comme le menu : score dans une pastille blanche en haut à gauche, multiplicateur
/// en anneau en haut à droite (MultiplierRing), points du drift en cours au centre.
///
/// Ajouté automatiquement par ScoreManager au lancement (option "Styled HUD") quand aucun texte TextMeshPro
/// n'est branché. Les couleurs viennent de DriftTheme.
/// </summary>
public class ScoreHUD : MonoBehaviour
{
    [Tooltip("Le ScoreManager affiché (trouvé tout seul si vide)")]
    public ScoreManager score;
    [Tooltip("Taille de l'anneau du multiplicateur (unités du canvas, base 1080x1920)")]
    public float ringSize = 300f;

    RectTransform safeArea, scorePill, driftRect, angleTag;
    Text scoreText, scoreTitle, distanceText, driftText, angleText;
    MultiplierRing ring;
    CanvasGroup driftGroup, angleGroup;
    Rect lastSafeArea;
    int lastScore;
    float scorePunch, driftPop, driftFade, angleShow;
    bool wasDrifting, newRecordShown;

    void Start()
    {
        if (score == null) score = GetComponent<ScoreManager>();
        if (score == null) score = FindFirstObjectByType<ScoreManager>();
        Build();
        ring.Snap(score != null ? score.TotalMultiplier : 1f);
    }

    // ---------- Construction ----------

    void Build()
    {
        var canvasRt = Node("HUD Score", transform);
        var canvas = canvasRt.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5; // sous le menu de fin et le fondu
        var scaler = canvasRt.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        // Pas de GraphicRaycaster : le HUD ne doit jamais bloquer le doigt qui pilote

        // Bandeau rouge en haut qui s'efface vers le bas, comme le décor du menu (la route reste visible)
        var top = Node("Rouge haut", canvasRt);
        top.anchorMin = new Vector2(0f, 1f);
        top.anchorMax = Vector2.one;
        top.sizeDelta = new Vector2(0f, 460f);
        top.anchoredPosition = new Vector2(0f, -230f);
        top.localScale = new Vector3(1f, -1f, 1f); // retourné : opaque en haut
        Fill(top, DriftTheme.WithAlpha(DriftTheme.Red, 0.7f)).sprite = GradientSprite;

        safeArea = Node("SafeArea", canvasRt);
        Stretch(safeArea);
        ApplySafeArea();

        BuildScore(safeArea);
        BuildRing(safeArea);
        BuildDrift(safeArea);
    }

    void BuildScore(RectTransform parent)
    {
        // Pastille blanche penchée avec ombre bleu nuit (comme RECORD dans le menu)
        scorePill = Pill(parent, "Score", DriftTheme.White, new Vector2(500f, 150f));
        scorePill.anchorMin = scorePill.anchorMax = scorePill.pivot = new Vector2(0f, 1f);
        scorePill.anchoredPosition = new Vector2(50f, -50f);

        scoreTitle = MultiplierRing.HudText(scorePill, "SCORE", 32, TextAnchor.MiddleLeft, DriftTheme.Red, 0f);
        Area(scoreTitle.rectTransform, 0.09f, 0.62f, 0.95f, 0.95f);
        scoreText = MultiplierRing.HudText(scorePill, "0", 84, TextAnchor.MiddleLeft, DriftTheme.Ink, 0f);
        Area(scoreText.rectTransform, 0.08f, 0.02f, 0.97f, 0.68f);

        // Petite pastille rouge foncé dessous : distance parcourue
        var dist = Pill(parent, "Distance", DriftTheme.DarkRed, new Vector2(240f, 70f));
        dist.anchorMin = dist.anchorMax = dist.pivot = new Vector2(0f, 1f);
        dist.anchoredPosition = new Vector2(70f, -220f);
        distanceText = MultiplierRing.HudText(dist, "0 m", 38, TextAnchor.MiddleCenter, DriftTheme.White, 2f);
        Stretch(distanceText.rectTransform);
    }

    void BuildRing(RectTransform parent)
    {
        ring = MultiplierRing.Build(parent, ringSize);
        var rt = (RectTransform)ring.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f); // grossit autour de son centre
        rt.anchoredPosition = new Vector2(-50f - ringSize * 0.5f, -40f - ringSize * 0.5f);

        // Étiquette bleue "ANGLE x1.8" sous l'anneau, visible pendant le multiplicateur d'angle
        angleTag = Pill(parent, "Angle", DriftTheme.Blue, new Vector2(270f, 70f));
        angleTag.anchorMin = angleTag.anchorMax = new Vector2(1f, 1f);
        angleTag.anchoredPosition = new Vector2(-50f - ringSize * 0.5f, -40f - ringSize - 55f);
        angleGroup = angleTag.gameObject.AddComponent<CanvasGroup>();
        angleGroup.alpha = 0f;
        angleText = MultiplierRing.HudText(angleTag, "ANGLE x1.0", 36, TextAnchor.MiddleCenter, DriftTheme.White, 2f);
        Stretch(angleText.rectTransform);
    }

    void BuildDrift(RectTransform parent)
    {
        // Points du drift en cours : gros texte bleu bordé de blanc, comme JOUER dans le menu
        driftRect = Node("Drift", parent);
        driftRect.anchorMin = driftRect.anchorMax = new Vector2(0.5f, 0.68f);
        driftRect.sizeDelta = new Vector2(900f, 200f);
        driftGroup = driftRect.gameObject.AddComponent<CanvasGroup>();
        driftGroup.alpha = 0f;

        driftText = MultiplierRing.HudText(driftRect, "+0", 130, TextAnchor.MiddleCenter, DriftTheme.Blue, 0f);
        Stretch(driftText.rectTransform);
        var white = driftText.gameObject.AddComponent<Outline>();
        white.effectColor = DriftTheme.White;
        white.effectDistance = new Vector2(7f, -7f);
        var depth = driftText.gameObject.AddComponent<Shadow>();
        depth.effectColor = DriftTheme.Ink;
        depth.effectDistance = new Vector2(3f, -12f);
        driftText.gameObject.AddComponent<DriftSkew>().angle = 8f;
    }

    /// <summary>Pastille penchée : ombre bleu nuit décalée vers le bas + face colorée bordée de blanc si foncée.</summary>
    static RectTransform Pill(RectTransform parent, string name, Color color, Vector2 size)
    {
        var pill = Node(name, parent);
        pill.sizeDelta = size;

        var shadow = Node("Ombre", pill);
        Stretch(shadow);
        shadow.offsetMin = shadow.offsetMax = new Vector2(0f, -8f);
        Rounded(shadow, DriftTheme.Ink, DriftTheme.PillRadius).gameObject.AddComponent<DriftSkew>();

        var face = Node("Face", pill);
        Stretch(face);
        var img = Rounded(face, color, DriftTheme.PillRadius);
        if (color != DriftTheme.White)
        {
            var rim = img.gameObject.AddComponent<Outline>(); // liseré blanc
            rim.effectColor = DriftTheme.White;
            rim.effectDistance = new Vector2(4f, -4f);
        }
        img.gameObject.AddComponent<DriftSkew>();
        return pill;
    }

    void ApplySafeArea()
    {
        Rect area = Screen.safeArea;
        if (area == lastSafeArea || Screen.width <= 0 || Screen.height <= 0) return;
        lastSafeArea = area;
        safeArea.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
        safeArea.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
    }

    // ---------- Mise à jour ----------

    void Update()
    {
        if (score == null || ring == null) return;
        float dt = Time.unscaledDeltaTime;
        ApplySafeArea();

        // Score : le texte "tape" quand il fait un saut (cône, fin de drift)
        int s = score.IntScore;
        if (s != lastScore)
        {
            if (s - lastScore >= 25) scorePunch = 1f;
            lastScore = s;
            scoreText.text = s.ToString();
        }
        scorePunch = Mathf.MoveTowards(scorePunch, 0f, dt * 4f);
        scorePill.localScale = Vector3.one * (1f + 0.12f * scorePunch * scorePunch);

        // Nouveau record en cours de partie
        bool record = score.BestScore > 0 && s > score.BestScore;
        if (score.IsRunning && record != newRecordShown) // figé à la mort (EndRun met le record à jour)
        {
            newRecordShown = record;
            scoreTitle.text = record ? "NOUVEAU RECORD !" : "SCORE";
            scoreTitle.color = record ? DriftTheme.Blue : DriftTheme.Red;
        }

        distanceText.text = Mathf.FloorToInt(score.Distance) + " m";

        ring.SetValue(score.TotalMultiplier);
        UpdateAngle(dt);
        UpdateDrift(dt);
    }

    void UpdateAngle(float dt)
    {
        bool active = score.IsRunning && score.AngleMultiplier > 1.01f;
        if (active) angleText.text = $"ANGLE x{score.AngleMultiplier:F1}";
        angleShow = Mathf.MoveTowards(angleShow, active ? 1f : 0f, dt * 6f);
        angleGroup.alpha = angleShow;
        angleTag.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, angleShow);
    }

    void UpdateDrift(float dt)
    {
        float pending = score.IsRunning ? score.PendingDriftPoints : 0f;
        bool drifting = pending > 0f;
        if (!score.IsRunning) driftFade = 0f; // mort pendant un drift : les points sont perdus, rien ne monte

        if (drifting)
        {
            if (!wasDrifting) driftPop = 1f; // le texte arrive en grossissant
            driftText.text = "+" + Mathf.FloorToInt(pending);
            driftFade = 1f;
            driftRect.anchoredPosition = Vector2.zero;
        }
        else if (driftFade > 0f)
        {
            // Fin du drift : les points montent et s'effacent (ils sont ajoutés au score)
            driftFade = Mathf.MoveTowards(driftFade, 0f, dt * 2.5f);
            driftRect.anchoredPosition = new Vector2(0f, (1f - driftFade) * 160f);
        }
        wasDrifting = drifting;

        driftPop = Mathf.MoveTowards(driftPop, 0f, dt * 4f);
        // Grossit un peu avec le nombre de points, pour donner envie de tenir le drift
        float grow = Mathf.Clamp01(Mathf.Log10(1f + pending) / 4f) * 0.25f;
        driftRect.localScale = Vector3.one * (1f + grow + 0.3f * driftPop * driftPop);
        driftGroup.alpha = driftFade;
    }
}
