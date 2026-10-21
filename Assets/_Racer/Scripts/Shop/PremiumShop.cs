using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static MenuStyle;

/// <summary>
/// Partie "Boutique" de la page Invocations : achats avec de l'argent réel (rubis, pack de démarrage)
/// et objets achetés avec des rubis (pilotes auto, pièces). Ajouté et construit par BoosterShop.
/// Les produits et les prix viennent de PremiumStore.
///
/// Les prix sont toujours affichés clairement ; les bonus et réductions affichés sont calculés à partir des prix.
/// En mode test (PremiumStore.TestMode), chaque achat en euros demande une confirmation qui précise
/// qu'aucun paiement n'a lieu.
/// </summary>
public class PremiumShop : MonoBehaviour
{
    class PriceTag
    {
        public Button button;
        public Image face, side;
        public int rubisCost;           // 0 = achat en argent réel
        public Color color;
    }

    RectTransform root, content, rubisSection, starterCard, carPackCard;
    ScrollRect scroll;
    MenuParticles particles;
    Text rubisText, reserveText, reserveBig;
    RectTransform reserveBigRect, wheelIcon;
    float reservePunch, scrollTarget = -1f;
    readonly List<PriceTag> tags = new List<PriceTag>();
    readonly List<RectTransform> badges = new List<RectTransform>();

    // Confirmation d'achat (mode test)
    GameObject confirm;
    Text confirmTitle, confirmBody;
    Action confirmAction;

    public void Build(RectTransform root, RectTransform overlayParent)
    {
        this.root = root;

        // Haut : rubis possédés et réserve de pilotes auto
        var header = Node("Solde", root);
        Area(header, 0f, 0.915f, 1f, 1f);
        var rubisPill = Node("Rubis", header);
        Area(rubisPill, 0.02f, 0.1f, 0.49f, 0.9f);
        Rounded(rubisPill, new Color(0f, 0f, 0f, 0.4f), 40f);
        var ruby = Ruby(rubisPill, 80f);
        ruby.anchorMin = ruby.anchorMax = new Vector2(0f, 0.5f);
        ruby.anchoredPosition = new Vector2(50f, 0f);
        rubisText = FitLabel(rubisPill, "", 44, 22, TextAnchor.MiddleCenter, Color.white, 3f);
        Stretch(rubisText.rectTransform);
        rubisText.rectTransform.offsetMin = new Vector2(90f, 4f);

        var autoPill = Node("Pilotes", header);
        Area(autoPill, 0.51f, 0.1f, 0.98f, 0.9f);
        Rounded(autoPill, new Color(0f, 0f, 0f, 0.4f), 40f);
        var wheel = Wheel(autoPill, 70f, Teal);
        wheel.anchorMin = wheel.anchorMax = new Vector2(0f, 0.5f);
        wheel.anchoredPosition = new Vector2(50f, 0f);
        reserveText = FitLabel(autoPill, "", 40, 20, TextAnchor.MiddleCenter, Color.white, 3f);
        Stretch(reserveText.rectTransform);
        reserveText.rectTransform.offsetMin = new Vector2(90f, 4f);

        // Liste qui défile
        content = VerticalScroll(root, 0f, 0f, 1f, 0.905f);
        scroll = content.GetComponentInParent<ScrollRect>();
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 30f;
        layout.padding = new RectOffset(6, 6, 10, 40);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildStarterPack();
        BuildCarPack();

        rubisSection = Section("RUBIS", "Pour acheter des pilotes auto et des pièces");
        var rubisRow = Row(430f);
        foreach (var p in PremiumStore.RubisPacks) RubisCard(rubisRow, p);

        Section("PILOTE AUTO", "Proposé au début de chaque partie");
        BuildAutopilotHero();
        var autoRow = Row(340f);
        foreach (var o in PremiumStore.AutopilotOffers) AutopilotCard(autoRow, o);

        Section("PIÈCES", "Pour ouvrir plus de boosters");
        var coinRow = Row(320f);
        foreach (var o in PremiumStore.CoinOffers) CoinCard(coinRow, o);

        if (PremiumStore.TestMode)
        {
            var note = FitLabel(content, "MODE TEST : les achats en euros sont simulés, aucun paiement n'a lieu.",
                                30, 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f), 2f);
            note.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
            var testRow = Row(110f);
            CandyButton(testRow, "+500 pièces", Teal, () => { PlayerData.AddCoins(500); MenuFx.Play(MenuFx.Sfx.Coin); }, 34, 100f, 30f);
            CandyButton(testRow, "+100 rubis", RubyRed, () => { PlayerData.AddRubis(100); MenuFx.Play(MenuFx.Sfx.Coin); }, 34, 100f, 30f);
        }

        var fx = Node("Particules", root);
        Stretch(fx);
        particles = fx.gameObject.AddComponent<MenuParticles>();

        BuildConfirm(overlayParent);

        PlayerData.OnChanged += Refresh;
        Refresh();
    }

    void OnDestroy() => PlayerData.OnChanged -= Refresh;

    void Refresh()
    {
        if (rubisText == null) return;
        rubisText.text = PlayerData.Rubis.ToString();
        int reserve = PlayerData.Autopilots;
        reserveText.text = "Réserve : " + reserve;
        if (reserveBig != null) reserveBig.text = reserve.ToString();
        if (starterCard != null) starterCard.gameObject.SetActive(!PremiumStore.IsSoldOut(PremiumStore.StarterPack));
        if (carPackCard != null) carPackCard.gameObject.SetActive(!PremiumStore.IsSoldOut(PremiumStore.CarPack));

        foreach (var tag in tags)
        {
            if (tag.rubisCost <= 0) continue;
            bool canPay = PlayerData.Rubis >= tag.rubisCost;
            tag.face.color = canPay ? tag.color : new Color(0.45f, 0.45f, 0.55f);
            tag.side.color = Darken(tag.face.color, 0.62f);
        }
    }

    void Update()
    {
        if (root == null || !root.gameObject.activeInHierarchy) return;
        Vector3 c = root.TransformPoint(root.rect.center);
        if (c.x < -Screen.width * 0.25f || c.x > Screen.width * 1.25f) return; // page pas à l'écran : rien à animer
        float t = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;

        if (wheelIcon != null) wheelIcon.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.5f) * 25f);
        reservePunch = Mathf.MoveTowards(reservePunch, 0f, dt * 3f);
        if (reserveBigRect != null) reserveBigRect.localScale = Vector3.one * (1f + 0.35f * Mathf.Sin(reservePunch * Mathf.PI));
        for (int i = 0; i < badges.Count; i++)
            badges[i].localRotation = Quaternion.Euler(0f, 0f, 10f + Mathf.Sin(t * 3f + i) * 5f);

        // Défilement automatique vers les rubis (quand il en manque)
        if (scrollTarget >= 0f)
        {
            var pos = content.anchoredPosition;
            pos.y = Mathf.Lerp(pos.y, scrollTarget, 1f - Mathf.Exp(-8f * dt));
            content.anchoredPosition = pos;
            if (Mathf.Abs(pos.y - scrollTarget) < 1f) scrollTarget = -1f;
        }
    }

    /// <summary>Remonte la liste en haut (à l'ouverture de la boutique).</summary>
    public void ScrollToTop()
    {
        if (content != null) content.anchoredPosition = Vector2.zero;
    }

    // ---------- Cartes ----------

    void BuildStarterPack()
    {
        var p = PremiumStore.StarterPack;
        starterCard = Card("Pack de démarrage", content, Color.Lerp(Navy, Purple, 0.65f), 48f, 12f);
        starterCard.gameObject.AddComponent<LayoutElement>().preferredHeight = 470f;
        var glow = starterCard.Find("Fond").gameObject.AddComponent<Outline>();
        glow.effectColor = Yellow;
        glow.effectDistance = new Vector2(6f, -6f);

        Badge(starterCard, p.badge, Pink, new Vector2(0.84f, 0.93f));

        var title = FitLabel(starterCard, p.title.ToUpper(), 60, 30, TextAnchor.MiddleLeft, Yellow, 4f);
        Area(title.rectTransform, 0.05f, 0.76f, 0.7f, 0.95f);

        // Contenu : trois icônes avec leur quantité
        var items = Node("Contenu", starterCard);
        Area(items, 0.03f, 0.3f, 0.97f, 0.74f);
        ContentItem(items, 0f, Ruby(items, 110f), p.rubis + " rubis");
        ContentItem(items, 1f, Wheel(items, 100f, Teal), p.autopilots + " pilotes auto");
        ContentItem(items, 2f, Coin(items, 100f), p.coins + " pièces");

        int value = PremiumStore.ValueInRubis(p);
        var valueText = FitLabel(starterCard, "Contenu d'une valeur de " + value + " rubis", 32, 18, TextAnchor.MiddleLeft, Color.white, 2f);
        Area(valueText.rectTransform, 0.05f, 0.06f, 0.58f, 0.28f);

        var buy = PriceButton(starterCard, PremiumStore.PriceOf(p), Yellow, 0, () => BuyReal(p, starterCard));
        Area((RectTransform)buy.transform, 0.6f, 0.06f, 0.96f, 0.28f);
    }

    // Pack pilote : une voiture, des boosters et des rubis
    void BuildCarPack()
    {
        var p = PremiumStore.CarPack;
        var catalog = CarCatalog.Load();
        var car = catalog.Find(p.carId);
        var booster = catalog.boosters.Find(b => b.id == p.boosterId);
        Color rc = car != null ? RarityInfo.Color(car.rarity) : Purple;

        carPackCard = Card("Pack pilote", content, Color.Lerp(Navy, rc, 0.45f), 48f, 12f);
        carPackCard.gameObject.AddComponent<LayoutElement>().preferredHeight = 500f;
        var glow = carPackCard.Find("Fond").gameObject.AddComponent<Outline>();
        glow.effectColor = rc;
        glow.effectDistance = new Vector2(6f, -6f);

        Badge(carPackCard, p.badge, Pink, new Vector2(0.84f, 0.94f));

        var title = FitLabel(carPackCard, p.title.ToUpper(), 60, 30, TextAnchor.MiddleLeft, Yellow, 4f);
        Area(title.rectTransform, 0.05f, 0.78f, 0.7f, 0.95f);

        var items = Node("Contenu", carPackCard);
        Area(items, 0.03f, 0.3f, 0.97f, 0.76f);

        // La voiture : pastille de sa couleur + nom et rareté
        var swatch = Node("Voiture", items);
        swatch.sizeDelta = new Vector2(110f, 110f);
        Fill(swatch, rc).sprite = Round;
        var tint = Node("Teinte", swatch);
        Area(tint, 0.12f, 0.12f, 0.88f, 0.88f);
        Fill(tint, car != null ? car.color : Color.white).sprite = Round;
        string carText = car != null ? car.displayName + "\n<color=#" + ColorUtility.ToHtmlStringRGB(rc) + ">" +
                                        RarityInfo.Name(car.rarity) + "</color>" : "Voiture";
        ContentItem(items, 0f, swatch, carText);

        // Les boosters : petit paquet aux couleurs du booster
        var pack = Node("Boosters", items);
        pack.sizeDelta = new Vector2(80f, 110f);
        pack.localRotation = Quaternion.Euler(0f, 0f, -8f);
        Rounded(pack, booster != null ? booster.color : Yellow, 16f);
        var packGem = Node("Cristal", pack);
        packGem.anchorMin = packGem.anchorMax = new Vector2(0.5f, 0.55f);
        packGem.sizeDelta = new Vector2(40f, 40f);
        packGem.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Rounded(packGem, Color.white, 8f);
        ContentItem(items, 1f, pack, "x" + p.boosterCount + " " + (booster != null ? booster.displayName : "boosters"));

        ContentItem(items, 2f, Ruby(items, 110f), p.rubis + " rubis");

        var note = FitLabel(carPackCard, car != null && car.IsUnlocked
                                ? "Tu as déjà cette voiture : +" + p.carDuplicateRubis + " rubis à la place"
                                : "Voiture débloquée tout de suite, boosters à ouvrir dans les Invocations",
                            28, 16, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.85f), 2f);
        Area(note.rectTransform, 0.05f, 0.06f, 0.58f, 0.28f);

        var buy = PriceButton(carPackCard, PremiumStore.PriceOf(p), Yellow, 0, () => BuyReal(p, carPackCard));
        Area((RectTransform)buy.transform, 0.6f, 0.06f, 0.96f, 0.28f);
    }

    static void ContentItem(RectTransform parent, float column, RectTransform icon, string text)
    {
        float x0 = column / 3f, x1 = (column + 1f) / 3f;
        icon.anchorMin = icon.anchorMax = new Vector2((x0 + x1) * 0.5f, 0.62f);
        var label = FitLabel(parent, text, 34, 18, TextAnchor.MiddleCenter, Color.white, 2f);
        Area(label.rectTransform, x0, 0f, x1, 0.3f);
    }

    void RubisCard(RectTransform row, PremiumStore.Product p)
    {
        var card = Card(p.id, row, Color.Lerp(Navy, RubyRed, 0.3f), 40f, 10f);
        if (!string.IsNullOrEmpty(p.badge)) Badge(card, p.badge, Yellow, new Vector2(0.5f, 0.97f));
        else if (p.bonusPercent > 0) Badge(card, "+" + p.bonusPercent + " % BONUS", Green, new Vector2(0.5f, 0.97f));

        // Plus le pack est gros, plus il y a de rubis dessinés
        int gems = p.rubis >= 1000 ? 3 : p.rubis >= 400 ? 2 : 1;
        for (int i = 0; i < gems; i++)
        {
            var g = Ruby(card, 120f - gems * 10f);
            g.anchorMin = g.anchorMax = new Vector2(0.5f, 0.62f);
            g.anchoredPosition = new Vector2((i - (gems - 1) * 0.5f) * 60f, (i % 2) * 22f);
        }

        var amount = FitLabel(card, p.rubis.ToString(), 60, 30, TextAnchor.MiddleCenter, Color.white, 4f);
        Area(amount.rectTransform, 0.05f, 0.33f, 0.95f, 0.47f);
        var title = FitLabel(card, p.title, 28, 16, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f), 2f);
        Area(title.rectTransform, 0.05f, 0.24f, 0.95f, 0.33f);

        var buy = PriceButton(card, PremiumStore.PriceOf(p), Yellow, 0, () => BuyReal(p, card));
        Area((RectTransform)buy.transform, 0.07f, 0.04f, 0.93f, 0.22f);
    }

    void BuildAutopilotHero()
    {
        var card = Card("Pilote auto", content, Color.Lerp(Navy, Teal, 0.4f), 44f, 10f);
        card.gameObject.AddComponent<LayoutElement>().preferredHeight = 300f;

        wheelIcon = Wheel(card, 170f, Teal);
        Isolate(wheelIcon, false);
        wheelIcon.anchorMin = wheelIcon.anchorMax = new Vector2(0.15f, 0.5f);

        var title = FitLabel(card, "30 S DE CONDUITE PARFAITE", 44, 24, TextAnchor.MiddleLeft, Color.white, 3f);
        Area(title.rectTransform, 0.29f, 0.62f, 0.78f, 0.92f);
        var desc = FitLabel(card, "La voiture drifte toute seule pendant que tes points s'envolent. Utilise-le au départ d'une partie.",
                            30, 18, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.85f), 2f);
        Area(desc.rectTransform, 0.29f, 0.1f, 0.78f, 0.6f);

        var reserve = Node("Réserve", card);
        Area(reserve, 0.79f, 0.12f, 0.97f, 0.88f);
        Rounded(reserve, new Color(0f, 0f, 0f, 0.35f), 30f);
        var caption = FitLabel(reserve, "RÉSERVE", 26, 14, TextAnchor.MiddleCenter, Teal, 2f);
        Area(caption.rectTransform, 0f, 0.7f, 1f, 0.95f);
        reserveBig = FitLabel(reserve, "", 80, 30, TextAnchor.MiddleCenter, Color.white, 4f);
        Area(reserveBig.rectTransform, 0f, 0.05f, 1f, 0.7f);
        reserveBigRect = reserveBig.rectTransform;
    }

    void AutopilotCard(RectTransform row, PremiumStore.RubisOffer o)
    {
        var card = Card("x" + o.amount, row, Color.Lerp(Navy, Teal, 0.25f), 40f, 10f);
        if (o.discount > 0) Badge(card, "-" + o.discount + " %", Green, new Vector2(0.78f, 0.95f));

        var icon = Wheel(card, 100f, Teal);
        icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.66f);
        var amount = FitLabel(card, "x" + o.amount, 56, 28, TextAnchor.MiddleCenter, Color.white, 4f);
        Area(amount.rectTransform, 0.05f, 0.3f, 0.95f, 0.47f);

        var offer = o;
        var buy = PriceButton(card, o.price.ToString(), RubyRed, o.price, () =>
            BuyWithRubis(offer.price, card, () => PlayerData.AddAutopilots(offer.amount),
                         "+" + offer.amount + (offer.amount > 1 ? " pilotes auto" : " pilote auto"), Teal, true));
        Area((RectTransform)buy.transform, 0.07f, 0.04f, 0.93f, 0.27f);
    }

    void CoinCard(RectTransform row, PremiumStore.RubisOffer o)
    {
        var card = Card(o.amount + " pièces", row, Color.Lerp(Navy, Yellow, 0.2f), 40f, 10f);
        if (o.discount > 0) Badge(card, "-" + o.discount + " %", Green, new Vector2(0.82f, 0.95f));

        int coins = o.amount >= 5000 ? 3 : 1;
        for (int i = 0; i < coins; i++)
        {
            var c = Coin(card, 100f);
            c.anchorMin = c.anchorMax = new Vector2(0.5f, 0.66f);
            c.anchoredPosition = new Vector2((i - (coins - 1) * 0.5f) * 50f, (i % 2) * 18f);
        }
        var amount = FitLabel(card, o.amount.ToString(), 54, 28, TextAnchor.MiddleCenter, Yellow, 4f);
        Area(amount.rectTransform, 0.05f, 0.3f, 0.95f, 0.46f);

        var offer = o;
        var buy = PriceButton(card, o.price.ToString(), RubyRed, o.price, () =>
            BuyWithRubis(offer.price, card, () => PlayerData.AddCoins(offer.amount), "+" + offer.amount + " pièces", Yellow, false));
        Area((RectTransform)buy.transform, 0.1f, 0.04f, 0.9f, 0.27f);
    }

    // ---------- Achats ----------

    void BuyReal(PremiumStore.Product p, RectTransform card)
    {
        MenuFx.Play(MenuFx.Sfx.Tick, 1.2f);
        var packCar = string.IsNullOrEmpty(p.carId) ? null : CarCatalog.Load().Find(p.carId);
        bool hadCar = packCar != null && packCar.IsUnlocked; // avant l'achat
        Action buy = () => PremiumStore.Buy(p, (ok, message) =>
        {
            if (!ok)
            {
                MenuFx.Play(MenuFx.Sfx.Error);
                FloatText(card, message, Red);
                return;
            }
            Celebrate(card, RubyRed);
            if (p.rubis > 0) FloatText(card, "+" + p.rubis + " rubis", RubyRed);
            if (packCar != null)
                StartCoroutine(FloatLater(card, hadCar ? "+" + p.carDuplicateRubis + " rubis (voiture déjà à toi)" : packCar.displayName + " débloquée !", Yellow, 0.5f));
            if (p.boosterCount > 0) StartCoroutine(FloatLater(card, "+" + p.boosterCount + " boosters offerts", Green, 1f));
            MenuFx.Play(MenuFx.Sfx.Fanfare);
            MenuFx.Vibrate();
            reservePunch = p.autopilots > 0 ? 1f : reservePunch;
        });

        if (PremiumStore.TestMode)
            AskConfirm("ACHAT DE TEST", p.title + "\n" + PremiumStore.PriceOf(p) + "\n\nMode test : aucun paiement réel.", buy);
        else buy();
    }

    void BuyWithRubis(int price, RectTransform card, Action grant, string gained, Color color, bool autopilot)
    {
        if (!PlayerData.TrySpendRubis(price, save: false))
        {
            // Pas assez : on le dit, et on remonte jusqu'aux packs de rubis
            MenuFx.Play(MenuFx.Sfx.Error);
            StartCoroutine(Shake(card));
            FloatText(card, "Il manque " + (price - PlayerData.Rubis) + " rubis", Red);
            ScrollTo(rubisSection);
            return;
        }
        grant(); // sauvegarde tout (rubis dépensés compris)
        MenuFx.Play(MenuFx.Sfx.Coin);
        MenuFx.Play(MenuFx.Sfx.Unlock, 1.1f, 0.6f);
        Celebrate(card, color);
        FloatText(card, gained, color);
        if (autopilot) reservePunch = 1f;
    }

    void ScrollTo(RectTransform section)
    {
        if (section == null || scroll == null) return;
        float max = Mathf.Max(0f, content.rect.height - scroll.viewport.rect.height);
        scrollTarget = Mathf.Clamp(-section.anchoredPosition.y - 20f, 0f, max);
    }

    void Celebrate(RectTransform card, Color color)
    {
        Vector2 at = LocalIn((RectTransform)particles.transform, card);
        particles.Burst(at, color, 36, 1100f, 26f);
        particles.Burst(at, Yellow, 14, 800f, 20f);
        StartCoroutine(Punch(card));
    }

    // ---------- Confirmation ----------

    void BuildConfirm(RectTransform parent)
    {
        var panel = Node("Confirmation", parent);
        Stretch(panel);
        Fill(panel, WithAlpha(Navy, 0.8f)).raycastTarget = true;
        confirm = panel.gameObject;

        var box = Card("Boîte", panel, Color.Lerp(Navy, Purple, 0.5f), 50f, 14f);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(860f, 640f);

        confirmTitle = FitLabel(box, "", 60, 30, TextAnchor.MiddleCenter, Yellow, 4f);
        Area(confirmTitle.rectTransform, 0.05f, 0.78f, 0.95f, 0.95f);
        confirmBody = FitLabel(box, "", 42, 22, TextAnchor.MiddleCenter, Color.white, 3f);
        Area(confirmBody.rectTransform, 0.06f, 0.33f, 0.94f, 0.78f);

        var row = Node("Boutons", box);
        Area(row, 0.06f, 0.06f, 0.94f, 0.28f);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 30f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        CandyButton(row, "ANNULER", new Color(0.45f, 0.45f, 0.55f), () => { MenuFx.Play(MenuFx.Sfx.Tick, 0.8f); confirm.SetActive(false); }, 46, 140f);
        CandyButton(row, "ACHETER", Green, () => { confirm.SetActive(false); confirmAction?.Invoke(); }, 46, 140f);

        confirm.SetActive(false);
    }

    void AskConfirm(string title, string body, Action onConfirm)
    {
        confirmTitle.text = title;
        confirmBody.text = body;
        confirmAction = onConfirm;
        confirm.transform.SetAsLastSibling();
        confirm.SetActive(true);
        StartCoroutine(Punch(confirm.transform.GetChild(0) as RectTransform));
    }

    /// <summary>Vrai si la boîte de confirmation est ouverte.</summary>
    public bool ConfirmOpen => confirm != null && confirm.activeSelf;

    /// <summary>Ferme la boîte de confirmation (bouton retour du téléphone).</summary>
    public void CloseConfirm()
    {
        if (confirm != null) confirm.SetActive(false);
    }

    // ---------- Outils ----------

    RectTransform Section(string title, string subtitle)
    {
        var rt = Node(title, content);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
        var t = FitLabel(rt, title, 56, 28, TextAnchor.LowerLeft, Color.white, 4f);
        Area(t.rectTransform, 0.02f, 0.4f, 0.98f, 1f);
        var s = FitLabel(rt, subtitle, 30, 16, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.7f), 2f);
        Area(s.rectTransform, 0.02f, 0f, 0.98f, 0.4f);
        return rt;
    }

    RectTransform Row(float height)
    {
        var rt = Node("Ligne", content);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 24f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        return rt;
    }

    void Badge(RectTransform card, string text, Color color, Vector2 anchor)
    {
        var b = Node("Étiquette", card);
        b.anchorMin = b.anchorMax = anchor;
        b.sizeDelta = new Vector2(Mathf.Clamp(text.Length * 22f + 40f, 140f, 360f), 64f);
        Rounded(b, color, 32f);
        Stretch(FitLabel(b, text, 30, 16, TextAnchor.MiddleCenter, Color.white, 2f).rectTransform);
        badges.Add(b);
        Isolate(b, false);
    }

    // Bouton de prix : en euros (rubisCost = 0) ou en rubis (avec un rubis dessiné)
    Button PriceButton(RectTransform parent, string price, Color color, int rubisCost, UnityAction onClick)
    {
        var b = CandyButton(parent, price, color, onClick, 46, 110f, 34f);
        b.GetComponent<LayoutElement>().ignoreLayout = true;
        var face = b.transform.Find("Face").GetComponent<Image>();
        var side = b.transform.Find("Relief").GetComponent<Image>();
        if (rubisCost > 0)
        {
            var gem = Ruby(face.transform, 60f);
            gem.anchorMin = gem.anchorMax = new Vector2(0f, 0.5f);
            gem.anchoredPosition = new Vector2(44f, 0f);
            ButtonText(b).rectTransform.offsetMin = new Vector2(70f, 4f);
        }
        else ButtonText(b).color = Ink; // prix en euros sur fond jaune
        var outline = ButtonText(b).GetComponent<Outline>();
        if (rubisCost <= 0 && outline != null) outline.effectColor = WithAlpha(Color.white, 0.4f);
        tags.Add(new PriceTag { button = b, face = face, side = side, rubisCost = rubisCost, color = color });
        return b;
    }

    void FloatText(RectTransform from, string text, Color color)
    {
        StartCoroutine(FloatTextRoutine(LocalIn(root, from) + new Vector2(0f, 60f), text, color));
    }

    IEnumerator FloatLater(RectTransform from, string text, Color color, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        FloatText(from, text, color);
    }

    IEnumerator FloatTextRoutine(Vector2 position, string text, Color color)
    {
        var label = Label(root, text, 46, TextAnchor.MiddleCenter, color, 3f);
        var rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(900f, 100f);
        for (float t = 0f; t < 1.1f; t += Time.unscaledDeltaTime)
        {
            float k = t / 1.1f;
            rt.anchoredPosition = position + new Vector2(0f, 120f * Mathf.Sqrt(k));
            rt.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(0.5f, 1.15f, k / 0.15f) : 1f);
            label.color = WithAlpha(color, 1f - Mathf.Clamp01((k - 0.6f) / 0.4f));
            yield return null;
        }
        Destroy(label.gameObject);
    }

    IEnumerator Punch(RectTransform rt)
    {
        for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
        {
            if (rt == null) yield break;
            rt.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(t / 0.3f * Mathf.PI));
            yield return null;
        }
        if (rt != null) rt.localScale = Vector3.one;
    }

    IEnumerator Shake(RectTransform rt)
    {
        Vector3 basePos = rt.localPosition;
        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            if (rt == null) yield break;
            rt.localPosition = basePos + new Vector3(Mathf.Sin(t * 70f) * 16f * (1f - t / 0.35f), 0f, 0f);
            yield return null;
        }
        if (rt != null) rt.localPosition = basePos;
    }

    static Vector2 LocalIn(RectTransform space, RectTransform target)
    {
        Vector3 world = target.TransformPoint(target.rect.center);
        Vector2 local = space.InverseTransformPoint(world);
        return local - space.rect.center;
    }
}
