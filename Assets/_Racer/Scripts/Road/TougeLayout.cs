using System;
using System.Collections.Generic;

/// <summary>
/// Tracé de route TOUGE : une suite serrée de "figures" (épingles, doubles épingles, enchaînements en S, chicanes,
/// virages qui se referment, virages serrés) tirées au hasard, de plus en plus dures avec la distance.
///
/// GAUCHE / DROITE : le sens des virages alterne (gauche, droite, gauche...). Seul le "virage qui se referme"
/// enchaîne deux fois le même sens. La route est ramenée vers le centre en jouant sur l'ANGLE des virages,
/// jamais en ajoutant des virages du même côté.
///
/// La route est une fonction x(z) (le décor défile en Z) : elle ne peut pas revenir en arrière, donc une épingle
/// fait jusqu'à 2 x l'angle max de changement de cap (ex : -60° -> +60° = 120°).
/// Chaque virage est un VRAI arc de cercle de rayon connu, suivi d'une courte portion droite :
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
    public float distanceToMaxDifficulty = 1500f;
    /// <summary>Angle max de la route par rapport à l'axe (degrés) au début / à la difficulté max.</summary>
    public float maxAngleStart = 40f, maxAngleEnd = 60f;
    /// <summary>Rayon de virage minimum absolu (m).</summary>
    public float minRadius = 8f;
    /// <summary>Accélération latérale visée dans les virages serrés (m/s²) : rayon = vitesse² / cornerGrip.</summary>
    public float cornerGrip = 8f;
    /// <summary>Écart latéral (m) à partir duquel les virages ramènent nettement la route vers x = 0.</summary>
    public float recenterDistance = 40f;
    /// <summary>Nombre moyen de figures entre deux courtes lignes droites de répit (0 = jamais de répit).</summary>
    public int figuresBetweenBreaks = 8;
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
        public float dir;       // +1 : le cap augmente (virage à droite), -1 : il diminue (à gauche), 0 : pas d'arc
        public float arcLen;    // longueur en Z de l'arc
        public float len;       // longueur totale en Z (arc + droite)
        public float arcEndX;   // décalage X à la fin de l'arc
        public float slope1;    // tan(th1)
    }

    readonly List<Piece> pieces = new List<Piece>();
    readonly Random rng;
    int lastIndex;
    int lastDir;              // sens du dernier virage (+1 droite, -1 gauche)
    int lastFigure = -1;
    int figuresSinceBreak;

    public TougeLayout(int seed)
    {
        rng = seed == 0 ? new Random() : new Random(seed);
        lastDir = rng.NextDouble() < 0.5 ? -1 : 1;
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
        return (float)Math.Tan(ArcAngle(p, u));
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
    float EndAngleDeg => pieces.Count == 0 ? 0f : pieces[pieces.Count - 1].th1 / Deg2Rad;

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

    float Difficulty => (float)Math.Min(1.0, Math.Max(0.0, EndZ / Math.Max(1f, distanceToMaxDifficulty)));
    float MaxAngle => maxAngleStart + (maxAngleEnd - maxAngleStart) * Difficulty;

    void AddFigure()
    {
        float d = Difficulty;
        float a = MaxAngle;
        float r = Radius(EndZ);

        // 0 virage serré, 1 épingle, 2 double épingle, 3 S, 4 chicane, 5 virage qui se referme, 6 répit
        float[] w =
        {
            2.0f,
            2.0f + 1.5f * d,
            0.4f + 1.6f * d,
            2.0f + 1.0f * d,
            1.5f,
            1.2f,
            figuresBetweenBreaks > 0 && figuresSinceBreak >= figuresBetweenBreaks ? 4f : 0f,
        };
        if (lastFigure >= 0 && lastFigure < w.Length && lastFigure != 3) w[lastFigure] *= 0.5f; // un peu de variété
        int fig = Pick(w);
        lastFigure = fig;
        figuresSinceBreak = fig == 6 ? 0 : figuresSinceBreak + 1;

        switch (fig)
        {
            case 0: // virage serré
                Turn(NextDir(), 0.7f * a, 1.5f * a, r * Rand(1.1f, 1.4f), Rand(3f, 9f));
                break;

            case 1: // épingle : bascule d'un bord à l'autre (jusqu'à 2 x l'angle max)
                Hairpin(a, r, Rand(3f, 9f));
                break;

            case 2: // double épingle : gauche-droite (ou droite-gauche) collées
                Hairpin(a, r, Rand(2f, 6f));
                Hairpin(a, r, Rand(3f, 9f));
                break;

            case 3: // enchaînement en S : 3 à 6 virages alternés, presque sans ligne droite
            {
                int n = 3 + rng.Next(3 + (int)Math.Round(d));
                for (int i = 0; i < n; i++)
                    Turn(NextDir(), 0.6f * a, 1.3f * a, r * Rand(1f, 1.25f), Rand(0.5f, 4f));
                break;
            }

            case 4: // chicane : deux coups de volant secs et rapprochés
                Turn(NextDir(), 0.6f * a, 1.0f * a, r * Rand(0.9f, 1.1f), Rand(0f, 2f));
                Turn(NextDir(), 0.6f * a, 1.0f * a, r * Rand(0.9f, 1.1f), Rand(3f, 8f));
                break;

            case 5: // virage qui se referme : rayon large puis serré, dans le MÊME sens
            {
                int dir = NextDir();
                if (Turn(dir, 0.35f * a, 0.6f * a, r * 2f, Rand(0f, 3f)))
                    Turn(dir, 0.35f * a, 1.0f * a, r, Rand(3f, 8f), keepDir: true, cross: false);
                break;
            }

            case 6: // répit : courte ligne droite (le cap revient presque dans l'axe)
                Turn(-Math.Sign(EndAngleDeg) == 0 ? NextDir() : -Math.Sign(EndAngleDeg), 0f, Math.Abs(EndAngleDeg), r * 1.5f, Rand(12f, 22f), toward: 0f, cross: false);
                break;
        }
    }

    // Sens du prochain virage : toujours l'inverse du précédent (gauche, droite, gauche...)
    int NextDir() => -lastDir;

    void Hairpin(float a, float r, float hold)
    {
        int dir = NextDir();
        // Il faut partir du bord opposé : si le cap ne le permet pas, on y va d'abord (c'est le virage "d'entrée")
        float room = dir > 0 ? a - EndAngleDeg : EndAngleDeg + a;
        if (room < 1.5f * a)
        {
            Turn(-dir, 0.3f * a, 2f * a, r * 1.3f, Rand(3f, 8f), toward: -dir * a * Rand(0.85f, 1f));
            dir = NextDir();
        }
        Turn(dir, 1.5f * a, 2f * a, r, hold, toward: dir * a);
    }

    /// <summary>
    /// Virage dans le sens dir (+1 droite, -1 gauche) qui change le cap de minDelta à maxDelta degrés,
    /// sans dépasser l'angle max. Renvoie faux si c'était impossible (le sens est alors inversé).
    /// toward : cap visé (sinon on choisit pour ramener la route vers le centre).
    /// cross : la route doit passer de l'autre côté de l'axe (un virage à droite finit orienté vers la droite) :
    /// sinon gauche-droite-gauche peut garder la route orientée du même côté pendant longtemps.
    /// </summary>
    bool Turn(int dir, float minDelta, float maxDelta, float r, float hold, float? toward = null, bool keepDir = false, bool cross = true)
    {
        float a = MaxAngle;
        float cur = EndAngleDeg;

        // Cap final possible dans ce sens
        float lo = dir > 0 ? cur + minDelta : Math.Max(-a, cur - maxDelta);
        float hi = dir > 0 ? Math.Min(a, cur + maxDelta) : cur - minDelta;
        if (cross)
        {
            float lo2 = dir > 0 ? Math.Max(lo, 0.3f * a) : lo;
            float hi2 = dir > 0 ? hi : Math.Min(hi, -0.3f * a);
            if (lo2 <= hi2) { lo = lo2; hi = hi2; }
        }
        bool ok = lo <= hi;
        if (!ok)
        {
            // Pas la place (déjà au bord) : on tourne dans l'autre sens
            dir = -dir;
            lo = dir > 0 ? cur + minDelta : Math.Max(-a, cur - maxDelta);
            hi = dir > 0 ? Math.Min(a, cur + maxDelta) : cur - minDelta;
            lo = Math.Max(-a, lo); hi = Math.Min(a, hi);
            if (lo > hi) { lo = hi = dir * a; }
        }

        float target;
        if (toward.HasValue)
        {
            target = Math.Max(lo, Math.Min(hi, toward.Value));
        }
        else
        {
            // Cap qui ramènerait la route vers x = 0 ; on garde le meilleur de 3 tirages si la route s'éloigne
            double x = EndX;
            float ideal = (float)Math.Max(-a, Math.Min(a, -x / Math.Max(1f, recenterDistance) * a));
            target = Rand(lo, hi);
            if (Math.Abs(x) > 5.0)
                for (int i = 0; i < 2; i++)
                {
                    float t = Rand(lo, hi);
                    if (Math.Abs(t - ideal) < Math.Abs(target - ideal)) target = t;
                }
        }

        // Portion droite plus courte quand elle éloigne la route du centre, plus longue quand elle l'y ramène
        double ex = EndX;
        if (Math.Abs(ex) > 10.0 && Math.Abs(target) > 5f)
            hold *= Math.Sign(target) == Math.Sign(ex) ? 0.5f : 1.4f;

        AddPiece(target, r, hold);
        if (!keepDir || !ok) lastDir = dir;
        return ok;
    }

    int Pick(float[] w)
    {
        float total = 0f;
        foreach (float x in w) total += x;
        float t = Rand(0f, total);
        for (int i = 0; i < w.Length; i++)
        {
            t -= w[i];
            if (t <= 0f && w[i] > 0f) return i;
        }
        return 0;
    }

    /// <summary>Ajoute un arc de rayon r jusqu'au cap angleDeg, puis "hold" mètres (en Z) tout droit à ce cap.</summary>
    void AddPiece(float angleDeg, float r, float hold)
    {
        float limit = Math.Min(70f, Math.Max(maxAngleStart, maxAngleEnd)) * Deg2Rad;
        var p = new Piece
        {
            z0 = EndZ,
            x0 = EndX,
            th0 = pieces.Count == 0 ? 0f : pieces[pieces.Count - 1].th1,
            th1 = Math.Max(-limit, Math.Min(limit, angleDeg * Deg2Rad)),
            r = Math.Max(1f, r),
        };
        p.dir = Math.Sign(p.th1 - p.th0);
        p.arcLen = p.r * (float)Math.Abs(Math.Sin(p.th1) - Math.Sin(p.th0));
        if (p.arcLen < 0.01f) { p.arcLen = 0f; p.dir = 0f; p.th1 = p.th0; }
        p.len = p.arcLen + Math.Max(0.5f, hold);
        p.slope1 = (float)Math.Tan(p.th1);
        p.arcEndX = p.dir * p.r * (float)(Math.Cos(p.th0) - Math.Cos(p.th1));
        pieces.Add(p);
    }
}
