using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Shop de boosters + Garage (inventaire des voitures à débloquer avec leurs blueprints).
/// L'UI est créée par code, dans le même style que MainMenu, qui ajoute ce composant tout seul et appelle Build().
///
/// - Boosters : chaque booster a un prix, un nombre de blueprints et des probabilités par rareté.
///   À l'achat, les cartes sont révélées une par une (voiture, rareté, progression, déblocage ou doublon).
/// - Garage : toutes les voitures, triées par rareté, avec les blueprints obtenus / nécessaires,
///   le nombre de blueprints manquants, et le bouton Équiper pour les voitures débloquées.
///
/// Le contenu (voitures, boosters, prix, probabilités) vient de CarCatalog.
/// </summary>
public class BoosterShop : MonoBehaviour
{
    [Tooltip("Vide = Assets/Resources/CarCatalog.asset, ou le catalogue par défaut s'il n'existe pas")]
    public CarCatalog catalog;
    [Tooltip("Délai entre l'apparition de deux cartes à l'ouverture d'un booster (secondes)")]
    public float revealDelay = 0.4f;

    public GameObject ShopPanel { get; private set; }
    public GameObject InventoryPanel { get; private set; }

    Font font;
    Color buttonColor, accent;

    Text shopCoinsText, garageCountText;
    readonly List<(Button button, CarCatalog.Booster booster)> buyButtons = new List<(Button, CarCatalog.Booster)>();
    RectTransform carGrid;
    GridLayoutGroup carGridLayout;

    // Écran d'ouverture d'un booster
    GameObject overlay;
    Text overlayTitle, againLabel;
    RectTransform overlayCards;
    Button againButton;
    CarCatalog.Booster lastBooster;
    Coroutine reveal;
    readonly List<RectTransform> revealCards = new List<RectTransform>();

    /// <summary>Crée les panneaux Boosters et Garage (cachés) dans le canvas du menu.</summary>
    public void Build(RectTransform canvas, Font font, Color buttonColor, Color accent,
                      UnityAction showMain, UnityAction showShop, UnityAction showGarage)
    {
        if (catalog == null) catalog = CarCatalog.Load();
        this.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        this.buttonColor = buttonColor;
        this.accent = accent;

        BuildShop(canvas, showMain, showGarage);
        BuildGarage(canvas, showMain, showShop);
        BuildOverlay(canvas);

        PlayerData.OnChanged += Refresh;
        Refresh();
    }

    void OnDestroy() => PlayerData.OnChanged -= Refresh;

    // 2 colonnes de cartes qui remplissent la largeur de l'écran, quel que soit son format
    void LateUpdate()
    {
        if (carGridLayout == null || !InventoryPanel.activeInHierarchy) return;
        float w = (carGrid.rect.width - carGridLayout.spacing.x) * 0.5f;
        if (w > 1f && Mathf.Abs(carGridLayout.cellSize.x - w) > 0.5f)
            carGridLayout.cellSize = new Vector2(w, carGridLayout.cellSize.y);
    }

    void Refresh()
    {
        if (catalog == null) return;

        string coins = PlayerData.Coins + " pièces";
        if (shopCoinsText != null) shopCoinsText.text = coins;
        foreach (var (button, booster) in buyButtons)
            button.interactable = PlayerData.Coins >= booster.price;

        if (garageCountText != null)
            garageCountText.text = catalog.UnlockedCount + " / " + catalog.cars.Count + " voitures débloquées";
        RebuildGarage();
        RefreshAgainButton();
    }

    // ---------- Shop ----------

    void BuildShop(RectTransform canvas, UnityAction showMain, UnityAction showGarage)
    {
        var panel = Panel("BoosterShopPanel", canvas, 0.78f);
        ShopPanel = panel.gameObject;

        Place(Label(panel, "Boosters", 96, TextAnchor.MiddleCenter, accent).rectTransform, 0.915f, 150f);
        shopCoinsText = Label(panel, "", 54, TextAnchor.MiddleCenter, Color.white);
        Place(shopCoinsText.rectTransform, 0.855f, 80f);

        var list = Scroll(panel, 0.17f, 0.82f);
        var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 28f;
        layout.padding = new RectOffset(0, 0, 10, 10);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        buyButtons.Clear();
        foreach (var booster in catalog.boosters)
            BoosterCard(list, booster);

        var row = Row(panel, 0.08f);
        MakeButton(row, "Garage", showGarage, buttonColor);
        if (Application.isEditor || Debug.isDebugBuild)
            MakeButton(row, "+500", () => PlayerData.AddCoins(500), new Color(0.15f, 0.4f, 0.15f, 0.8f));
        MakeButton(row, "Retour", showMain, buttonColor);

        ShopPanel.SetActive(false);
    }

    void BoosterCard(RectTransform parent, CarCatalog.Booster booster)
    {
        var card = NewRect(booster.displayName, parent);
        card.gameObject.AddComponent<LayoutElement>().preferredHeight = 310f;
        card.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        var stripe = NewRect("Stripe", card);
        stripe.anchorMin = new Vector2(0f, 0f);
        stripe.anchorMax = new Vector2(0f, 1f);
        stripe.sizeDelta = new Vector2(20f, 0f);
        stripe.anchoredPosition = new Vector2(10f, 0f);
        stripe.gameObject.AddComponent<Image>().color = booster.color;

        Area(Label(card, booster.displayName, 58, TextAnchor.MiddleLeft, booster.color).rectTransform, 0.05f, 0.64f, 0.7f, 0.95f);
        Area(Label(card, booster.cards + " blueprints", 40, TextAnchor.MiddleLeft, Color.white).rectTransform, 0.05f, 0.44f, 0.7f, 0.64f);

        // Probabilités, chaque rareté dans sa couleur
        var odds = new System.Text.StringBuilder();
        for (int i = 0; i < RarityInfo.Count; i++)
        {
            var r = (CarRarity)i;
            float p = booster.Percent(r);
            if (i == 2) odds.Append('\n');
            else if (i > 0) odds.Append("    ");
            odds.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(RarityInfo.Color(r))).Append('>')
                .Append(RarityInfo.Name(r)).Append(' ').Append(FormatPercent(p)).Append("</color>");
        }
        var oddsText = Label(card, odds.ToString(), 32, TextAnchor.MiddleLeft, Color.white);
        oddsText.supportRichText = true;
        Area(oddsText.rectTransform, 0.05f, 0.06f, 0.72f, 0.44f);

        var buy = MakeButton(card, booster.price + "\npièces", () => Open(booster), accent);
        buy.GetComponent<LayoutElement>().ignoreLayout = true;
        Area(buy.GetComponent<RectTransform>(), 0.73f, 0.18f, 0.97f, 0.82f);
        buy.GetComponentInChildren<Text>().fontSize = 46;
        buyButtons.Add((buy, booster));
    }

    static string FormatPercent(float p)
    {
        return p <= 0f ? "0%" : p.ToString("0.#") + "%";
    }

    // ---------- Garage ----------

    void BuildGarage(RectTransform canvas, UnityAction showMain, UnityAction showShop)
    {
        var panel = Panel("GaragePanel", canvas, 0.78f);
        InventoryPanel = panel.gameObject;

        Place(Label(panel, "Garage", 96, TextAnchor.MiddleCenter, accent).rectTransform, 0.915f, 150f);
        garageCountText = Label(panel, "", 46, TextAnchor.MiddleCenter, Color.white);
        Place(garageCountText.rectTransform, 0.855f, 80f);

        carGrid = Scroll(panel, 0.17f, 0.82f);
        var grid = carGridLayout = carGrid.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(430f, 380f); // largeur ajustée à l'écran dans LateUpdate
        grid.spacing = new Vector2(24f, 24f);
        grid.padding = new RectOffset(0, 0, 10, 10);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;
        carGrid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var row = Row(panel, 0.08f);
        MakeButton(row, "Boosters", showShop, accent);
        MakeButton(row, "Retour", showMain, buttonColor);

        InventoryPanel.SetActive(false);
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

        var card = NewRect(car.id, parent);
        card.gameObject.AddComponent<Image>().color = unlocked
            ? new Color(rc.r * 0.35f, rc.g * 0.35f, rc.b * 0.35f, 0.92f)
            : new Color(0.07f, 0.07f, 0.09f, 0.9f);

        var stripe = NewRect("Stripe", card);
        Area(stripe, 0f, 0.96f, 1f, 1f);
        stripe.gameObject.AddComponent<Image>().color = unlocked ? rc : rc * 0.6f;

        // Aperçu de la couleur de la voiture (assombri tant qu'elle est verrouillée)
        var swatch = NewRect("Couleur", card);
        Area(swatch, 0.74f, 0.62f, 0.94f, 0.9f);
        Color cc = car.color;
        swatch.gameObject.AddComponent<Image>().color = unlocked ? cc : new Color(cc.r * 0.3f, cc.g * 0.3f, cc.b * 0.3f, 1f);
        if (!unlocked) Stretch(Label(swatch, "?", 60, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.6f)).rectTransform);

        var nameText = Label(card, car.displayName, 42, TextAnchor.MiddleLeft, unlocked ? Color.white : new Color(0.8f, 0.8f, 0.8f));
        nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
        nameText.resizeTextForBestFit = true;
        nameText.resizeTextMinSize = 26;
        nameText.resizeTextMaxSize = 42;
        Area(nameText.rectTransform, 0.06f, 0.74f, 0.72f, 0.93f);
        Area(Label(card, RarityInfo.Name(car.rarity), 32, TextAnchor.MiddleLeft, rc).rectTransform, 0.06f, 0.6f, 0.72f, 0.74f);

        // Barre de blueprints
        var bar = NewRect("Blueprints", card);
        Area(bar, 0.06f, 0.36f, 0.94f, 0.52f);
        bar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
        var fill = NewRect("Fill", bar);
        float ratio = car.blueprintsToUnlock > 0 ? car.Blueprints / (float)car.blueprintsToUnlock : 1f;
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        fill.gameObject.AddComponent<Image>().color = rc;
        Stretch(Label(bar, car.Blueprints + " / " + car.blueprintsToUnlock + " blueprints", 30, TextAnchor.MiddleCenter, Color.white).rectTransform);

        if (unlocked)
        {
            var b = MakeButton(card, isEquipped ? "Équipée" : "Équiper", () => PlayerData.EquippedCarId = car.id,
                           isEquipped ? new Color(0.2f, 0.2f, 0.2f, 0.8f) : accent);
            b.GetComponent<LayoutElement>().ignoreLayout = true;
            Area(b.GetComponent<RectTransform>(), 0.06f, 0.05f, 0.94f, 0.29f);
            b.GetComponentInChildren<Text>().fontSize = 40;
            b.interactable = !isEquipped;
        }
        else
        {
            int m = car.Missing;
            string missing = "Manque " + m + (m > 1 ? " blueprints" : " blueprint");
            Area(Label(card, missing, 36, TextAnchor.MiddleCenter, accent).rectTransform, 0.04f, 0.05f, 0.96f, 0.3f);
        }
    }

    // ---------- Ouverture d'un booster ----------

    void BuildOverlay(RectTransform canvas)
    {
        var panel = Panel("BoosterOpening", canvas, 0.9f);
        panel.GetComponent<Image>().raycastTarget = true; // bloque les clics vers le menu derrière
        overlay = panel.gameObject;

        overlayTitle = Label(panel, "", 80, TextAnchor.MiddleCenter, accent);
        Place(overlayTitle.rectTransform, 0.84f, 130f);

        overlayCards = NewRect("Cartes", panel);
        overlayCards.anchorMin = overlayCards.anchorMax = new Vector2(0.5f, 0.5f);
        overlayCards.sizeDelta = new Vector2(900f, 830f);
        var grid = overlayCards.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(280f, 400f);
        grid.spacing = new Vector2(24f, 24f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.MiddleCenter;

        var row = Row(panel, 0.12f);
        againButton = MakeButton(row, "", () => { if (lastBooster != null) Open(lastBooster); }, accent);
        againLabel = againButton.GetComponentInChildren<Text>();
        MakeButton(row, "OK", CloseOrSkip, buttonColor);

        overlay.SetActive(false);
    }

    void Open(CarCatalog.Booster booster)
    {
        if (reveal != null) return;
        var pulls = catalog.OpenBooster(booster);
        if (pulls == null) return; // pas assez de pièces

        lastBooster = booster;
        overlay.transform.SetAsLastSibling();
        overlay.SetActive(true);
        overlayTitle.text = booster.displayName;
        overlayTitle.color = booster.color;

        foreach (Transform child in overlayCards)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        revealCards.Clear();
        foreach (var pull in pulls) revealCards.Add(PullCard(overlayCards, pull));

        reveal = StartCoroutine(Reveal());
        RefreshAgainButton();
    }

    IEnumerator Reveal()
    {
        foreach (var c in revealCards) c.localScale = Vector3.zero;

        foreach (var c in revealCards)
        {
            yield return new WaitForSecondsRealtime(revealDelay);
            // Petit "pop" : 0 -> 1.15 -> 1
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.25f)
            {
                float s = t < 0.6f ? Mathf.Lerp(0f, 1.15f, t / 0.6f) : Mathf.Lerp(1.15f, 1f, (t - 0.6f) / 0.4f);
                if (c != null) c.localScale = Vector3.one * s;
                yield return null;
            }
            if (c != null) c.localScale = Vector3.one;
        }
        reveal = null;
        RefreshAgainButton();
    }

    // OK : pendant l'animation, révèle tout d'un coup ; sinon ferme l'écran
    void CloseOrSkip()
    {
        if (reveal != null)
        {
            StopCoroutine(reveal);
            reveal = null;
            foreach (var c in revealCards) if (c != null) c.localScale = Vector3.one;
            RefreshAgainButton();
            return;
        }
        overlay.SetActive(false);
    }

    void RefreshAgainButton()
    {
        if (againButton == null) return;
        bool show = lastBooster != null;
        againButton.gameObject.SetActive(show);
        if (!show) return;
        againLabel.text = "Encore (" + lastBooster.price + ")";
        againButton.interactable = reveal == null && PlayerData.Coins >= lastBooster.price;
    }

    RectTransform PullCard(RectTransform parent, CarCatalog.Pull pull)
    {
        var car = pull.car;
        Color rc = RarityInfo.Color(pull.rarity);

        var card = NewRect(car.id, parent);
        card.gameObject.AddComponent<Image>().color = new Color(rc.r * 0.3f, rc.g * 0.3f, rc.b * 0.3f, 0.97f);
        var outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = rc;
        outline.effectDistance = new Vector2(5f, -5f);

        Area(Label(card, RarityInfo.Name(pull.rarity).ToUpper(), 30, TextAnchor.MiddleCenter, rc).rectTransform, 0.04f, 0.84f, 0.96f, 0.96f);
        var nameText = Label(card, car.displayName, 40, TextAnchor.MiddleCenter, Color.white);
        nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
        Area(nameText.rectTransform, 0.04f, 0.6f, 0.96f, 0.84f);

        var swatch = NewRect("Couleur", card);
        Area(swatch, 0.25f, 0.36f, 0.75f, 0.58f);
        swatch.gameObject.AddComponent<Image>().color = car.color;

        string status;
        Color statusColor = Color.white;
        if (pull.unlockedNow) { status = "DÉBLOQUÉE !"; statusColor = accent; }
        else if (pull.duplicateCoins > 0) status = "Doublon\n+" + pull.duplicateCoins + " pièces";
        else status = "+1 blueprint\n" + pull.blueprints + " / " + car.blueprintsToUnlock;
        var st = Label(card, status, 34, TextAnchor.MiddleCenter, statusColor);
        st.horizontalOverflow = HorizontalWrapMode.Wrap;
        Area(st.rectTransform, 0.04f, 0.04f, 0.96f, 0.34f);
        return card;
    }

    // ---------- Petits outils d'UI (même style que MainMenu) ----------

    static RectTransform NewRect(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt) => Area(rt, 0f, 0f, 1f, 1f);

    static void Area(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
    {
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
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

    static RectTransform Panel(string name, Transform parent, float dim)
    {
        var rt = NewRect(name, parent);
        Stretch(rt);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, dim);
        img.raycastTarget = false;
        return rt;
    }

    // Zone qui défile verticalement entre yMin et yMax ; renvoie le contenu (à remplir)
    static RectTransform Scroll(Transform parent, float yMin, float yMax)
    {
        var view = NewRect("Scroll", parent);
        Area(view, 0.04f, yMin, 0.96f, yMax);
        view.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // capte le glissé du doigt
        view.gameObject.AddComponent<RectMask2D>();

        var content = NewRect("Content", view);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;

        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = view;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;
        return content;
    }

    static RectTransform Row(Transform parent, float y)
    {
        var rt = NewRect("Boutons", parent);
        rt.anchorMin = new Vector2(0.04f, y);
        rt.anchorMax = new Vector2(0.96f, y);
        rt.sizeDelta = new Vector2(0f, 140f);
        var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 30f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        return rt;
    }

    Text Label(Transform parent, string content, int size, TextAnchor anchor, Color color)
    {
        var rt = NewRect("Text", parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = content;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var shadow = rt.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
        shadow.effectDistance = new Vector2(3f, -3f);
        return t;
    }

    Button MakeButton(Transform parent, string label, UnityAction onClick, Color color)
    {
        var rt = NewRect(label, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 140f;
        Stretch(Label(rt, label, 52, TextAnchor.MiddleCenter, Color.white).rectTransform);
        return button;
    }
}
