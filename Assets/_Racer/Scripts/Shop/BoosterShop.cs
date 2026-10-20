using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static MenuStyle;

/// <summary>
/// Pages Invocations (boosters de blueprints) et Garage (voitures à débloquer) du menu principal.
/// L'UI est créée par code avec MenuStyle ; MainMenu ajoute ce composant tout seul et appelle Build().
///
/// - Invocations : les boosters sont présentés en grand, on passe de l'un à l'autre en glissant (BoosterCarousel).
///   Sous le booster : nom, nombre de blueprints, probabilités par rareté (barre + pourcentages) et bouton d'achat.
///   À l'achat : le booster tremble de plus en plus, explose, puis les cartes se retournent une par une
///   (suspense et effets plus forts pour les raretés élevées, tampon "DÉBLOQUÉE !", doublons convertis en pièces).
/// - Garage : toutes les voitures, triées par rareté, avec les blueprints obtenus / nécessaires,
///   le nombre de blueprints manquants, et le bouton Équiper pour les voitures débloquées.
///
/// Retours : sons et vibrations (MenuFx), particules (MenuParticles), animations partout.
/// Le contenu (voitures, boosters, prix, probabilités) vient de CarCatalog.
/// </summary>
public class BoosterShop : MonoBehaviour
{
    [Tooltip("Vide = Assets/_Racer/Resources/CarCatalog.asset, ou le catalogue par défaut s'il n'existe pas")]
    public CarCatalog catalog;
    [Tooltip("Délai entre deux cartes retournées à l'ouverture d'un booster (secondes)")]
    public float revealDelay = 0.45f;

    public GameObject ShopPanel { get; private set; }
    public GameObject InventoryPanel { get; private set; }
    /// <summary>Vrai pendant l'écran d'ouverture d'un booster.</summary>
    public bool OverlayOpen => (overlay != null && overlay.activeSelf) || (premium != null && premium.ConfirmOpen);

    Text garageCountText;
    RectTransform carGrid;
    GridLayoutGroup carGridLayout;

    // ---------- Données de la page Invocations ----------

    class Pack
    {
        public CarCatalog.Booster booster;
        public int tier;               // 0 = le moins cher
        public RectTransform root, rays, shine;
        public Image raysImg, haloImg;
        public CanvasGroup group;
        public float punch;            // rebond quand il arrive au centre ou qu'on le touche
        public float wiggle;
    }

    // Onglets Boosters / Boutique
    RectTransform boostersBody, boutiqueBody, boutiqueDot;
    PremiumShop premium;
    readonly List<(Image face, Image side, ButtonBounce bounce, Color color)> sectionTabs = new List<(Image, Image, ButtonBounce, Color)>();
    int section;
    float sectionPunch, boutiqueNudge;

    BoosterCarousel carousel;
    readonly List<Pack> packs = new List<Pack>();
    RectTransform carouselArea, packsRoot;
    MenuParticles pageParticles;
    float sparkleTimer;

    Text nameText, countText, taglineText;
    readonly RectTransform[] oddsSegments = new RectTransform[RarityInfo.Count];
    readonly Text[] oddsLabels = new Text[RarityInfo.Count];
    readonly float[] oddsShown = new float[RarityInfo.Count];
    readonly List<Image> dots = new List<Image>();
    RectTransform infoRoot;
    float infoPunch;

    Button buyButton;
    RectTransform buyRect;
    Image buyFace, buySide;
    Text buyText;
    float buyShake;
    ButtonBounce buyBounce;

    CarCatalog.Booster Selected => packs.Count > 0 ? packs[Mathf.Clamp(carousel.Current, 0, packs.Count - 1)].booster : null;

    // ---------- Données de l'écran d'ouverture ----------

    class RevealCard
    {
        public CarCatalog.Pull pull;
        public RectTransform root, back, front, rays;
        public Image raysImg, haloImg;
        public bool revealed, effectsDone; // effectsDone : effets (tampon, rayons...) déjà joués
    }

    GameObject overlay;
    Image overlayBg, flash;
    RectTransform stage, bigRays, packHolder, overlayCards, buttonsRow;
    Image bigRaysImg;
    MenuParticles overlayParticles;
    Text overlayTitle, summaryText, okLabel, againLabel;
    Button againButton;
    CanvasGroup buttonsGroup;
    CarCatalog.Booster lastBooster;
    Coroutine sequence;
    bool skip;
    float shake;
    readonly List<RevealCard> revealCards = new List<RevealCard>();

    /// <summary>
    /// Remplit la page Invocations et la page Garage (zones sous leur titre),
    /// et crée l'écran d'ouverture des boosters dans overlayParent (le canvas, pour passer devant tout).
    /// </summary>
    public void Build(RectTransform invocationsBody, RectTransform garageBody, RectTransform overlayParent)
    {
        if (catalog == null) catalog = CarCatalog.Load();

        BuildInvocations(invocationsBody);
        BuildGarage(garageBody);
        BuildOverlay(overlayParent);

        // Boutique (rubis, pilote auto, pièces) : voir PremiumShop
        premium = GetComponent<PremiumShop>();
        if (premium == null) premium = gameObject.AddComponent<PremiumShop>();
        premium.Build(boutiqueBody, overlayParent);

        PlayerData.OnChanged += Refresh;
        Refresh();
    }

    void OnDestroy() => PlayerData.OnChanged -= Refresh;

    void Refresh()
    {
        if (catalog == null) return;
        RefreshBuyButton();
        if (garageCountText != null)
            garageCountText.text = catalog.UnlockedCount + " / " + catalog.cars.Count + " voitures débloquées";
        RebuildGarage();
        RefreshAgainButton();
    }

    void LateUpdate()
    {
        // Garage : 2 colonnes de cartes qui remplissent la largeur de l'écran, quel que soit son format
        if (carGridLayout != null)
        {
            float w = (carGrid.rect.width - carGridLayout.spacing.x) * 0.5f;
            if (w > 1f && Mathf.Abs(carGridLayout.cellSize.x - w) > 0.5f)
                carGridLayout.cellSize = new Vector2(w, carGridLayout.cellSize.y);
        }

        float dt = Time.unscaledDeltaTime;
        if (sectionTabs.Count > 0) AnimateTabs(dt);
        if (carousel != null && boostersBody.gameObject.activeInHierarchy) AnimateInvocations(dt);
        if (OverlayOpen) AnimateOverlay(dt);
    }

    // ======================================================================
    // Invocations
    // ======================================================================

    void BuildInvocations(RectTransform page)
    {
        ShopPanel = page.gameObject;

        // Deux parties : les boosters (pièces) et la boutique (rubis, pilote auto)
        BuildSectionTabs(page);
        var body = Node("Boosters", page);
        Area(body, 0f, 0f, 1f, 0.905f);
        boostersBody = body;
        boutiqueBody = Node("Boutique", page);
        Area(boutiqueBody, 0f, 0f, 1f, 0.905f);
        boutiqueBody.gameObject.SetActive(false);

        // Carrousel des boosters (haut de la page)
        carouselArea = Node("Carrousel", body);
        Area(carouselArea, 0f, 0.36f, 1f, 1f);
        Fill(carouselArea, Color.clear).raycastTarget = true; // capte le glissé du doigt
        carouselArea.gameObject.AddComponent<RectMask2D>();   // les boosters voisins dépassent juste du bord
        carousel = carouselArea.gameObject.AddComponent<BoosterCarousel>();
        carousel.spacing = 430f; // les boosters voisins dépassent d'environ 180 px de chaque côté
        carousel.Count = catalog.boosters.Count;

        packsRoot = Node("Boosters", carouselArea);
        Stretch(packsRoot);
        for (int i = 0; i < catalog.boosters.Count; i++)
        {
            int index = i;
            var pack = new Pack { booster = catalog.boosters[i], tier = i };
            pack.root = PackVisual(packsRoot, pack.booster, i, out pack.rays, out pack.raysImg, out pack.haloImg, out pack.shine);
            pack.root.anchorMin = pack.root.anchorMax = new Vector2(0.5f, 0.5f);
            pack.root.sizeDelta = new Vector2(470f, 640f);
            pack.group = pack.root.gameObject.AddComponent<CanvasGroup>();
            // Toucher un booster : celui du centre fait un petit numéro, ceux des côtés viennent au centre
            var touch = pack.root.gameObject.AddComponent<Button>();
            touch.transition = Selectable.Transition.None;
            touch.onClick.AddListener(() => OnPackTapped(index));
            packs.Add(pack);
        }

        var sparkles = Node("Etincelles", carouselArea);
        Stretch(sparkles);
        pageParticles = sparkles.gameObject.AddComponent<MenuParticles>();

        // Points de pagination
        var dotsRow = Node("Pagination", body);
        Area(dotsRow, 0.3f, 0.335f, 0.7f, 0.365f);
        var dl = dotsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        dl.spacing = 26f;
        dl.childAlignment = TextAnchor.MiddleCenter;
        dl.childControlWidth = dl.childControlHeight = false;
        for (int i = 0; i < packs.Count; i++)
        {
            int index = i;
            var d = Node("Point", dotsRow);
            d.sizeDelta = new Vector2(30f, 30f);
            var img = Fill(d, Color.white);
            img.sprite = Round;
            img.raycastTarget = true;
            d.gameObject.AddComponent<Button>().onClick.AddListener(() => carousel.GoTo(index));
            dots.Add(img);
        }

        // Infos du booster sélectionné
        infoRoot = Node("Infos", body);
        Area(infoRoot, 0f, 0.1f, 1f, 0.335f);

        nameText = FitLabel(infoRoot, "", 70, 36, TextAnchor.MiddleCenter, Color.white, 4f);
        Area(nameText.rectTransform, 0.05f, 0.7f, 0.95f, 1f);

        var chip = Node("Blueprints", infoRoot);
        Area(chip, 0.25f, 0.5f, 0.75f, 0.7f);
        Rounded(chip, new Color(0f, 0f, 0f, 0.35f), 30f);
        countText = FitLabel(chip, "", 36, 20, TextAnchor.MiddleCenter, Yellow, 2f);
        Stretch(countText.rectTransform);

        taglineText = FitLabel(infoRoot, "", 32, 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f), 2f);
        Area(taglineText.rectTransform, 0.03f, 0.35f, 0.97f, 0.5f);

        // Barre des probabilités : un segment par rareté, proportionnel à sa chance
        var bar = Node("Probabilités", infoRoot);
        Area(bar, 0.04f, 0.2f, 0.96f, 0.32f);
        Rounded(bar, new Color(0f, 0f, 0f, 0.45f), 24f);
        var clip = Node("Segments", bar);
        Stretch(clip);
        clip.gameObject.AddComponent<RectMask2D>();
        for (int i = 0; i < RarityInfo.Count; i++)
        {
            var seg = Node(RarityInfo.Name((CarRarity)i), clip);
            Fill(seg, RarityInfo.Color((CarRarity)i));
            oddsSegments[i] = seg;
        }

        var labels = Node("Pourcentages", infoRoot);
        Area(labels, 0.02f, 0f, 0.98f, 0.19f);
        for (int i = 0; i < RarityInfo.Count; i++)
        {
            var l = FitLabel(labels, "", 30, 16, TextAnchor.MiddleCenter, RarityInfo.Color((CarRarity)i), 2f);
            Area(l.rectTransform, i / (float)RarityInfo.Count, 0f, (i + 1) / (float)RarityInfo.Count, 1f);
            oddsLabels[i] = l;
        }

        // Bouton d'achat (toujours cliquable : sans assez de pièces, il proteste)
        buyButton = CandyButton(body, "", Green, OnBuyClicked, 58, 150f);
        buyButton.GetComponent<LayoutElement>().ignoreLayout = true;
        buyRect = (RectTransform)buyButton.transform;
        Area(buyRect, 0.12f, 0f, 0.88f, 0.095f);
        buyFace = buyRect.Find("Face").GetComponent<Image>();
        buySide = buyRect.Find("Relief").GetComponent<Image>();
        var coin = Coin(buyFace.transform, 62f);
        coin.anchorMin = coin.anchorMax = new Vector2(0f, 0.5f);
        coin.anchoredPosition = new Vector2(60f, 0f);
        buyText = ButtonText(buyButton);
        buyBounce = buyButton.GetComponent<ButtonBounce>();
        buyText.rectTransform.offsetMin = new Vector2(100f, 4f);


        carousel.OnChanged += i =>
        {
            MenuFx.Play(MenuFx.Sfx.Tick, 1f + 0.12f * i);
            packs[i].punch = 1f;
            infoPunch = 1f;
            ShowInfo(i);
        };
        carousel.GoTo(0, true);
        ShowInfo(0);
    }

    // ---------- Onglets Boosters / Boutique ----------

    void BuildSectionTabs(RectTransform page)
    {
        var row = Node("Onglets", page);
        Area(row, 0.02f, 0.915f, 0.98f, 1f);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 24f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;

        AddSectionTab(row, "BOOSTERS", Purple, 0);
        var boutique = AddSectionTab(row, "BOUTIQUE", RubyRed, 1);
        var gem = Ruby(boutique.Find("Face"), 60f);
        gem.anchorMin = gem.anchorMax = new Vector2(0f, 0.5f);
        gem.anchoredPosition = new Vector2(46f, 0f);
        ButtonText(boutique.GetComponent<Button>()).rectTransform.offsetMin = new Vector2(70f, 4f);

        // Pastille "!" tant que le pack de démarrage n'a pas été pris
        boutiqueDot = Node("Nouveau", boutique);
        boutiqueDot.anchorMin = boutiqueDot.anchorMax = new Vector2(0.95f, 0.85f);
        boutiqueDot.sizeDelta = new Vector2(54f, 54f);
        Fill(boutiqueDot, Yellow).sprite = Round;
        Stretch(Label(boutiqueDot, "!", 40, TextAnchor.MiddleCenter, Ink, 0f).rectTransform);
    }

    RectTransform AddSectionTab(RectTransform row, string label, Color color, int index)
    {
        var b = CandyButton(row, label, color, () => ShowSection(index), 40, 100f, 34f);
        var rt = (RectTransform)b.transform;
        sectionTabs.Add((rt.Find("Face").GetComponent<Image>(), rt.Find("Relief").GetComponent<Image>(),
                         b.GetComponent<ButtonBounce>(), color));
        return rt;
    }

    void ShowSection(int index)
    {
        if (index == section) return;
        section = index;
        sectionPunch = 1f;
        MenuFx.Play(MenuFx.Sfx.Whoosh, 1.3f, 0.6f);
        boostersBody.gameObject.SetActive(index == 0);
        boutiqueBody.gameObject.SetActive(index == 1);
        if (index == 1) premium.ScrollToTop();
    }

    /// <summary>Affiche la boutique (rubis, pilote auto). Appelé par la pastille des rubis du menu.</summary>
    public void ShowBoutique() => ShowSection(1);

    void AnimateTabs(float dt)
    {
        sectionPunch = Mathf.MoveTowards(sectionPunch, 0f, dt * 4f);
        boutiqueNudge = Mathf.MoveTowards(boutiqueNudge, 0f, dt);
        for (int i = 0; i < sectionTabs.Count; i++)
        {
            var tab = sectionTabs[i];
            bool active = i == section;
            Color face = active ? tab.color : Color.Lerp(Navy, tab.color, 0.35f);
            tab.face.color = face;
            tab.side.color = Darken(face, 0.6f);
            tab.bounce.extraScale = active ? 1f + 0.08f * Mathf.Sin(sectionPunch * Mathf.PI) : 0.94f;
            if (i == 1 && !active) tab.bounce.extraScale += 0.1f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 12f)) * Mathf.Min(1f, boutiqueNudge);
        }
        if (boutiqueDot != null)
        {
            bool show = !PremiumStore.IsSoldOut(PremiumStore.StarterPack) && section != 1;
            if (boutiqueDot.gameObject.activeSelf != show) boutiqueDot.gameObject.SetActive(show);
            boutiqueDot.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f));
        }
    }

    void ShowInfo(int index)
    {
        var b = catalog.boosters[index];
        nameText.text = b.displayName.ToUpper();
        nameText.color = Color.Lerp(b.color, Color.white, 0.25f);
        countText.text = b.cards + " BLUEPRINTS";
        taglineText.text = b.tagline;
        for (int i = 0; i < RarityInfo.Count; i++)
        {
            float p = b.Percent((CarRarity)i);
            oddsLabels[i].text = RarityInfo.Name((CarRarity)i) + "\n" + FormatPercent(p);
            oddsLabels[i].color = p > 0f ? RarityInfo.Color((CarRarity)i) : new Color(1f, 1f, 1f, 0.25f);
        }
        RefreshBuyButton();
    }

    void RefreshBuyButton()
    {
        var b = Selected;
        if (b == null || buyText == null) return;
        bool canPay = PlayerData.Coins >= b.price;
        buyText.text = "OUVRIR  " + b.price;
        buyFace.color = canPay ? Green : new Color(0.45f, 0.45f, 0.55f);
        buySide.color = Darken(buyFace.color, 0.62f);
    }

    static string FormatPercent(float p) => p <= 0f ? "0%" : p.ToString("0.#") + "%";

    void OnPackTapped(int index)
    {
        if (carousel.Dragging) return;
        if (index != carousel.Current)
        {
            carousel.GoTo(index);
            return;
        }
        // Booster du centre : il sautille et brille
        var p = packs[index];
        p.wiggle = 1f;
        p.punch = 1f;
        pageParticles.Burst(p.root.anchoredPosition, p.booster.color, 14, 600f, 20f, -600f);
        MenuFx.Play(MenuFx.Sfx.Pop, 1.1f + 0.1f * index, 0.7f);
    }

    void OnBuyClicked()
    {
        var b = Selected;
        if (b == null || sequence != null) return;

        if (PlayerData.Coins < b.price)
        {
            // Pas assez de pièces : le bouton tremble et dit combien il manque
            buyShake = 1f;
            MenuFx.Play(MenuFx.Sfx.Error);
            FloatText(ShopPanel.transform as RectTransform, LocalIn(ShopPanel.transform as RectTransform, buyRect, new Vector2(0f, 120f)),
                      "Il manque " + (b.price - PlayerData.Coins) + " pièces", Red, 44);
            boutiqueNudge = 2f; // l'onglet Boutique sautille : on peut y obtenir des pièces
            return;
        }
        Open(b);
    }

    void AnimateInvocations(float dt)
    {
        float t = Time.unscaledTime;
        float pos = carousel.Position;

        // Placement des boosters : celui du centre en grand, les voisins plus petits, inclinés et plus sombres
        for (int i = 0; i < packs.Count; i++)
        {
            var p = packs[i];
            float d = i - pos;
            float ad = Mathf.Min(Mathf.Abs(d), 1.5f);
            float w = 1f - Mathf.Min(ad, 1f); // 1 au centre

            p.punch = Mathf.MoveTowards(p.punch, 0f, dt * 3.5f);
            p.wiggle = Mathf.MoveTowards(p.wiggle, 0f, dt * 1.8f);
            float punch = Mathf.Sin(p.punch * Mathf.PI) * 0.12f;
            float wiggle = Mathf.Sin(t * 30f) * 9f * p.wiggle;

            float scale = Mathf.Lerp(1f, 0.68f, Mathf.Min(ad, 1f)) * (1f + punch);
            float bob = Mathf.Sin(t * 2.2f + i) * 14f * w;
            p.root.anchoredPosition = new Vector2(d * carousel.spacing, bob);
            p.root.localScale = Vector3.one * scale;
            p.root.localRotation = Quaternion.Euler(0f, 0f, -d * 9f + Mathf.Sin(t * 1.4f + i) * 2.5f * w + wiggle);
            p.group.alpha = Mathf.Lerp(1f, 0.5f, Mathf.Min(ad, 1f));

            // Halo qui respire, rayons qui tournent (plus forts pour les boosters chers), reflet qui passe
            p.haloImg.color = WithAlpha(p.booster.color, (0.22f + 0.1f * Mathf.Sin(t * 3f + i)) * (0.3f + 0.7f * w));
            p.rays.localRotation = Quaternion.Euler(0f, 0f, t * (12f + 8f * p.tier));
            p.raysImg.color = WithAlpha(Color.Lerp(p.booster.color, Color.white, 0.3f), (0.18f + 0.14f * p.tier) * w);
            float phase = Mathf.Repeat(t / (3.2f - 0.5f * p.tier) + i * 0.37f, 1f);
            p.shine.anchoredPosition = new Vector2(Mathf.Lerp(-520f, 520f, Mathf.SmoothStep(0f, 1f, phase / 0.4f)), 0f);
        }

        // Le booster le plus proche du centre passe devant les autres
        var order = new List<Pack>(packs);
        order.Sort((a, b) => Mathf.Abs(b.tier - pos).CompareTo(Mathf.Abs(a.tier - pos)));
        for (int i = 0; i < order.Count; i++) order[i].root.SetSiblingIndex(i);

        // Étincelles autour du booster du centre (plus nombreuses pour les boosters chers)
        if (!OverlayOpen && packs.Count > 0)
        {
            var center = packs[carousel.Current];
            sparkleTimer -= dt * (5f + 5f * center.tier);
            while (sparkleTimer < 0f)
            {
                sparkleTimer += 1f;
                var r = UnityEngine.Random.insideUnitCircle;
                var at = center.root.anchoredPosition + new Vector2(r.x * 300f, r.y * 380f);
                pageParticles.Sparkle(at, Random01() < 0.5f ? Color.white : Color.Lerp(center.booster.color, Color.white, 0.4f), 18f);
            }
        }

        // Points de pagination
        for (int i = 0; i < dots.Count; i++)
        {
            float k = Mathf.Clamp01(1f - Mathf.Abs(i - pos));
            dots[i].rectTransform.localScale = Vector3.one * (1f + 0.6f * k);
            dots[i].color = Color.Lerp(new Color(1f, 1f, 1f, 0.3f), Color.Lerp(packs[i].booster.color, Color.white, 0.2f), k);
        }

        // Infos : petit rebond à chaque changement, barre des probabilités qui glisse vers les nouvelles valeurs
        infoPunch = Mathf.MoveTowards(infoPunch, 0f, dt * 4f);
        nameText.rectTransform.localScale = Vector3.one * (1f + 0.18f * Mathf.Sin(infoPunch * Mathf.PI));
        var sel = Selected;
        float x0 = 0f;
        for (int i = 0; i < RarityInfo.Count; i++)
        {
            float target = sel != null ? sel.Percent((CarRarity)i) / 100f : 0f;
            oddsShown[i] = Mathf.Lerp(oddsShown[i], target, 1f - Mathf.Exp(-12f * dt));
            var seg = oddsSegments[i];
            seg.anchorMin = new Vector2(x0, 0f);
            seg.anchorMax = new Vector2(x0 + oddsShown[i], 1f);
            seg.offsetMin = seg.offsetMax = Vector2.zero;
            x0 += oddsShown[i];
        }

        // Bouton d'achat : respire s'il est utilisable, tremble s'il refuse
        buyShake = Mathf.MoveTowards(buyShake, 0f, dt * 2.5f);
        bool canPay = sel != null && PlayerData.Coins >= sel.price;
        buyBounce.extraScale = canPay ? 1f + Mathf.Sin(t * 4f) * 0.03f : 1f;
        buyFace.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(t * 70f) * 24f * buyShake, buyFace.rectTransform.anchoredPosition.y);
    }

    static float Random01() => UnityEngine.Random.value;

    // ---------- Dessin d'un booster ----------

    /// <summary>Booster dessiné par code : halo, rayons, paquet coloré avec cristal, étoiles, nom et nombre de cartes.</summary>
    RectTransform PackVisual(Transform parent, CarCatalog.Booster b, int tier, out RectTransform rays, out Image raysImg,
                             out Image haloImg, out RectTransform shine)
    {
        Color c = b.color;
        var root = Node(b.id, parent);

        rays = Node("Rayons", root);
        rays.anchorMin = rays.anchorMax = new Vector2(0.5f, 0.5f);
        rays.sizeDelta = new Vector2(1100f, 1100f);
        raysImg = Fill(rays, WithAlpha(c, 0.3f));
        raysImg.sprite = MenuFx.Rays;

        var halo = Node("Halo", root);
        Area(halo, -0.22f, -0.15f, 1.22f, 1.15f);
        haloImg = Fill(halo, WithAlpha(c, 0.25f));
        haloImg.sprite = Round;

        var body = Card("Paquet", root, Darken(c, 0.5f), 60f, 18f);
        Stretch(body);
        var face = body.Find("Fond");

        // Dégradé clair vers le haut
        var light = Node("Lumière", face);
        Area(light, 0f, 0.3f, 1f, 1f);
        light.localScale = new Vector3(1f, -1f, 1f);
        var lightImg = Fill(light, WithAlpha(Color.Lerp(c, Color.white, 0.3f), 0.85f));
        lightImg.sprite = GradientSprite;

        // Zone découpée pour le reflet qui balaie le paquet
        var clip = Node("Reflets", face);
        Stretch(clip);
        clip.offsetMin = new Vector2(10f, 10f);
        clip.offsetMax = new Vector2(-10f, -10f);
        clip.gameObject.AddComponent<RectMask2D>();
        for (int i = 0; i < 3; i++)
        {
            var stripe = Node("Bande", clip);
            stripe.anchorMin = stripe.anchorMax = new Vector2(0.5f, 0.5f);
            stripe.sizeDelta = new Vector2(26f, 1500f);
            stripe.anchoredPosition = new Vector2(-200f + i * 170f, 0f);
            stripe.localRotation = Quaternion.Euler(0f, 0f, -28f);
            Fill(stripe, WithAlpha(Color.white, 0.06f));
        }
        shine = Node("Brillance", clip);
        shine.anchorMin = shine.anchorMax = new Vector2(0.5f, 0.5f);
        shine.sizeDelta = new Vector2(110f, 1500f);
        shine.localRotation = Quaternion.Euler(0f, 0f, -28f);
        Rounded(shine, WithAlpha(Color.white, 0.28f), 40f);

        // Bandeau scellé en haut
        var seal = Node("Scellé", face);
        Area(seal, 0f, 0.86f, 1f, 0.95f);
        Fill(seal, new Color(0f, 0f, 0f, 0.3f));
        Stretch(FitLabel(seal, "BOOSTER", 36, 20, TextAnchor.MiddleCenter, WithAlpha(Color.white, 0.9f), 2f).rectTransform);

        // Cristal au centre
        var crystal = Node("Cristal", face);
        crystal.anchorMin = crystal.anchorMax = new Vector2(0.5f, 0.57f);
        crystal.sizeDelta = new Vector2(200f, 200f);
        crystal.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Rounded(crystal, Darken(c, 0.35f), 40f);
        var gem = Node("Gemme", crystal);
        Stretch(gem);
        gem.offsetMin = new Vector2(18f, 18f);
        gem.offsetMax = new Vector2(-18f, -18f);
        Rounded(gem, c, 30f);
        var gemLight = Node("Facette", gem);
        Area(gemLight, 0f, 0.5f, 0.5f, 1f);
        Rounded(gemLight, WithAlpha(Color.white, 0.35f), 20f);
        var glint = Node("Reflet", gem);
        Area(glint, 0.15f, 0.6f, 0.4f, 0.85f);
        Rounded(glint, WithAlpha(Color.white, 0.8f), 12f);

        // Étoiles de rang (petits losanges)
        int stars = tier + 1;
        for (int i = 0; i < stars; i++)
        {
            var s = Node("Étoile", face);
            s.anchorMin = s.anchorMax = new Vector2(0.5f, 0.33f);
            s.sizeDelta = new Vector2(38f, 38f);
            s.anchoredPosition = new Vector2((i - (stars - 1) * 0.5f) * 58f, 0f);
            s.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Rounded(s, Yellow, 8f);
        }

        // Nom (sans le mot "Booster")
        string shortName = b.displayName.Replace("Booster", "").Trim().ToUpper();
        var nameLabel = FitLabel(face, shortName, 84, 40, TextAnchor.MiddleCenter, Color.white, 5f);
        Area(nameLabel.rectTransform, 0.05f, 0.08f, 0.95f, 0.26f);

        // Pastille du nombre de cartes
        var badge = Node("Cartes", root);
        badge.anchorMin = badge.anchorMax = new Vector2(0.88f, 0.9f);
        badge.sizeDelta = new Vector2(130f, 130f);
        badge.localRotation = Quaternion.Euler(0f, 0f, 12f);
        Fill(badge, Darken(Yellow, 0.7f)).sprite = Round;
        var badgeIn = Node("Centre", badge);
        Area(badgeIn, 0.08f, 0.08f, 0.92f, 0.92f);
        Fill(badgeIn, Yellow).sprite = Round;
        Stretch(Label(badgeIn, "x" + b.cards, 56, TextAnchor.MiddleCenter, Ink, 0f).rectTransform);

        return root;
    }

    // ======================================================================
    // Ouverture d'un booster
    // ======================================================================

    void BuildOverlay(RectTransform parent)
    {
        var panel = Node("BoosterOpening", parent);
        Stretch(panel);
        overlayBg = Fill(panel, WithAlpha(Navy, 0.95f));
        overlayBg.raycastTarget = true; // bloque les clics et glissés vers le menu derrière
        overlay = panel.gameObject;

        stage = Node("Scène", panel);
        Stretch(stage);

        bigRays = Node("Rayons", stage);
        bigRays.anchorMin = bigRays.anchorMax = new Vector2(0.5f, 0.55f);
        bigRays.sizeDelta = new Vector2(1700f, 1700f);
        bigRaysImg = Fill(bigRays, Color.clear);
        bigRaysImg.sprite = MenuFx.Rays;

        packHolder = Node("Booster", stage);
        packHolder.anchorMin = packHolder.anchorMax = new Vector2(0.5f, 0.55f);
        packHolder.sizeDelta = new Vector2(500f, 680f);

        overlayCards = Node("Cartes", stage);
        overlayCards.anchorMin = overlayCards.anchorMax = new Vector2(0.5f, 0.53f);
        overlayCards.sizeDelta = new Vector2(920f, 880f);
        var grid = overlayCards.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(280f, 410f);
        grid.spacing = new Vector2(28f, 36f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.MiddleCenter;

        var fx = Node("Particules", panel);
        Stretch(fx);
        overlayParticles = fx.gameObject.AddComponent<MenuParticles>();

        overlayTitle = Label(panel, "", 84, TextAnchor.MiddleCenter, Yellow, 5f);
        Place(overlayTitle.rectTransform, 0.88f, 140f);

        summaryText = FitLabel(panel, "", 44, 24, TextAnchor.MiddleCenter, Color.white, 3f);
        Area(summaryText.rectTransform, 0.05f, 0.18f, 0.95f, 0.24f);

        buttonsRow = Node("Boutons", panel);
        buttonsRow.anchorMin = new Vector2(0.06f, 0.1f);
        buttonsRow.anchorMax = new Vector2(0.94f, 0.1f);
        buttonsRow.sizeDelta = new Vector2(0f, 160f);
        buttonsGroup = buttonsRow.gameObject.AddComponent<CanvasGroup>();
        var layout = buttonsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 36f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;

        againButton = CandyButton(buttonsRow, "", Purple, () => { if (lastBooster != null) Again(); }, 46, 160f);
        againLabel = ButtonText(againButton);
        var ok = CandyButton(buttonsRow, "OK", Green, CloseOrSkip, 52, 160f);
        okLabel = ButtonText(ok);

        flash = Fill(Node("Flash", panel), Color.clear);
        Stretch(flash.rectTransform);

        overlay.SetActive(false);
    }

    void Again()
    {
        if (sequence != null) return;
        if (PlayerData.Coins < lastBooster.price)
        {
            MenuFx.Play(MenuFx.Sfx.Error);
            FloatText(overlay.transform as RectTransform, LocalIn(overlay.transform as RectTransform, (RectTransform)againButton.transform, new Vector2(0f, 130f)),
                      "Il manque " + (lastBooster.price - PlayerData.Coins) + " pièces", Red, 44);
            return;
        }
        Open(lastBooster);
    }

    void Open(CarCatalog.Booster booster)
    {
        if (sequence != null) return;
        var pulls = catalog.OpenBooster(booster);
        if (pulls == null) return; // pas assez de pièces

        lastBooster = booster;
        MenuFx.Play(MenuFx.Sfx.Coin);
        sequence = StartCoroutine(Sequence(booster, pulls));
    }

    IEnumerator Sequence(CarCatalog.Booster booster, List<CarCatalog.Pull> pulls)
    {
        skip = false;
        int tier = catalog.boosters.IndexOf(booster);
        int best = 0;
        foreach (var p in pulls) best = Mathf.Max(best, (int)p.rarity);
        Color bestColor = RarityInfo.Color((CarRarity)best);

        // --- Préparation ---
        overlay.transform.SetAsLastSibling();
        overlay.SetActive(true);
        overlayTitle.text = booster.displayName.ToUpper();
        overlayTitle.color = Color.Lerp(booster.color, Color.white, 0.25f);
        summaryText.text = "";
        okLabel.text = "TOUT VOIR";
        againButton.gameObject.SetActive(false);
        buttonsGroup.alpha = 0f;
        flash.color = Color.clear;
        bigRaysImg.color = Color.clear;

        foreach (Transform child in overlayCards) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        foreach (Transform child in packHolder) Destroy(child.gameObject);
        revealCards.Clear();

        var pack = PackVisual(packHolder, booster, Mathf.Max(0, tier), out _, out _, out var packHalo, out _);
        Stretch(pack);
        packHolder.gameObject.SetActive(true);
        packHolder.localScale = Vector3.zero;
        packHolder.localRotation = Quaternion.identity;

        // --- Le booster arrive ---
        MenuFx.Play(MenuFx.Sfx.Whoosh);
        yield return Tween(0.25f, k => overlayBg.color = WithAlpha(Navy, 0.95f * k));
        yield return Tween(0.35f, k => packHolder.localScale = Vector3.one * Overshoot(k));
        yield return Wait(0.2f);
        buttonsGroup.alpha = 1f; // "TOUT VOIR" disponible

        // --- Il se charge : tremble de plus en plus, la lueur prend la couleur de la meilleure carte (suspense) ---
        float charge = 0.8f + 0.3f * best;
        float nextTick = 0f;
        for (float t = 0f; t < charge && !skip; t += Time.unscaledDeltaTime)
        {
            float k = t / charge;
            packHolder.anchoredPosition = UnityEngine.Random.insideUnitCircle * (4f + 26f * k * k);
            packHolder.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 50f) * 6f * k);
            packHolder.localScale = Vector3.one * (1f + 0.12f * k);
            Color glow = k > 0.5f ? Color.Lerp(booster.color, bestColor, (k - 0.5f) * 2f) : booster.color;
            packHalo.color = WithAlpha(glow, 0.3f + 0.5f * k);
            bigRaysImg.color = WithAlpha(glow, 0.5f * k);
            if (t >= nextTick)
            {
                MenuFx.Play(MenuFx.Sfx.Tick, 0.8f + 1.2f * k, 0.8f);
                nextTick = t + Mathf.Lerp(0.18f, 0.05f, k);
            }
            yield return null;
        }

        // --- Explosion ---
        packHolder.gameObject.SetActive(false);
        packHolder.anchoredPosition = Vector2.zero;
        Color flashColor = Color.Lerp(Color.white, bestColor, 0.25f);
        if (!skip)
        {
            MenuFx.Play(MenuFx.Sfx.Pop, 0.8f);
            if (best >= (int)CarRarity.Epique) MenuFx.Play(MenuFx.Sfx.Reveal, 0.8f);
            if (best >= (int)CarRarity.Legendaire) MenuFx.Vibrate();
            Vector2 center = LocalIn(overlayParticles.transform as RectTransform, packHolder, Vector2.zero);
            overlayParticles.Burst(center, booster.color, 40 + 15 * best, 1500f, 30f);
            overlayParticles.Burst(center, bestColor, 15 + 15 * best, 1100f, 24f);
            shake = 1f;
        }
        bigRaysImg.color = WithAlpha(bestColor, 0.35f);
        StartCoroutine(Flash(flashColor, 0.45f));

        // --- Les cartes apparaissent, face cachée ---
        foreach (var p in pulls) revealCards.Add(BuildRevealCard(overlayCards, booster, p));
        foreach (var c in revealCards) c.root.localScale = Vector3.zero;
        foreach (var c in revealCards)
        {
            if (!skip) MenuFx.Play(MenuFx.Sfx.Tick, 1.4f, 0.6f);
            var card = c;
            StartCoroutine(Tween(0.3f, k => card.root.localScale = Vector3.one * Overshoot(k)));
            yield return Wait(0.07f);
        }
        yield return Wait(0.35f);

        // --- On les retourne une par une ---
        foreach (var c in revealCards)
        {
            int r = (int)c.pull.rarity;
            if (r >= (int)CarRarity.Epique && !skip)
            {
                // Suspense : la carte tremble et s'illumine avant de se retourner
                c.haloImg.color = WithAlpha(RarityInfo.Color(c.pull.rarity), 0f);
                for (float t = 0f; t < 0.25f + 0.15f * r && !skip; t += Time.unscaledDeltaTime)
                {
                    c.root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 60f) * 5f);
                    c.haloImg.color = WithAlpha(RarityInfo.Color(c.pull.rarity), Mathf.Min(0.8f, t * 2f));
                    yield return null;
                }
                c.root.localRotation = Quaternion.identity;
            }

            yield return Flip(c);
            yield return Wait(revealDelay * (r >= (int)CarRarity.Epique ? 1.4f : 1f));
        }

        // --- Fin ---
        if (skip)
        {
            foreach (var c in revealCards) FinishCard(c);
            MenuFx.Play(MenuFx.Sfx.Reveal, 1f + 0.1f * best);
        }

        summaryText.text = Summary(pulls);
        okLabel.text = "OK";
        againButton.gameObject.SetActive(true);
        RefreshAgainButton();
        StartCoroutine(Tween(0.3f, k => buttonsRow.anchoredPosition = new Vector2(0f, -60f * (1f - Overshoot(k)))));
        sequence = null;
    }

    IEnumerator Flip(RevealCard c)
    {
        var card = c;
        if (!skip) MenuFx.Play(MenuFx.Sfx.Whoosh, 1.4f, 0.5f);
        yield return Tween(0.12f, k => card.root.localScale = new Vector3(1f - k, 1f + 0.08f * k, 1f));
        card.back.gameObject.SetActive(false);
        card.front.gameObject.SetActive(true);
        card.revealed = true;
        yield return Tween(0.16f, k => card.root.localScale = new Vector3(k, 1.08f - 0.08f * k, 1f));
        card.root.localScale = Vector3.one;
        if (!skip) RevealEffects(card);
    }

    // Effets à la révélation d'une carte : plus la rareté est haute, plus c'est fort
    void RevealEffects(RevealCard c)
    {
        c.effectsDone = true;
        int r = (int)c.pull.rarity;
        Color rc = RarityInfo.Color(c.pull.rarity);
        Vector2 at = LocalIn(overlayParticles.transform as RectTransform, c.root, Vector2.zero);

        overlayParticles.Burst(at, rc, 10 + 14 * r, 700f + 250f * r, 20f);
        StartCoroutine(Punch(c.root, 0.25f + 0.08f * r));

        if (r >= (int)CarRarity.Legendaire)
        {
            MenuFx.Play(MenuFx.Sfx.Fanfare);
            MenuFx.Vibrate();
            shake = 0.8f;
            StartCoroutine(Flash(WithAlpha(rc, 0.6f), 0.35f));
        }
        else MenuFx.Play(MenuFx.Sfx.Reveal, 1f + 0.12f * r, 0.6f + 0.15f * r);

        if (r >= (int)CarRarity.Epique)
        {
            c.rays.gameObject.SetActive(true);
            c.haloImg.color = WithAlpha(rc, 0.6f);
        }

        if (c.pull.unlockedNow)
        {
            MenuFx.Play(MenuFx.Sfx.Unlock);
            MenuFx.Vibrate();
            StartCoroutine(Stamp(c.root));
            overlayParticles.Burst(at, Yellow, 30, 1200f, 26f);
        }
        else if (c.pull.duplicateCoins > 0)
        {
            MenuFx.Play(MenuFx.Sfx.Coin, 1.1f, 0.7f);
            FloatText(overlay.transform as RectTransform, LocalIn(overlay.transform as RectTransform, c.root, new Vector2(0f, 230f)),
                      "+" + c.pull.duplicateCoins, Yellow, 56);
        }
    }

    // Fin de séquence en accéléré : cartes retournées, sans effets
    void FinishCard(RevealCard c)
    {
        c.root.localScale = Vector3.one;
        c.root.localRotation = Quaternion.identity;
        c.back.gameObject.SetActive(false);
        c.front.gameObject.SetActive(true);
        c.revealed = true;
        if (c.effectsDone) return;
        c.effectsDone = true;
        if (c.pull.rarity >= CarRarity.Epique)
        {
            c.rays.gameObject.SetActive(true);
            c.haloImg.color = WithAlpha(RarityInfo.Color(c.pull.rarity), 0.6f);
        }
        if (c.pull.unlockedNow) StartCoroutine(Stamp(c.root));
    }

    string Summary(List<CarCatalog.Pull> pulls)
    {
        int blueprints = 0, coins = 0;
        var unlocked = new List<string>();
        foreach (var p in pulls)
        {
            if (p.duplicateCoins > 0) coins += p.duplicateCoins;
            else blueprints++;
            if (p.unlockedNow) unlocked.Add(p.car.displayName);
        }
        var parts = new List<string>();
        if (blueprints > 0) parts.Add("+" + blueprints + (blueprints > 1 ? " blueprints" : " blueprint"));
        if (coins > 0) parts.Add("+" + coins + " pièces");
        if (unlocked.Count > 0) parts.Add("<color=#FFD23F>" + string.Join(", ", unlocked) + " débloquée" + (unlocked.Count > 1 ? "s" : "") + " !</color>");
        return string.Join("   •   ", parts);
    }

    void AnimateOverlay(float dt)
    {
        float t = Time.unscaledTime;
        bigRays.localRotation = Quaternion.Euler(0f, 0f, t * 14f);

        shake = Mathf.MoveTowards(shake, 0f, dt * 3f);
        stage.anchoredPosition = shake > 0f ? UnityEngine.Random.insideUnitCircle * 30f * shake * shake : Vector2.zero;

        foreach (var c in revealCards)
        {
            if (c.root == null) continue;
            if (c.rays.gameObject.activeSelf) c.rays.localRotation = Quaternion.Euler(0f, 0f, t * 40f);
            if (c.revealed && c.pull.rarity >= CarRarity.Epique)
                c.haloImg.color = WithAlpha(RarityInfo.Color(c.pull.rarity), 0.45f + 0.2f * Mathf.Sin(t * 5f));
        }
    }

    // OK : pendant l'animation, tout se révèle d'un coup ; sinon ferme l'écran
    void CloseOrSkip()
    {
        if (sequence != null)
        {
            skip = true;
            return;
        }
        MenuFx.Play(MenuFx.Sfx.Pop, 1.2f, 0.6f);
        overlay.SetActive(false);
    }

    /// <summary>Ferme l'écran d'ouverture (bouton retour du téléphone).</summary>
    public void CloseOverlay()
    {
        if (premium != null && premium.ConfirmOpen) { premium.CloseConfirm(); return; }
        if (overlay == null || !overlay.activeSelf) return;
        if (sequence != null) { skip = true; return; }
        overlay.SetActive(false);
    }

    void RefreshAgainButton()
    {
        if (againButton == null || lastBooster == null) return;
        againLabel.text = "ENCORE  " + lastBooster.price;
        bool canPay = PlayerData.Coins >= lastBooster.price;
        var face = againButton.transform.Find("Face").GetComponent<Image>();
        face.color = canPay ? Purple : new Color(0.45f, 0.45f, 0.55f);
        againButton.transform.Find("Relief").GetComponent<Image>().color = Darken(face.color, 0.62f);
    }

    // ---------- Cartes ----------

    RevealCard BuildRevealCard(RectTransform parent, CarCatalog.Booster booster, CarCatalog.Pull pull)
    {
        var car = pull.car;
        Color rc = RarityInfo.Color(pull.rarity);
        var c = new RevealCard { pull = pull };

        c.root = Node(car.id, parent);

        // Derrière la carte : rayons (Épique et Légendaire) et halo
        c.rays = Node("Rayons", c.root);
        c.rays.anchorMin = c.rays.anchorMax = new Vector2(0.5f, 0.5f);
        c.rays.sizeDelta = new Vector2(620f, 620f);
        c.raysImg = Fill(c.rays, WithAlpha(rc, 0.75f));
        c.raysImg.sprite = MenuFx.Rays;
        c.rays.gameObject.SetActive(false);
        var halo = Node("Halo", c.root);
        Area(halo, -0.18f, -0.12f, 1.18f, 1.12f);
        c.haloImg = Fill(halo, Color.clear);
        c.haloImg.sprite = Round;

        // Dos : couleurs du booster et point d'interrogation
        c.back = Card("Dos", c.root, Darken(booster.color, 0.55f), 36f, 10f);
        Stretch(c.back);
        var backFace = c.back.Find("Fond");
        var frame = Node("Cadre", backFace);
        Stretch(frame);
        frame.offsetMin = new Vector2(16f, 16f);
        frame.offsetMax = new Vector2(-16f, -16f);
        Rounded(frame, WithAlpha(booster.color, 0.35f), 26f);
        var diamond = Node("Losange", backFace);
        diamond.anchorMin = diamond.anchorMax = new Vector2(0.5f, 0.5f);
        diamond.sizeDelta = new Vector2(150f, 150f);
        diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Rounded(diamond, booster.color, 30f);
        Stretch(Label(backFace, "?", 110, TextAnchor.MiddleCenter, Color.white, 5f).rectTransform);

        // Face : la voiture tirée
        c.front = Card("Face", c.root, Color.Lerp(Navy, rc, 0.5f), 36f, 10f);
        Stretch(c.front);
        var outline = c.front.Find("Fond").gameObject.AddComponent<Outline>();
        outline.effectColor = rc;
        outline.effectDistance = new Vector2(6f, -6f);

        var tag = Node("Rareté", c.front);
        Area(tag, 0.08f, 0.85f, 0.92f, 0.96f);
        Rounded(tag, rc, 22f);
        Stretch(FitLabel(tag, RarityInfo.Name(pull.rarity).ToUpper(), 30, 16, TextAnchor.MiddleCenter, Color.white, 2f).rectTransform);

        var nameLabel = FitLabel(c.front, car.displayName, 40, 22, TextAnchor.MiddleCenter, Color.white, 3f);
        Area(nameLabel.rectTransform, 0.05f, 0.62f, 0.95f, 0.83f);

        var swatch = Node("Couleur", c.front);
        swatch.anchorMin = swatch.anchorMax = new Vector2(0.5f, 0.47f);
        swatch.sizeDelta = new Vector2(120f, 120f);
        Fill(swatch, Color.white).sprite = Round;
        var inner = Node("Teinte", swatch);
        Area(inner, 0.1f, 0.1f, 0.9f, 0.9f);
        Fill(inner, car.color).sprite = Round;

        string status;
        Color statusColor = Color.white;
        if (pull.unlockedNow) { status = "NOUVELLE\nVOITURE !"; statusColor = Yellow; }
        else if (pull.duplicateCoins > 0) status = "Doublon\n+" + pull.duplicateCoins + " pièces";
        else status = "+1 blueprint\n" + pull.blueprints + " / " + car.blueprintsToUnlock;
        var st = FitLabel(c.front, status, 34, 18, TextAnchor.MiddleCenter, statusColor, 3f);
        Area(st.rectTransform, 0.05f, 0.04f, 0.95f, 0.3f);

        c.front.gameObject.SetActive(false);
        return c;
    }

    // ---------- Petites animations ----------

    IEnumerator Tween(float duration, Action<float> step)
    {
        for (float t = 0f; t < duration && !skip; t += Time.unscaledDeltaTime)
        {
            step(Mathf.Clamp01(t / duration));
            yield return null;
        }
        step(1f);
    }

    IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds && !skip; t += Time.unscaledDeltaTime) yield return null;
    }

    // 0 -> 1 en dépassant un peu (effet "pop")
    static float Overshoot(float k)
    {
        k = Mathf.Clamp01(k);
        const float s = 1.9f;
        k -= 1f;
        return k * k * ((s + 1f) * k + s) + 1f;
    }

    IEnumerator Flash(Color color, float duration)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            flash.color = WithAlpha(color, color.a * (1f - t / duration));
            yield return null;
        }
        flash.color = Color.clear;
    }

    IEnumerator Punch(RectTransform rt, float amount)
    {
        for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
        {
            if (rt == null) yield break;
            rt.localScale = Vector3.one * (1f + amount * Mathf.Sin(t / 0.3f * Mathf.PI) * (1f - t / 0.3f));
            yield return null;
        }
        if (rt != null) rt.localScale = Vector3.one;
    }

    // Tampon "DÉBLOQUÉE !" qui s'écrase sur la carte
    IEnumerator Stamp(RectTransform card)
    {
        var stamp = Node("Tampon", card);
        stamp.anchorMin = stamp.anchorMax = new Vector2(0.5f, 0.42f);
        stamp.sizeDelta = new Vector2(330f, 96f);
        stamp.localRotation = Quaternion.Euler(0f, 0f, -14f);
        Rounded(stamp, Red, 30f);
        var inner = Node("Bord", stamp);
        Stretch(inner);
        inner.offsetMin = new Vector2(8f, 8f);
        inner.offsetMax = new Vector2(-8f, -8f);
        Rounded(inner, Darken(Red, 0.75f), 24f);
        Stretch(FitLabel(stamp, "DÉBLOQUÉE !", 44, 24, TextAnchor.MiddleCenter, Yellow, 3f).rectTransform);

        for (float t = 0f; t < 0.22f; t += Time.unscaledDeltaTime)
        {
            if (stamp == null) yield break;
            stamp.localScale = Vector3.one * Mathf.Lerp(2.6f, 1f, t / 0.22f);
            yield return null;
        }
        if (stamp != null) stamp.localScale = Vector3.one;
        shake = Mathf.Max(shake, 0.5f);
    }

    // Texte qui monte et s'efface (pièces gagnées, pièces manquantes...)
    void FloatText(RectTransform parent, Vector2 position, string text, Color color, int size)
    {
        if (parent == null) return;
        StartCoroutine(FloatTextRoutine(parent, position, text, color, size));
    }

    IEnumerator FloatTextRoutine(RectTransform parent, Vector2 position, string text, Color color, int size)
    {
        var label = Label(parent, text, size, TextAnchor.MiddleCenter, color, 3f);
        var rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(900f, 100f);
        for (float t = 0f; t < 1.1f; t += Time.unscaledDeltaTime)
        {
            float k = t / 1.1f;
            rt.anchoredPosition = position + new Vector2(0f, 110f * Mathf.Sqrt(k));
            rt.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(0.5f, 1.15f, k / 0.15f) : 1f);
            label.color = WithAlpha(color, 1f - Mathf.Clamp01((k - 0.6f) / 0.4f));
            yield return null;
        }
        Destroy(label.gameObject);
    }

    // Position (locale à "space", centrée) d'un point de "target" décalé de offset
    static Vector2 LocalIn(RectTransform space, RectTransform target, Vector2 offset)
    {
        if (space == null || target == null) return offset;
        Vector3 world = target.TransformPoint(target.rect.center);
        Vector2 local = space.InverseTransformPoint(world);
        return local - space.rect.center + offset;
    }

    // ======================================================================
    // Garage
    // ======================================================================

    // ---------- Garage ----------

    void BuildGarage(RectTransform body)
    {
        InventoryPanel = body.gameObject;

        var pill = Node("Compteur", body);
        Area(pill, 0.12f, 0.925f, 0.88f, 1f);
        Rounded(pill, new Color(0f, 0f, 0f, 0.35f), 40f);
        garageCountText = FitLabel(pill, "", 40, 24, TextAnchor.MiddleCenter, Color.white, 2f);
        Stretch(garageCountText.rectTransform);

        carGrid = VerticalScroll(body, 0f, 0f, 1f, 0.9f);
        var grid = carGridLayout = carGrid.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(430f, 420f); // largeur ajustée à l'écran dans LateUpdate
        grid.spacing = new Vector2(28f, 36f);
        grid.padding = new RectOffset(0, 0, 16, 30);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;
        carGrid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    void RebuildGarage()
    {
        if (carGrid == null) return;
        foreach (Transform child in carGrid)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        // Débloquées d'abord, puis par rareté, puis par progression
        var cars = new List<CarCatalog.Car>(catalog.cars);
        cars.Sort((a, b) =>
        {
            int c = b.IsUnlocked.CompareTo(a.IsUnlocked);
            if (c != 0) return c;
            c = a.rarity.CompareTo(b.rarity);
            if (c != 0) return c;
            return a.Missing.CompareTo(b.Missing);
        });

        var equipped = catalog.Equipped;
        foreach (var car in cars) CarCard(carGrid, car, car == equipped);
    }

    void CarCard(RectTransform parent, CarCatalog.Car car, bool isEquipped)
    {
        Color rc = RarityInfo.Color(car.rarity);
        bool unlocked = car.IsUnlocked;

        var card = Card(car.id, parent, unlocked ? Color.Lerp(Navy, rc, 0.45f) : Color.Lerp(Navy, Color.black, 0.3f), 40f, 10f);
        if (isEquipped)
        {
            var glow = card.Find("Fond").gameObject.AddComponent<Outline>();
            glow.effectColor = Yellow;
            glow.effectDistance = new Vector2(6f, -6f);
        }

        // Étiquette de rareté
        var tag = Node("Rareté", card);
        Area(tag, 0.05f, 0.83f, 0.6f, 0.95f);
        Rounded(tag, unlocked ? rc : Darken(rc, 0.5f), 24f);
        var tagText = FitLabel(tag, RarityInfo.Name(car.rarity).ToUpper(), 30, 18, TextAnchor.MiddleCenter, Color.white, 2f);
        Stretch(tagText.rectTransform);

        // Aperçu de la couleur de la voiture (assombri tant qu'elle est verrouillée)
        var swatch = Node("Couleur", card);
        swatch.anchorMin = swatch.anchorMax = new Vector2(0.8f, 0.82f);
        swatch.sizeDelta = new Vector2(100f, 100f);
        var ring = Fill(swatch, Color.white);
        ring.sprite = Round;
        var inner = Node("Teinte", swatch);
        Area(inner, 0.1f, 0.1f, 0.9f, 0.9f);
        Color cc = car.color;
        Fill(inner, unlocked ? cc : new Color(cc.r * 0.25f, cc.g * 0.25f, cc.b * 0.25f, 1f)).sprite = Round;
        if (!unlocked) Stretch(Label(inner, "?", 56, TextAnchor.MiddleCenter, Color.white).rectTransform);

        var nameText = FitLabel(card, car.displayName, 44, 24, TextAnchor.MiddleLeft, unlocked ? Color.white : new Color(0.75f, 0.75f, 0.8f), 3f);
        Area(nameText.rectTransform, 0.06f, 0.56f, 0.94f, 0.78f);

        // Barre de blueprints
        var bar = Node("Blueprints", card);
        Area(bar, 0.06f, 0.37f, 0.94f, 0.52f);
        Rounded(bar, new Color(0f, 0f, 0f, 0.4f), 30f);
        float ratio = car.blueprintsToUnlock > 0 ? car.Blueprints / (float)car.blueprintsToUnlock : 1f;
        if (ratio > 0f)
        {
            var fill = Node("Fill", bar);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Rounded(fill, unlocked ? Green : rc, 30f);
        }
        var barText = FitLabel(bar, car.Blueprints + " / " + car.blueprintsToUnlock, 32, 18, TextAnchor.MiddleCenter, Color.white, 2f);
        Stretch(barText.rectTransform);

        if (unlocked)
        {
            var b = CandyButton(card, isEquipped ? "ÉQUIPÉE" : "ÉQUIPER", isEquipped ? Yellow : Green, () => Equip(car), 42, 120f, 32f);
            b.GetComponent<LayoutElement>().ignoreLayout = true;
            Area((RectTransform)b.transform, 0.06f, 0.05f, 0.94f, 0.31f);
            b.interactable = !isEquipped;
            if (isEquipped)
            {
                // Bouton désactivé mais en couleur : c'est la voiture choisie
                var colors = b.colors;
                colors.disabledColor = Color.white;
                b.colors = colors;
            }
        }
        else
        {
            int m = car.Missing;
            string missing = "Manque " + m + (m > 1 ? " blueprints" : " blueprint");
            var t = FitLabel(card, missing, 36, 20, TextAnchor.MiddleCenter, Orange, 3f);
            Area(t.rectTransform, 0.04f, 0.06f, 0.96f, 0.3f);
        }
    }

    void Equip(CarCatalog.Car car)
    {
        PlayerData.EquippedCarId = car.id;
        // La voiture de la démo en fond prend tout de suite la nouvelle couleur (si elle a un GarageCarApplier)
        foreach (var applier in FindObjectsByType<GarageCarApplier>(FindObjectsSortMode.None))
            applier.Apply();
    }
}
