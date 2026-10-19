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

    [Header("Multiplicateur d'angle")]
    [Tooltip("Angle de drift (degrés) à partir duquel le multiplicateur d'angle s'active")]
    public float angleThreshold = 45f;
    [Tooltip("Multiplicateur appliqué pile à l'angle seuil")]
    public float angleBaseMultiplier = 1.5f;
    [Tooltip("Multiplicateur ajouté par degré au-dessus du seuil")]
    public float angleMultiplierPerDegree = 0.05f;
    public float maxAngleMultiplier = 5f;
    [Tooltip("Vitesse minimale (m/s) pour que le multiplicateur d'angle soit actif")]
    public float angleMinSpeed = 5f;
    [Tooltip("Une fois actif, il faut descendre de (seuil - cette valeur) degrés pour commencer à le perdre")]
    public float angleHysteresis = 8f;
    [Tooltip("Secondes passées sous la limite avant de perdre vraiment le multiplicateur d'angle")]
    public float angleGraceTime = 0.5f;
    [Tooltip("Vitesse de descente du multiplicateur d'angle quand il se désactive (par seconde)")]
    public float angleFallSpeed = 6f;

    [Header("Drift")]
    [Tooltip("Ajouter les points de drift au score (multipliés)")]
    public bool addDriftPoints = true;

    [Header("Affichage")]
    public bool showHUD = true;

    public float Score { get; private set; }
    /// <summary>Multiplicateur gagné avec les cônes.</summary>
    public float Multiplier { get; private set; } = 1f;
    /// <summary>Multiplicateur d'angle en direct (1 = inactif).</summary>
    public float AngleMultiplier { get; private set; } = 1f;
    /// <summary>Multiplicateur final appliqué au score (cônes x angle).</summary>
    public float TotalMultiplier => Multiplier * AngleMultiplier;
    public float Distance { get; private set; }

    double lastRoadDistance;
    float timeSinceCone;
    bool angleActive;
    float angleLostTimer;

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
        Multiplier = Mathf.Min(maxMultiplier, Multiplier + multiplierPerCone);
        Score += conePoints * TotalMultiplier;
        timeSinceCone = 0f;
    }

    void HandleDriftEnded(float points)
    {
        if (addDriftPoints) Score += points * TotalMultiplier;
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

        string txt = $"{Mathf.FloorToInt(Score)}\nx{TotalMultiplier:F1}   {Mathf.FloorToInt(Distance)} m";
        if (AngleMultiplier > 1f) txt += $"\nAngle x{AngleMultiplier:F1}";
        GUI.Label(new Rect(Screen.width - 20 - size * 12, 15, size * 12, size * 3f), txt, style);
    }
}