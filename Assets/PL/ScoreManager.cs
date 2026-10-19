using UnityEngine;

/// <summary>
/// Gère le score : points à la distance parcourue, multiplicateur gagné en touchant des cônes,
/// et bonus à la fin d'un drift. À mettre sur un objet vide "ScoreManager" dans la scène.
/// </summary>
public class ScoreManager : MonoBehaviour
{
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

    [Header("Drift")]
    [Tooltip("Ajouter les points de drift au score (multipliés)")]
    public bool addDriftPoints = true;

    [Header("Affichage")]
    public bool showHUD = true;

    public float Score { get; private set; }
    public float Multiplier { get; private set; } = 1f;
    public float Distance { get; private set; }

    double lastRoadDistance;
    float timeSinceCone;

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
        if (car != null)
        {
            car.OnDriftEnded -= HandleDriftEnded;
            car.OnWallHit -= HandleWallHit;
        }
    }

    void Update()
    {
        // Points à la distance parcourue (on utilise la distance du décor, la voiture ne bouge pas en Z)
        if (road != null)
        {
            double now = road.TotalDistance;
            float delta = Mathf.Max(0f, (float)(now - lastRoadDistance));
            lastRoadDistance = now;

            Distance += delta;
            Score += delta * pointsPerMeter * Multiplier;
        }

        // Le multiplicateur redescend doucement si on ne touche plus de cônes
        timeSinceCone += Time.deltaTime;
        if (decayPerSecond > 0f && Multiplier > 1f && timeSinceCone > decayDelay)
            Multiplier = Mathf.Max(1f, Multiplier - decayPerSecond * Time.deltaTime);
    }

    /// <summary>Appelé par un cône quand la voiture le touche.</summary>
    public void OnConeHit()
    {
        Multiplier = Mathf.Min(maxMultiplier, Multiplier + multiplierPerCone);
        Score += conePoints * Multiplier;
        timeSinceCone = 0f;
    }

    void HandleDriftEnded(float points)
    {
        if (addDriftPoints) Score += points * Multiplier;
    }

    void HandleWallHit()
    {
        if (resetOnWallHit) Multiplier = 1f;
    }

    public void ResetScore()
    {
        Score = 0f;
        Distance = 0f;
        Multiplier = 1f;
        timeSinceCone = 0f;
        if (road != null) lastRoadDistance = road.TotalDistance;
    }

    void OnGUI()
    {
        if (!showHUD) return;

        int size = Mathf.Max(20, Screen.height / 25);
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            alignment = TextAnchor.UpperRight,
            fontStyle = FontStyle.Bold
        };

        string txt = $"{Mathf.FloorToInt(Score)}\nx{Multiplier:F1}   {Mathf.FloorToInt(Distance)} m";
        GUI.Label(new Rect(Screen.width - 20 - size * 12, 15, size * 12, size * 3f), txt, style);
    }
}