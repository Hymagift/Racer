using System;
using System.Collections.Generic;

/// <summary>
/// Tracé de route TOUGE : une suite de "figures" (épingles, enchaînements en S, virages serrés, courbes, lignes droites)
/// tirées au hasard, de plus en plus dures avec la distance.
///
/// La route est une fonction x(z) (le décor défile en Z) : elle ne peut pas revenir en arrière, donc une épingle
/// fait jusqu'à 2 x l'angle max de changement de cap (ex : -60° -> +60° = 120°).
/// Chaque virage est un VRAI arc de cercle de rayon connu, suivi d'une portion droite (en diagonale) :
/// le rayon est choisi pour que le virage reste faisable à la vitesse de la voiture à cet endroit.
///
/// Classe C# simple (pas un MonoBehaviour), utilisée par ZigzagRoadGenerator. Entièrement déterministe pour une seed.
/// </summary>
public class TougeLayout
{
    // ---------- Réglages (renseignés par ZigzagRoadGenerator) ----------

    /// <summary>Ligne droite au départ (m).</summary>
    public float straightStart = 50f;
    /// <summary>Distance pour atteindre la difficulté max (angles max, épingles et S fréquents).</summary>
    public float distanceToMaxDifficulty = 2500f;
    /// <summary>Angle max de la route par rapport à l'axe (degrés) au début / à la difficulté max.</summary>
    public float maxAngleStart = 35f, maxAngleEnd = 60f;
    /// <summary>Rayon de virage minimum absolu (m).</summary>
    public float minRadius = 8f;
    /// <summary>Accélération latérale visée dans les virages serrés (m/s²) : rayon = vitesse² / cornerGrip.</summary>
    public float cornerGrip = 8f;
    /// <summary>Au-delà de cet écart latéral, les virages ramènent la route vers x = 0.</summary>
    public float recenterDistance = 40f;
    /// <summary>Vitesse de la voiture (m/s) à une distance donnée, pour calculer les rayons. Null = 12 m/s.</summary>
    public Func<double, float> speedAt;

    // ---------- Données ----------

    const float Deg2Rad = (float)(Math.PI / 180.0);

    // Une pièce = un arc de cercle (de th0 à th1, rayon r) puis une portion droite au cap th1
    struct Piece
    {
        public double z0, x0;   // début de la pièce (z absolu)
        public float th0, th1;  // cap au début / à la fin de l'arc (rad, 0 = tout droit, + = vers +X)
        public float r;         // rayon de l'arc
        public float dir;       // +1 : le cap augmente, -1 : il diminue, 0 : pas d'arc
        public float arcLen;    // longueur en Z de l'arc
        public float len;       // longueur totale en Z (arc + droite)
        public float arcEndX;   // décalage X à la fin de l'arc
        public float slope1;    // tan(th1)
    }

    readonly List<Piece> pieces = new List<Piece>();
    readonly Random rng;
    int lastFigure = -1;
    int lastIndex;

    public TougeLayout(int seed)
    {
        rng = seed == 0 ? new Random() : new Random(seed);
    }

    // ---------- Lecture ----------

    /// <summary>X du centre de la route à un z absolu.</summary>
    public float CenterX(double z)
    {
        if (z <= 0.0) return 0f;
        Piece p = pieces[Find(z)];
        return (float)(p.x0 + Offset(p, (float)(z - p.z0)));
    }

    /// <summary>Pente dx/dz de la route à un z absolu (tan du cap).</summary>
    public float Slope(double z)
    {
        if (z <= 0.0) return 0f;
        Piece p = pieces[Find(z)];
        float u = (float)(z - p.z0);
        if (u >= p.arcLen) return p.slope1;
        float th = ArcAngle(p, u);
        return (float)Math.Tan(th);
    }

    static float ArcAngle(in Piece p, float u)
    {
        double s = Math.Sin(p.th0) + p.dir * u / p.r;
        return (float)Math.Asin(Math.Max(-1.0, Math.Min(1.0, s)));
    }

    static float Offset(in Piece p, float u)
    {
        if (u >= p.arcLen) return p.arcEndX + p.slope1 * (u - p.arcLen);
        float th = ArcAngle(p, u);
        return p.dir * p.r * (float)(Math.Cos(p.th0) - Math.Cos(th));
    }

    int Find(double z)
    {
        Ensure(z);

        // La plupart des appels sont proches du précédent
        if (lastIndex < pieces.Count && Contains(lastIndex, z)) return lastIndex;
        if (lastIndex + 1 < pieces.Count && Contains(lastIndex + 1, z)) return ++lastIndex;

        int lo = 0, hi = pieces.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (pieces[mid].z0 <= z) lo = mid; else hi = mid - 1;
        }
        lastIndex = lo;
        return lo;
    }

    bool Contains(int i, double z) => pieces[i].z0 <= z && z < pieces[i].z0 + pieces[i].len;

    // ---------- Génération ----------

    double EndZ => pieces.Count == 0 ? 0.0 : pieces[pieces.Count - 1].z0 + pieces[pieces.Count - 1].len;
    double EndX => pieces.Count == 0 ? 0.0 : pieces[pieces.Count - 1].x0 + Offset(pieces[pieces.Count - 1], pieces[pieces.Count - 1].len);
    float EndAngle => pieces.Count == 0 ? 0f : pieces[pieces.Count - 1].th1;

    void Ensure(double z)
    {
        if (pieces.Count == 0) AddPiece(0f, 1f, straightStart);
        while (EndZ <= z + 1.0) AddFigure();
    }

    float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();

    /// <summary>Rayon de virage faisable à la vitesse prévue à cet endroit.</summary>
    float Radius(double z)
    {
        float v = speedAt != null ? speedAt(z) : 12f;
        return Math.Max(minRadius, v * v / Math.Max(1f, cornerGrip));
    }

    // Sens d'un nouveau virage : opposé au cap actuel (zigzag), sinon au hasard, en ramenant la route vers x = 0
    float PickSide()
    {
        double x = EndX;
        if (x > recenterDistance) return -1f;
        if (x < -recenterDistance) return 1f;
        float a = EndAngle;
        if (Math.Abs(a) > 5f * Deg2Rad) return -Math.Sign(a);
        return rng.NextDouble() < 0.5 ? -1f : 1f;
    }

    void AddFigure()
    {
        double z = EndZ;
        float d = (float)Math.Min(1.0, Math.Max(0.0, z / Math.Max(1f, distanceToMaxDifficulty)));
        float maxA = maxAngleStart + (maxAngleEnd - maxAngleStart) * d;   // degrés
        float r = Radius(z);
        float rush = 1f - 0.4f * d; // les portions droites raccourcissent avec la difficulté

        // Poids de chaque figure : 0 ligne droite, 1 courbe rapide, 2 virage serré, 3 épingle, 4 enchaînement en S
        float[] w =
        {
            lastFigure == 0 ? 0f : 2.5f - 1.8f * d,
            2.5f - 1.5f * d,
            3f,
            0.6f + 2.6f * d,
            0.6f + 2.2f * d,
        };
        int fig = Pick(w);
        lastFigure = fig;

        switch (fig)
        {
            case 0: // ligne droite : un peu de répit
                AddPiece(0f, r * 2f, Rand(25f, 55f) * rush);
                break;

            case 1: // courbe rapide, grand rayon
            {
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                if (Math.Abs(EndX) > recenterDistance) side = -Math.Sign(EndX);
                AddPiece(side * maxA * Rand(0.3f, 0.55f), r * Rand(2.2f, 3.2f), Rand(15f, 35f) * rush);
                break;
            }

            case 2: // virage serré
                AddPiece(PickSide() * maxA * Rand(0.6f, 0.9f), r * Rand(1.15f, 1.6f), Rand(10f, 25f) * rush);
                break;

            case 3: // épingle : on part franchement d'un côté, puis on bascule à fond de l'autre
            {
                float side = PickSide();
                if (Math.Abs(EndAngle) < maxA * 0.7f * Deg2Rad)
                {
                    AddPiece(-side * maxA * Rand(0.85f, 1f), r * 1.4f, Rand(8f, 16f) * rush);
                }
                else side = -Math.Sign(EndAngle);
                AddPiece(side * maxA * Rand(0.9f, 1f), r, Rand(12f, 22f) * rush);
                break;
            }

            case 4: // enchaînement en S : 3 à 5 virages alternés, presque sans ligne droite entre eux
            {
                int n = 3 + rng.Next(3);
                float side = PickSide();
                for (int i = 0; i < n; i++)
                {
                    AddPiece(side * maxA * Rand(0.45f, 0.75f), r * Rand(1f, 1.3f), Rand(2f, 7f));
                    side = -side;
                }
                AddPiece(EndAngle * 0.3f / Deg2Rad, r * 1.5f, Rand(10f, 20f) * rush); // sortie
                break;
            }
        }
    }

    int Pick(float[] w)
    {
        float total = 0f;
        foreach (float x in w) total += x;
        float t = Rand(0f, total);
        for (int i = 0; i < w.Length; i++)
        {
            t -= w[i];
            if (t <= 0f) return i;
        }
        return w.Length - 1;
    }

    /// <summary>Ajoute un arc de rayon r jusqu'au cap angleDeg, puis "hold" mètres (en Z) tout droit à ce cap.</summary>
    void AddPiece(float angleDeg, float r, float hold)
    {
        float limit = Math.Min(70f, Math.Max(maxAngleStart, maxAngleEnd)) * Deg2Rad;
        var p = new Piece
        {
            z0 = EndZ,
            x0 = EndX,
            th0 = EndAngle,
            th1 = Math.Max(-limit, Math.Min(limit, angleDeg * Deg2Rad)),
            r = Math.Max(1f, r),
        };
        p.dir = Math.Sign(p.th1 - p.th0);
        p.arcLen = p.r * (float)Math.Abs(Math.Sin(p.th1) - Math.Sin(p.th0));
        if (p.arcLen < 0.01f) { p.arcLen = 0f; p.dir = 0f; p.th1 = p.th0; }
        p.len = p.arcLen + Math.Max(1f, hold);
        p.slope1 = (float)Math.Tan(p.th1);
        p.arcEndX = p.dir * p.r * (float)(Math.Cos(p.th0) - Math.Cos(p.th1));
        pieces.Add(p);
    }
}
