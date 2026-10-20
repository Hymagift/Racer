using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Menu principal : Jouer, Shop, Options, Quitter.
/// À mettre sur un objet vide "MainMenu" dans la scène du menu, avec MenuBackground pour la partie en fond.
///
/// Si les panneaux ne sont pas assignés, une UI simple est créée automatiquement au lancement
/// (titre, record, pièces, boutons, volume). Pour faire ta propre UI : crée les panneaux dans un Canvas,
/// glisse-les dans les champs ci-dessous et branche les boutons (OnClick -> MainMenu -> méthode correspondante).
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Panneaux (vides = UI créée automatiquement)")]
    public GameObject mainPanel;
    public GameObject shopPanel;
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
    public Color buttonColor = new Color(0f, 0f, 0f, 0.6f);
    public Color accentColor = new Color(1f, 0.55f, 0.1f, 1f);

    // Textes de l'UI automatique
    Text autoBestText;
    readonly List<Text> autoCoinsTexts = new List<Text>();
    Font font;
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
        string best = "Record : " + PlayerData.BestScore;
        string coins = PlayerData.Coins + " pièces";
        if (bestScoreText != null) bestScoreText.text = best;
        if (coinsText != null) coinsText.text = PlayerData.Coins.ToString();
        if (autoBestText != null) autoBestText.text = best;
        foreach (var t in autoCoinsTexts) t.text = coins;
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

    public void ShowMain() => ShowOnly(mainPanel);
    public void OpenShop() => ShowOnly(shopPanel);
    public void OpenOptions() => ShowOnly(optionsPanel);

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void ShowOnly(GameObject panel)
    {
        if (mainPanel != null) mainPanel.SetActive(mainPanel == panel);
        if (shopPanel != null) shopPanel.SetActive(shopPanel == panel);
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

    // ---------- UI automatique ----------

    void BuildDefaultUI()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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

        var canvasRt = CreateRect("MenuCanvas", transform);
        var canvas = canvasRt.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasRt.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRt.gameObject.AddComponent<GraphicRaycaster>();

        // Principal : la partie en fond reste bien visible
        var main = CreatePanel("MainPanel", canvasRt, 0.15f);
        mainPanel = main.gameObject;
        var titleText = CreateText(main, title, 150, TextAnchor.MiddleCenter);
        titleText.color = accentColor;
        Place(titleText.rectTransform, 0.78f, 220f);
        autoBestText = CreateText(main, "", 54, TextAnchor.MiddleCenter);
        Place(autoBestText.rectTransform, 0.68f, 80f);
        var mainCoins = CreateText(main, "", 44, TextAnchor.MiddleCenter);
        Place(mainCoins.rectTransform, 0.635f, 70f);
        autoCoinsTexts.Add(mainCoins);
        var mainButtons = CreateColumn(main, 0.32f);
        CreateButton(mainButtons, "Jouer", Play).GetComponent<Image>().color = accentColor;
        CreateButton(mainButtons, "Shop", OpenShop);
        CreateButton(mainButtons, "Options", OpenOptions);
        CreateButton(mainButtons, "Quitter", Quit);

        // Shop : le vrai shop (ShopManager + ShopCatalog) se branche à la place de ce panneau
        var shop = CreatePanel("ShopPanel", canvasRt, 0.6f);
        shopPanel = shop.gameObject;
        Place(CreateText(shop, "Shop", 100, TextAnchor.MiddleCenter).rectTransform, 0.78f, 160f);
        var shopCoins = CreateText(shop, "", 54, TextAnchor.MiddleCenter);
        Place(shopCoins.rectTransform, 0.68f, 80f);
        autoCoinsTexts.Add(shopCoins);
        Place(CreateText(shop, "Bientôt disponible", 48, TextAnchor.MiddleCenter).rectTransform, 0.55f, 80f);
        CreateButton(CreateColumn(shop, 0.25f), "Retour", ShowMain);

        // Options : volume + réinitialiser
        var options = CreatePanel("OptionsPanel", canvasRt, 0.6f);
        optionsPanel = options.gameObject;
        Place(CreateText(options, "Options", 100, TextAnchor.MiddleCenter).rectTransform, 0.78f, 160f);
        var optionsColumn = CreateColumn(options, 0.45f);
        CreateText(optionsColumn, "Volume", 54, TextAnchor.MiddleCenter).gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
        volumeSlider = CreateSlider(optionsColumn);
        CreateButton(optionsColumn, "Réinitialiser", ResetProgress);
        CreateButton(optionsColumn, "Retour", ShowMain);
    }

    static RectTransform CreateRect(string name, Transform parent)
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

    // Bande horizontale pleine largeur centrée à la hauteur "y" (0 = bas, 1 = haut de l'écran)
    static void Place(RectTransform rt, float y, float height)
    {
        rt.anchorMin = new Vector2(0f, y);
        rt.anchorMax = new Vector2(1f, y);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = Vector2.zero;
    }

    static RectTransform CreatePanel(string name, Transform parent, float dim)
    {
        var rt = CreateRect(name, parent);
        Stretch(rt);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, dim);
        img.raycastTarget = false;
        return rt;
    }

    static RectTransform CreateColumn(Transform parent, float y)
    {
        var rt = CreateRect("Buttons", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, y);
        rt.sizeDelta = new Vector2(640f, 0f);
        var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 30f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    Text CreateText(Transform parent, string content, int size, TextAnchor anchor)
    {
        var rt = CreateRect("Text", parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = content;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = anchor;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var shadow = rt.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
        shadow.effectDistance = new Vector2(3f, -3f);
        return t;
    }

    Button CreateButton(Transform parent, string label, UnityAction onClick)
    {
        var rt = CreateRect(label, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = buttonColor;
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 140f;
        Stretch(CreateText(rt, label, 60, TextAnchor.MiddleCenter).rectTransform);
        return button;
    }

    Slider CreateSlider(Transform parent)
    {
        var rt = CreateRect("Volume", parent);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 70f;

        var bg = CreateRect("Background", rt);
        Stretch(bg);
        bg.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);

        var fillArea = CreateRect("Fill Area", rt);
        Stretch(fillArea);
        var fill = CreateRect("Fill", fillArea);
        fill.sizeDelta = Vector2.zero;
        fill.gameObject.AddComponent<Image>().color = accentColor;

        var handleArea = CreateRect("Handle Slide Area", rt);
        Stretch(handleArea);
        var handle = CreateRect("Handle", handleArea);
        handle.sizeDelta = new Vector2(60f, 0f);
        var handleImg = handle.gameObject.AddComponent<Image>();

        var slider = rt.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        return slider;
    }
}
