using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gère la mort et l'écran de fin. Le score est géré UNIQUEMENT par le ScoreManager
/// (ajouté automatiquement sur cet objet s'il n'y en a pas dans la scène).
///
/// MORT :
/// - Death zone automatique : la voiture tombe sous la route (falaise) de plus de "fallDepth" mètres.
/// - Optionnel : sortie de route trop loin du bord.
/// - La voiture s'arrête (vitesse quasi nulle), une fois qu'elle a démarré.
/// - Tout objet avec le composant DeathZone (trigger) tue aussi la voiture.
///
/// Mise en place :
/// 1. GameObject vide "GameManager" avec ce script (la voiture, la route et le score sont trouvés automatiquement).
/// 2. Dans ton Canvas, crée un objet vide "GameOverPanel" avec dedans des textes TextMeshPro
///    (score, meilleur score) et un bouton "Rejouer". Glisse-les dans les champs ci-dessous.
/// 3. Sur le bouton : OnClick -> GameManager -> DriftGameManager.Restart().
/// 4. Ajoute ta scène dans File > Build Profiles (Scene List), sinon Restart ne pourra pas la recharger.
///
/// Sans écran de fin assigné, la mort fait un fondu au noir puis retourne au menu.
/// Dans la partie de démo du menu (MenuBackground), la mort relance juste la démo : ni pièces ni record.
/// </summary>
public class DriftGameManager : MonoBehaviour
{
    [Header("Références (trouvées automatiquement si vides)")]
    public DriftCar car;
    public ZigzagRoadGenerator road;
    public ScoreManager score;

    [Header("UI - écran de fin")]
    [Tooltip("L'objet vide qui contient l'UI de fin. Il est caché au début et affiché à la mort.")]
    public GameObject gameOverPanel;
    public TMP_Text finalScoreText;
    public TMP_Text bestScoreText;
    [Tooltip("Optionnel : objet affiché seulement en cas de nouveau record")]
    public GameObject newRecordObject;

    [Header("Mort")]
    [Tooltip("Meurt si la voiture tombe sous le niveau de la route")]
    public bool dieOnFall = true;
    [Tooltip("Hauteur de chute sous la route avant la mort")]
    public float fallDepth = 6f;
    [Tooltip("Meurt si la voiture s'éloigne trop du bord de la route (même sans tomber)")]
    public bool dieOffRoad = false;
    public float offRoadMargin = 3f;
    [Tooltip("Meurt si la voiture s'arrête (vitesse quasi nulle)")]
    public bool dieWhenStopped = true;
    [Tooltip("En dessous de cette vitesse (m/s), la voiture est considérée comme arrêtée")]
    public float stoppedSpeed = 0.5f;
    [Tooltip("Temps (s) à l'arrêt avant la mort (évite de mourir sur un simple choc)")]
    public float stoppedTime = 0.3f;
    [Tooltip("Vitesse (m/s) à atteindre au départ avant que l'arrêt puisse tuer (la voiture part de 0)")]
    public float stoppedArmSpeed = 3f;
    [Tooltip("Délai avant d'afficher l'écran de fin (secondes, temps réel)")]
    public float panelDelay = 0.6f;

    [Header("Continuer après la mort")]
    [Tooltip("À la mort, propose de continuer (rubis ou publicité) avant de finir la partie")]
    public bool offerContinue = true;
    [Tooltip("Prix en rubis du premier \"Continuer\" de la partie")]
    public int continueBasePrice = 10;
    [Tooltip("Le prix est multiplié par ça à chaque \"Continuer\" payé dans la même partie (10, 20, 40...)")]
    public float continuePriceMultiplier = 2f;
    [Tooltip("Une publicité permet de continuer une seule fois par partie")]
    public bool oneAdPerRun = true;
    [Tooltip("Temps (s) pour se décider avant que la partie se termine")]
    public float continueTime = 6f;

    [Header("Menu")]
    [Tooltip("Nom exact de la scène du menu (elle doit être dans Build Profiles > Scene List)")]
    public string menuSceneName = "Menu";
    [Tooltip("Sans écran de fin : délai après la mort avant le fondu au noir et le retour au menu")]
    public float backToMenuDelay = 1.5f;

    /// <summary>Vrai une fois la partie perdue.</summary>
    public bool IsDead { get; private set; }

    Rigidbody carRb;
    bool stopArmed;   // la voiture a démarré : l'arrêt peut maintenant tuer
    float stoppedFor; // temps passé à l'arrêt

    void Awake()
    {
        if (car == null) car = FindFirstObjectByType<DriftCar>();
        if (road == null) road = FindFirstObjectByType<ZigzagRoadGenerator>();
        if (car != null) carRb = car.GetComponent<Rigidbody>();

        if (car == null || road == null)
            Debug.LogError("[DriftGameManager] Voiture ou route introuvable dans la scène.", this);

        if (score == null) score = FindFirstObjectByType<ScoreManager>();
        if (score == null) score = gameObject.AddComponent<ScoreManager>();

        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        // Pilote auto proposé au début de chaque partie (voir AutopilotBoost, inactif dans la démo du menu)
        if (GetComponent<AutopilotBoost>() == null) gameObject.AddComponent<AutopilotBoost>();
        // Petit tutoriel au tout début : glisser le doigt de gauche à droite (voir Onboarding)
        if (GetComponent<Onboarding>() == null) gameObject.AddComponent<Onboarding>();
    }

    void Update()
    {
        if (IsDead || car == null || road == null) return;
        CheckDeath();
    }

    // ---------- Death zone automatique ----------

    void CheckDeath()
    {
        Vector3 p = car.transform.position;
        float trackZ = road.TrackZ(p.z);

        if (dieOnFall && p.y < road.RoadHeightAt(trackZ) - fallDepth)
        {
            Die();
            return;
        }

        if (dieOffRoad && Mathf.Abs(road.LateralOffset(p)) > road.HalfWidthXAt(road.TrackZ(p.z)) + offRoadMargin)
        {
            Die();
            return;
        }

        if (dieWhenStopped && IsStopped())
            Die();
    }

    // Vrai si la voiture est restée à l'arrêt assez longtemps (seulement après avoir démarré)
    bool IsStopped()
    {
        float speed = car.Speed;
        if (!stopArmed)
        {
            stopArmed = speed >= stoppedArmSpeed;
            return false;
        }
        stoppedFor = speed <= stoppedSpeed ? stoppedFor + Time.deltaTime : 0f;
        return stoppedFor >= stoppedTime;
    }

    // ---------- Mort / fin de partie ----------

    /// <summary>Termine la partie. Appelé par la death zone, un DeathZone, ou ton propre code.</summary>
    public void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // Arrête la voiture et le défilement du décor
        if (car != null) car.enabled = false; // DriftCar remet le défilement à 0 quand il est désactivé
        if (road != null) road.ScrollSpeed = 0f;
        if (carRb != null)
        {
            carRb.linearVelocity = Vector3.zero;
            carRb.angularVelocity = Vector3.zero;
            carRb.isKinematic = true;
        }

        // Partie de démo derrière le menu : on laisse le menu faire le fondu et relancer
        if (MenuBackground.DemoRunning)
        {
            MenuBackground.NotifyRunEnded();
            return;
        }

        // Avant de finir : proposer de continuer
        if (offerContinue)
        {
            StartCoroutine(OfferContinue());
            return;
        }
        EndGame();
    }

    // ---------- Continuer ----------

    int paidContinues;
    bool adUsed;

    /// <summary>Prix en rubis du prochain "Continuer" (augmente à chaque fois dans la même partie).</summary>
    public int ContinuePrice => Mathf.RoundToInt(continueBasePrice * Mathf.Pow(Mathf.Max(1f, continuePriceMultiplier), paidContinues));

    System.Collections.IEnumerator OfferContinue()
    {
        yield return new WaitForSecondsRealtime(0.5f); // le temps de voir la chute
        Time.timeScale = 0f;                            // score, multiplicateurs et combo en pause

        var offer = GetComponent<ContinueOffer>();
        if (offer == null) offer = gameObject.AddComponent<ContinueOffer>();
        bool adOk = AdService.RewardedAvailable && !(oneAdPerRun && adUsed);
        ContinueOffer.Choice choice = ContinueOffer.Choice.None;
        yield return offer.Ask(ContinuePrice, adOk, continueTime, c => choice = c);

        Time.timeScale = 1f;
        if (choice == ContinueOffer.Choice.Rubis) { paidContinues++; Revive(); }
        else if (choice == ContinueOffer.Choice.Ad) { adUsed = true; Revive(); }
        else EndGame();
    }

    // Remet la voiture au milieu de la route, dans l'axe, et relance la partie
    void Revive()
    {
        Vector3 p = car.transform.position;
        float z = road.TrackZ(p.z);
        float x = road.CenterXAt(z);
        float heading = Mathf.Atan2(road.CenterXAt(z + 1f) - road.CenterXAt(z - 1f), 2f) * Mathf.Rad2Deg;
        if (carRb != null) carRb.isKinematic = false;
        car.enabled = true;
        car.Respawn(new Vector3(x, road.RoadHeightAt(z) + 1f, p.z), heading);
        stoppedFor = 0f;
        IsDead = false;
    }

    // Fin définitive de la partie
    void EndGame()
    {
        // Score figé (le drift en cours est perdu) + record enregistré
        bool newRecord = score.EndRun();
        PlayerData.AddCoins(score.IntScore / 10);

        if (gameOverPanel == null) StartCoroutine(FadeAndLoad(menuSceneName, backToMenuDelay));
        else if (panelDelay > 0f) StartCoroutine(ShowPanelAfterDelay(newRecord));
        else ShowPanel(newRecord);
    }

    /// <summary>Fondu au noir puis retour au menu. À brancher sur un bouton "Menu".</summary>
    public void LoadMenu()
    {
        StartCoroutine(FadeAndLoad(menuSceneName, 0f));
    }

    System.Collections.IEnumerator FadeAndLoad(string sceneName, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        yield return ScreenFader.Instance.Fade(1f);
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }

    System.Collections.IEnumerator ShowPanelAfterDelay(bool newRecord)
    {
        yield return new WaitForSecondsRealtime(panelDelay);
        ShowPanel(newRecord);
    }

    void ShowPanel(bool newRecord)
    {
        if (finalScoreText != null) finalScoreText.text = "Score : " + score.IntScore;
        if (bestScoreText != null) bestScoreText.text = "Meilleur score : " + score.BestScore;
        if (newRecordObject != null) newRecordObject.SetActive(newRecord);
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
    }

    /// <summary>Recharge la scène. À brancher sur le bouton "Rejouer".</summary>
    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
