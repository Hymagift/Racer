using TMPro;
using UnityEngine;

/// <summary>
/// SEUL script qui gère le score : points à la distance parcourue, multiplicateur gagné en touchant des cônes,
/// multiplicateur de drift, bonus à la fin d'un drift, et meilleur score sauvegardé (PlayerPrefs).
///
/// Multiplicateur total = cônes x drift. Les deux bougent progressivement :
/// - drift : monte petit à petit pendant qu'on drifte (plus vite si l'angle est fort), reste en place un moment
///   entre deux drifts (pour enchaîner), puis redescend lentement ;
/// - cônes : chaque cône ajoute un bonus qui arrive en douceur ; il redescend lentement si on n'en touche plus.
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
    [Tooltip("Remettre le multiplicateur des cônes à 1 quand on tape un mur (il redescend en douceur, pas d'un coup)")]
    public bool resetOnWallHit = true;
    [Tooltip("Points bonus à chaque cône (avant multiplicateur)")]
    public float conePoints = 50f;
    [Tooltip("Vitesse (par seconde) à laquelle le multiplicateur des cônes rejoint sa valeur : le bonus d'un cône arrive en douceur")]
    public float coneSmoothSpeed = 2f;

    [Header("Multiplicateur de drift")]
    [Tooltip("Angle de drift (degrés) à partir duquel le multiplicateur monte")]
    public float driftMinAngle = 15f;
    [Tooltip("Angle de drift (degrés) à partir duquel il monte le plus vite")]
    public float driftFullAngle = 40f;
    [Tooltip("Montée par seconde de drift à l'angle min")]
    public float driftRiseMin = 0.4f;
    [Tooltip("Montée par seconde de drift à l'angle max")]
    public float driftRiseMax = 1f;
    public float maxDriftMultiplier = 5f;
    [Tooltip("Vitesse minimale (m/s) pour que le drift fasse monter le multiplicateur")]
    public float driftMinSpeed = 5f;
    [Tooltip("Secondes sans drifter pendant lesquelles le multiplicateur ne bouge pas (pour enchaîner les drifts)")]
    public float driftHoldTime = 2f;
    [Tooltip("Descente par seconde une fois l'attente écoulée (0.35 : de x5 à x1 en ~11 s)")]
    public float driftFallSpeed = 0.35f;
    [Tooltip("Taper un mur annule l'attente : le multiplicateur de drift commence tout de suite à redescendre")]
    public bool wallHitSkipsHold = true;

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

    [Header("Cercle de combo")]
    [Tooltip("Multiplicateur au centre de l'écran dans un cercle qui se remplit, points du combo en dessous (voir ComboHUD)")]
    public bool showComboRing = true;

    [Header("Affichage de secours")]
    [Tooltip("Affiche le score avec OnGUI tant qu'aucun texte TextMeshPro n'est assigné. Faux = aucun HUD (démo du menu)")]
    public bool showHUD = true;

    /// <summary>Appelé quand un drift est encaissé, avec les points ajoutés au score (déjà multipliés).</summary>
    public event System.Action<float> OnDriftBanked;

    /// <summary>Score en cours (à virgule).</summary>
    public float Score { get; private set; }
    /// <summary>Score arrondi, celui qui est affiché et enregistré.</summary>
    public int IntScore => Mathf.FloorToInt(Score);
    /// <summary>Meilleur score sauvegardé.</summary>
    public int BestScore { get; private set; }
    /// <summary>Multiplicateur gagné avec les cônes (suit coneTarget en douceur).</summary>
    public float Multiplier { get; private set; } = 1f;
    /// <summary>Multiplicateur de drift : monte en driftant, redescend lentement quand on arrête.</summary>
    public float DriftMultiplier { get; private set; } = 1f;
    /// <summary>Multiplicateur final appliqué au score (cônes x drift).</summary>
    public float TotalMultiplier => Multiplier * DriftMultiplier;
    /// <summary>Distance parcourue (m).</summary>
    public float Distance { get; private set; }
    /// <summary>Points (multipliés) que rapporterait le drift en cours s'il se terminait maintenant.</summary>
    public float PendingDriftPoints => car != null && car.IsDrifting ? car.CurrentDriftPoints * TotalMultiplier : 0f;
    /// <summary>Faux une fois la partie terminée (EndRun) : le score ne bouge plus.</summary>
    public bool IsRunning { get; private set; } = true;

    double lastRoadDistance;
    float timeSinceCone;
    float coneTarget = 1f;     // valeur que le multiplicateur des cônes rejoint progressivement
    float timeSinceDrift = 999f;

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

        if (showComboRing && GetComponent<ComboHUD>() == null) gameObject.AddComponent<ComboHUD>();
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

        UpdateDriftMultiplier();
        UpdateConeMultiplier();

        // Points à la distance parcourue (on utilise la distance du décor, la voiture ne bouge pas en Z)
        if (road != null)
        {
            double now = road.TotalDistance;
            float delta = Mathf.Max(0f, (float)(now - lastRoadDistance));
            lastRoadDistance = now;

            Distance += delta;
            Score += delta * pointsPerMeter * TotalMultiplier;
        }

        UpdateHUD();
    }

    // Drift : monte petit à petit pendant le drift, attend un peu quand on arrête, puis redescend lentement
    void UpdateDriftMultiplier()
    {
        float dt = Time.deltaTime;
        float angle = car != null ? Mathf.Abs(car.DriftAngle) : 0f;
        bool drifting = car != null && angle >= driftMinAngle && car.Speed >= driftMinSpeed;

        if (drifting)
        {
            float rise = Mathf.Lerp(driftRiseMin, driftRiseMax, Mathf.InverseLerp(driftMinAngle, driftFullAngle, angle));
            DriftMultiplier = Mathf.Min(maxDriftMultiplier, DriftMultiplier + rise * dt);
            timeSinceDrift = 0f;
        }
        else
        {
            timeSinceDrift += dt;
            if (timeSinceDrift > driftHoldTime)
                DriftMultiplier = Mathf.Max(1f, DriftMultiplier - driftFallSpeed * dt);
        }
    }

    // Cônes : la valeur visée change d'un coup (cône, mur), le multiplicateur la rejoint en douceur
    void UpdateConeMultiplier()
    {
        float dt = Time.deltaTime;
        timeSinceCone += dt;
        if (decayPerSecond > 0f && coneTarget > 1f && timeSinceCone > decayDelay)
            coneTarget = Mathf.Max(1f, coneTarget - decayPerSecond * dt);

        float speed = Mathf.Max(0.01f, coneSmoothSpeed);
        // La descente normale (decayPerSecond) ne doit pas être ralentie par le lissage
        if (coneTarget < Multiplier) speed = Mathf.Max(speed, decayPerSecond);
        Multiplier = Mathf.MoveTowards(Multiplier, coneTarget, speed * dt);
    }

    /// <summary>Appelé par un cône quand la voiture le touche.</summary>
    public void OnConeHit()
    {
        if (!IsRunning) return;
        coneTarget = Mathf.Min(maxMultiplier, coneTarget + multiplierPerCone);
        Score += conePoints * TotalMultiplier;
        timeSinceCone = 0f;
    }

    void HandleDriftEnded(float points)
    {
        if (!IsRunning || !addDriftPoints) return;
        float gained = points * TotalMultiplier;
        Score += gained;
        OnDriftBanked?.Invoke(gained);
    }

    void HandleWallHit()
    {
        if (!IsRunning) return;
        if (resetOnWallHit) coneTarget = 1f;                     // redescend en douceur (coneSmoothSpeed)
        if (wallHitSkipsHold) timeSinceDrift = driftHoldTime;    // le drift commence à redescendre tout de suite
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
        Multiplier = coneTarget = 1f;
        DriftMultiplier = 1f;
        timeSinceDrift = 999f;
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

        // Avec le cercle de combo, multiplicateur et points du drift sont au centre : ici seulement score et distance
        bool ring = showComboRing && GetComponent<ComboHUD>() != null;
        string txt = ring
            ? $"{IntScore}\n{Mathf.FloorToInt(Distance)} m"
            : $"{IntScore}\nx{TotalMultiplier:F1}   {Mathf.FloorToInt(Distance)} m";
        float pending = PendingDriftPoints;
        if (!ring && pending > 0f) txt += $"\nDrift +{Mathf.FloorToInt(pending)}";
        if (!ring && DriftMultiplier > 1f) txt += $"\nDrift x{DriftMultiplier:F1}";
        GUI.Label(new Rect(Screen.width - 20 - size * 12, 15, size * 12, size * 4f), txt, style);
    }
}
