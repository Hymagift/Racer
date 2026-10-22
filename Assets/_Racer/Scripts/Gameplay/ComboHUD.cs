using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Multiplicateur dans un cercle qui se remplit, placé sous la voiture (il la suit), avec les points du combo en cours en dessous.
///
/// - Le cercle montre la progression vers le palier suivant : à x2.4 il est rempli à 40 % ; en passant x3
///   il se remplit, "pulse", puis repart de zéro. Sa couleur change à chaque palier.
/// - En dessous : les points du drift en cours (déjà multipliés). À la fin du drift ils sont encaissés
///   ("+1 234" en vert), et si on tape un mur pendant le drift : "Combo perdu" en rouge.
///
/// - Feux d'artifice : à chaque nouveau palier, le cercle rebondit, une gerbe d'éclats et d'étoiles part du bord,
///   avec une onde de choc, des rayons, un "x3 !" qui saute et un petit son (de plus en plus fort à chaque palier ;
///   à partir de x3, des explosions secondaires éclatent autour). Le bout du cercle laisse des étincelles quand il se
///   remplit, et aux hauts paliers le cercle crépite et bat comme un cœur. Tout est en UI (pas de VFX Graph).
///
/// Ajouté automatiquement par le ScoreManager (option "Show Combo Ring"). L'UI est créée par code.
/// Caché pendant la démo du menu et après la mort.
/// </summary>
public class ComboHUD : MonoBehaviour
{
    [Header("Position")]
    [Tooltip("Le cercle suit la voiture à l'écran et se place juste en dessous")]
    public bool followCar = true;
    [Tooltip("Écart entre le centre de la voiture et le haut du cercle (pour un écran de référence 1080 x 1920)")]
    public float carGap = 90f;
    [Tooltip("Vitesse à laquelle le cercle suit la voiture (plus grand = plus collé, plus petit = plus souple)")]
    public float followSpeed = 18f;
    [Tooltip("Hauteur du cercle à l'écran si la voiture n'est pas suivie (0 = bas, 0.5 = milieu, 1 = haut)")]
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

    [Header("Feux d'artifice")]
    [Tooltip("Explosions, étincelles et rebonds quand le multiplicateur monte")]
    public bool fireworks = true;
    [Tooltip("Nombre d'éclats de la gerbe au premier palier (x2)")]
    public int burstCount = 22;
    [Tooltip("Éclats en plus à chaque palier au-dessus")]
    public int burstCountPerLevel = 8;
    public int maxBurstCount = 80;
    [Tooltip("Vitesse des éclats (pour un écran de référence 1080 x 1920, par seconde)")]
    public float burstSpeed = 1000f;
    [Tooltip("À partir de ce palier, des explosions secondaires éclatent autour du cercle (une de plus par palier, 4 max)")]
    public int secondaryBurstsFromLevel = 3;
    [Tooltip("Rebond du cercle au premier palier (0.3 = +30 %)")]
    public float punch = 0.3f;
    [Tooltip("Rebond en plus à chaque palier au-dessus")]
    public float punchPerLevel = 0.07f;
    [Tooltip("Étincelles laissées par le bout du cercle quand il se remplit (pour +1 de multiplicateur)")]
    public float trailSparkles = 16f;
    [Tooltip("À partir de ce palier, le cercle crépite en continu et bat comme un cœur")]
    public int crackleFromLevel = 4;
    [Tooltip("Étincelles par seconde du crépitement, par palier à partir de crackleFromLevel")]
    public float crackleRate = 5f;
    [Tooltip("Petit son à chaque palier (de plus en plus aigu)")]
    public bool levelUpSound = true;
    [Tooltip("Petit coup de caméra à chaque palier (réglages dans CarCameraFeel)")]
    public bool cameraKick = true;

    ScoreManager score;
    DriftCar car;

    Canvas canvas;
    RectTransform canvasRect, root;
    Camera cam;
    bool placed;
    Image fill;
    Text multiplierText, comboText, scoreText;
    Texture2D ringTexture;
    Sprite ringSprite;

    int lastLevel = 1;
    float shownFill, fullTimer, hitFlash;
    float bankedPoints, bankedTimer, lostTimer, lastPending;
    // Dernières valeurs affichées : on ne réécrit un texte que s'il change (sinon l'UI est recalculée à chaque image)
    int shownMult = -1, shownPending = -1, shownScore = -1, shownDist = -1, shownState = -1;
    float shownScale = -1f, shownTilt;

    // Feux d'artifice
    RectTransform fxBack, fxFront, rays, shockwave, popRt;
    Image raysImg, shockImg;
    Text popText;
    MenuParticles sparks, stars;
    Color fxColor = Color.white;
    float lastM = 1f, scaleOffset, scaleVel, tilt, tiltVel, flash, beat;
    float shockT = 1f, raysT, popT, trailAccum, crackleAccum;
    // Explosions secondaires en attente : x = délai, y/z = position par rapport au cercle, w = palier
    readonly List<Vector4> pendingBursts = new List<Vector4>();

    void Start()
    {
        score = GetComponent<ScoreManager>();
        if (score == null) score = ScoreManager.Instance;
        if (score == null) { enabled = false; return; }

        car = score.car;
        if (car == null) car = FindFirstObjectByType<DriftCar>();
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
        if (fireworks && sparks != null && (lastLevel > 1 || lastPending > 0f)) Fizzle();
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
            fullTimer = 0.15f;
            shownFill = progress;
            LevelUp(level);
        }
        else if (level < lastLevel)
        {
            shownFill = progress;
        }
        else
        {
            shownFill = Mathf.Lerp(shownFill, progress, 1f - Mathf.Exp(-fillSpeed * dt));
            if (m > lastM) trailAccum += (m - lastM) * trailSparkles;
        }
        lastLevel = level;
        lastM = m;

        fullTimer -= dt;
        fill.fillAmount = fullTimer > 0f ? 1f : shownFill;

        Color c = LevelColor(level);
        flash = Mathf.MoveTowards(flash, 0f, dt / 0.3f);
        c = Color.Lerp(c, Color.white, flash * 0.8f); // éclair blanc au passage d'un palier
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

        // Rebond (ressort) du cercle et petite rotation du texte à chaque palier
        Spring(ref scaleOffset, ref scaleVel, 260f, 0.3f, dt);
        Spring(ref tilt, ref tiltVel, 200f, 0.25f, dt);
        float scale = 1f + scaleOffset;
        if (fireworks && level >= crackleFromLevel)
        {
            // Battement de cœur, un peu plus rapide à chaque palier
            beat += dt * (5f + level);
            float b = Mathf.Sin(beat);
            scale += 0.05f * (b > 0f ? b * b * b * b * b * b : 0f);
        }
        if (scale != shownScale) { shownScale = scale; root.localScale = Vector3.one * scale; }
        if (tilt != shownTilt) { shownTilt = tilt; multiplierText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, tilt); }

        FollowCar(dt);
        UpdateFireworks(dt, level, c);
        UpdateCombo(dt);
        UpdateScore();
    }

    // Place le cercle juste sous la voiture à l'écran (position de la voiture projetée par la caméra)
    void FollowCar(float dt)
    {
        if (!followCar || car == null) return;
        if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
        if (cam == null) return;

        Vector3 sp = cam.WorldToScreenPoint(car.transform.position);
        if (sp.z <= 0f) return; // voiture derrière la caméra
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out Vector2 local)) return;

        // Sous la voiture, sans sortir de l'écran (on garde la place du texte du combo en dessous)
        Vector2 half = canvasRect.rect.size * 0.5f;
        if (half.x <= 0f || half.y <= 0f) return; // canvas pas encore mis en page
        Vector2 target = local + new Vector2(0f, -carGap - size * 0.5f);
        float marginX = size * 0.5f + 20f;
        target.x = Mathf.Clamp(target.x, -half.x + marginX, half.x - marginX);
        target.y = Mathf.Clamp(target.y, -half.y + size * 0.9f, half.y - size * 0.5f);

        root.anchoredPosition = placed ? Vector2.Lerp(root.anchoredPosition, target, 1f - Mathf.Exp(-followSpeed * dt)) : target;
        placed = true;
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

    // ---------- Feux d'artifice ----------

    // Nouveau palier : rebond, gerbe, onde de choc, rayons, "x3 !", son, coup de caméra
    void LevelUp(int level)
    {
        int up = Mathf.Max(0, level - 2); // 0 au premier palier (x2)
        scaleVel += (punch + punchPerLevel * up) * Mathf.Sqrt(260f) * 1.6f;
        tiltVel += (Random.value < 0.5f ? -1f : 1f) * (8f + 2f * up) * Mathf.Sqrt(200f) * 1.6f;
        flash = 1f;
        if (levelUpSound) MenuFx.Play(MenuFx.Sfx.Pop, 0.9f + 0.12f * up, 0.8f);
        if (cameraKick) CarCameraFeel.LevelUp(level);
        if (!fireworks || sparks == null) return;

        Color c = LevelColor(level), next = LevelColor(level + 1);
        fxColor = c;
        Vector2 center = Center();
        float radius = size * 0.45f;

        // Gerbe principale : du bord du cercle vers l'extérieur
        int count = Mathf.Min(maxBurstCount, burstCount + burstCountPerLevel * up);
        float speed = burstSpeed * (1f + 0.08f * up);
        for (int i = 0; i < count; i++)
        {
            Vector2 dir = RandomDir();
            float r = Random.value;
            Color pc = r < 0.25f ? Color.white : r < 0.65f ? c : next;
            sparks.Emit(center + dir * radius, dir * speed * Random.Range(0.35f, 1f), pc,
                        Random.Range(14f, 28f), Random.Range(0.5f, 1f), -900f, 2.4f, true);
        }
        // Étoiles plus lentes et plus grosses
        for (int i = 0; i < count / 3; i++)
        {
            Vector2 dir = RandomDir();
            stars.Emit(center + dir * radius * Random.Range(0.8f, 1.1f), dir * speed * Random.Range(0.2f, 0.5f),
                       Color.Lerp(c, Color.white, 0.5f), Random.Range(28f, 48f), Random.Range(0.6f, 1.1f), -400f, 1.8f, true);
        }

        shockT = 0f;
        shockwave.gameObject.SetActive(true);
        raysT = 1f;
        rays.gameObject.SetActive(true);

        popT = 1f;
        popText.text = "x" + level + " !";
        popText.fontSize = Mathf.RoundToInt(size * (0.3f + 0.03f * Mathf.Min(up, 5)));
        popRt.gameObject.SetActive(true);

        // Explosions secondaires autour du cercle, les unes après les autres
        if (level >= secondaryBurstsFromLevel)
        {
            int extra = Mathf.Min(4, level - secondaryBurstsFromLevel + 1);
            for (int i = 0; i < extra; i++)
            {
                Vector2 o = RandomDir() * size * Random.Range(0.75f, 1.3f);
                pendingBursts.Add(new Vector4(0.12f + 0.13f * i + Random.Range(0f, 0.06f), o.x, o.y, level));
            }
        }
    }

    void UpdateFireworks(float dt, int level, Color c)
    {
        if (!fireworks || sparks == null) return;
        Vector2 center = Center();

        if (shockT < 1f)
        {
            shockT = Mathf.Min(1f, shockT + dt / 0.45f);
            float e = 1f - (1f - shockT) * (1f - shockT) * (1f - shockT);
            float d = size * (1f + 1.3f * e);
            shockwave.anchoredPosition = center;
            shockwave.sizeDelta = new Vector2(d, d);
            shockImg.color = WithAlpha(fxColor, (1f - shockT) * 0.9f);
            if (shockT >= 1f) shockwave.gameObject.SetActive(false);
        }

        if (raysT > 0f)
        {
            raysT = Mathf.Max(0f, raysT - dt / 0.9f);
            float d = size * (1.7f + 0.7f * (1f - raysT));
            rays.anchoredPosition = center;
            rays.sizeDelta = new Vector2(d, d);
            rays.localRotation = Quaternion.Euler(0f, 0f, raysT * 120f);
            raysImg.color = WithAlpha(fxColor, Mathf.Min(0.35f + 0.08f * Mathf.Max(0, lastLevel - 2), 0.7f) * raysT);
            if (raysT <= 0f) rays.gameObject.SetActive(false);
        }

        if (popT > 0f)
        {
            popT = Mathf.Max(0f, popT - dt / 0.9f);
            float t = 1f - popT;
            float rise = 1f - (1f - t) * (1f - t);
            popRt.anchoredPosition = center + new Vector2(0f, size * 0.6f + 120f * rise);
            float s = t < 0.12f ? Mathf.Lerp(0.3f, 1.3f, t / 0.12f) : Mathf.Lerp(1.3f, 1f, Mathf.Clamp01((t - 0.12f) / 0.2f));
            popRt.localScale = new Vector3(s, s, 1f);
            popText.color = WithAlpha(Color.Lerp(fxColor, Color.white, 0.3f), Mathf.Clamp01(popT * 3f));
            if (popT <= 0f) popRt.gameObject.SetActive(false);
        }

        for (int i = pendingBursts.Count - 1; i >= 0; i--)
        {
            var b = pendingBursts[i];
            b.x -= dt;
            if (b.x > 0f) { pendingBursts[i] = b; continue; }
            pendingBursts.RemoveAt(i);
            SmallBurst(center + new Vector2(b.y, b.z), (int)b.w);
        }

        float radius = size * 0.5f * (1f - thickness * 0.5f) * root.localScale.x;

        // Traînée d'étincelles au bout du cercle qui se remplit
        trailAccum = Mathf.Min(trailAccum, 6f);
        for (int n = 0; trailAccum >= 1f && n < 3; n++)
        {
            trailAccum -= 1f;
            float a = fill.fillAmount * Mathf.PI * 2f;      // depuis le haut, dans le sens des aiguilles d'une montre
            var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            var back = new Vector2(-dir.y, dir.x);           // vers l'arrière du remplissage
            stars.Emit(center + dir * radius, dir * Random.Range(60f, 160f) + back * Random.Range(80f, 220f),
                       Color.Lerp(c, Color.white, Random.value * 0.5f), Random.Range(16f, 28f), Random.Range(0.35f, 0.6f), -300f, 2f, true);
        }

        // Crépitement aux hauts paliers
        if (level >= crackleFromLevel)
        {
            crackleAccum = Mathf.Min(crackleAccum + dt * crackleRate * (level - crackleFromLevel + 1), 3f);
            for (; crackleAccum >= 1f; crackleAccum -= 1f)
            {
                Vector2 dir = RandomDir();
                var pc = Random.value < 0.3f ? Color.white : LevelColor(level + (Random.value < 0.5f ? 0 : 1));
                stars.Emit(center + dir * radius * Random.Range(0.95f, 1.1f), dir * Random.Range(40f, 140f),
                           pc, Random.Range(12f, 24f), Random.Range(0.3f, 0.55f), 0f, 1.5f, false);
            }
        }
        else crackleAccum = 0f;
    }

    // Petite explosion secondaire (feu d'artifice) autour du cercle
    void SmallBurst(Vector2 pos, int level)
    {
        Color c = levelColors != null && levelColors.Length > 1
            ? levelColors[Random.Range(1, levelColors.Length)]
            : LevelColor(level);
        int n = Mathf.Min(30, 12 + 2 * level);
        for (int i = 0; i < n; i++)
        {
            Vector2 dir = RandomDir();
            sparks.Emit(pos, dir * burstSpeed * 0.55f * Random.Range(0.4f, 1f), Random.value < 0.3f ? Color.white : c,
                        Random.Range(10f, 20f), Random.Range(0.4f, 0.8f), -700f, 2.6f, true);
        }
        stars.Emit(pos, Vector2.zero, Color.white, size * 0.4f, 0.22f, 0f, 0f, true); // éclair au centre
        if (levelUpSound) MenuFx.Play(MenuFx.Sfx.Pop, 1.3f + Random.Range(0f, 0.3f), 0.35f);
    }

    // Combo perdu (mur) : quelques étincelles rouges qui tombent
    void Fizzle()
    {
        Vector2 center = Center();
        float radius = size * 0.45f;
        for (int i = 0; i < 14; i++)
        {
            Vector2 dir = RandomDir();
            sparks.Emit(center + dir * radius, dir * Random.Range(100f, 350f), Random.value < 0.3f ? Color.white : lostColor,
                        Random.Range(10f, 18f), Random.Range(0.35f, 0.6f), -1400f, 1.5f, true);
        }
    }

    // Centre du cercle dans les couches d'effets (elles couvrent tout l'écran)
    Vector2 Center() => fxFront.InverseTransformPoint(root.position);

    static Vector2 RandomDir()
    {
        float a = Random.value * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    static void Spring(ref float x, ref float v, float stiffness, float dampingRatio, float dt)
    {
        dt = Mathf.Min(dt, 0.05f);
        v += (-stiffness * x - 2f * dampingRatio * Mathf.Sqrt(stiffness) * v) * dt;
        x += v * dt;
        if (Mathf.Abs(x) < 0.0005f && Mathf.Abs(v) < 0.005f) x = v = 0f;
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

        canvasRect = (RectTransform)go.transform;
        BuildRingSprite();

        // Effets derrière le cercle : rayons et onde de choc. Chaque couche d'effets a son propre Canvas,
        // pour que les particules qui bougent ne fassent pas recalculer le cercle et les textes à chaque image.
        fxBack = NewLayer("Effets derriere", go.transform);
        rays = NewRect("Rayons", fxBack);
        raysImg = rays.gameObject.AddComponent<Image>();
        raysImg.sprite = MenuFx.Rays;
        raysImg.raycastTarget = false;
        rays.gameObject.SetActive(false);
        shockwave = NewRect("Onde de choc", fxBack);
        shockImg = shockwave.gameObject.AddComponent<Image>();
        shockImg.sprite = ringSprite;
        shockImg.raycastTarget = false;
        shockwave.gameObject.SetActive(false);

        root = NewRect("Combo", go.transform);
        // En suivant la voiture, la position est donnée depuis le centre de l'écran
        root.anchorMin = root.anchorMax = new Vector2(0.5f, followCar ? 0.5f : verticalPosition);
        root.sizeDelta = new Vector2(size, size);

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

        // Effets devant le cercle : éclats, étoiles et "x3 !"
        fxFront = NewLayer("Effets devant", go.transform);
        var sparksRt = NewRect("Eclats", fxFront);
        Stretch(sparksRt);
        sparks = sparksRt.gameObject.AddComponent<MenuParticles>();
        var starsRt = NewRect("Etoiles", fxFront);
        Stretch(starsRt);
        stars = starsRt.gameObject.AddComponent<MenuParticles>();
        stars.sprite = MenuStyle.StarSprite(4, 0.3f, 128, 0f);
        popText = NewText(fxFront, Mathf.RoundToInt(size * 0.3f));
        popRt = popText.rectTransform;
        popRt.sizeDelta = new Vector2(size * 2f, size * 0.5f);
        popRt.gameObject.SetActive(false);

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

    static RectTransform NewLayer(string name, Transform parent)
    {
        var rt = NewRect(name, parent);
        Stretch(rt);
        rt.gameObject.AddComponent<Canvas>();
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
