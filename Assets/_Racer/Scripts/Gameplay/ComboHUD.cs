using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Multiplicateur au centre de l'écran, dans un cercle qui se remplit, avec les points du combo en cours en dessous.
///
/// - Le cercle montre la progression vers le palier suivant : à x2.4 il est rempli à 40 % ; en passant x3
///   il se remplit, "pulse", puis repart de zéro. Sa couleur change à chaque palier.
/// - En dessous : les points du drift en cours (déjà multipliés). À la fin du drift ils sont encaissés
///   ("+1 234" en vert), et si on tape un mur pendant le drift : "Combo perdu" en rouge.
///
/// Ajouté automatiquement par le ScoreManager (option "Show Combo Ring"). L'UI est créée par code.
/// Caché pendant la démo du menu et après la mort.
/// </summary>
public class ComboHUD : MonoBehaviour
{
    [Header("Position")]
    [Tooltip("Hauteur du cercle à l'écran (0 = bas, 0.5 = milieu, 1 = haut). Au milieu exact, il cache la voiture.")]
    [Range(0f, 1f)] public float verticalPosition = 0.68f;
    [Tooltip("Diamètre du cercle (pour un écran de référence 1080 x 1920)")]
    public float size = 270f;

    [Header("Cercle")]
    [Tooltip("Épaisseur de l'anneau (fraction du diamètre)")]
    [Range(0.03f, 0.3f)] public float thickness = 0.11f;
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.45f);
    [Tooltip("Couleur de chaque palier : x1, x2, x3, x4, x5 et plus")]
    public Color[] levelColors =
    {
        new Color(1f, 1f, 1f),
        new Color(1f, 0.9f, 0.3f),
        new Color(1f, 0.6f, 0.15f),
        new Color(1f, 0.28f, 0.2f),
        new Color(0.85f, 0.35f, 1f),
    };
    [Tooltip("Vitesse à laquelle le cercle suit le multiplicateur")]
    public float fillSpeed = 10f;

    [Header("Combo")]
    public Color bankedColor = new Color(0.45f, 1f, 0.5f);
    public Color lostColor = new Color(1f, 0.3f, 0.25f);
    [Tooltip("Durée d'affichage des points encaissés / du combo perdu (secondes)")]
    public float messageTime = 1.2f;

    ScoreManager score;
    DriftCar car;

    Canvas canvas;
    RectTransform root;
    Image fill;
    Text multiplierText, comboText, scoreText;
    Texture2D ringTexture;
    Sprite ringSprite;

    int lastLevel = 1;
    float shownFill, pulse, fullTimer, hitFlash;
    float bankedPoints, bankedTimer, lostTimer, lastPending;
    // Dernières valeurs affichées : on ne réécrit un texte que s'il change (sinon l'UI est recalculée à chaque image)
    int shownMult = -1, shownPending = -1, shownScore = -1, shownDist = -1, shownState = -1;
    float shownScale = -1f;

    void Start()
    {
        score = GetComponent<ScoreManager>();
        if (score == null) score = ScoreManager.Instance;
        if (score == null) { enabled = false; return; }

        car = score.car;
        score.OnDriftBanked += HandleBanked;
        if (car != null) car.OnWallHit += HandleWallHit;

        Build();
    }

    void OnDestroy()
    {
        if (score != null) score.OnDriftBanked -= HandleBanked;
        if (car != null) car.OnWallHit -= HandleWallHit;
        if (canvas != null) Destroy(canvas.gameObject);
        if (ringSprite != null) Destroy(ringSprite);
        if (ringTexture != null) Destroy(ringTexture);
    }

    void HandleBanked(float points)
    {
        bankedPoints = points;
        bankedTimer = messageTime;
        shownState = -1; // nouveau montant à afficher
        lostTimer = 0f;
    }

    void HandleWallHit()
    {
        hitFlash = 1f;
        if (lastPending > 0f)
        {
            lostTimer = messageTime;
            bankedTimer = 0f;
        }
    }

    // ---------- Mise à jour ----------

    void Update()
    {
        bool visible = score != null && score.showHUD && score.IsRunning && !MenuBackground.DemoRunning;
        if (canvas.enabled != visible) canvas.enabled = visible;
        if (!visible) return;

        float dt = Time.unscaledDeltaTime;
        float m = score.TotalMultiplier;
        int level = Mathf.Max(1, Mathf.FloorToInt(m + 0.0001f));
        float progress = Mathf.Clamp01(m - level);

        // Changement de palier : le cercle se montre plein un instant, pulse, puis repart du bon niveau
        if (level > lastLevel)
        {
            pulse = 1f;
            fullTimer = 0.15f;
            shownFill = progress;
        }
        else if (level < lastLevel)
        {
            shownFill = progress;
        }
        else
        {
            shownFill = Mathf.Lerp(shownFill, progress, 1f - Mathf.Exp(-fillSpeed * dt));
        }
        lastLevel = level;

        fullTimer -= dt;
        fill.fillAmount = fullTimer > 0f ? 1f : shownFill;

        Color c = LevelColor(level);
        hitFlash = Mathf.MoveTowards(hitFlash, 0f, dt / 0.4f);
        c = Color.Lerp(c, lostColor, hitFlash);
        fill.color = c;
        multiplierText.color = c;
        int multKey = m < 10f ? Mathf.FloorToInt(m * 10f + 0.0001f) : 1000 + Mathf.FloorToInt(m);
        if (multKey != shownMult)
        {
            shownMult = multKey;
            multiplierText.text = "x" + (m < 10f ? (multKey / 10f).ToString("0.0", CultureInfo.InvariantCulture) : Mathf.FloorToInt(m).ToString());
        }

        pulse = Mathf.MoveTowards(pulse, 0f, dt / 0.35f);
        float scale = 1f + 0.25f * pulse * pulse;
        if (scale != shownScale) { shownScale = scale; root.localScale = Vector3.one * scale; }

        UpdateCombo(dt);
        UpdateScore();
    }

    // Score et distance en haut à droite (remplace l'ancien affichage OnGUI, trop lent sur téléphone)
    void UpdateScore()
    {
        if (scoreText == null) return;
        int s = score.IntScore, d = Mathf.FloorToInt(score.Distance);
        if (s == shownScore && d == shownDist) return;
        shownScore = s;
        shownDist = d;
        scoreText.text = FormatPoints(s) + "\n<size=" + Mathf.RoundToInt(scoreText.fontSize * 0.6f) + ">" + d + " m</size>";
    }

    void UpdateCombo(float dt)
    {
        float pending = score.PendingDriftPoints;
        bankedTimer -= dt;
        lostTimer -= dt;

        if (pending > 0f)
        {
            int p = Mathf.FloorToInt(pending);
            if (shownState != 0 || p != shownPending) comboText.text = "+" + FormatPoints(p);
            shownState = 0;
            shownPending = p;
            comboText.color = Color.white;
            comboText.fontSize = Mathf.RoundToInt(size * 0.19f);
        }
        else if (lostTimer > 0f)
        {
            if (shownState != 1) comboText.text = "Combo perdu";
            shownState = 1;
            comboText.color = Fade(lostColor, lostTimer);
        }
        else if (bankedTimer > 0f)
        {
            if (shownState != 2) comboText.text = "+" + FormatPoints(bankedPoints);
            shownState = 2;
            comboText.color = Fade(bankedColor, bankedTimer);
            comboText.fontSize = Mathf.RoundToInt(size * 0.19f * (1f + 0.15f * Mathf.Clamp01((bankedTimer - messageTime + 0.3f) / 0.3f)));
        }
        else if (shownState != 3)
        {
            shownState = 3;
            comboText.text = "";
        }

        lastPending = pending;
    }

    Color Fade(Color c, float timer)
    {
        c.a = Mathf.Clamp01(timer / (messageTime * 0.4f));
        return c;
    }

    Color LevelColor(int level)
    {
        if (levelColors == null || levelColors.Length == 0) return Color.white;
        return levelColors[Mathf.Clamp(level - 1, 0, levelColors.Length - 1)];
    }

    // 12345 -> "12 345"
    static string FormatPoints(float p) =>
        Mathf.FloorToInt(p).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');

    // ---------- Création de l'UI ----------

    void Build()
    {
        var go = new GameObject("ComboHUD", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        root = NewRect("Combo", go.transform);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, verticalPosition);
        root.sizeDelta = new Vector2(size, size);

        BuildRingSprite();

        var bg = NewRect("Fond", root);
        Stretch(bg);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.sprite = ringSprite;
        bgImg.color = backgroundColor;
        bgImg.raycastTarget = false;

        var fillRt = NewRect("Remplissage", root);
        Stretch(fillRt);
        fill = fillRt.gameObject.AddComponent<Image>();
        fill.sprite = ringSprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = true;
        fill.fillAmount = 0f;
        fill.raycastTarget = false;

        multiplierText = NewText(root, Mathf.RoundToInt(size * 0.3f));
        Stretch(multiplierText.rectTransform);

        comboText = NewText(root, Mathf.RoundToInt(size * 0.19f));
        var ct = comboText.rectTransform;
        ct.anchorMin = new Vector2(0.5f, 0f);
        ct.anchorMax = new Vector2(0.5f, 0f);
        ct.pivot = new Vector2(0.5f, 1f);
        ct.sizeDelta = new Vector2(size * 3f, size * 0.3f);
        ct.anchoredPosition = new Vector2(0f, -size * 0.06f);

        // Score + distance en haut à droite, sauf si la scène a déjà ses propres textes TextMeshPro
        if (score.hudScoreText == null)
        {
            scoreText = NewText(go.transform, 64);
            scoreText.alignment = TextAnchor.UpperRight;
            scoreText.supportRichText = true;
            var st = scoreText.rectTransform;
            st.anchorMin = st.anchorMax = st.pivot = new Vector2(1f, 1f);
            st.sizeDelta = new Vector2(600f, 200f);
            st.anchoredPosition = new Vector2(-40f, -50f);
        }
    }

    // Anneau blanc anti-aliasé généré par code (pas besoin d'image dans le projet)
    void BuildRingSprite()
    {
        const int n = 256;
        ringTexture = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "ComboRing",
        };
        float outer = n * 0.5f - 1f;
        float inner = outer - n * thickness;
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        ringTexture.SetPixels32(px);
        ringTexture.Apply(false, true);
        ringSprite = Sprite.Create(ringTexture, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Text NewText(Transform parent, int fontSize)
    {
        var rt = NewRect("Texte", parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var outline = rt.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
        outline.effectDistance = new Vector2(3f, -3f);
        return t;
    }
}
