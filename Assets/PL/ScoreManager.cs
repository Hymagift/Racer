using TMPro;
using UnityEngine;

/// <summary>
/// SEUL script qui gère le score : points à la distance parcourue, multiplicateur gagné en touchant des cônes,
/// multiplicateur d'angle, bonus à la fin d'un drift, et meilleur score sauvegardé (PlayerPrefs).
///
/// À mettre sur un objet vide "ScoreManager" dans la scène (un seul par scène).
/// S'il n'y en a pas, DriftGameManager en ajoute un automatiquement.
/// À la mort, DriftGameManager appelle EndRun() : le score est figé et le record enregistré.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    /// <summary>Clé PlayerPrefs du meilleur score (lue aussi par PlayerData pour le menu).</summary>
    public const string BestScoreKey = "DriftBestScore";

    /// <summary>Le ScoreManager de la scène.</summary>
    public static ScoreManager Instance { get; private set; }

    [Header("Références")]
    public DriftCar car;
    public ZigzagRoadGenerator road;

    [Header("Distance")]
    [Tooltip("Points gagnés par mètre parcouru (avant multiplicateur)")]
    public float pointsPerMeter = 1f;

    [Header("Multiplicateur (cônes)")]
    [Tooltip("Multiplicateur ajouté à chaque cône touché")]
    public float multiplierPerCone = 0.5f;
    public float maxMultiplier = 10f;
    [Tooltip("Secondes sans toucher de cône avant que le multiplicateur commence à baisser")]
    public float decayDelay = 5f;
    [Tooltip("Baisse du multiplicateur par seconde une fois le délai écoulé (0 = ne baisse jamais)")]
    public float decayPerSecond = 1f;
    [Tooltip("Remettre le multiplicateur à 1 quand on tape un mur")]
    public bool resetOnWallHit = true;
    [Tooltip("Points bonus à chaque cône (avant multiplicateur)")]
    public float conePoints = 50f;

    [Header("Multiplicateur d'angle")]
    [Tooltip("Angle de drift (degrés) à partir duquel le multiplicateur d'angle s'active")]
    public float angleThreshold = 30f;
    [Tooltip("Multiplicateur appliqué pile à l'angle seuil")]
    public float angleBaseMultiplier = 1.5f;
    [Tooltip("Multiplicateur ajouté par degré au-dessus du seuil")]
    public float angleMultiplierPerDegree = 0.05f;
    public float maxAngleMultiplier = 5f;
    [Tooltip("Vitesse minimale (m/s) pour que le multiplicateur d'angle soit actif")]
    public float angleMinSpeed = 5f;
    [Tooltip("Une fois actif, il faut descendre de (seuil - cette valeur) degrés pour commencer à le perdre")]
    public float angleHysteresis = 6f;
    [Tooltip("Secondes passées sous la limite avant de perdre vraiment le multiplicateur d'angle")]
    public float angleGraceTime = 0.5f;
    [Tooltip("Vitesse de descente du multiplicateur d'angle quand il se désactive (par seconde)")]
    public float angleFallSpeed = 6f;

    [Header("Drift")]
    [Tooltip("Ajouter les points de drift au score (multipliés) à la fin de chaque drift")]
    public bool addDriftPoints = true;

    [Header("UI pendant la partie (optionnel, TextMeshPro)")]
    public TMP_Text hudScoreText;
    [Tooltip("Multiplicateur total (ex : x2.5)")]
    public TMP_Text hudMultiplierText;
    [Tooltip("Points du drift en cours, affiché seulement pendant un drift")]
    public TMP_Text hudDriftText;
    public TMP_Text hudDistanceText;

    [Header("Affichage de secours")]
    [Tooltip("Affiche le score avec OnGUI tant qu'aucun texte TextMeshPro n'est assigné")]
    public bool showHUD = true;

    /// <summary>Score en cours (à virgule).</summary>
    public float Score { get; private set; }
    /// <summary>Score arrondi, celui qui est affiché et enregistré.</summary>
    public int IntScore => Mathf.FloorToInt(Score);
    /// <summary>Meilleur score sauvegardé.</summary>
    public int BestScore { get; private set; }
    /// <summary>Multiplicateur gagné avec les cônes.</summary>
    public float Multiplier { get; private set; } = 1f;
    /// <summary>Multiplicateur d'angle en direct (1 = inactif).</summary>
    public float AngleMultiplier { get; private set; } = 1f;
    /// <summary>Multiplicateur final appliqué au score (cônes x angle).</summary>
    public float TotalMultiplier => Multiplier * AngleMultiplier;
    /// <summary>Distance parcourue (m).</summary>
    public float Distance { get; private set; }
    /// <summary>Points (multipliés) que rapporterait le drift en cours s'il se terminait maintenant.</summary>
    public float PendingDriftPoints => car != null && car.IsDrifting ? car.CurrentDriftPoints * TotalMultiplier : 0f;
    /// <summary>Faux une fois la partie terminée (EndRun) : le score ne bouge plus.</summary>
    public bool IsRunning { get; private set; } = true;

    double lastRoadDistance;
    float timeSinceCone;
    bool angleActive;
    float angleLostTimer;

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("[ScoreManager] Il y a plusieurs ScoreManager dans la scène : un seul doit rester.", this);
        Instance = this;

        BestScore = PlayerPrefs.GetInt(BestScoreKey, 0);
        if (hudDriftText != null) hudDriftText.gameObject.SetActive(false);
    }

    void Start()
    {
        if (car == null) car = FindFirstObjectByType<DriftCar>();
        if (road == null) road = FindFirstObjectByType<ZigzagRoadGenerator>();

        if (road != null) lastRoadDistance = road.TotalDistance;

        if (car != null)
        {
            car.OnDriftEnded += HandleDriftEnded;
            car.OnWallHit += HandleWallHit;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (car != null)
        {
            car.OnDriftEnded -= HandleDriftEnded;
            car.OnWallHit -= HandleWallHit;
        }
    }

    void Update()
    {
        if (!IsRunning) return;

        // Multiplicateur d'angle en direct, avec hystérésis + délai de grâce pour éviter qu'il
        // saute à x1 quand l'angle oscille un instant autour du seuil pendant un drift.
        UpdateAngleMultiplier();

        // Points à la distance parcourue (on utilise la distance du décor, la voiture ne bouge pas en Z)
        if (road != null)
        {
            double now = road.TotalDistance;
            float delta = Mathf.Max(0f, (float)(now - lastRoadDistance));
            lastRoadDistance = now;

            Distance += delta;
            Score += delta * pointsPerMeter * TotalMultiplier;
        }

        // Le multiplicateur redescend doucement si on ne touche plus de cônes
        timeSinceCone += Time.deltaTime;
        if (decayPerSecond > 0f && Multiplier > 1f && timeSinceCone > decayDelay)
            Multiplier = Mathf.Max(1f, Multiplier - decayPerSecond * Time.deltaTime);

        UpdateHUD();
    }

    void UpdateAngleMultiplier()
    {
        float target = 1f;

        if (car != null)
        {
            float angle = Mathf.Abs(car.DriftAngle);
            bool fastEnough = car.Speed >= angleMinSpeed;

            // Une fois actif, la limite basse est abaissée (hystérésis)
            float limit = angleActive ? angleThreshold - angleHysteresis : angleThreshold;

            if (angle >= limit && fastEnough)
            {
                angleActive = true;
                angleLostTimer = 0f;
            }
            else if (angleActive)
            {
                angleLostTimer += Time.deltaTime;
                if (angleLostTimer > angleGraceTime || !fastEnough) angleActive = false;
            }

            if (angleActive)
            {
                // max(angle, seuil) : pendant la grâce, on ne descend pas sous le multiplicateur de base
                float a = Mathf.Max(angle, angleThreshold);
                float m = angleBaseMultiplier + (a - angleThreshold) * angleMultiplierPerDegree;
                target = Mathf.Clamp(m, 1f, maxAngleMultiplier);
            }
        }

        // Monte instantanément (en direct), redescend vite mais sans à-coup
        if (target >= AngleMultiplier) AngleMultiplier = target;
        else AngleMultiplier = Mathf.MoveTowards(AngleMultiplier, target, angleFallSpeed * Time.deltaTime);
    }

    /// <summary>Appelé par un cône quand la voiture le touche.</summary>
    public void OnConeHit()
    {
        if (!IsRunning) return;
        Multiplier = Mathf.Min(maxMultiplier, Multiplier + multiplierPerCone);
        Score += conePoints * TotalMultiplier;
        timeSinceCone = 0f;
    }

    void HandleDriftEnded(float points)
    {
        if (IsRunning && addDriftPoints) Score += points * TotalMultiplier;
    }

    void HandleWallHit()
    {
        if (IsRunning && resetOnWallHit) Multiplier = 1f;
    }

    // ---------- Fin de partie ----------

    /// <summary>
    /// Fige le score (le drift en cours est perdu) et enregistre le record.
    /// Renvoie vrai si c'est un nouveau record. Appelé par DriftGameManager à la mort.
    /// </summary>
    public bool EndRun()
    {
        if (!IsRunning) return false;
        IsRunning = false;

        if (hudDriftText != null) hudDriftText.gameObject.SetActive(false);

        if (IntScore <= BestScore) return false;
        BestScore = IntScore;
        PlayerPrefs.SetInt(BestScoreKey, BestScore);
        PlayerPrefs.Save();
        return true;
    }

    public void ResetScore()
    {
        Score = 0f;
        Distance = 0f;
        Multiplier = 1f;
        AngleMultiplier = 1f;
        angleActive = false;
        timeSinceCone = 0f;
        IsRunning = true;
        if (road != null) lastRoadDistance = road.TotalDistance;
    }

    /// <summary>Remet le meilleur score à 0 (utile pour tester).</summary>
    [ContextMenu("Reset meilleur score")]
    public void ResetBestScore()
    {
        BestScore = 0;
        PlayerPrefs.DeleteKey(BestScoreKey);
    }

    // ---------- Affichage ----------

    void UpdateHUD()
    {
        if (hudScoreText != null) hudScoreText.text = IntScore.ToString();
        if (hudMultiplierText != null) hudMultiplierText.text = $"x{TotalMultiplier:F1}";
        if (hudDistanceText != null) hudDistanceText.text = $"{Mathf.FloorToInt(Distance)} m";
        if (hudDriftText != null)
        {
            float pending = PendingDriftPoints;
            bool show = pending > 0f;
            if (hudDriftText.gameObject.activeSelf != show) hudDriftText.gameObject.SetActive(show);
            if (show) hudDriftText.text = "+" + Mathf.FloorToInt(pending);
        }
    }

    void OnGUI()
    {
        if (!showHUD || hudScoreText != null) return;

        int size = Mathf.Max(20, Screen.height / 25);
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            alignment = TextAnchor.UpperRight,
            fontStyle = FontStyle.Bold
        };

        string txt = $"{IntScore}\nx{TotalMultiplier:F1}   {Mathf.FloorToInt(Distance)} m";
        float pending = PendingDriftPoints;
        if (pending > 0f) txt += $"\nDrift +{Mathf.FloorToInt(pending)}";
        if (AngleMultiplier > 1f) txt += $"\nAngle x{AngleMultiplier:F1}";
        GUI.Label(new Rect(Screen.width - 20 - size * 12, 15, size * 12, size * 4f), txt, style);
    }
}
