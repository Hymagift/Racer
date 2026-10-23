using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Style "jeu mobile" du menu : couleurs vives, coins arrondis, gros boutons en relief.
/// Tout est généré par code (aucune image à importer) : les sprites arrondis et dégradés sont créés au lancement.
/// Utilisé par MainMenu et BoosterShop.
/// </summary>
public static class MenuStyle
{
    // ---------- Couleurs ----------

    public static readonly Color Orange = Hex(0xFF8A1F);
    public static readonly Color Green = Hex(0x3EDB5A);
    public static readonly Color Purple = Hex(0xA34BFF);
    public static readonly Color Blue = Hex(0x2E9BFF);
    public static readonly Color Teal = Hex(0x1FC7A0);
    public static readonly Color Pink = Hex(0xFF4F9A);
    public static readonly Color Red = Hex(0xFF4B4B);
    public static readonly Color Yellow = Hex(0xFFD23F);
    // DA noir et blanc (images de l'UI) : fonds presque noirs, contours des textes noirs
    public static readonly Color Navy = Hex(0x151515);
    public static readonly Color Ink = Hex(0x000000); // contours des textes

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

    static Sprite round, gradient;

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

    static Sprite white;

    /// <summary>Carré blanc : pour une Image de type Filled sans coins arrondis (une Image sans sprite ignore le remplissage).</summary>
    public static Sprite WhiteSprite
    {
        get
        {
            if (white != null) return white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            white.name = "MenuWhite";
            return white;
        }
    }

    static readonly System.Collections.Generic.Dictionary<int, Sprite> stars = new System.Collections.Generic.Dictionary<int, Sprite>();

    /// <summary>
    /// Étoile blanche à contour noir, à "branches" pointes (première pointe vers la droite), à teinter avec la couleur de l'Image.
    /// inner = rayon des creux / rayon des pointes. 3 branches avec inner 0.5 = triangle (flèches).
    /// </summary>
    public static Sprite StarSprite(int branches, float inner, int size = 512, float border = 0.035f)
    {
        int key = branches * 100000 + Mathf.RoundToInt(inner * 1000f) * 10 + (size > 256 ? 1 : 0);
        if (stars.TryGetValue(key, out var cached) && cached != null) return cached;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "Etoile" + branches,
        };
        float sector = Mathf.PI * 2f / branches;
        const float outer = 0.96f;
        var a = new Vector2(outer, 0f);
        var b = new Vector2(Mathf.Cos(sector * 0.5f), Mathf.Sin(sector * 0.5f)) * (outer * inner);
        var e = b - a;
        var n = new Vector2(e.y, -e.x).normalized; // vers l'extérieur
        float px = 2f / size;                      // un pixel en unités de l'étoile (rayon 1)
        var colors = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                float r = p.magnitude;
                float ang = Mathf.Repeat(Mathf.Atan2(p.y, p.x), sector);
                if (ang > sector * 0.5f) ang = sector - ang; // symétrie : une demi-branche suffit
                var q = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                float d = Vector2.Dot(q - a, n);            // < 0 à l'intérieur
                float alpha = Mathf.Clamp01(0.5f - d / (px * 1.2f));
                float white = Mathf.Clamp01(0.5f - (d + border) / (px * 1.2f)); // intérieur moins le contour
                byte v = (byte)(white * 255f);
                colors[y * size + x] = new Color32(v, v, v, (byte)(alpha * 255f));
            }
        tex.SetPixels32(colors);
        tex.Apply(true, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = tex.name;
        stars[key] = sprite;
        return sprite;
    }

    static readonly System.Collections.Generic.Dictionary<int, Sprite> bursts = new System.Collections.Generic.Dictionary<int, Sprite>();

    /// <summary>
    /// Étoile "explosion" blanche pleine, sans contour, comme sur les images de l'UI (son ombre noire = la même, teintée en noir).
    /// tips = nombre de pointes, inner = rayon des creux / rayon des pointes, jitter = irrégularité des pointes (0 = régulière),
    /// startDeg = angle de la première pointe (90 = vers le haut).
    /// </summary>
    public static Sprite BurstSprite(int tips, float inner, float jitter = 0f, float startDeg = 90f, int seed = 1, int size = 512)
    {
        int key = tips * 1000003 + Mathf.RoundToInt(inner * 1000f) * 7919 + Mathf.RoundToInt(jitter * 1000f) * 131
                  + Mathf.RoundToInt(startDeg) * 17 + seed * 3 + size;
        if (bursts.TryGetValue(key, out var cached) && cached != null) return cached;

        // Contour : pointes et creux en alternance, un peu irréguliers pour un effet "BD"
        var rng = new System.Random(seed);
        int n = tips * 2;
        var pts = new Vector2[n];
        float step = Mathf.PI / tips;
        for (int i = 0; i < n; i++)
        {
            bool tip = i % 2 == 0;
            float r = tip ? 0.96f * (1f - jitter * (float)rng.NextDouble())
                          : 0.96f * inner * (1f - jitter * 0.5f * (float)rng.NextDouble());
            float ang = startDeg * Mathf.Deg2Rad + i * step + (tip ? (float)(rng.NextDouble() - 0.5) * jitter * step : 0f);
            pts[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
        }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "Explosion" + tips,
        };
        float px = 2f / size; // un pixel en unités de l'étoile (rayon 1)
        var colors = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                bool inside = false;
                float best = float.MaxValue;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    Vector2 a = pts[j], b = pts[i];
                    if ((b.y > p.y) != (a.y > p.y) && p.x < (a.x - b.x) * (p.y - b.y) / (a.y - b.y) + b.x) inside = !inside;
                    Vector2 e = a - b;
                    float k = Mathf.Clamp01(Vector2.Dot(p - b, e) / e.sqrMagnitude);
                    best = Mathf.Min(best, (p - (b + e * k)).sqrMagnitude);
                }
                float d = Mathf.Sqrt(best) * (inside ? 1f : -1f);
                byte alpha = (byte)(Mathf.Clamp01(0.5f + d / (px * 1.2f)) * 255f); // bord adouci
                colors[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        tex.SetPixels32(colors);
        tex.Apply(true, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = tex.name;
        bursts[key] = sprite;
        return sprite;
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

    public static readonly Color RubyRed = Hex(0xFF2E63);

    /// <summary>Rubis dessiné (losange rouge à facettes) : la monnaie achetée avec de l'argent réel.</summary>
    public static RectTransform Ruby(Transform parent, float size)
    {
        var rt = Node("Rubis", parent);
        rt.sizeDelta = new Vector2(size, size);
        var gem = Node("Gemme", rt);
        gem.anchorMin = gem.anchorMax = new Vector2(0.5f, 0.5f);
        gem.sizeDelta = new Vector2(size * 0.72f, size * 0.72f);
        gem.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Rounded(gem, Darken(RubyRed, 0.55f), size * 0.12f);
        var inner = Node("Face", gem);
        Area(inner, 0.12f, 0.12f, 0.88f, 0.88f);
        Rounded(inner, RubyRed, size * 0.08f);
        var facet = Node("Facette", inner);
        Area(facet, 0f, 0.5f, 0.5f, 1f);
        Rounded(facet, new Color(1f, 1f, 1f, 0.35f), size * 0.05f);
        var shine = Node("Reflet", inner);
        Area(shine, 0.15f, 0.6f, 0.38f, 0.85f);
        Rounded(shine, new Color(1f, 1f, 1f, 0.85f), size * 0.04f);
        return rt;
    }

    /// <summary>Volant dessiné (icône du pilote auto).</summary>
    public static RectTransform Wheel(Transform parent, float size, Color color)
    {
        var rt = Node("Volant", parent);
        rt.sizeDelta = new Vector2(size, size);
        Fill(rt, color).sprite = Round;
        var hole = Node("Intérieur", rt);
        Area(hole, 0.14f, 0.14f, 0.86f, 0.86f);
        Fill(hole, Darken(color, 0.3f)).sprite = Round;
        for (int i = 0; i < 3; i++)
        {
            var spoke = Node("Branche", rt);
            spoke.anchorMin = spoke.anchorMax = new Vector2(0.5f, 0.5f);
            spoke.pivot = new Vector2(0.5f, 0f);
            spoke.sizeDelta = new Vector2(size * 0.14f, size * 0.4f);
            spoke.localRotation = Quaternion.Euler(0f, 0f, 90f + i * 120f);
            Rounded(spoke, color, size * 0.05f);
        }
        var hub = Node("Centre", rt);
        hub.anchorMin = hub.anchorMax = new Vector2(0.5f, 0.5f);
        hub.sizeDelta = new Vector2(size * 0.3f, size * 0.3f);
        Fill(hub, color).sprite = Round;
        return rt;
    }

    // ---------- Textes ----------

    /// <summary>Texte gras blanc avec contour sombre façon cartoon.</summary>
    public static Text Label(Transform parent, string content, int size, TextAnchor anchor, Color color, float outline = 3f)
    {
        var rt = Node("Text", parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = UIFont;
        t.text = content;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
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
    /// Gros bouton "bonbon" : face colorée arrondie, relief plus foncé dessous, reflet en haut, rebond quand on appuie.
    /// La taille vient du parent (layout ou ancres) ; preferredHeight sert dans les layouts.
    /// </summary>
    public static Button CandyButton(Transform parent, string label, Color color, UnityAction onClick,
                                     int fontSize = 56, float preferredHeight = 150f, float radius = 40f)
    {
        var rt = Node(label, parent);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = preferredHeight;

        float depth = Mathf.Clamp(preferredHeight * 0.09f, 6f, 14f);
        var side = Node("Relief", rt);
        Stretch(side);
        Rounded(side, Darken(color, 0.62f), radius);

        var face = Node("Face", rt);
        Stretch(face);
        face.offsetMin = new Vector2(0f, depth);
        var faceImg = Rounded(face, color, radius);
        faceImg.raycastTarget = true;

        var shine = Node("Reflet", face);
        Area(shine, 0.04f, 0.55f, 0.96f, 0.92f);
        Rounded(shine, new Color(1f, 1f, 1f, 0.22f), radius * 0.7f);

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

    // ---------- Images de l'UI (MenuSkin) ----------

    /// <summary>
    /// Remplace l'apparence d'un bouton créé par CandyButton par une image (texte compris dans l'image) :
    /// relief, reflet et texte cachés, tout le reste garde son comportement (clic, rebond, sons).
    /// Peut être rappelé avec une autre image ; sprite null remet le bouton dessiné d'origine.
    /// </summary>
    public static bool Skin(Button b, Sprite sprite)
    {
        if (b == null) return false;
        var state = b.GetComponent<ButtonSkinState>();
        if (sprite == null)
        {
            if (state != null) state.Restore(b);
            return false;
        }

        var rt = (RectTransform)b.transform;
        var face = (RectTransform)rt.Find("Face");
        var img = face.GetComponent<Image>();
        var sideT = rt.Find("Relief");
        var side = sideT != null ? sideT.GetComponent<Image>() : null;
        if (state == null)
        {
            state = b.gameObject.AddComponent<ButtonSkinState>();
            state.Capture(b, face, img, side);
        }

        if (side != null) side.enabled = false;
        face.offsetMin = face.offsetMax = Vector2.zero;
        img.sprite = sprite;
        img.type = Image.Type.Simple;
        img.preserveAspect = true;
        img.pixelsPerUnitMultiplier = 1f;
        img.color = Color.white;
        foreach (Transform child in face) state.Hide(child.gameObject); // reflet, texte, icônes : déjà dans l'image
        var colors = b.colors;
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        b.colors = colors;
        state.MarkSkinned();
        return true;
    }

    /// <summary>Vrai si le bouton (sa face) affiche une image de l'UI au lieu du style dessiné.</summary>
    public static bool IsSkinned(Image face) => face != null && face.sprite != null && face.sprite != Round;

    /// <summary>
    /// Couleur de la face d'un bouton et de son relief. Sur un bouton en image, une couleur vive laisse l'image telle quelle
    /// et une couleur terne (bouton indisponible, onglet inactif) la grise.
    /// </summary>
    public static void SetFaceColor(Image face, Image side, Color color)
    {
        if (IsSkinned(face))
        {
            Color.RGBToHSV(color, out _, out float s, out float v);
            face.color = s > 0.35f && v > 0.6f ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);
            return;
        }
        face.color = color;
        if (side != null) side.color = Darken(color, 0.62f);
    }

    /// <summary>Image simple (sans clic) qui garde ses proportions.</summary>
    public static Image Picture(RectTransform rt, Sprite sprite)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        return img;
    }

<<<<<<< Updated upstream
    /// <summary>
    /// Image de l'UI posée dans le parent à ses proportions (la plus grande qui tient, centrée) ; renvoie son rect,
    /// pour y placer des textes ou des boutons par rapport à l'image elle-même.
    /// </summary>
    public static RectTransform SkinBox(RectTransform parent, Sprite sprite, string name = "Image")
    {
        var rt = Node(name, parent);
        Stretch(rt);
        var img = Picture(rt, sprite);
        img.preserveAspect = false; // le rect a déjà les proportions de l'image
        var fit = rt.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
        return rt;
    }

    /// <summary>Texte noir à contour blanc (sur les fonds sombres des images de l'UI, comme "PILOTE AUTO").</summary>
    public static Text Inverted(Text t)
    {
        t.color = Color.black;
        foreach (var sh in t.GetComponents<Shadow>()) sh.effectColor = Color.white; // Outline compris (c'est un Shadow)
        return t;
=======
    /// <summary>Image calée à gauche de son parent, à sa hauteur (titres de section, "VOLUME"...).</summary>
    public static Image PictureLeft(RectTransform parent, Sprite sprite)
    {
        var rt = Node("Image", parent);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var fit = rt.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
        fit.aspectRatio = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
        return Picture(rt, sprite);
    }

    /// <summary>
    /// Remplace le dessin d'une carte (Card) par une image : l'ombre et le fond dessinés sont cachés.
    /// Renvoie faux (et ne touche à rien) si sprite est null.
    /// </summary>
    public static bool SkinCard(RectTransform card, Sprite sprite)
    {
        if (card == null || sprite == null) return false;
        foreach (Transform child in card) child.gameObject.SetActive(false);
        var pic = Node("Image", card);
        Stretch(pic);
        Picture(pic, sprite);
        return true;
    }

    /// <summary>Slider de CandySlider avec la barre en image : remplissage noir dans la barre, curseur blanc cerclé de noir.</summary>
    public static void SkinSlider(Slider slider, Sprite track)
    {
        if (slider == null || track == null) return;
        var rt = (RectTransform)slider.transform;
        var bg = (RectTransform)rt.Find("Fond");
        Area(bg, 0f, 0.15f, 1f, 0.85f);
        var bgImg = bg.GetComponent<Image>();
        bgImg.sprite = track;
        bgImg.type = Image.Type.Simple;
        bgImg.pixelsPerUnitMultiplier = 1f;
        bgImg.color = Color.white;
        bgImg.preserveAspect = false;

        var fillArea = (RectTransform)rt.Find("Fill Area");
        Area(fillArea, 0.012f, 0.37f, 0.985f, 0.63f); // l'intérieur blanc de la barre
        slider.fillRect.GetComponent<Image>().color = Color.black;

        var handle = slider.handleRect.GetComponent<Image>();
        handle.color = Color.white;
        var ring = handle.GetComponent<Outline>();
        if (ring != null)
        {
            ring.effectColor = Color.black;
            ring.effectDistance = new Vector2(6f, -6f);
        }
    }

    /// <summary>Pastille en image (haut de l'écran...) : remplace le dessin de l'Image et prend les proportions de l'image.</summary>
    public static bool SkinPill(RectTransform pill, Sprite sprite, float height)
    {
        if (pill == null || sprite == null) return false;
        var img = pill.GetComponent<Image>();
        foreach (var o in pill.GetComponents<Outline>()) o.enabled = false;
        img.sprite = sprite;
        img.type = Image.Type.Simple;
        img.pixelsPerUnitMultiplier = 1f;
        img.color = Color.white;
        img.preserveAspect = true;
        pill.sizeDelta = new Vector2(height * sprite.rect.width / Mathf.Max(1f, sprite.rect.height), height);
        return true;
>>>>>>> Stashed changes
    }

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

    /// <summary>Curseur en images de l'UI : barre (9-slice) et bouton rond. Même comportement que CandySlider.</summary>
    public static Slider SkinSlider(Transform parent, Sprite bar, Sprite knob)
    {
        var rt = Node("Slider", parent);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 100f;

        var bg = Node("Fond", rt);
        Area(bg, 0f, 0.3f, 1f, 0.7f);
        var barImg = Fill(bg, Color.white);
        barImg.sprite = bar;
        barImg.type = Image.Type.Sliced;
        barImg.pixelsPerUnitMultiplier = bar.rect.height / 40f; // bouts arrondis à la hauteur de la barre (40)
        barImg.raycastTarget = true; // un appui sur la barre déplace le curseur

        var handleArea = Node("Handle Slide Area", rt);
        Stretch(handleArea);
        handleArea.offsetMin = new Vector2(45f, 0f);
        handleArea.offsetMax = new Vector2(-45f, 0f);
        var handle = Node("Handle", handleArea);
        handle.sizeDelta = new Vector2(90f, 0f);
        var handleImg = Fill(handle, Color.white);
        handleImg.sprite = knob;
        handleImg.preserveAspect = true;
        handleImg.raycastTarget = true;

        var slider = rt.gameObject.AddComponent<Slider>();
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        return slider;
    }

    // ---------- Performance ----------

    /// <summary>
    /// Met un élément animé (qui bouge à chaque image) dans son propre sous-canvas : Unity ne recalcule alors
    /// que ce petit morceau au lieu de tout le menu. clickable = il contient des boutons (ajoute un GraphicRaycaster).
    /// </summary>
    public static void Isolate(RectTransform rt, bool clickable)
    {
        if (rt == null || rt.GetComponent<Canvas>() != null) return;
        rt.gameObject.AddComponent<Canvas>();
        if (clickable) rt.gameObject.AddComponent<GraphicRaycaster>();
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
