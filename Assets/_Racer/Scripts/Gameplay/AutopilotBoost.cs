using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using static MenuStyle;

/// <summary>
/// Pilote auto proposé au début de chaque partie : pendant le compte à rebours "3, 2, 1, GO !" (temps figé),
/// une carte propose d'activer 30 s de conduite automatique (DriftAutoPilot). Elle reste encore quelques secondes
/// après le GO. Activer prend un pilote dans la réserve (PlayerData.Autopilots) ou, si elle est vide,
/// le paie en rubis. Pendant le pilote auto : compteur en haut de l'écran, puis "REPRENDS LE VOLANT !"
/// les 3 dernières secondes. Les points sont comptés normalement.
///
/// Ajouté automatiquement par DriftGameManager. Inactif dans la démo du menu.
/// </summary>
public class AutopilotBoost : MonoBehaviour
{
    [Tooltip("Durée du pilote auto (secondes de jeu)")]
    public float duration = 30f;
    [Tooltip("Compte à rebours au départ (3 = 3, 2, 1, GO)")]
    public int countdownFrom = 3;
    [Tooltip("Durée de chaque chiffre du compte à rebours (secondes)")]
    public float stepTime = 0.75f;
    [Tooltip("L'offre reste affichée ce temps après le GO (secondes)")]
    public float offerAfterGo = 3f;

    DriftCar car;
    DriftGameManager manager;
    DriftAutoPilot pilot;

    Canvas canvas;
    RectTransform root;
    MenuParticles particles;

    // Départ
    Text countText;
    RectTransform offerCard, offerWheel, offerTimer;
    Text offerTitle, offerSub, offerButtonText;
    Image offerFace, offerSide;
    ButtonBounce offerBounce;
    RectTransform offerRuby;
    bool armed, started, offerOpen, freezing;
    float offerTime, offerTimeTotal;

    // Pendant le pilote auto
    RectTransform hud, hudWheel;
    Image hudFill;
    Text hudTime, warnText;
    bool running;
    float remaining;
    int lastWarnSecond;
    int shownSeconds = -1;
    string secondsSuffix = " s";

    void Start()
    {
        if (MenuBackground.DemoRunning) { enabled = false; return; }
        car = FindFirstObjectByType<DriftCar>();
        manager = GetComponent<DriftGameManager>();
        if (manager == null) manager = FindFirstObjectByType<DriftGameManager>();
        if (car == null) { enabled = false; return; }

        EnsureEventSystem();
        Build();
        StartCoroutine(Intro());
    }

    void OnDestroy()
    {
        if (freezing) Time.timeScale = 1f;
    }

    // ---------- Départ : compte à rebours + offre ----------

    IEnumerator Intro()
    {
        freezing = true;
        Time.timeScale = 0f;

        offerTimeTotal = countdownFrom * stepTime + 0.4f + offerAfterGo;
        offerTime = offerTimeTotal;
        offerOpen = true;
        RefreshOffer();
        StartCoroutine(Slide(offerCard, -700f, 0f, 0.35f));
        MenuFx.Play(MenuFx.Sfx.Whoosh, 1.1f, 0.6f);

        yield return new WaitForSecondsRealtime(0.4f);
        for (int n = countdownFrom; n >= 1; n--)
        {
            ShowCount(n.ToString(), Color.white);
            MenuFx.Play(MenuFx.Sfx.Tick, 0.9f + 0.15f * (countdownFrom - n), 1f);
            yield return new WaitForSecondsRealtime(stepTime);
        }

        ShowCount("GO !", Green);
        MenuFx.Play(MenuFx.Sfx.Pop, 1.1f);
        Time.timeScale = 1f;
        freezing = false;
        started = true;
        if (armed) StartPilot();

        yield return new WaitForSecondsRealtime(0.6f);
        StartCoroutine(Fade(countText, 0.25f));

        // L'offre reste encore un peu, puis s'en va (sauf si elle est utilisée)
        while (offerOpen && offerTime > 0f) yield return null;
        CloseOffer();
    }

    void ShowCount(string text, Color color)
    {
        countText.text = text;
        countText.color = color;
        StartCoroutine(Pop(countText.rectTransform, 2.2f));
    }

    void OnOfferTapped()
    {
        if (armed || !offerOpen) return;

        if (PlayerData.TryUseAutopilot())
        {
            Arm();
        }
        else if (PlayerData.TrySpendRubis(PremiumStore.AutopilotPrice))
        {
            Float(offerCard, "-" + PremiumStore.AutopilotPrice + " rubis", RubyRed);
            Arm();
        }
        else
        {
            // Rien en réserve et pas assez de rubis : on explique où recharger
            MenuFx.Play(MenuFx.Sfx.Error);
            StartCoroutine(Shake(offerCard));
            offerSub.text = "Recharge ta réserve dans la Boutique du menu";
            offerSub.color = Yellow;
        }
    }

    void Arm()
    {
        armed = true;
        MenuFx.Play(MenuFx.Sfx.Unlock);
        MenuFx.Vibrate();
        particles.Burst(Local(offerCard), Teal, 40, 1200f, 26f);
        particles.Burst(Local(offerCard), Yellow, 16, 900f, 20f);
        StartCoroutine(Pop(offerCard, 1.15f));

        if (started) StartPilot();
        else
        {
            offerTitle.text = "PILOTE AUTO PRÊT !";
            offerSub.text = "Il prend le volant au GO";
            offerSub.color = Color.white;
            offerButtonText.text = "PRÊT";
            offerFace.color = Teal;
            offerSide.color = Darken(Teal, 0.62f);
        }
    }

    void RefreshOffer()
    {
        int reserve = PlayerData.Autopilots;
        offerTitle.text = "PILOTE AUTO  ·  " + Mathf.RoundToInt(duration) + " s";
        offerRuby.gameObject.SetActive(false);
        offerButtonText.rectTransform.offsetMin = new Vector2(12f, 4f);

        if (reserve > 0)
        {
            offerSub.text = PlayerData.WelcomeAutopilotsJustGranted && reserve == PlayerData.WelcomeAutopilots
                ? PlayerData.WelcomeAutopilots + " pilotes auto offerts pour essayer !"
                : "Réserve : " + reserve;
            offerButtonText.text = "ACTIVER";
            SetOfferColor(Green);
        }
        else if (PlayerData.Rubis >= PremiumStore.AutopilotPrice)
        {
            offerSub.text = "Réserve vide · " + PlayerData.Rubis + " rubis";
            offerButtonText.text = PremiumStore.AutopilotPrice.ToString();
            offerRuby.gameObject.SetActive(true);
            offerButtonText.rectTransform.offsetMin = new Vector2(70f, 4f);
            SetOfferColor(RubyRed);
        }
        else
        {
            offerSub.text = "Réserve vide";
            offerButtonText.text = "ACTIVER";
            SetOfferColor(new Color(0.45f, 0.45f, 0.55f));
        }
    }

    void SetOfferColor(Color c)
    {
        offerFace.color = c;
        offerSide.color = Darken(c, 0.62f);
    }

    void CloseOffer()
    {
        if (!offerOpen) return;
        offerOpen = false;
        StartCoroutine(Slide(offerCard, offerCard.anchoredPosition.y, -700f, 0.3f));
    }

    // ---------- Pilote auto ----------

    void StartPilot()
    {
        if (running) return;
        pilot = car.GetComponent<DriftAutoPilot>();
        if (pilot == null) pilot = car.gameObject.AddComponent<DriftAutoPilot>();
        pilot.enabled = true;

        running = true;
        remaining = duration;
        lastWarnSecond = -1;
        hud.gameObject.SetActive(true);
        StartCoroutine(Pop(hud, 1.4f));
        MenuFx.Play(MenuFx.Sfx.Whoosh, 0.9f);
        CloseOffer();
    }

    void StopPilot(bool handBack)
    {
        if (!running) return;
        running = false;
        if (pilot != null) pilot.enabled = false; // rend le volant au joueur
        hud.gameObject.SetActive(false);
        warnText.gameObject.SetActive(false);

        if (handBack)
        {
            ShowCount("À TOI !", Yellow);
            countText.color = Yellow;
            StartCoroutine(FadeLater(countText, 0.8f));
            MenuFx.Play(MenuFx.Sfx.Pop, 1.2f);
            MenuFx.Vibrate();
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float t = Time.unscaledTime;

        if (offerOpen)
        {
            if (started) offerTime -= dt;
            else offerTime = Mathf.Max(offerTime - dt, offerAfterGo);
            offerTimer.anchorMax = new Vector2(Mathf.Clamp01(offerTime / offerTimeTotal), 1f);
            offerWheel.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 2.5f) * 30f);
            offerBounce.extraScale = armed ? 1f : 1f + Mathf.Sin(t * 6f) * 0.06f;
        }

        if (!running) return;
        if (manager != null && manager.IsDead) { StopPilot(false); return; }

        remaining -= Time.deltaTime;
        hudFill.fillAmount = Mathf.Clamp01(remaining / duration);
        int shown = Mathf.CeilToInt(Mathf.Max(0f, remaining));
        if (shown != shownSeconds) { shownSeconds = shown; hudTime.text = shown + secondsSuffix; } // texte refait seulement quand il change
        hudWheel.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 3f) * 40f);

        // 3 dernières secondes : on prévient, avec un bip par seconde
        int sec = Mathf.CeilToInt(remaining);
        if (remaining <= 3f)
        {
            warnText.gameObject.SetActive(true);
            warnText.text = "REPRENDS LE VOLANT !  " + Mathf.Max(1, sec);
            warnText.color = WithAlpha(Yellow, 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(t * 8f)));
            if (sec != lastWarnSecond)
            {
                lastWarnSecond = sec;
                MenuFx.Play(MenuFx.Sfx.Tick, 1.6f);
                StartCoroutine(Pop(warnText.rectTransform, 1.3f));
            }
        }

        if (remaining <= 0f) StopPilot(true);
    }

    // ---------- UI ----------

    void Build()
    {
        var go = new GameObject("AutopilotUI", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        root = (RectTransform)go.transform;

        // Compte à rebours
        countText = Label(root, "", 220, TextAnchor.MiddleCenter, Color.white, 8f);
        countText.rectTransform.anchorMin = countText.rectTransform.anchorMax = new Vector2(0.5f, 0.6f);
        countText.rectTransform.sizeDelta = new Vector2(1000f, 300f);

        BuildOffer();
        BuildHud();

        var fx = Node("Particules", root);
        Stretch(fx);
        particles = fx.gameObject.AddComponent<MenuParticles>();
    }

    void BuildOffer()
    {
        offerCard = Card("Offre", root, WithAlpha(Color.Lerp(Navy, Teal, 0.35f), 0.95f), 50f, 14f);
        offerCard.anchorMin = offerCard.anchorMax = new Vector2(0.5f, 0.16f);
        offerCard.sizeDelta = new Vector2(940f, 280f);
        offerCard.anchoredPosition = new Vector2(0f, -700f);
        var glow = offerCard.Find("Fond").gameObject.AddComponent<Outline>();
        glow.effectColor = WithAlpha(Teal, 0.9f);
        glow.effectDistance = new Vector2(5f, -5f);

        offerWheel = Wheel(offerCard, 170f, Teal);
        offerWheel.anchorMin = offerWheel.anchorMax = new Vector2(0.12f, 0.55f);

        offerTitle = FitLabel(offerCard, "", 50, 26, TextAnchor.MiddleLeft, Color.white, 4f);
        Area(offerTitle.rectTransform, 0.23f, 0.55f, 0.66f, 0.92f);
        offerSub = FitLabel(offerCard, "", 34, 18, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.85f), 2f);
        Area(offerSub.rectTransform, 0.23f, 0.2f, 0.66f, 0.55f);

        var button = CandyButton(offerCard, "ACTIVER", Green, OnOfferTapped, 50, 150f, 40f);
        button.GetComponent<LayoutElement>().ignoreLayout = true;
        var brt = (RectTransform)button.transform;
        Area(brt, 0.68f, 0.18f, 0.97f, 0.85f);
        offerFace = brt.Find("Face").GetComponent<Image>();
        offerSide = brt.Find("Relief").GetComponent<Image>();
        offerBounce = button.GetComponent<ButtonBounce>();
        offerButtonText = ButtonText(button);
        offerRuby = Ruby(offerFace.transform, 60f);
        offerRuby.anchorMin = offerRuby.anchorMax = new Vector2(0f, 0.5f);
        offerRuby.anchoredPosition = new Vector2(46f, 0f);

        // Temps restant pour se décider
        var timer = Node("Temps", offerCard);
        Area(timer, 0.05f, 0.05f, 0.95f, 0.11f);
        Rounded(timer, new Color(0f, 0f, 0f, 0.4f), 10f);
        offerTimer = Node("Barre", timer);
        Area(offerTimer, 0f, 0f, 1f, 1f);
        Rounded(offerTimer, Teal, 10f);
    }

    void BuildHud()
    {
        hud = Node("PiloteAuto", root);
        hud.anchorMin = hud.anchorMax = new Vector2(0.5f, 0.9f);
        var art = MenuSkin.Current != null ? MenuSkin.Current.autopilotHud : null;
        if (art != null) BuildSkinnedHud(art);
        else BuildDrawnHud();

        warnText = Label(root, "", 64, TextAnchor.MiddleCenter, Yellow, 5f);
        warnText.rectTransform.anchorMin = warnText.rectTransform.anchorMax = new Vector2(0.5f, 0.8f);
        warnText.rectTransform.sizeDelta = new Vector2(1000f, 120f);
        warnText.gameObject.SetActive(false);

        hud.gameObject.SetActive(false);
    }

    // Compteur en image du dossier UI : volant et "PILOTE AUTO" dans l'image, le jeu ajoute le temps et le remplissage
    void BuildSkinnedHud(Sprite art)
    {
        hud.sizeDelta = new Vector2(600f, 600f * art.rect.height / art.rect.width);
        Picture(hud, art);
        hudWheel = Node("Volant", hud); // le volant est dessiné dans l'image : il ne tourne pas

        var bar = Node("Temps", hud);
        Area(bar, 0.261f, 0.31f, 0.719f, 0.419f); // l'intérieur de la barre blanche de l'image
        hudFill = Fill(bar, Color.black);
        hudFill.sprite = WhiteSprite;
        hudFill.type = Image.Type.Filled;
        hudFill.fillMethod = Image.FillMethod.Horizontal;

        hudTime = Inverted(FitLabel(hud, "", 56, 26, TextAnchor.MiddleCenter, Color.black, 3f)); // noir cerné de blanc, comme "PILOTE AUTO"
        Area(hudTime.rectTransform, 0.75f, 0.22f, 0.97f, 0.78f);
        secondsSuffix = "S";
    }

    void BuildDrawnHud()
    {
        hud.sizeDelta = new Vector2(560f, 130f);
        Rounded(hud, WithAlpha(Navy, 0.85f), 65f).gameObject.AddComponent<Outline>().effectColor = Teal;

        hudWheel = Wheel(hud, 100f, Teal);
        hudWheel.anchorMin = hudWheel.anchorMax = new Vector2(0f, 0.5f);
        hudWheel.anchoredPosition = new Vector2(70f, 0f);

        var title = FitLabel(hud, "PILOTE AUTO", 40, 22, TextAnchor.MiddleLeft, Teal, 3f);
        Area(title.rectTransform, 0.24f, 0.45f, 0.75f, 0.92f);
        hudTime = FitLabel(hud, "", 52, 26, TextAnchor.MiddleRight, Color.white, 3f);
        Area(hudTime.rectTransform, 0.7f, 0.2f, 0.95f, 0.85f);

        var bar = Node("Temps", hud);
        Area(bar, 0.24f, 0.18f, 0.7f, 0.38f);
        Rounded(bar, new Color(0f, 0f, 0f, 0.5f), 12f);
        var fill = Node("Barre", bar);
        Stretch(fill);
        hudFill = Fill(fill, Teal);
        hudFill.sprite = Round;
        hudFill.type = Image.Type.Filled;
        hudFill.fillMethod = Image.FillMethod.Horizontal;
    }

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    // ---------- Petites animations (temps réel : marchent pendant le compte à rebours) ----------

    Vector2 Local(RectTransform target)
    {
        var space = (RectTransform)particles.transform;
        Vector2 local = space.InverseTransformPoint(target.TransformPoint(target.rect.center));
        return local - space.rect.center;
    }

    IEnumerator Pop(RectTransform rt, float from)
    {
        for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime)
        {
            rt.localScale = Vector3.one * Mathf.Lerp(from, 1f, 1f - (1f - t / 0.25f) * (1f - t / 0.25f));
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    IEnumerator Slide(RectTransform rt, float fromY, float toY, float time)
    {
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            float k = t / time;
            k = 1f - (1f - k) * (1f - k) * (1f - k);
            rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(fromY, toY, k));
            yield return null;
        }
        rt.anchoredPosition = new Vector2(0f, toY);
    }

    IEnumerator Fade(Text text, float time)
    {
        Color c = text.color;
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            text.color = WithAlpha(c, 1f - t / time);
            yield return null;
        }
        text.text = "";
        text.color = c;
    }

    IEnumerator FadeLater(Text text, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        yield return Fade(text, 0.3f);
    }

    IEnumerator Shake(RectTransform rt)
    {
        Vector2 basePos = rt.anchoredPosition;
        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            rt.anchoredPosition = basePos + new Vector2(Mathf.Sin(t * 70f) * 20f * (1f - t / 0.35f), 0f);
            yield return null;
        }
        rt.anchoredPosition = basePos;
    }

    void Float(RectTransform from, string text, Color color) => StartCoroutine(FloatRoutine(Local(from), text, color));

    IEnumerator FloatRoutine(Vector2 position, string text, Color color)
    {
        var label = Label(particles.transform, text, 50, TextAnchor.MiddleCenter, color, 3f);
        var rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(800f, 100f);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
        {
            rt.anchoredPosition = position + new Vector2(0f, 160f * Mathf.Sqrt(t));
            label.color = WithAlpha(color, 1f - Mathf.Clamp01((t - 0.6f) / 0.4f));
            yield return null;
        }
        Destroy(label.gameObject);
    }
}
