using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif
using static MenuStyle;

/// <summary>
/// Menu principal façon jeu mobile : 4 pages qu'on fait défiler en glissant le doigt (ou avec les onglets en bas) :
/// Jouer, Invocations (boosters de blueprints), Garage (voitures), Options.
/// À mettre sur un objet vide "MainMenu" dans la scène du menu, avec MenuBackground pour la partie en fond.
///
/// Si les panneaux ne sont pas assignés, l'UI est créée automatiquement au lancement (voir MenuStyle, MenuPager, BoosterShop).
/// Pour faire ta propre UI : crée les panneaux dans un Canvas, glisse-les dans les champs ci-dessous
/// et branche les boutons (OnClick -> MainMenu -> méthode correspondante).
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Panneaux (vides = UI créée automatiquement)")]
    public GameObject mainPanel;
    [Tooltip("Invocations : ouverture des boosters")]
    public GameObject shopPanel;
    [Tooltip("Garage : inventaire des voitures et des blueprints")]
    public GameObject garagePanel;
    public GameObject optionsPanel;

    [Header("Scène de jeu")]
    [Tooltip("Nom exact de la scène de jeu (elle doit être dans Build Profiles > Scene List)")]
    public string gameSceneName = "PL";

    [Header("Textes (optionnel)")]
    public TMP_Text bestScoreText;
    public TMP_Text coinsText;

    [Header("Options")]
    public Slider volumeSlider;

    [Header("UI automatique")]
    public string title = "RACER";

    // Pages de l'UI automatique (dans l'ordre du glissé)
    public const int PagePlay = 0, PageInvocations = 1, PageGarage = 2, PageOptions = 3;

    MenuPager pager;
    BoosterShop boosterShop;
    RectTransform safeArea;
    Rect lastSafeArea;

    // Éléments animés ou mis à jour de l'UI automatique
    Text autoBestText, autoCoinsText, carNameText, resetLabel;
    Image carDot;
    RectTransform titleRect, swipeHint, gearIcon;
    ButtonBounce playBounce;
    readonly List<(ButtonBounce bounce, Image face, Text label)> tabs = new List<(ButtonBounce, Image, Text)>();
    float resetArmedUntil, tabWidth;
    bool starting;

    void Start()
    {
        Time.timeScale = 1f;

        if (mainPanel == null) BuildDefaultUI();

        float v = PlayerData.Volume;
        AudioListener.volume = v;
        if (volumeSlider != null)
        {
            volumeSlider.SetValueWithoutNotify(v);
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }

        PlayerData.OnChanged += RefreshTexts;
        RefreshTexts();
        ShowMain();
    }

    void OnDestroy()
    {
        PlayerData.OnChanged -= RefreshTexts;
    }

    void RefreshTexts()
    {
        if (bestScoreText != null) bestScoreText.text = "Record : " + PlayerData.BestScore;
        if (coinsText != null) coinsText.text = PlayerData.Coins.ToString();
        if (autoBestText != null) autoBestText.text = PlayerData.BestScore.ToString();
        if (autoCoinsText != null) autoCoinsText.text = PlayerData.Coins.ToString();

        if (carNameText != null)
        {
            var car = CarCatalog.Load().Equipped;
            carNameText.text = car != null ? car.displayName : "";
            if (car != null) carDot.color = car.color;
        }
    }

    // ---------- Navigation (à brancher sur les boutons) ----------

    /// <summary>Fondu au noir puis lancement de la partie.</summary>
    public void Play()
    {
        if (starting) return;
        starting = true;
        StartCoroutine(PlayRoutine());
    }

    IEnumerator PlayRoutine()
    {
        var background = GetComponent<MenuBackground>();
        if (background != null) background.StopDemo();

        yield return ScreenFader.Instance.Fade(1f);
        SceneManager.LoadScene(gameSceneName); // remplace le menu et la démo (le fader revient tout seul du noir)
    }

    public void ShowMain() => Show(PagePlay, mainPanel);
    public void OpenInvocations() => Show(PageInvocations, shopPanel);
    public void OpenShop() => OpenInvocations();
    public void OpenGarage() => Show(PageGarage, garagePanel);
    public void OpenOptions() => Show(PageOptions, optionsPanel);

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // UI automatique : on fait glisser jusqu'à la page. UI faite à la main : on n'affiche que son panneau.
    void Show(int page, GameObject panel)
    {
        if (pager != null)
        {
            pager.GoTo(page);
            return;
        }
        if (mainPanel != null) mainPanel.SetActive(mainPanel == panel);
        if (shopPanel != null) shopPanel.SetActive(shopPanel == panel);
        if (garagePanel != null) garagePanel.SetActive(garagePanel == panel);
        if (optionsPanel != null) optionsPanel.SetActive(optionsPanel == panel);
    }

    // ---------- Options ----------

    public void SetVolume(float value)
    {
        AudioListener.volume = value;
        PlayerData.Volume = value;
    }

    /// <summary>Efface pièces, achats, record et volume. À brancher sur un bouton "Réinitialiser".</summary>
    public void ResetProgress()
    {
        PlayerData.ResetAll();
        AudioListener.volume = 1f;
        if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(1f);
        RefreshTexts();
    }

    // Le bouton de l'UI automatique demande une confirmation (deuxième appui dans les 3 secondes)
    void ResetButton()
    {
        if (Time.unscaledTime < resetArmedUntil)
        {
            resetArmedUntil = 0f;
            ResetProgress();
        }
        else resetArmedUntil = Time.unscaledTime + 3f;
    }

    // ---------- Animations de l'UI automatique ----------

    void Update()
    {
        if (pager == null) return;

        FitSafeArea();
        FitTabLabels();
        if (BackPressed())
        {
            if (boosterShop != null && boosterShop.OverlayOpen) boosterShop.CloseOverlay();
            else if (pager.Current != PagePlay) pager.GoTo(PagePlay);
        }

        float t = Time.unscaledTime;

        // Titre qui se balance, JOUER qui "respire", indice de glissé qui clignote
        titleRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.6f) * 3f);
        titleRect.localScale = Vector3.one * (1f + Mathf.Sin(t * 2.2f) * 0.03f);
        playBounce.extraScale = 1f + Mathf.Sin(t * 4f) * 0.035f;
        swipeHint.anchoredPosition = new Vector2(Mathf.Sin(t * 3f) * 12f, 0f);

        // Roue crantée qui tourne doucement
        gearIcon.localRotation = Quaternion.Euler(0f, 0f, -t * 25f);

        // Onglets : celui de la page affichée est plus grand, blanc avec le texte rouge ; les autres sont rouges
        for (int i = 0; i < tabs.Count; i++)
        {
            float k = Mathf.Clamp01(1f - Mathf.Abs(pager.Position - i));
            var tab = tabs[i];
            tab.bounce.extraScale = 1f + 0.14f * k;
            tab.face.color = Color.Lerp(Red, Color.white, k);
            tab.label.color = Color.Lerp(Color.white, Red, k);
        }

        if (resetLabel != null)
            resetLabel.text = t < resetArmedUntil ? "SÛR ? APPUIE ENCORE" : "RÉINITIALISER";
    }

    static bool BackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame; // bouton retour Android = Échap
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    // Même taille de texte pour tous les onglets, la plus grande qui laisse "INVOCATIONS" sur une ligne
    void FitTabLabels()
    {
        if (tabs.Count == 0) return;
        float w = tabs[0].label.rectTransform.rect.width;
        if (w <= 1f || Mathf.Approximately(w, tabWidth)) return;
        tabWidth = w;

        for (int size = 34; size >= 16; size--)
        {
            bool fits = true;
            foreach (var tab in tabs)
            {
                tab.label.fontSize = size;
                if (tab.label.preferredWidth > w) fits = false;
            }
            if (fits) break;
        }
    }

    // Garde l'UI hors de l'encoche et de la barre du téléphone
    void FitSafeArea()
    {
        Rect safe = Screen.safeArea;
        if (safe == lastSafeArea || Screen.width <= 0 || Screen.height <= 0) return;
        lastSafeArea = safe;
        var size = new Vector2(Screen.width, Screen.height);
        safeArea.anchorMin = safe.position / size;
        safeArea.anchorMax = (safe.position + safe.size) / size;
        safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
    }

    // ---------- UI automatique ----------

    const float TopBarHeight = 170f;
    const float TabBarHeight = 250f;

    void BuildDefaultUI()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        var canvasRt = Node("MenuCanvas", transform);
        var canvas = canvasRt.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasRt.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRt.gameObject.AddComponent<GraphicRaycaster>();

        BuildBackdrop(canvasRt);

        safeArea = Node("SafeArea", canvasRt);
        Stretch(safeArea);
        FitSafeArea();

        // Pages qu'on fait glisser
        var pagerRt = Node("Pages", safeArea);
        Stretch(pagerRt);
        pagerRt.offsetMin = new Vector2(0f, TabBarHeight);
        pagerRt.offsetMax = new Vector2(0f, -TopBarHeight);
        Fill(pagerRt, Color.clear).raycastTarget = true; // capte le glissé du doigt partout
        pager = pagerRt.gameObject.AddComponent<MenuPager>();

        var playPage = pager.AddPage("Jouer");
        BuildPlayPage(playPage);
        var invocations = SheetPage(pager.AddPage("Invocations"), "INVOCATIONS");
        var garage = SheetPage(pager.AddPage("Garage"), "GARAGE");
        BuildOptionsPage(SheetPage(pager.AddPage("Options"), "OPTIONS"));

        BuildTopBar(safeArea);
        BuildTabBar(safeArea);

        // Invocations + Garage (et l'écran d'ouverture des boosters, devant tout) : voir BoosterShop
        boosterShop = GetComponent<BoosterShop>();
        if (boosterShop == null) boosterShop = gameObject.AddComponent<BoosterShop>();
        boosterShop.Build(invocations, garage, canvasRt);

        mainPanel = playPage.gameObject;
        shopPanel = boosterShop.ShopPanel;
        garagePanel = boosterShop.InventoryPanel;
        pager.GoTo(PagePlay, true);
    }

    // Décor derrière les pages, façon affiche de drift : haut et bas rouges, bandes blanches de piste,
    // trame de points et volants en filigrane. Le milieu reste dégagé pour voir la partie de démo.
    static void BuildBackdrop(RectTransform canvas)
    {
        var deco = Node("Décor", canvas);
        Stretch(deco);

        var top = Node("Rouge haut", deco);
        top.anchorMin = new Vector2(0f, 1f);
        top.anchorMax = Vector2.one;
        top.sizeDelta = new Vector2(0f, 820f);
        top.anchoredPosition = new Vector2(0f, -410f);
        top.localScale = new Vector3(1f, -1f, 1f); // retourné : opaque en haut
        Fill(top, WithAlpha(Red, 0.95f)).sprite = GradientSprite;

        var bottom = Node("Rouge bas", deco);
        bottom.anchorMin = Vector2.zero;
        bottom.anchorMax = new Vector2(1f, 0f);
        bottom.pivot = new Vector2(0.5f, 0f);
        bottom.sizeDelta = new Vector2(0f, 900f);
        Fill(bottom, WithAlpha(Red, 0.97f)).sprite = GradientSprite;

        // Volants en filigrane
        Wheel(deco, new Vector2(0.06f, 0.9f), 300f, -15f);
        Wheel(deco, new Vector2(0.95f, 0.78f), 260f, 20f);
        Wheel(deco, new Vector2(0.9f, 0.16f), 280f, -30f);
        Wheel(deco, new Vector2(0.04f, 0.3f), 240f, 10f);

        // Bandes blanches de piste qui partent du haut à gauche et s'effacent vers le bas
        Stripe(deco, 0.12f, 190f);
        Stripe(deco, 0.4f, 110f);

        // Trame de points blancs, dense en haut de la zone et qui s'efface vers le bas
        var dots = Node("Trame", deco);
        dots.anchorMin = new Vector2(0f, 0.12f);
        dots.anchorMax = new Vector2(1f, 0.42f);
        dots.offsetMin = dots.offsetMax = Vector2.zero;
        dots.localScale = new Vector3(1f, -1f, 1f);
        Fill(dots, WithAlpha(Color.white, 0.35f)).sprite = HalftoneSprite;
    }

    static void Stripe(RectTransform parent, float x, float width)
    {
        var stripe = Node("Bande", parent);
        stripe.anchorMin = stripe.anchorMax = new Vector2(x, 1f);
        // Pivot en bas + retourné : la bande pend sous le bord du haut, opaque en haut et effacée en bas
        stripe.pivot = new Vector2(0.5f, 0f);
        stripe.localScale = new Vector3(1f, -1f, 1f);
        stripe.sizeDelta = new Vector2(width, 1500f);
        stripe.anchoredPosition = new Vector2(0f, 60f);
        stripe.localRotation = Quaternion.Euler(0f, 0f, 20f); // le bas part vers la droite
        Fill(stripe, WithAlpha(Color.white, 0.92f)).sprite = GradientSprite;
    }

    static void Wheel(RectTransform parent, Vector2 anchor, float size, float angle)
    {
        var img = Icon(parent, WheelSprite, WithAlpha(Color.white, 0.1f), size);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    void BuildPlayPage(RectTransform page)
    {
        var titleText = Label(page, title, 170, TextAnchor.MiddleCenter, Color.white, 7f);
        titleRect = titleText.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.82f);
        titleRect.sizeDelta = new Vector2(1000f, 240f);
        var outer = titleText.gameObject.AddComponent<Outline>(); // deuxième contour, bleu, pour un titre bien épais
        outer.effectColor = Blue;
        outer.effectDistance = new Vector2(-6f, 6f);

        // Voiture équipée
        var carPill = Node("Voiture", page);
        carPill.anchorMin = carPill.anchorMax = new Vector2(0.5f, 0.66f);
        carPill.sizeDelta = new Vector2(560f, 100f);
        Skew(Rounded(carPill, WithAlpha(Color.white, 0.95f), 18f));
        var dot = Node("Couleur", carPill);
        dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
        dot.sizeDelta = new Vector2(70f, 70f);
        dot.anchoredPosition = new Vector2(60f, 0f);
        Fill(dot, Ink).sprite = Round;
        var dotInner = Node("Teinte", dot);
        Area(dotInner, 0.14f, 0.14f, 0.86f, 0.86f);
        carDot = Fill(dotInner, Color.white);
        carDot.sprite = Round;
        carNameText = FitLabel(carPill, "", 44, 24, TextAnchor.MiddleLeft, Ink, 0f);
        Stretch(carNameText.rectTransform);
        carNameText.rectTransform.offsetMin = new Vector2(115f, 8f);
        carNameText.rectTransform.offsetMax = new Vector2(-30f, -8f);

        // JOUER : gros texte bleu bordé de blanc sur une lueur, comme sur l'affiche
        var play = Node("Jouer", page);
        play.anchorMin = play.anchorMax = new Vector2(0.5f, 0.26f);
        play.sizeDelta = new Vector2(820f, 260f);
        var hit = Fill(play, Color.clear);
        hit.raycastTarget = true;
        var glow = Node("Lueur", play);
        Area(glow, -0.1f, 0.05f, 1.1f, 0.95f);
        Fill(glow, WithAlpha(Color.white, 0.35f)).sprite = Round;
        var playText = Label(play, "JOUER", 170, TextAnchor.MiddleCenter, Blue, 0f);
        Stretch(playText.rectTransform);
        var white = playText.gameObject.AddComponent<Outline>();
        white.effectColor = Color.white;
        white.effectDistance = new Vector2(9f, -9f);
        var depth = playText.gameObject.AddComponent<Shadow>();
        depth.effectColor = Ink;
        depth.effectDistance = new Vector2(4f, -16f);
        Skew(playText, 8f);
        var button = play.gameObject.AddComponent<Button>();
        button.targetGraphic = playText;
        button.onClick.AddListener(Play);
        playBounce = play.gameObject.AddComponent<ButtonBounce>();

        var hintRow = Node("Indice", page);
        Place(hintRow, 0.06f, 70f);
        swipeHint = Node("Glisse", hintRow);
        Stretch(swipeHint);
        Stretch(Label(swipeHint, "Glisse pour les invocations  »", 38, TextAnchor.MiddleCenter, Color.white, 3f).rectTransform);
    }

    // Page avec un grand panneau coloré semi-transparent et un bandeau titre ; renvoie la zone sous le titre
    static RectTransform SheetPage(RectTransform page, string heading)
    {
        var sheet = Card("Panneau", page, WithAlpha(DarkRed, 0.92f), 40f, 14f);
        Area(sheet, 0.035f, 0.02f, 0.965f, 0.95f);
        var rim = sheet.Find("Fond").gameObject.AddComponent<Outline>(); // liseré blanc
        rim.effectColor = Color.white;
        rim.effectDistance = new Vector2(5f, -5f);

        var banner = Node("Bandeau", sheet);
        banner.anchorMin = banner.anchorMax = new Vector2(0.5f, 1f);
        banner.sizeDelta = new Vector2(700f, 130f);
        banner.anchoredPosition = new Vector2(0f, -10f);
        var side = Node("Relief", banner);
        Stretch(side);
        side.offsetMin = side.offsetMax = new Vector2(0f, -12f);
        Skew(Rounded(side, Ink, 18f));
        var face = Node("Face", banner);
        Stretch(face);
        var faceImg = Rounded(face, Blue, 18f);
        var border = faceImg.gameObject.AddComponent<Outline>();
        border.effectColor = Color.white;
        border.effectDistance = new Vector2(6f, -6f);
        Skew(faceImg);
        var shine = Node("Reflet", face);
        Area(shine, 0.04f, 0.55f, 0.96f, 0.9f);
        Skew(Rounded(shine, WithAlpha(Color.white, 0.2f), 14f));
        Stretch(FitLabel(face, heading, 72, 36, TextAnchor.MiddleCenter, Color.white, 4f).rectTransform);

        var body = Node("Contenu", sheet);
        Area(body, 0.05f, 0.03f, 0.95f, 1f);
        body.offsetMax = new Vector2(0f, -100f);
        return body;
    }

    void BuildOptionsPage(RectTransform body)
    {
        optionsPanel = body.gameObject;

        var column = Node("Colonne", body);
        column.anchorMin = new Vector2(0.04f, 1f);
        column.anchorMax = new Vector2(0.96f, 1f);
        column.pivot = new Vector2(0.5f, 1f);
        column.anchoredPosition = new Vector2(0f, -40f);
        var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 40f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var volumeLabel = Label(column, "VOLUME", 56, TextAnchor.MiddleLeft, Color.white, 3f);
        volumeLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
        volumeSlider = CandySlider(column, LightBlue);

        Spacer(column, 40f);
        resetLabel = ButtonText(CandyButton(column, "RÉINITIALISER", Red, ResetButton, 52, 150f));
        CandyButton(column, "QUITTER", Blue, Quit, 52, 150f);

        var version = Label(body, "Version " + Application.version, 30, TextAnchor.MiddleCenter, WithAlpha(Color.white, 0.5f), 0f);
        Place(version.rectTransform, 0.04f, 50f);
    }

    static void Spacer(Transform parent, float height)
    {
        Node("Espace", parent).gameObject.AddComponent<LayoutElement>().preferredHeight = height;
    }

    void BuildTopBar(RectTransform parent)
    {
        var bar = Node("Barre du haut", parent);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = Vector2.one;
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, TopBarHeight);

        // Record et pièces (pastilles blanches penchées, à gauche)
        var best = Pill(bar, new Vector2(0.2f, 0.5f), 360f);
        var bestTitle = Label(best, "RECORD", 28, TextAnchor.MiddleLeft, Red, 0f);
        Area(bestTitle.rectTransform, 0.1f, 0.52f, 0.95f, 0.95f);
        autoBestText = FitLabel(best, "0", 46, 24, TextAnchor.MiddleLeft, Ink, 0f);
        Area(autoBestText.rectTransform, 0.1f, 0.05f, 0.95f, 0.6f);

        var coins = Pill(bar, new Vector2(0.56f, 0.5f), 320f);
        var coin = Coin(coins, 90f);
        coin.anchorMin = coin.anchorMax = new Vector2(0f, 0.5f);
        coin.anchoredPosition = new Vector2(14f, 0f);
        autoCoinsText = FitLabel(coins, "0", 50, 24, TextAnchor.MiddleRight, Ink, 0f);
        Stretch(autoCoinsText.rectTransform);
        autoCoinsText.rectTransform.offsetMin = new Vector2(70f, 6f);
        autoCoinsText.rectTransform.offsetMax = new Vector2(-34f, -6f);

        // Roue crantée (à droite) : va aux options
        var gear = Node("Options", bar);
        gear.anchorMin = gear.anchorMax = new Vector2(0.9f, 0.5f);
        gear.sizeDelta = new Vector2(124f, 124f);
        var disc = Fill(gear, WithAlpha(DarkRed, 0.8f));
        disc.sprite = Round;
        disc.raycastTarget = true;
        var icon = Icon(gear, GearSprite, Color.white, 92f);
        var gearButton = gear.gameObject.AddComponent<Button>();
        gearButton.targetGraphic = icon;
        gearButton.onClick.AddListener(OpenOptions);
        gear.gameObject.AddComponent<ButtonBounce>();
        gearIcon = icon.rectTransform;
    }

    static RectTransform Pill(Transform parent, Vector2 anchor, float width)
    {
        var pill = Node("Pastille", parent);
        pill.anchorMin = pill.anchorMax = anchor;
        pill.sizeDelta = new Vector2(width, 106f);
        var shadow = Node("Ombre", pill);
        Stretch(shadow);
        shadow.offsetMin = shadow.offsetMax = new Vector2(0f, -8f);
        Skew(Rounded(shadow, Ink, 16f));
        var face = Node("Face", pill);
        Stretch(face);
        Skew(Rounded(face, Color.white, 16f));
        return pill;
    }

    void BuildTabBar(RectTransform parent)
    {
        var bar = Node("Onglets", parent);
        bar.anchorMin = Vector2.zero;
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(0f, TabBarHeight);

        // Fond de la barre, prolongé sous le bas de l'écran
        var bg = Node("Fond", bar);
        Stretch(bg);
        bg.offsetMin = new Vector2(-20f, -300f);
        bg.offsetMax = new Vector2(20f, -20f);
        var bgImg = Rounded(bg, WithAlpha(DarkRed, 0.95f), 40f);
        var rim = bgImg.gameObject.AddComponent<Outline>();
        rim.effectColor = Color.white;
        rim.effectDistance = new Vector2(0f, 6f);

        var row = Node("Boutons", bar);
        Stretch(row);
        row.offsetMin = new Vector2(24f, 50f);
        row.offsetMax = new Vector2(-24f, -60f);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.LowerCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;

        Tab(row, "JOUER", PagePlay);
        Tab(row, "INVOCATIONS", PageInvocations);
        Tab(row, "GARAGE", PageGarage);
        Tab(row, "OPTIONS", PageOptions);
    }

    void Tab(Transform row, string label, int page)
    {
        var b = CandyButton(row, label, Red, () => pager.GoTo(page), 34, 140f, 16f, 8f);
        var rt = (RectTransform)b.transform;
        rt.pivot = new Vector2(0.5f, 0f); // grossit vers le haut quand l'onglet est actif
        var text = ButtonText(b);
        text.resizeTextForBestFit = false; // taille réglée par FitTabLabels
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        tabs.Add((b.GetComponent<ButtonBounce>(), (Image)b.targetGraphic, text));
    }
}
