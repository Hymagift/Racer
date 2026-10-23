using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using static MenuStyle;

/// <summary>
/// Écran "CONTINUER ?" affiché à la mort : continuer en payant des rubis (le prix monte à chaque fois dans la partie),
/// ou en regardant une pub (AdService), ou abandonner. Un compte à rebours laisse quelques secondes pour choisir.
/// Ajouté et appelé par DriftGameManager. Tout fonctionne en temps réel (le jeu est en pause pendant l'écran).
/// </summary>
public class ContinueOffer : MonoBehaviour
{
    public enum Choice { None, Rubis, Ad }

    Canvas canvas;
    RectTransform card, timerFill, rubisButton;
    Text priceText, timeText, infoText;
    Image rubisFace, rubisSide;
    GameObject adButton, adScreen;
    Text adCountdown;
    MenuParticles particles;
    ButtonBounce rubisBounce, adBounce;

    Choice choice;
    bool decided, watchingAd;
    int price;

    /// <summary>Affiche l'offre et attend la réponse du joueur (done reçoit le choix, None = abandon).</summary>
    public IEnumerator Ask(int rubisPrice, bool adAvailable, float time, Action<Choice> done)
    {
        if (canvas == null) Build();
        price = rubisPrice;
        choice = Choice.None;
        decided = false;
        watchingAd = false;

        priceText.text = price.ToString();
        // Bouton en image pour ce prix s'il existe (le prix monte à chaque mort), sinon le bouton dessiné
        Skin(rubisButton.GetComponent<Button>(), MenuSkin.Current != null ? MenuSkin.Price(MenuSkin.Current.continuePrices, price.ToString()) : null);
        bool canPay = PlayerData.Rubis >= price;
        SetFaceColor(rubisFace, rubisSide, canPay ? RubyRed : new Color(0.45f, 0.45f, 0.55f));
        infoText.text = "Tu as " + PlayerData.Rubis + " rubis";
        infoText.color = Color.white;
        adButton.SetActive(adAvailable);
        adScreen.SetActive(false);
        shopPanel.SetActive(false);
        confirmPanel.SetActive(false);

        canvas.gameObject.SetActive(true);
        MenuFx.Play(MenuFx.Sfx.Whoosh, 1.1f, 0.7f);
        StartCoroutine(Pop(card, 0.4f));

        // Compte à rebours (en pause pendant la pub)
        float left = time;
        int lastSecond = -1;
        while (!decided && left > 0f)
        {
            float dt = Time.unscaledDeltaTime;
            if (!watchingAd)
            {
                left -= dt;
                int sec = Mathf.CeilToInt(left);
                if (sec != lastSecond && sec > 0)
                {
                    lastSecond = sec;
                    MenuFx.Play(MenuFx.Sfx.Tick, sec <= 2 ? 1.6f : 1.1f, 0.7f);
                    StartCoroutine(Pop(timeText.rectTransform, 0.3f));
                }
            }
            timeText.text = Mathf.CeilToInt(Mathf.Max(0f, left)).ToString();
            timerFill.anchorMax = new Vector2(Mathf.Clamp01(left / time), 1f);
            float t = Time.unscaledTime;
            rubisBounce.extraScale = PlayerData.Rubis >= price ? 1f + Mathf.Sin(t * 6f) * 0.05f : 1f;
            if (adBounce != null) adBounce.extraScale = 1f + Mathf.Sin(t * 6f + 1.5f) * 0.05f;
            yield return null;
        }

        if (choice != Choice.None)
        {
            MenuFx.Play(MenuFx.Sfx.Unlock);
            MenuFx.Vibrate();
            particles.Burst(Vector2.zero, choice == Choice.Rubis ? RubyRed : Green, 50, 1400f, 28f);
            infoText.text = "C'EST REPARTI !";
            infoText.color = Yellow;
            yield return new WaitForSecondsRealtime(0.5f);
        }
        canvas.gameObject.SetActive(false);
        done?.Invoke(choice);
    }

    void OnRubis()
    {
        if (decided || watchingAd) return;
        if (!PlayerData.TrySpendRubis(price))
        {
            // Pas assez de rubis : la boutique de rubis s'ouvre ici même (la partie attend)
            MenuFx.Play(MenuFx.Sfx.Tick, 1.2f);
            infoText.text = "Il te manque " + (price - PlayerData.Rubis) + " rubis";
            infoText.color = Red;
            OpenShop();
            return;
        }
        choice = Choice.Rubis;
        decided = true;
    }

    void OnAd()
    {
        if (decided || watchingAd) return;
        MenuFx.Play(MenuFx.Sfx.Tick, 1.2f);
        if (AdService.TestMode) StartCoroutine(TestAd());
        else
        {
            watchingAd = true;
            AdService.ShowRewarded(ok =>
            {
                watchingAd = false;
                if (ok) { choice = Choice.Ad; decided = true; }
            });
        }
    }

    // Pub simulée (mode test) : écran noir de 5 s
    IEnumerator TestAd()
    {
        watchingAd = true;
        adScreen.SetActive(true);
        for (float t = 5f; t > 0f; t -= Time.unscaledDeltaTime)
        {
            adCountdown.text = "PUBLICITÉ (TEST)\n" + Mathf.CeilToInt(t) + " s\n\n<size=34>Mode test : aucune vraie pub</size>";
            yield return null;
        }
        adScreen.SetActive(false);
        watchingAd = false;
        choice = Choice.Ad;
        decided = true;
    }

    void OnGiveUp()
    {
        if (decided || watchingAd) return;
        MenuFx.Play(MenuFx.Sfx.Pop, 0.8f, 0.6f);
        decided = true;
    }

    // ---------- Boutique de rubis (sans quitter la partie) ----------

    GameObject shopPanel, confirmPanel;
    Text shopBalance, confirmText;
    PremiumStore.Product pending;

    void OpenShop()
    {
        watchingAd = true; // le compte à rebours attend
        shopBalance.text = "Tu as " + PlayerData.Rubis + " rubis  ·  il en faut " + price;
        shopPanel.SetActive(true);
        StartCoroutine(Pop((RectTransform)shopPanel.transform.GetChild(0), 0.35f));
        MenuFx.Play(MenuFx.Sfx.Whoosh, 1.2f, 0.6f);
    }

    void CloseShop()
    {
        shopPanel.SetActive(false);
        confirmPanel.SetActive(false);
        watchingAd = false;
        RefreshPrice();
    }

    void AskBuy(PremiumStore.Product p)
    {
        pending = p;
        MenuFx.Play(MenuFx.Sfx.Tick, 1.2f);
        if (!PremiumStore.TestMode) { ConfirmBuy(); return; } // le store affiche sa propre confirmation
        confirmText.text = p.title + "\n" + p.rubis + " rubis  ·  " + PremiumStore.PriceOf(p) + "\n\n<size=32>Achat de test : aucun paiement réel</size>";
        confirmPanel.SetActive(true);
    }

    void ConfirmBuy()
    {
        confirmPanel.SetActive(false);
        if (pending == null) return;
        PremiumStore.Buy(pending, (ok, message) =>
        {
            if (!ok)
            {
                MenuFx.Play(MenuFx.Sfx.Error);
                shopBalance.text = message;
                return;
            }
            MenuFx.Play(MenuFx.Sfx.Coin);
            MenuFx.Play(MenuFx.Sfx.Fanfare, 1f, 0.7f);
            particles.Burst(Vector2.zero, RubyRed, 40, 1300f, 26f);
            CloseShop();
            infoText.text = "+" + pending.rubis + " rubis !  Tu en as " + PlayerData.Rubis;
            infoText.color = Green;
        });
    }

    void RefreshPrice()
    {
        bool canPay = PlayerData.Rubis >= price;
        SetFaceColor(rubisFace, rubisSide, canPay ? RubyRed : new Color(0.45f, 0.45f, 0.55f));
    }

    void BuildShop(RectTransform root)
    {
        var panel = Node("BoutiqueRubis", root);
        Stretch(panel);
        Fill(panel, WithAlpha(Navy, 0.85f)).raycastTarget = true;
        shopPanel = panel.gameObject;

        var box = Card("Boîte", panel, Color.Lerp(Navy, RubyRed, 0.3f), 56f, 14f);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(920f, 1180f);

        var title = FitLabel(box, "RUBIS", 90, 40, TextAnchor.MiddleCenter, Yellow, 5f);
        Area(title.rectTransform, 0.05f, 0.87f, 0.95f, 0.97f);
        shopBalance = FitLabel(box, "", 36, 20, TextAnchor.MiddleCenter, Color.white, 2f);
        Area(shopBalance.rectTransform, 0.05f, 0.8f, 0.95f, 0.87f);

        // Un bouton par pack de rubis
        var packs = PremiumStore.RubisPacks;
        for (int i = 0; i < packs.Length; i++)
        {
            var p = packs[i];
            float y1 = 0.77f - i * 0.2f, y0 = y1 - 0.17f;
            var row = Card(p.id, box, Color.Lerp(Navy, RubyRed, 0.2f), 36f, 8f);
            Area(row, 0.06f, y0, 0.94f, y1);
            var gem = Ruby(row, 110f);
            gem.anchorMin = gem.anchorMax = new Vector2(0.12f, 0.5f);
            var amount = FitLabel(row, p.rubis + " rubis", 52, 26, TextAnchor.MiddleLeft, Color.white, 3f);
            Area(amount.rectTransform, 0.24f, 0.45f, 0.62f, 0.95f);
            string extra = p.bonusPercent > 0 ? "+" + p.bonusPercent + " % de bonus" : p.title;
            var sub = FitLabel(row, extra, 30, 16, TextAnchor.MiddleLeft, p.bonusPercent > 0 ? Green : new Color(1f, 1f, 1f, 0.7f), 2f);
            Area(sub.rectTransform, 0.24f, 0.08f, 0.62f, 0.45f);

            var product = p;
            var buy = CandyButton(row, PremiumStore.PriceOf(p), Yellow, () => AskBuy(product), 44, 120f, 36f);
            buy.GetComponent<LayoutElement>().ignoreLayout = true;
            Area((RectTransform)buy.transform, 0.64f, 0.15f, 0.96f, 0.85f);
            ButtonText(buy).color = Ink;
        }

        var back = CandyButton(box, "RETOUR", new Color(0.45f, 0.45f, 0.55f), () => { MenuFx.Play(MenuFx.Sfx.Tick, 0.8f); CloseShop(); }, 46, 130f);
        back.GetComponent<LayoutElement>().ignoreLayout = true;
        Area((RectTransform)back.transform, 0.25f, 0.03f, 0.75f, 0.13f);

        // Confirmation (mode test)
        var conf = Node("Confirmation", panel);
        Stretch(conf);
        Fill(conf, WithAlpha(Navy, 0.8f)).raycastTarget = true;
        confirmPanel = conf.gameObject;
        var cbox = Card("Boîte", conf, Color.Lerp(Navy, Purple, 0.5f), 50f, 14f);
        cbox.anchorMin = cbox.anchorMax = new Vector2(0.5f, 0.5f);
        cbox.sizeDelta = new Vector2(860f, 600f);
        confirmText = FitLabel(cbox, "", 44, 22, TextAnchor.MiddleCenter, Color.white, 3f);
        Area(confirmText.rectTransform, 0.06f, 0.33f, 0.94f, 0.92f);
        var crow = Node("Boutons", cbox);
        Area(crow, 0.06f, 0.06f, 0.94f, 0.28f);
        var layout = crow.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 30f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        CandyButton(crow, "ANNULER", new Color(0.45f, 0.45f, 0.55f), () => confirmPanel.SetActive(false), 44, 130f);
        CandyButton(crow, "ACHETER", Green, ConfirmBuy, 44, 130f);

        confirmPanel.SetActive(false);
        shopPanel.SetActive(false);
    }

    // ---------- UI ----------

    void Build()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        var go = new GameObject("ContinuerUI", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        var root = (RectTransform)go.transform;

        Fill(root, WithAlpha(Navy, 0.75f)).raycastTarget = true;

        card = Card("Carte", root, Color.Lerp(Navy, Purple, 0.45f), 56f, 14f);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(900f, 1000f);

        var title = FitLabel(card, "CONTINUER ?", 96, 40, TextAnchor.MiddleCenter, Yellow, 5f);
        Area(title.rectTransform, 0.05f, 0.84f, 0.95f, 0.97f);

        // Compte à rebours : gros chiffre + barre
        timeText = Label(card, "", 130, TextAnchor.MiddleCenter, Color.white, 6f);
        timeText.rectTransform.anchorMin = timeText.rectTransform.anchorMax = new Vector2(0.5f, 0.71f);
        timeText.rectTransform.sizeDelta = new Vector2(300f, 180f);
        var bar = Node("Temps", card);
        Area(bar, 0.12f, 0.6f, 0.88f, 0.625f);
        Rounded(bar, new Color(0f, 0f, 0f, 0.45f), 12f);
        timerFill = Node("Barre", bar);
        Area(timerFill, 0f, 0f, 1f, 1f);
        Rounded(timerFill, Yellow, 12f);

        // Continuer avec des rubis
        var rb = CandyButton(card, "", RubyRed, OnRubis, 64, 170f, 46f);
        rb.GetComponent<LayoutElement>().ignoreLayout = true;
        rubisButton = (RectTransform)rb.transform;
        Area(rubisButton, 0.1f, 0.39f, 0.9f, 0.55f);
        rubisFace = rubisButton.Find("Face").GetComponent<Image>();
        rubisSide = rubisButton.Find("Relief").GetComponent<Image>();
        rubisBounce = rb.GetComponent<ButtonBounce>();
        var gem = Ruby(rubisFace.transform, 100f);
        gem.anchorMin = gem.anchorMax = new Vector2(0.5f, 0.5f);
        gem.anchoredPosition = new Vector2(-150f, 0f);
        priceText = ButtonText(rb);
        priceText.alignment = TextAnchor.MiddleLeft;
        priceText.rectTransform.offsetMin = new Vector2(380f, 4f);

        // Continuer avec une pub
        var ad = CandyButton(card, "REGARDER UNE PUB", Green, OnAd, 52, 150f, 42f);
        ad.GetComponent<LayoutElement>().ignoreLayout = true;
        Area((RectTransform)ad.transform, 0.1f, 0.21f, 0.9f, 0.35f);
        adButton = ad.gameObject;
        adBounce = ad.GetComponent<ButtonBounce>();

        infoText = FitLabel(card, "", 36, 20, TextAnchor.MiddleCenter, Color.white, 2f);
        Area(infoText.rectTransform, 0.05f, 0.13f, 0.95f, 0.2f);

        // Abandonner : petit texte cliquable
        var giveUp = Node("NonMerci", card);
        Area(giveUp, 0.3f, 0.02f, 0.7f, 0.11f);
        Fill(giveUp, Color.clear).raycastTarget = true;
        giveUp.gameObject.AddComponent<Button>().onClick.AddListener(OnGiveUp);
        if (MenuSkin.Current != null && MenuSkin.Current.continueGiveUp != null)
        {
            var pic = Node("Image", giveUp);
            Stretch(pic);
            Picture(pic, MenuSkin.Current.continueGiveUp); // "Non merci" en image
        }
        else Stretch(Label(giveUp, "Non merci", 42, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f), 2f).rectTransform);

        BuildShop(root);

        var fx = Node("Particules", root);
        Stretch(fx);
        particles = fx.gameObject.AddComponent<MenuParticles>();

        // Écran de la pub simulée
        var screen = Node("Pub", root);
        Stretch(screen);
        Fill(screen, Color.black).raycastTarget = true;
        adCountdown = FitLabel(screen, "", 80, 30, TextAnchor.MiddleCenter, Color.white, 0f);
        Stretch(adCountdown.rectTransform);
        adScreen = screen.gameObject;
        adScreen.SetActive(false);

        canvas.gameObject.SetActive(false);
    }

    IEnumerator Pop(RectTransform rt, float duration)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = t / duration;
            rt.localScale = Vector3.one * (k < 0.6f ? Mathf.Lerp(0.6f, 1.08f, k / 0.6f) : Mathf.Lerp(1.08f, 1f, (k - 0.6f) / 0.4f));
            yield return null;
        }
        rt.localScale = Vector3.one;
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
}
