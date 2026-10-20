using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static MenuStyle;

/// <summary>
/// Pages Invocations (boosters de blueprints) et Garage (voitures à débloquer) du menu principal.
/// L'UI est créée par code avec MenuStyle ; MainMenu ajoute ce composant tout seul et appelle Build().
///
/// - Invocations : chaque booster a un prix, un nombre de blueprints et des probabilités par rareté.
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
    /// <summary>Vrai pendant l'écran d'ouverture d'un booster.</summary>
    public bool OverlayOpen => overlay != null && overlay.activeSelf;

    Text garageCountText;
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

        PlayerData.OnChanged += Refresh;
        Refresh();
    }

    void OnDestroy() => PlayerData.OnChanged -= Refresh;

    // 2 colonnes de cartes qui remplissent la largeur de l'écran, quel que soit son format
    void LateUpdate()
    {
        if (carGridLayout == null) return;
        float w = (carGrid.rect.width - carGridLayout.spacing.x) * 0.5f;
        if (w > 1f && Mathf.Abs(carGridLayout.cellSize.x - w) > 0.5f)
            carGridLayout.cellSize = new Vector2(w, carGridLayout.cellSize.y);
    }

    void Refresh()
    {
        if (catalog == null) return;

        foreach (var (button, booster) in buyButtons)
            button.interactable = PlayerData.Coins >= booster.price;

        if (garageCountText != null)
            garageCountText.text = catalog.UnlockedCount + " / " + catalog.cars.Count + " voitures débloquées";
        RebuildGarage();
        RefreshAgainButton();
    }

    // ---------- Invocations ----------

    void BuildInvocations(RectTransform body)
    {
        ShopPanel = body.gameObject;

        bool debug = Application.isEditor || Debug.isDebugBuild;
        var list = VerticalScroll(body, 0f, 0f, 1f, debug ? 0.9f : 1f);
        var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 36f;
        layout.padding = new RectOffset(6, 6, 16, 30);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        buyButtons.Clear();
        foreach (var booster in catalog.boosters)
            BoosterCard(list, booster);

        // Pièces de test (éditeur et builds de développement seulement)
        if (debug)
        {
            var row = Node("Test", body);
            Area(row, 0.62f, 0.915f, 1f, 1f);
            var add = CandyButton(row, "+500 (test)", Blue, () => PlayerData.AddCoins(500), 36, 90f, 16f);
            Stretch((RectTransform)add.transform);
        }
    }

    void BoosterCard(RectTransform parent, CarCatalog.Booster booster)
    {
        Color bc = booster.color;
        var card = Card(booster.displayName, parent, Color.Lerp(Navy, bc, 0.4f), 24f, 12f);
        var rim = card.Find("Fond").gameObject.AddComponent<Outline>(); // liseré blanc
        rim.effectColor = WithAlpha(Color.white, 0.9f);
        rim.effectDistance = new Vector2(4f, -4f);
        card.gameObject.AddComponent<LayoutElement>().preferredHeight = 360f;

        // Liseré clair en haut de la carte
        var band = Node("Bande", card);
        Area(band, 0.03f, 0.86f, 0.97f, 0.93f);
        Rounded(band, WithAlpha(Color.white, 0.12f), 20f);

        // Cristal d'invocation : losange aux couleurs du booster
        var crystal = Node("Cristal", card);
        crystal.anchorMin = crystal.anchorMax = new Vector2(0.13f, 0.5f);
        crystal.sizeDelta = new Vector2(130f, 130f);
        crystal.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Rounded(crystal, Darken(bc, 0.6f), 26f);
        var gem = Node("Gemme", crystal);
        Stretch(gem);
        gem.offsetMin = new Vector2(12f, 12f);
        gem.offsetMax = new Vector2(-12f, -12f);
        Rounded(gem, bc, 18f);
        var glint = Node("Reflet", gem);
        Area(glint, 0.15f, 0.55f, 0.45f, 0.85f);
        Rounded(glint, WithAlpha(Color.white, 0.6f), 10f);

        var title = FitLabel(card, booster.displayName, 58, 30, TextAnchor.MiddleLeft, Color.white, 4f);
        Area(title.rectTransform, 0.26f, 0.6f, 0.68f, 0.86f);
        var count = Label(card, booster.cards + " blueprints", 38, TextAnchor.MiddleLeft, Yellow);
        Area(count.rectTransform, 0.26f, 0.44f, 0.68f, 0.6f);

        // Probabilités, chaque rareté dans sa couleur
        var odds = new System.Text.StringBuilder();
        for (int i = 0; i < RarityInfo.Count; i++)
        {
            var r = (CarRarity)i;
            if (i == 2) odds.Append('\n');
            else if (i > 0) odds.Append("   ");
            odds.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(RarityInfo.Color(r))).Append('>')
                .Append(RarityInfo.Name(r)).Append(' ').Append(FormatPercent(booster.Percent(r))).Append("</color>");
        }
        var oddsText = Label(card, odds.ToString(), 29, TextAnchor.MiddleLeft, Color.white, 2f);
        Area(oddsText.rectTransform, 0.26f, 0.08f, 0.7f, 0.44f);

        var buy = PriceButton(card, booster.price, Yellow, () => Open(booster));
        Area((RectTransform)buy.transform, 0.71f, 0.2f, 0.97f, 0.8f);
        buyButtons.Add((buy, booster));
    }

    // Bouton avec une pièce et le prix
    static Button PriceButton(Transform parent, int price, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var buy = CandyButton(parent, price.ToString(), color, onClick, 56);
        buy.GetComponent<LayoutElement>().ignoreLayout = true;
        var face = buy.transform.Find("Face");
        var coin = Coin(face, 56f);
        coin.anchorMin = coin.anchorMax = new Vector2(0f, 0.5f);
        coin.anchoredPosition = new Vector2(48f, 0f);
        var text = ButtonText(buy);
        text.rectTransform.offsetMin = new Vector2(80f, 4f);
        return buy;
    }

    static string FormatPercent(float p)
    {
        return p <= 0f ? "0%" : p.ToString("0.#") + "%";
    }

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

        var card = Card(car.id, parent, unlocked ? Color.Lerp(Navy, rc, 0.45f) : Color.Lerp(Navy, Color.black, 0.3f), 24f, 10f);
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
            var b = CandyButton(card, isEquipped ? "ÉQUIPÉE" : "ÉQUIPER", isEquipped ? Yellow : Blue, () => Equip(car), 42, 120f, 16f);
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

    // ---------- Ouverture d'un booster ----------

    void BuildOverlay(RectTransform parent)
    {
        var panel = Node("BoosterOpening", parent);
        Stretch(panel);
        Fill(panel, WithAlpha(Navy, 0.94f)).raycastTarget = true; // bloque les clics vers le menu derrière
        overlay = panel.gameObject;

        overlayTitle = Label(panel, "", 84, TextAnchor.MiddleCenter, Yellow, 5f);
        Place(overlayTitle.rectTransform, 0.84f, 140f);

        overlayCards = Node("Cartes", panel);
        overlayCards.anchorMin = overlayCards.anchorMax = new Vector2(0.5f, 0.5f);
        overlayCards.sizeDelta = new Vector2(920f, 860f);
        var grid = overlayCards.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(280f, 410f);
        grid.spacing = new Vector2(28f, 32f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.MiddleCenter;

        var row = Node("Boutons", panel);
        row.anchorMin = new Vector2(0.06f, 0.12f);
        row.anchorMax = new Vector2(0.94f, 0.12f);
        row.sizeDelta = new Vector2(0f, 160f);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 36f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;

        againButton = CandyButton(row, "", Red, () => { if (lastBooster != null) Open(lastBooster); }, 50, 160f);
        againLabel = ButtonText(againButton);
        CandyButton(row, "OK", Blue, CloseOrSkip, 56, 160f);

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
        overlayTitle.color = Color.Lerp(booster.color, Color.white, 0.2f);

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
            // Petit "pop" avec un tour : 0 -> 1.15 -> 1
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.3f)
            {
                float s = t < 0.6f ? Mathf.Lerp(0f, 1.15f, t / 0.6f) : Mathf.Lerp(1.15f, 1f, (t - 0.6f) / 0.4f);
                if (c != null)
                {
                    c.localScale = Vector3.one * s;
                    c.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-12f, 0f, t));
                }
                yield return null;
            }
            if (c != null)
            {
                c.localScale = Vector3.one;
                c.localRotation = Quaternion.identity;
            }
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
            foreach (var c in revealCards)
                if (c != null)
                {
                    c.localScale = Vector3.one;
                    c.localRotation = Quaternion.identity;
                }
            RefreshAgainButton();
            return;
        }
        overlay.SetActive(false);
    }

    /// <summary>Ferme l'écran d'ouverture (bouton retour du téléphone).</summary>
    public void CloseOverlay()
    {
        if (!OverlayOpen) return;
        CloseOrSkip();
        if (reveal == null) overlay.SetActive(false);
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

        var card = Card(car.id, parent, Color.Lerp(Navy, rc, 0.5f), 24f, 10f);
        var outline = card.Find("Fond").gameObject.AddComponent<Outline>();
        outline.effectColor = rc;
        outline.effectDistance = new Vector2(6f, -6f);

        var tag = Node("Rareté", card);
        Area(tag, 0.08f, 0.85f, 0.92f, 0.96f);
        Rounded(tag, rc, 22f);
        Stretch(FitLabel(tag, RarityInfo.Name(pull.rarity).ToUpper(), 30, 16, TextAnchor.MiddleCenter, Color.white, 2f).rectTransform);

        var nameText = FitLabel(card, car.displayName, 40, 22, TextAnchor.MiddleCenter, Color.white, 3f);
        Area(nameText.rectTransform, 0.05f, 0.62f, 0.95f, 0.83f);

        var swatch = Node("Couleur", card);
        swatch.anchorMin = swatch.anchorMax = new Vector2(0.5f, 0.47f);
        swatch.sizeDelta = new Vector2(120f, 120f);
        Fill(swatch, Color.white).sprite = Round;
        var inner = Node("Teinte", swatch);
        Area(inner, 0.1f, 0.1f, 0.9f, 0.9f);
        Fill(inner, car.color).sprite = Round;

        string status;
        Color statusColor = Color.white;
        if (pull.unlockedNow) { status = "DÉBLOQUÉE !"; statusColor = Yellow; }
        else if (pull.duplicateCoins > 0) status = "Doublon\n+" + pull.duplicateCoins + " pièces";
        else status = "+1 blueprint\n" + pull.blueprints + " / " + car.blueprintsToUnlock;
        var st = FitLabel(card, status, 34, 18, TextAnchor.MiddleCenter, statusColor, 3f);
        Area(st.rectTransform, 0.05f, 0.04f, 0.95f, 0.3f);
        return card;
    }
}
