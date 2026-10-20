using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Style du menu, DA "drift" rouge / blanc / bleu : textes gras en italique, boutons penchés bordés de blanc,
/// bandes blanches façon piste, trame de points, volants en filigrane.
/// Tout est généré par code (aucune image à importer) : les sprites (arrondis, dégradé, trame, volant, roue crantée)
/// sont créés au lancement.
/// Utilisé par MainMenu et BoosterShop.
/// </summary>
public static class MenuStyle
{
    // ---------- Couleurs ----------

    public static readonly Color Red = Hex(0xE0243C);      // couleur principale
    public static readonly Color DarkRed = Hex(0xA3122A);
    public static readonly Color Blue = Hex(0x1F6BFF);     // boutons d'action
    public static readonly Color LightBlue = Hex(0x4FA8FF);
    public static readonly Color Yellow = Hex(0xFFD23F);
    public static readonly Color Orange = Hex(0xFF8A1F);
    public static readonly Color Green = Hex(0x2FCB5A);
    public static readonly Color Purple = Hex(0x8A4BFF);
    public static readonly Color Teal = Hex(0x1FC7A0);
    public static readonly Color Pink = Hex(0xFF4F9A);
    public static readonly Color Navy = Hex(0x6E0B1E);     // fond des cartes (rouge très sombre)
    public static readonly Color Ink = Hex(0x0A1A44);      // contours des textes (bleu nuit)

    /// <summary>Inclinaison par défaut des boutons et bandeaux (degrés).</summary>
    public const float SkewAngle = 10f;

    public static Color Hex(int rgb, float a = 1f)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
    }

    public static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // ---------- Police ----------

    static Font font;
    public static Font UIFont
    {
        get
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
        set => font = value;
    }

    // ---------- Sprites générés ----------

    const int RoundSize = 66;      // texture carrée (rayon 33 : un cercle en mode Simple)
    const float RoundBorder = 32f; // bord du 9-slice (le rayon des coins)

    static Sprite round, gradient, halftone, wheel, gear;

    /// <summary>Cercle blanc anti-aliasé, utilisable en 9-slice (Sliced) pour faire des rectangles arrondis.</summary>
    public static Sprite Round
    {
        get
        {
            if (round != null) return round;
            var tex = new Texture2D(RoundSize, RoundSize, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float c = (RoundSize - 1) * 0.5f, r = RoundSize * 0.5f;
            var px = new Color32[RoundSize * RoundSize];
            for (int y = 0; y < RoundSize; y++)
                for (int x = 0; x < RoundSize; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    byte a = (byte)(Mathf.Clamp01(r - d) * 255f); // bord adouci sur 1 pixel
                    px[y * RoundSize + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            round = Sprite.Create(tex, new Rect(0, 0, RoundSize, RoundSize), new Vector2(0.5f, 0.5f), 100f, 0,
                                  SpriteMeshType.FullRect, new Vector4(RoundBorder, RoundBorder, RoundBorder, RoundBorder));
            round.name = "MenuRound";
            return round;
        }
    }

    /// <summary>Dégradé vertical blanc : opaque en bas, transparent en haut (à teinter avec la couleur de l'Image).</summary>
    public static Sprite GradientSprite
    {
        get
        {
            if (gradient != null) return gradient;
            const int h = 64;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < h; y++)
            {
                float t = 1f - y / (h - 1f);
                tex.SetPixel(0, y, new Color(1f, 1f, 1f, t * t));
            }
            tex.Apply(false, true);
            gradient = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 100f);
            gradient.name = "MenuGradient";
            return gradient;
        }
    }

    /// <summary>Trame de points blancs, petits en haut, gros en bas (à teinter avec la couleur de l'Image).</summary>
    public static Sprite HalftoneSprite
    {
        get
        {
            if (halftone != null) return halftone;
            const int w = 256, h = 256, step = 8;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int row = y / step;
                    float ox = (row % 2 == 0) ? 0f : step * 0.5f; // rangées décalées
                    float cx = Mathf.Floor((x - ox) / step) * step + ox + step * 0.5f;
                    float cy = row * step + step * 0.5f;
                    float t = 1f - cy / h;                        // 0 en haut, 1 en bas
                    float r = Mathf.Lerp(0.3f, step * 0.62f, t * t);
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f));
                }
            halftone = MakeSprite("MenuHalftone", w, h, px);
            return halftone;
        }
    }

    /// <summary>Volant blanc (cercle, moyeu et trois branches), pour décorer le fond.</summary>
    public static Sprite WheelSprite
    {
        get
        {
            if (wheel != null) return wheel;
            const int n = 128;
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f, R = n * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - c) / R, dy = (y - c) / R;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Band(d, 0.74f, 0.97f) ;           // jante
                    a = Mathf.Max(a, Band(d, -1f, 0.24f));       // moyeu
                    if (d < 0.8f)                                 // branches à 90°, 210° et 330°
                        for (int i = 0; i < 3; i++)
                        {
                            float ang = (90f + 120f * i) * Mathf.Deg2Rad;
                            float along = dx * Mathf.Cos(ang) + dy * Mathf.Sin(ang);
                            float across = Mathf.Abs(-dx * Mathf.Sin(ang) + dy * Mathf.Cos(ang));
                            if (along > 0f) a = Mathf.Max(a, Mathf.Clamp01((0.11f - across) * R));
                        }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            wheel = MakeSprite("MenuWheel", n, n, px);
            return wheel;
        }
    }

    /// <summary>Roue crantée blanche (icône des options).</summary>
    public static Sprite GearSprite
    {
        get
        {
            if (gear != null) return gear;
            const int n = 128;
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f, R = n * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - c) / R, dy = (y - c) / R;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    // 8 dents : le rayon extérieur passe de 0.72 à 0.96 sur chaque dent
                    float tooth = Mathf.Clamp01((Mathf.Cos(ang * 8f) - 0.15f) * 3f);
                    float outer = Mathf.Lerp(0.72f, 0.96f, tooth);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Band(d, 0.3f, outer) * 255f));
                }
            gear = MakeSprite("MenuGear", n, n, px);
            return gear;
        }
    }

    // Opacité d'un anneau entre les rayons a et b (en fraction du rayon), bord adouci
    static float Band(float d, float a, float b)
    {
        const float soft = 0.025f;
        return Mathf.Clamp01((d - a) / soft) * Mathf.Clamp01((b - d) / soft);
    }

    static Sprite MakeSprite(string name, int w, int h, Color32[] px)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = name;
        return sprite;
    }

    /// <summary>Image blanche d'un sprite décoratif (raycastTarget désactivé).</summary>
    public static Image Icon(Transform parent, Sprite sprite, Color color, float size)
    {
        var rt = Node(sprite.name, parent);
        rt.sizeDelta = new Vector2(size, size);
        var img = Fill(rt, color);
        img.sprite = sprite;
        img.preserveAspect = true;
        return img;
    }

    /// <summary>Penche un graphique (voir UISkew).</summary>
    public static UISkew Skew(Graphic g, float angle = SkewAngle)
    {
        var skew = g.gameObject.AddComponent<UISkew>();
        skew.angle = angle;
        return skew;
    }

    // ---------- Rects ----------

    public static RectTransform Node(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    public static void Stretch(RectTransform rt) => Area(rt, 0f, 0f, 1f, 1f);

    /// <summary>Place le rect entre deux points de son parent (0 = gauche/bas, 1 = droite/haut).</summary>
    public static void Area(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
    {
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// <summary>Bande pleine largeur centrée à la hauteur "y" (0 = bas, 1 = haut du parent).</summary>
    public static void Place(RectTransform rt, float y, float height)
    {
        rt.anchorMin = new Vector2(0f, y);
        rt.anchorMax = new Vector2(1f, y);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = Vector2.zero;
    }

    // ---------- Images ----------

    /// <summary>Image unie (raycastTarget désactivé).</summary>
    public static Image Fill(RectTransform rt, Color color)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Rectangle arrondi (rayon en unités du canvas, base 1080x1920).</summary>
    public static Image Rounded(RectTransform rt, Color color, float radius = 32f)
    {
        var img = Fill(rt, color);
        img.sprite = Round;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = RoundBorder / Mathf.Max(1f, radius);
        return img;
    }

    /// <summary>Carte arrondie avec une ombre portée sombre en dessous.</summary>
    public static RectTransform Card(string name, Transform parent, Color color, float radius = 36f, float depth = 10f)
    {
        var rt = Node(name, parent);
        var shadow = Node("Ombre", rt);
        Stretch(shadow);
        shadow.offsetMin = new Vector2(0f, -depth);
        shadow.offsetMax = new Vector2(0f, -depth);
        Rounded(shadow, new Color(0f, 0f, 0f, 0.35f), radius);
        var bg = Node("Fond", rt);
        Stretch(bg);
        Rounded(bg, color, radius);
        return rt;
    }

    /// <summary>Pièce dorée dessinée (deux cercles).</summary>
    public static RectTransform Coin(Transform parent, float size)
    {
        var rt = Node("Pièce", parent);
        rt.sizeDelta = new Vector2(size, size);
        Fill(rt, Hex(0xE09A00)).sprite = Round;
        var inner = Node("Centre", rt);
        Area(inner, 0.16f, 0.16f, 0.84f, 0.84f);
        Fill(inner, Yellow).sprite = Round;
        var shine = Node("Reflet", rt);
        Area(shine, 0.3f, 0.55f, 0.5f, 0.75f);
        Fill(shine, new Color(1f, 1f, 1f, 0.7f)).sprite = Round;
        return rt;
    }

    // ---------- Textes ----------

    /// <summary>Texte gras italique avec contour bleu nuit façon cartoon.</summary>
    public static Text Label(Transform parent, string content, int size, TextAnchor anchor, Color color, float outline = 3f)
    {
        var rt = Node("Text", parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = UIFont;
        t.text = content;
        t.fontSize = size;
        t.fontStyle = FontStyle.BoldAndItalic;
        t.alignment = anchor;
        t.color = color;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        if (outline > 0f)
        {
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = Ink;
            o.effectDistance = new Vector2(outline, -outline);
            var s = rt.gameObject.AddComponent<Shadow>();
            s.effectColor = WithAlpha(Ink, 0.8f);
            s.effectDistance = new Vector2(0f, -outline * 2f);
        }
        return t;
    }

    /// <summary>Texte qui rétrécit tout seul pour tenir dans son rect.</summary>
    public static Text FitLabel(Transform parent, string content, int maxSize, int minSize, TextAnchor anchor, Color color, float outline = 3f)
    {
        var t = Label(parent, content, maxSize, anchor, color, outline);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = minSize;
        t.resizeTextMaxSize = maxSize;
        return t;
    }

    // ---------- Boutons ----------

    /// <summary>
    /// Gros bouton penché : face colorée bordée de blanc, relief plus foncé dessous, reflet en haut, rebond quand on appuie.
    /// La taille vient du parent (layout ou ancres) ; preferredHeight sert dans les layouts.
    /// </summary>
    public static Button CandyButton(Transform parent, string label, Color color, UnityAction onClick,
                                     int fontSize = 56, float preferredHeight = 150f, float radius = 18f, float skew = SkewAngle)
    {
        var rt = Node(label, parent);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = preferredHeight;

        float depth = Mathf.Clamp(preferredHeight * 0.09f, 6f, 14f);
        var side = Node("Relief", rt);
        Stretch(side);
        Skew(Rounded(side, Ink, radius), skew);

        var face = Node("Face", rt);
        Stretch(face);
        face.offsetMin = new Vector2(0f, depth);
        var faceImg = Rounded(face, color, radius);
        faceImg.raycastTarget = true;
        var border = face.gameObject.AddComponent<Outline>(); // liseré blanc
        border.effectColor = Color.white;
        border.effectDistance = new Vector2(5f, -5f);
        Skew(faceImg, skew);

        var shine = Node("Reflet", face);
        Area(shine, 0.05f, 0.55f, 0.95f, 0.9f);
        Skew(Rounded(shine, new Color(1f, 1f, 1f, 0.2f), radius * 0.7f), skew);

        var text = FitLabel(face, label, fontSize, Mathf.Max(16, fontSize / 2), TextAnchor.MiddleCenter, Color.white, 3f);
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(12f, 4f);
        text.rectTransform.offsetMax = new Vector2(-12f, -4f);

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = faceImg;
        var colors = button.colors;
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.5f, 1f);
        button.colors = colors;
        if (onClick != null) button.onClick.AddListener(onClick);
        rt.gameObject.AddComponent<ButtonBounce>();
        return button;
    }

    /// <summary>Texte du bouton créé par CandyButton.</summary>
    public static Text ButtonText(Button b) => b.GetComponentInChildren<Text>(true);

    // ---------- Slider ----------

    public static Slider CandySlider(Transform parent, Color color)
    {
        var rt = Node("Slider", parent);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 90f;

        var bg = Node("Fond", rt);
        Area(bg, 0f, 0.3f, 1f, 0.7f);
        Rounded(bg, new Color(0f, 0f, 0f, 0.45f), 20f).raycastTarget = true; // un appui sur la barre déplace le curseur

        var fillArea = Node("Fill Area", rt);
        Area(fillArea, 0f, 0.3f, 1f, 0.7f);
        fillArea.offsetMax = new Vector2(-20f, 0f);
        var fill = Node("Fill", fillArea);
        fill.sizeDelta = new Vector2(20f, 0f);
        Rounded(fill, color, 20f);

        var handleArea = Node("Handle Slide Area", rt);
        Stretch(handleArea);
        handleArea.offsetMin = new Vector2(30f, 0f);
        handleArea.offsetMax = new Vector2(-30f, 0f);
        var handle = Node("Handle", handleArea);
        handle.sizeDelta = new Vector2(80f, 0f);
        var handleImg = Fill(handle, Color.white);
        handleImg.sprite = Round;
        handleImg.preserveAspect = true;
        handleImg.raycastTarget = true;
        var ring = handle.gameObject.AddComponent<Outline>();
        ring.effectColor = Darken(color, 0.6f);
        ring.effectDistance = new Vector2(4f, -4f);

        var slider = rt.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        return slider;
    }

    // ---------- Zone qui défile ----------

    /// <summary>
    /// Zone qui défile verticalement ; renvoie le contenu (à remplir).
    /// Les glissés horizontaux sont passés au MenuPager parent pour changer de page.
    /// </summary>
    public static RectTransform VerticalScroll(Transform parent, float xMin, float yMin, float xMax, float yMax)
    {
        var view = Node("Scroll", parent);
        Area(view, xMin, yMin, xMax, yMax);
        Fill(view, Color.clear).raycastTarget = true; // capte le glissé du doigt
        view.gameObject.AddComponent<RectMask2D>();

        var content = Node("Content", view);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;

        var scroll = view.gameObject.AddComponent<NestedScrollRect>();
        scroll.content = content;
        scroll.viewport = view;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;
        return content;
    }
}
