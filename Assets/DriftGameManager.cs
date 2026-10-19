using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gère le score, la mort et l'écran de fin.
///
/// SCORE = distance parcourue x pointsPerMeter + points de drift validés x driftMultiplier.
/// Le meilleur score est sauvegardé sur le téléphone (PlayerPrefs).
///
/// MORT :
/// - Death zone automatique : la voiture tombe sous la route (falaise) de plus de "fallDepth" mètres.
/// - Optionnel : sortie de route trop loin du bord.
/// - Tout objet avec le composant DeathZone (trigger) tue aussi la voiture.
///
/// Mise en place :
/// 1. GameObject vide "GameManager" avec ce script (la voiture et la route sont trouvées automatiquement).
/// 2. Dans ton Canvas, crée un objet vide "GameOverPanel" avec dedans des textes TextMeshPro
///    (score, meilleur score) et un bouton "Rejouer". Glisse-les dans les champs ci-dessous.
/// 3. Sur le bouton : OnClick -> GameManager -> DriftGameManager.Restart().
/// 4. Ajoute ta scène dans File > Build Profiles (Scene List), sinon Restart ne pourra pas la recharger.
/// </summary>
public class DriftGameManager : MonoBehaviour
{
    [Header("Références (trouvées automatiquement si vides)")]
    public DriftCar car;
    public ZigzagRoadGenerator road;

    [Header("UI - écran de fin")]
    [Tooltip("L'objet vide qui contient l'UI de fin. Il est caché au début et affiché à la mort.")]
    public GameObject gameOverPanel;
    public TMP_Text finalScoreText;
    public TMP_Text bestScoreText;
    [Tooltip("Optionnel : objet affiché seulement en cas de nouveau record")]
    public GameObject newRecordObject;

    [Header("UI - pendant la partie (optionnel)")]
    public TMP_Text hudScoreText;
    [Tooltip("Points du drift en cours, affiché seulement pendant un drift")]
    public TMP_Text hudDriftText;

    [Header("Score")]
    public float pointsPerMeter = 1f;
    public float driftMultiplier = 1f;

    [Header("Mort")]
    [Tooltip("Meurt si la voiture tombe sous le niveau de la route")]
    public bool dieOnFall = true;
    [Tooltip("Hauteur de chute sous la route avant la mort")]
    public float fallDepth = 6f;
    [Tooltip("Meurt si la voiture s'éloigne trop du bord de la route (même sans tomber)")]
    public bool dieOffRoad = false;
    public float offRoadMargin = 3f;
    [Tooltip("Délai avant d'afficher l'écran de fin (secondes, temps réel)")]
    public float panelDelay = 0.6f;

    const string BestScoreKey = "DriftBestScore";

    /// <summary>Vrai une fois la partie perdue.</summary>
    public bool IsDead { get; private set; }
    /// <summary>Score actuel (points de drift validés + distance).</summary>
    public int Score { get; private set; }
    /// <summary>Meilleur score sauvegardé.</summary>
    public int BestScore { get; private set; }

    float driftBank;      // points de drift validés
    Rigidbody carRb;

    void Awake()
    {
        if (car == null) car = FindFirstObjectByType<DriftCar>();
        if (road == null) road = FindFirstObjectByType<ZigzagRoadGenerator>();
        if (car != null) carRb = car.GetComponent<Rigidbody>();

        if (car == null || road == null)
            Debug.LogError("[DriftGameManager] Voiture ou route introuvable dans la scène.", this);

        BestScore = PlayerPrefs.GetInt(BestScoreKey, 0);

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (hudDriftText != null) hudDriftText.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (car != null) car.OnDriftEnded += HandleDriftEnded;
    }

    void OnDisable()
    {
        if (car != null) car.OnDriftEnded -= HandleDriftEnded;
    }

    void HandleDriftEnded(float points)
    {
        if (!IsDead) driftBank += points * driftMultiplier;
    }

    void Update()
    {
        if (IsDead || car == null || road == null) return;

        Score = Mathf.FloorToInt((float)road.TotalDistance * pointsPerMeter + driftBank);

        // HUD
        if (hudScoreText != null) hudScoreText.text = Score.ToString();
        if (hudDriftText != null)
        {
            bool show = car.IsDrifting && car.CurrentDriftPoints > 0f;
            if (hudDriftText.gameObject.activeSelf != show) hudDriftText.gameObject.SetActive(show);
            if (show) hudDriftText.text = "+" + Mathf.FloorToInt(car.CurrentDriftPoints * driftMultiplier);
        }

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

        if (dieOffRoad && Mathf.Abs(road.LateralOffset(p)) > road.roadWidth * 0.5f + offRoadMargin)
            Die();
    }

    // ---------- Mort / fin de partie ----------

    /// <summary>Termine la partie. Appelé par la death zone, un DeathZone, ou ton propre code.</summary>
    public void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // Score final : seuls les drifts terminés comptent
        Score = Mathf.FloorToInt((float)road.TotalDistance * pointsPerMeter + driftBank);

        // Arrête la voiture et le défilement du décor
        if (car != null) car.enabled = false; // DriftCar remet le défilement à 0 quand il est désactivé
        if (road != null) road.ScrollSpeed = 0f;
        if (carRb != null)
        {
            carRb.linearVelocity = Vector3.zero;
            carRb.angularVelocity = Vector3.zero;
            carRb.isKinematic = true;
        }

        if (hudDriftText != null) hudDriftText.gameObject.SetActive(false);

        // Meilleur score
        bool newRecord = Score > BestScore;
        if (newRecord)
        {
            BestScore = Score;
            PlayerPrefs.SetInt(BestScoreKey, BestScore);
            PlayerPrefs.Save();
        }

        if (panelDelay > 0f) StartCoroutine(ShowPanelAfterDelay(newRecord));
        else ShowPanel(newRecord);
    }

    System.Collections.IEnumerator ShowPanelAfterDelay(bool newRecord)
    {
        yield return new WaitForSecondsRealtime(panelDelay);
        ShowPanel(newRecord);
    }

    void ShowPanel(bool newRecord)
    {
        if (finalScoreText != null) finalScoreText.text = "Score : " + Score;
        if (bestScoreText != null) bestScoreText.text = "Meilleur score : " + BestScore;
        if (newRecordObject != null) newRecordObject.SetActive(newRecord);
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
    }

    /// <summary>Recharge la scène. À brancher sur le bouton "Rejouer".</summary>
    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>Remet le meilleur score à 0 (utile pour tester).</summary>
    [ContextMenu("Reset meilleur score")]
    public void ResetBestScore()
    {
        BestScore = 0;
        PlayerPrefs.DeleteKey(BestScoreKey);
    }
}