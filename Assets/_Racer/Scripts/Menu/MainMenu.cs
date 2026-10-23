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
    Text autoBestText, autoCoinsText, autoRubisText, carNameText, resetLabel;
    Image carDot;
    RectTransform titleRect, swipeHint;
    ButtonBounce playBounce;
    readonly List<(ButtonBounce bounce, Image face, Image side, Text label, Color color)> tabs =
        new List<(ButtonBounce, Image, Image, Text, Color)>();
    float resetArmedUntil, tabWidth;
    bool starting, resetSkinned;

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
        if (autoRubisText != null) autoRubisText.text = PlayerData.Rubis.ToString();

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
        Onboarding.ResetTutorial(); // le tutoriel du début revient
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

        // Titre qui se balance, bouton Jouer qui "respire", indice de glissé qui clignote
        titleRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.6f) * 3f);
        titleRect.localScale = Vector3.one * (1f + Mathf.Sin(t * 2.2f) * 0.03f);
        playBounce.extraScale = 1f + Mathf.Sin(t * 4f) * 0.035f;
        swipeHint.anchoredPosition = new Vector2(Mathf.Sin(t * 3f) * 12f, 0f);

        // Onglets : celui de la page affichée est plus grand et en couleur, les autres sont plus sombres
        for (int i = 0; i < tabs.Count; i++)
        {
            float k = Mathf.Clamp01(1f - Mathf.Abs(pager.Position - i));
            var tab = tabs[i];
            tab.bounce.extraScale = 1f + 0.14f * k;
            Color dim = Color.Lerp(Navy, tab.color, 0.35f);
            SetFaceColor(tab.face, tab.side, Color.Lerp(dim, tab.color, k));
            tab.label.color = Color.Lerp(new Color(1f, 1f, 1f, 0.6f), Color.white, k);
        }

        if (resetLabel != null)
        {
            bool armed = t < resetArmedUntil;
            // Bouton en image ("Réinitialiser" dans l'image) : la confirmation s'affiche sous le bouton
            resetLabel.text = resetSkinned ? (armed ? "SÛR ? APPUIE ENCORE POUR TOUT EFFACER" : "")
                                           : armed ? "SÛR ? APPUIE ENCORE" : "RÉINITIALISER";
        }
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

        // Dégradés sombres en haut et en bas : la partie en fond reste visible au milieu
        var top = Node("Dégradé haut", canvasRt);
        top.anchorMin = new Vector2(0f, 1f);
        top.anchorMax = Vector2.one;
        top.sizeDelta = new Vector2(0f, 420f);
        top.anchoredPosition = new Vector2(0f, -210f);
        top.localScale = new Vector3(1f, -1f, 1f); // retourné : opaque en haut
        Fill(top, WithAlpha(Navy, 0.8f)).sprite = GradientSprite;
        var bottom = Node("Dégradé bas", canvasRt);
        bottom.anchorMin = Vector2.zero;
        bottom.anchorMax = new Vector2(1f, 0f);
        bottom.pivot = new Vector2(0.5f, 0f);
        bottom.sizeDelta = new Vector2(0f, 560f);
        Fill(bottom, WithAlpha(Navy, 0.9f)).sprite = GradientSprite;

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
        var skin = MenuSkin.Current;
        var invocations = SheetPage(pager.AddPage("Invocations"), "INVOCATIONS", Purple, skin != null ? skin.invocationsPanel : null);
        // Garage et Options : panneau sans titre, avec l'onglet de la page en guise de titre
        var garage = SheetPage(pager.AddPage("Garage"), "GARAGE", Blue, skin != null ? skin.emptyPanel : null, skin != null ? skin.tabGarage : null);
        BuildOptionsPage(SheetPage(pager.AddPage("Options"), "OPTIONS", Teal, skin != null ? skin.emptyPanel : null, skin != null ? skin.tabOptions : null));

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

    void BuildPlayPage(RectTransform page)
    {
        var skin = MenuSkin.Current;
        if (skin != null && skin.logo != null)
        {
            // Logo RACER du dossier UI
            titleRect = Node("Logo", page);
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.8f);
            titleRect.sizeDelta = new Vector2(880f, 880f * skin.logo.rect.height / skin.logo.rect.width);
            Picture(titleRect, skin.logo);
        }
        else
        {
            var titleText = Label(page, title, 200, TextAnchor.MiddleCenter, Yellow, 8f);
            titleRect = titleText.rectTransform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.8f);
            titleRect.sizeDelta = new Vector2(1000f, 260f);
            var outer = titleText.gameObject.AddComponent<Outline>(); // deuxième contour, coloré, pour un titre bien épais
            outer.effectColor = Orange;
            outer.effectDistance = new Vector2(-6f, 6f);
        }
        Isolate(titleRect, false); // le titre se balance à chaque image : sous-canvas (perf)

        // Voiture équipée
        var carPill = Node("Voiture", page);
        carPill.anchorMin = carPill.anchorMax = new Vector2(0.5f, 0.6f);
        carPill.sizeDelta = new Vector2(620f, 110f);
        Rounded(carPill, WithAlpha(Navy, 0.8f), 55f);
        var dot = Node("Couleur", carPill);
        dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
        dot.sizeDelta = new Vector2(76f, 76f);
        dot.anchoredPosition = new Vector2(62f, 0f);
        Fill(dot, Color.white).sprite = Round;
        var dotInner = Node("Teinte", dot);
        Area(dotInner, 0.12f, 0.12f, 0.88f, 0.88f);
        carDot = Fill(dotInner, Color.white);
        carDot.sprite = Round;
        carNameText = FitLabel(carPill, "", 46, 24, TextAnchor.MiddleLeft, Color.white, 3f);
        Stretch(carNameText.rectTransform);
        carNameText.rectTransform.offsetMin = new Vector2(120f, 8f);
        carNameText.rectTransform.offsetMax = new Vector2(-30f, -8f);

        var play = CandyButton(page, "JOUER", Green, Play, 110, 260f, 70f);
        if (MenuSkin.Current != null) Skin(play, MenuSkin.Current.playButton);
        var playRt = (RectTransform)play.transform;
        playRt.anchorMin = playRt.anchorMax = new Vector2(0.5f, 0.3f);
        playRt.sizeDelta = new Vector2(700f, 260f);
        playBounce = play.GetComponent<ButtonBounce>();
        Isolate(playRt, true);

        var hintRow = Node("Indice", page);
        Place(hintRow, 0.08f, 70f);
        swipeHint = Node("Glisse", hintRow);
        Stretch(swipeHint);
        Isolate(swipeHint, false);
        if (skin != null && skin.swipeHint != null) Picture(swipeHint, skin.swipeHint); // texte dans l'image
        else Stretch(Label(swipeHint, "Glisse pour les invocations  »", 38, TextAnchor.MiddleCenter, WithAlpha(Color.white, 0.85f), 2f).rectTransform);
    }

    // Page avec un grand panneau coloré semi-transparent et un bandeau titre ; renvoie la zone sous le titre.
    // image : panneau du dossier UI (titre compris) à la place du panneau dessiné.
    // titleImage : panneau sans titre ; cette image (l'onglet de la page) est posée à cheval sur son bord haut.
    static RectTransform SheetPage(RectTransform page, string heading, Color color, Sprite image = null, Sprite titleImage = null)
    {
        if (image != null && titleImage != null)
        {
            // Mêmes proportions que le panneau Invocations (titre compris) : le titre dépasse de 7 % au-dessus du cadre
            const float titleBand = 0.07f;
            var panel = Node("Panneau", page);
            Area(panel, 0.035f, 0.02f, 0.965f, 0.95f);
            var frame = Node("Cadre", panel);
            Area(frame, 0f, 0f, 1f, 1f - titleBand);
            Picture(frame, image).preserveAspect = false; // remplit la page

            var titleRt = Node("Titre", panel);
            Area(titleRt, 0.5f, 1f - titleBand * 2f, 0.5f, 1f);
            var fit = titleRt.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            fit.aspectRatio = titleImage.rect.width / titleImage.rect.height;
            Picture(titleRt, titleImage);

            var inside = Node("Contenu", panel);
            Area(inside, 0.05f, 0.03f, 0.95f, 1f - titleBand * 2f - 0.01f);
            return inside;
        }

        if (image != null)
        {
            var panel = Node("Panneau", page);
            Area(panel, 0.035f, 0.02f, 0.965f, 0.95f);
            var img = Picture(panel, image);
            img.preserveAspect = false; // remplit la page
            var inside = Node("Contenu", panel);
            Area(inside, 0.05f, 0.03f, 0.95f, 1f);
            inside.offsetMax = new Vector2(0f, -100f);
            return inside;
        }

        var sheet = Card("Panneau", page, WithAlpha(Color.Lerp(Navy, color, 0.3f), 0.88f), 56f, 14f);
        Area(sheet, 0.035f, 0.02f, 0.965f, 0.95f);

        var banner = Node("Bandeau", sheet);
        banner.anchorMin = banner.anchorMax = new Vector2(0.5f, 1f);
        banner.sizeDelta = new Vector2(700f, 130f);
        banner.anchoredPosition = new Vector2(0f, -10f);
        var side = Node("Relief", banner);
        Stretch(side);
        side.offsetMin = side.offsetMax = new Vector2(0f, -12f);
        Rounded(side, Darken(color, 0.6f), 50f);
        var face = Node("Face", banner);
        Stretch(face);
        Rounded(face, color, 50f);
        var shine = Node("Reflet", face);
        Area(shine, 0.04f, 0.55f, 0.96f, 0.9f);
        Rounded(shine, WithAlpha(Color.white, 0.2f), 30f);
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

        var skin = MenuSkin.Current;
        if (skin != null && skin.volumeTitle != null)
        {
            var volumeRow = Node("Volume", column);
            volumeRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 90f;
            var volumeTitle = Node("Image", volumeRow); // calé à gauche, à ses proportions
            Area(volumeTitle, 0f, 0f, 0f, 1f);
            volumeTitle.pivot = new Vector2(0f, 0.5f);
            var fit = volumeTitle.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            fit.aspectRatio = skin.volumeTitle.rect.width / skin.volumeTitle.rect.height;
            Picture(volumeTitle, skin.volumeTitle);
        }
        else
        {
            var volumeLabel = Label(column, "VOLUME", 56, TextAnchor.MiddleLeft, Color.white, 3f);
            volumeLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
        }
        volumeSlider = skin != null && skin.sliderBar != null && skin.sliderKnob != null
            ? SkinSlider(column, skin.sliderBar, skin.sliderKnob)
            : CandySlider(column, Teal);

        Spacer(column, 40f);
        var reset = CandyButton(column, "RÉINITIALISER", Red, ResetButton, 52, 150f);
        resetLabel = ButtonText(reset);
        if (skin != null && Skin(reset, skin.resetButton))
        {
            resetSkinned = true;
            resetLabel = Label(column, "", 34, TextAnchor.MiddleCenter, Color.white, 3f); // confirmation, sous le bouton
            resetLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;
        }
        var quit = CandyButton(column, "QUITTER", Pink, Quit, 52, 150f);
        if (skin != null) Skin(quit, skin.quitButton);

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

        var skin = MenuSkin.Current;
        if (skin != null && skin.recordBox != null && skin.rubisCounter != null && skin.coinCounter != null)
        {
            BuildSkinnedTopBar(bar, skin);
            return;
        }

        // Record (à gauche)
        var best = Pill(bar, new Vector2(0.18f, 0.5f), Orange, 300f);
        var bestTitle = Label(best, "RECORD", 28, TextAnchor.MiddleLeft, Orange, 2f);
        Area(bestTitle.rectTransform, 0.1f, 0.52f, 0.95f, 0.95f);
        autoBestText = FitLabel(best, "0", 46, 24, TextAnchor.MiddleLeft, Color.white, 3f);
        Area(autoBestText.rectTransform, 0.1f, 0.05f, 0.95f, 0.6f);

        // Rubis (au milieu) : un appui ouvre la boutique
        var rubis = Pill(bar, new Vector2(0.5f, 0.5f), RubyRed, 290f);
        var gem = Ruby(rubis, 100f);
        gem.anchorMin = gem.anchorMax = new Vector2(0f, 0.5f);
        gem.anchoredPosition = new Vector2(12f, 0f);
        autoRubisText = FitLabel(rubis, "0", 50, 24, TextAnchor.MiddleRight, Color.white, 3f);
        Stretch(autoRubisText.rectTransform);
        autoRubisText.rectTransform.offsetMin = new Vector2(70f, 6f);
        autoRubisText.rectTransform.offsetMax = new Vector2(-70f, -6f);
        var plus = Node("Plus", rubis);
        plus.anchorMin = plus.anchorMax = new Vector2(1f, 0.5f);
        plus.sizeDelta = new Vector2(62f, 62f);
        plus.anchoredPosition = new Vector2(-38f, 0f);
        Fill(plus, Green).sprite = Round;
        Stretch(Label(plus, "+", 50, TextAnchor.MiddleCenter, Color.white, 3f).rectTransform);
        rubis.GetComponent<Image>().raycastTarget = true;
        var rubisButton = rubis.gameObject.AddComponent<Button>();
        rubisButton.onClick.AddListener(OpenBoutique);
        rubis.gameObject.AddComponent<ButtonBounce>();

        // Pièces (à droite), avec une grosse pièce qui dépasse
        var coins = Pill(bar, new Vector2(0.82f, 0.5f), Yellow, 300f);
        var coin = Coin(coins, 96f);
        coin.anchorMin = coin.anchorMax = new Vector2(0f, 0.5f);
        coin.anchoredPosition = new Vector2(10f, 0f);
        autoCoinsText = FitLabel(coins, "0", 50, 24, TextAnchor.MiddleRight, Color.white, 3f);
        Stretch(autoCoinsText.rectTransform);
        autoCoinsText.rectTransform.offsetMin = new Vector2(70f, 6f);
        autoCoinsText.rectTransform.offsetMax = new Vector2(-34f, -6f);
    }

    // Barre du haut en images du dossier UI : record, rubis (un appui ouvre la boutique), pièces
    void BuildSkinnedTopBar(RectTransform bar, MenuSkin skin)
    {
        var best = Counter(bar, 0.18f, skin.recordBox);
        autoBestText = FitLabel(best, "0", 54, 24, TextAnchor.MiddleCenter, Color.white, 3f);
        Area(autoBestText.rectTransform, 0.08f, 0.1f, 0.92f, 0.68f); // sous l'étiquette RECORD

        var rubis = Counter(bar, 0.5f, skin.rubisCounter);
        autoRubisText = FitLabel(rubis, "0", 50, 24, TextAnchor.MiddleCenter, Color.white, 3f);
        Area(autoRubisText.rectTransform, 0.28f, 0.15f, 0.78f, 0.85f); // entre le rubis et le +
        rubis.GetComponent<Image>().raycastTarget = true;
        rubis.gameObject.AddComponent<Button>().onClick.AddListener(OpenBoutique);
        rubis.gameObject.AddComponent<ButtonBounce>();

        var coins = Counter(bar, 0.82f, skin.coinCounter);
        autoCoinsText = FitLabel(coins, "0", 50, 24, TextAnchor.MiddleCenter, Color.white, 3f);
        Area(autoCoinsText.rectTransform, 0.3f, 0.15f, 0.92f, 0.85f); // à droite de la pièce
    }

    static RectTransform Counter(RectTransform bar, float x, Sprite sprite)
    {
        var slot = Node("Pastille", bar);
        slot.anchorMin = slot.anchorMax = new Vector2(x, 0.5f);
        slot.sizeDelta = new Vector2(320f, TopBarHeight - 20f);
        return SkinBox(slot, sprite);
    }

    /// <summary>Ouvre la boutique (rubis, pilote auto) dans la page Invocations.</summary>
    public void OpenBoutique()
    {
        MenuFx.Play(MenuFx.Sfx.Tick, 1.2f);
        OpenInvocations();
        if (boosterShop != null) boosterShop.ShowBoutique();
    }

    static RectTransform Pill(Transform parent, Vector2 anchor, Color border, float width = 400f)
    {
        var pill = Node("Pastille", parent);
        pill.anchorMin = pill.anchorMax = anchor;
        pill.sizeDelta = new Vector2(width, 110f);
        var img = Rounded(pill, WithAlpha(Navy, 0.85f), 55f);
        var outline = img.gameObject.AddComponent<Outline>();
        outline.effectColor = WithAlpha(border, 0.8f);
        outline.effectDistance = new Vector2(4f, -4f);
        return pill;
    }

    void BuildTabBar(RectTransform parent)
    {
        var bar = Node("Onglets", parent);
        Isolate(bar, true); // les onglets s'animent à chaque image : sous-canvas (perf)
        bar.anchorMin = Vector2.zero;
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(0f, TabBarHeight);

        // Fond de la barre, prolongé sous le bas de l'écran
        var bg = Node("Fond", bar);
        Stretch(bg);
        bg.offsetMin = new Vector2(-20f, -300f);
        bg.offsetMax = new Vector2(20f, -20f);
        var bgImg = Rounded(bg, WithAlpha(Navy, 0.92f), 60f);
        var rim = bgImg.gameObject.AddComponent<Outline>();
        rim.effectColor = WithAlpha(Color.white, 0.15f);
        rim.effectDistance = new Vector2(0f, 5f);

        var row = Node("Boutons", bar);
        Stretch(row);
        row.offsetMin = new Vector2(24f, 50f);
        row.offsetMax = new Vector2(-24f, -60f);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.LowerCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;

        var skin = MenuSkin.Current;
        Tab(row, "JOUER", Orange, PagePlay, skin != null ? skin.tabPlay : null);
        Tab(row, "INVOCATIONS", Purple, PageInvocations, skin != null ? skin.tabInvocations : null);
        Tab(row, "GARAGE", Blue, PageGarage, skin != null ? skin.tabGarage : null);
        Tab(row, "OPTIONS", Teal, PageOptions, skin != null ? skin.tabOptions : null);
    }

    void Tab(Transform row, string label, Color color, int page, Sprite image)
    {
        var b = CandyButton(row, label, color, () => pager.GoTo(page), 34, 140f, 34f);
        Skin(b, image); // image du dossier UI (texte compris), sinon le bouton dessiné
        var rt = (RectTransform)b.transform;
        rt.pivot = new Vector2(0.5f, 0f); // grossit vers le haut quand l'onglet est actif
        var text = ButtonText(b);
        text.resizeTextForBestFit = false; // taille réglée par FitTabLabels
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        tabs.Add((b.GetComponent<ButtonBounce>(), (Image)b.targetGraphic, rt.Find("Relief").GetComponent<Image>(), text, color));
    }
}
