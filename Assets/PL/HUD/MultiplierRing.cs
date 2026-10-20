using UnityEngine;
using UnityEngine.UI;
using static MenuStyle;

/// <summary>
/// Multiplicateur affiché comme un anneau qui se remplit, avec le nombre entier au milieu.
/// Chaque nombre entier = 1 tour : à x2.5, le "x2" est affiché et l'anneau est rempli à moitié vers x3.
/// Une traînée lumineuse suit le bout du remplissage quand il avance (et une traînée rouge quand il recule).
///
/// Créé par ScoreHUD (Build), qui lui donne la valeur à afficher avec SetValue chaque image.
/// </summary>
public class MultiplierRing : MonoBehaviour
{
    [Header("Animation")]
    [Tooltip("Vitesse à laquelle le remplissage rattrape la vraie valeur (plus grand = plus nerveux)")]
    public float fillSpeed = 10f;
    [Tooltip("Vitesse à laquelle la traînée rattrape le remplissage (plus petit = traînée plus longue)")]
    public float trailSpeed = 3.5f;
    [Tooltip("Longueur maximale de la traînée, en tours")]
    public float maxTrail = 0.6f;
    [Tooltip("Grossissement du nombre quand on passe un entier")]
    public float popScale = 1.35f;

    [Header("Anneau")]
    [Tooltip("Épaisseur de l'anneau, en fraction du rayon")]
    public float thickness = 0.24f;

    RingGraphic track, fill, trail;
    RectTransform head, numberRect, ringRect;
    Image headGlow;
    Text number;
    float target = 1f, shown = 1f, trailValue = 1f, pop, headRadius;
    int shownLap = 1;

    /// <summary>Crée l'anneau (fond, piste, traînée, remplissage, bout lumineux, nombre) sous "parent".</summary>
    public static MultiplierRing Build(Transform parent, float size)
    {
        var root = Node("Multiplicateur", parent);
        root.sizeDelta = new Vector2(size, size);
        var ring = root.gameObject.AddComponent<MultiplierRing>();
        ring.Create(root);
        return ring;
    }

    void Create(RectTransform root)
    {
        ringRect = root;

        // Ombre décalée + disque rouge foncé + liseré blanc, comme les pastilles du menu
        var shadow = Node("Ombre", root);
        Stretch(shadow);
        shadow.offsetMin = shadow.offsetMax = new Vector2(0f, -10f);
        Fill(shadow, DriftTheme.WithAlpha(DriftTheme.Ink, 0.9f)).sprite = Round;

        var disc = Node("Fond", root);
        Stretch(disc);
        Fill(disc, DriftTheme.WithAlpha(DriftTheme.DarkRed, 0.95f)).sprite = Round;

        var rim = Ring(root, "Liseré", 0f, DriftTheme.RimWidth * 2f / Mathf.Max(1f, root.sizeDelta.x));
        rim.SetColors(DriftTheme.White, DriftTheme.White);

        // L'anneau de remplissage est un peu à l'intérieur du liseré
        float inset = root.sizeDelta.x * 0.07f;
        track = Ring(root, "Piste", inset, thickness);
        track.SetColors(DriftTheme.WithAlpha(DriftTheme.Ink, 0.55f), DriftTheme.WithAlpha(DriftTheme.Ink, 0.55f));

        fill = Ring(root, "Remplissage", inset, thickness);
        fill.SetColors(DriftTheme.Blue, DriftTheme.LightBlue);

        // Traînée un peu plus large que le remplissage, pour faire une lueur
        trail = Ring(root, "Traînée", inset - root.sizeDelta.x * 0.02f, Mathf.Min(1f, thickness + 0.1f));

        // Bout lumineux du remplissage
        float fillRadius = root.sizeDelta.x * 0.5f - inset;
        float band = fillRadius * thickness;
        headRadius = fillRadius - band * 0.5f; // milieu de l'anneau
        head = Node("Bout", root);
        head.sizeDelta = Vector2.one * band * 2.2f;
        headGlow = Fill(head, DriftTheme.WithAlpha(DriftTheme.White, 0.45f));
        headGlow.sprite = Round;
        var core = Node("Coeur", head);
        Area(core, 0.27f, 0.27f, 0.73f, 0.73f);
        Fill(core, DriftTheme.White).sprite = Round;

        // Nombre au milieu, gras italique, contour bleu nuit, penché
        number = HudText(root, "x1", Mathf.RoundToInt(root.sizeDelta.x * 0.36f), TextAnchor.MiddleCenter, DriftTheme.White, 4f);
        numberRect = number.rectTransform;
        Stretch(numberRect);
        numberRect.gameObject.AddComponent<DriftSkew>();
    }

    RingGraphic Ring(RectTransform parent, string name, float inset, float ringThickness)
    {
        var rt = Node(name, parent);
        Stretch(rt);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        var g = rt.gameObject.AddComponent<RingGraphic>();
        g.raycastTarget = false;
        g.thickness = ringThickness;
        g.SetArc(0f, 1f);
        return g;
    }

    /// <summary>Valeur du multiplicateur à afficher (1 = anneau vide, 2 = un tour, etc.).</summary>
    public void SetValue(float value) => target = Mathf.Max(0f, value);

    /// <summary>Saute directement à la valeur, sans animation (début de partie).</summary>
    public void Snap(float value)
    {
        target = shown = trailValue = Mathf.Max(0f, value);
        shownLap = Lap(shown);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-fillSpeed * dt));
        if (Mathf.Abs(shown - target) < 0.001f) shown = target;
        trailValue = Mathf.Lerp(trailValue, shown, 1f - Mathf.Exp(-trailSpeed * dt));
        trailValue = Mathf.Clamp(trailValue, shown - maxTrail, shown + maxTrail);

        // Nombre entier et remplissage du tour en cours
        int lap = Lap(shown);
        float frac = Mathf.Clamp01(shown - lap);
        fill.SetArc(lap, lap + frac);

        if (lap != shownLap)
        {
            if (lap > shownLap) pop = 1f; // un tour complet : le nombre grossit d'un coup
            shownLap = lap;
        }
        number.text = "x" + lap;

        // Traînée : blanche/bleue derrière le bout quand ça monte, rouge quand ça redescend
        if (shown >= trailValue)
        {
            trail.SetArc(trailValue, shown);
            trail.SetColors(DriftTheme.WithAlpha(DriftTheme.LightBlue, 0f), DriftTheme.WithAlpha(DriftTheme.White, 0.9f));
        }
        else
        {
            trail.SetArc(shown, trailValue);
            trail.SetColors(DriftTheme.WithAlpha(DriftTheme.Red, 0.95f), DriftTheme.WithAlpha(DriftTheme.Red, 0f));
        }

        // Bout lumineux : sur le bout du remplissage, plus gros quand la traînée est longue (ça bouge vite)
        bool showHead = frac > 0.002f;
        if (head.gameObject.activeSelf != showHead) head.gameObject.SetActive(showHead);
        if (showHead)
        {
            float ang = Mathf.PI * 0.5f - frac * Mathf.PI * 2f;
            head.anchoredPosition = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * headRadius;
            float speed = Mathf.Clamp01(Mathf.Abs(shown - trailValue) / 0.25f);
            head.localScale = Vector3.one * (0.8f + 0.5f * speed);
            headGlow.color = DriftTheme.WithAlpha(DriftTheme.White, 0.3f + 0.4f * speed);
        }

        // "Pop" du nombre et de l'anneau quand on passe un entier
        pop = Mathf.MoveTowards(pop, 0f, dt * 3f);
        float p = pop * pop;
        numberRect.localScale = Vector3.one * (1f + (popScale - 1f) * p);
        ringRect.localScale = Vector3.one * (1f + 0.08f * p);
    }

    static int Lap(float v) => Mathf.Max(0, Mathf.FloorToInt(v + 0.0001f));

    // ---------- Texte ----------

    /// <summary>Texte du HUD : police du menu, gras italique, contour bleu nuit et ombre portée.</summary>
    public static Text HudText(Transform parent, string content, int size, TextAnchor anchor, Color color, float outline)
    {
        var rt = Node("Text", parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = UIFont;
        t.text = content;
        t.fontSize = size;
        t.fontStyle = DriftTheme.TextStyle;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        if (outline > 0f)
        {
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = DriftTheme.Ink;
            o.effectDistance = new Vector2(outline, -outline);
            var s = rt.gameObject.AddComponent<Shadow>();
            s.effectColor = DriftTheme.WithAlpha(DriftTheme.Ink, 0.8f);
            s.effectDistance = new Vector2(0f, -outline * 2f);
        }
        return t;
    }
}
