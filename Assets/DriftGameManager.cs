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
/// - Tout objet avec le composant DeathZone (trigger) tue aussi la voiture.
///
/// Mise en place :
/// 1. GameObject vide "GameManager" avec ce script (la voiture, la route et le score sont trouvés automatiquement).
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
    [Tooltip("Délai avant d'afficher l'écran de fin (secondes, temps réel)")]
    public float panelDelay = 0.6f;

    /// <summary>Vrai une fois la partie perdue.</summary>
    public bool IsDead { get; private set; }

    Rigidbody carRb;

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

        if (dieOffRoad && Mathf.Abs(road.LateralOffset(p)) > road.roadWidth * 0.5f + offRoadMargin)
            Die();
    }

    // ---------- Mort / fin de partie ----------

    /// <summary>Termine la partie. Appelé par la death zone, un DeathZone, ou ton propre code.</summary>
    public void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // Score figé (le drift en cours est perdu) + record enregistré
        bool newRecord = score.EndRun();

        // Arrête la voiture et le défilement du décor
        if (car != null) car.enabled = false; // DriftCar remet le défilement à 0 quand il est désactivé
        if (road != null) road.ScrollSpeed = 0f;
        if (carRb != null)
        {
            carRb.linearVelocity = Vector3.zero;
            carRb.angularVelocity = Vector3.zero;
            carRb.isKinematic = true;
        }

        PlayerData.AddCoins(score.IntScore / 10);

        if (panelDelay > 0f) StartCoroutine(ShowPanelAfterDelay(newRecord));
        else ShowPanel(newRecord);
    }

    public void LoadMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
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
