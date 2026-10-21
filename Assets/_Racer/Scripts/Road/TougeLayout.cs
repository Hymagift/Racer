using System;
using System.Collections.Generic;

/// <summary>
/// Tracé de route TOUGE : une suite serrée de "figures" (épingles, doubles épingles, enchaînements en S, chicanes,
/// virages qui se referment, virages serrés, VRAIES épingles à 180° et lacets) tirées au hasard, de plus en plus dures avec la distance.
///
/// GAUCHE / DROITE : le sens des virages alterne (gauche, droite, gauche...). Seul le "virage qui se referme"
/// enchaîne deux fois le même sens. La route est ramenée vers le centre en jouant sur l'ANGLE des virages,
/// jamais en ajoutant des virages du même côté.
///
/// La route est une courbe paramétrée par sa LONGUEUR s (distance parcourue le long de la route) :
/// chaque pièce est un VRAI arc de cercle de rayon connu, suivi d'une portion droite.
/// Le cap reste toujours entre -90° et +90° (0 = vers +Z) : la route ne recule jamais en Z, donc elle ne peut pas se recouper,
/// mais elle peut rouler en travers (branches de lacets) et faire de vrais demi-tours (de +90° à -90° = 180°).
/// Le rayon est choisi pour que le virage reste faisable à la vitesse de la voiture à cet endroit ;
/// avant une épingle à 180°, une zone de ralentissement (SpeedFactor) fait lever le pied à la voiture.
///
/// Classe C# simple (pas un MonoBehaviour), utilisée par ZigzagRoadGenerator. Entièrement déterministe pour une seed.
/// Coordonnées : x et z absolus (z depuis le départ, sans les rebases), s = longueur depuis le départ.
/// </summary>
public class TougeLayout
{
    // ---------- Réglages (renseignés par ZigzagRoadGenerator) ----------

    /// <summary>Ligne droite au départ (m).</summary>
    public float straightStart = 50f;
    /// <summary>Distance pour atteindre la difficulté max. La difficulté monte vite au début puis plus doucement.</summary>
    public float distanceToMaxDifficulty = 900f;
    /// <summary>Forme de la montée : 1 = linéaire, plus petit = difficile plus tôt.</summary>
    public float difficultyCurve = 0.75f;
    /// <summary>Angle max de la route par rapport à l'axe (degrés) au début / à la difficulté max (hors épingles à 180°).</summary>
    public float maxAngleStart = 40f, maxAngleEnd = 66f;
    /// <summary>Rayon de virage minimum absolu (m), au début / à la difficulté max.</summary>
    public float minRadius = 9f, minRadiusEnd = 6f;
    /// <summary>Accélération latérale visée dans les virages (m/s²) au début / à la difficulté max : rayon = vitesse² / cornerGrip.</summary>
    public float cornerGrip = 6.5f, cornerGripEnd = 10.5f;
    /// <summary>Longueur des portions droites entre deux virages, au début / à la difficulté max (x la longueur normale).</summary>
    public float holdScaleStart = 1.3f, holdScaleEnd = 0.35f;
    /// <summary>Écart latéral (m) à partir duquel les virages ramènent nettement la route vers x = 0.</summary>
    public float recenterDistance = 40f;
    /// <summary>Figures entre deux lignes droites de répit, au début / à la difficulté max (0 = jamais de répit).</summary>
    public int figuresBetweenBreaks = 5, figuresBetweenBreaksEnd = 18;
    /// <summary>Longueur d'une ligne droite de répit (m) au début / à la difficulté max.</summary>
    public float breakLengthStart = 24f, breakLengthEnd = 8f;
    /// <summary>Largeur de la route au début / à la difficulté max (pour que le bord intérieur d'une épingle reste un vrai virage).</summary>
    public float roadWidthStart = 9f, roadWidthEnd = 6f;

    /// <summary>
    /// Variété des rayons (0 = chaque virage au rayon "juste faisable", 1 = beaucoup de virages plus larges, de toutes tailles).
    /// Les virages ne sont jamais plus serrés que le rayon faisable : la variété ne fait qu'en élargir certains.
    /// </summary>
    public float radiusVariety = 0.6f;

    /// <summary>Fréquence des épingles à 180° et des lacets (0 = jamais, 1 = normal, 2 = deux fois plus).</summary>
    public float hairpinFrequency = 1f;
    /// <summary>Pas d'épingle à 180° avant cette distance (m).</summary>
    public float hairpinStartDistance = 250f;
    /// <summary>Cap des branches d'une épingle (degrés) : 90 = vrai demi-tour de 180°, 75 = épingle de 150°.</summary>
    public float hairpinHeading = 90f;
    /// <summary>Accélération latérale visée dans une épingle (m/s²) : rayon = vitesse² / hairpinGrip.</summary>
    public float hairpinGrip = 7f;
    /// <summary>Rayon minimum d'une épingle (m), mesuré au centre de la route.</summary>
    public float minHairpinRadius = 10f;
    /// <summary>Vitesse visée dans une épingle (x la vitesse normale). 1 = la voiture ne ralentit pas (épingles très larges).</summary>
    public float hairpinSlowdown = 0.7f;
    /// <summary>Décélération (m/s²) supposée pour placer le début du ralentissement avant une épingle.</summary>
    public float brakeDecel = 2.5f;
    /// <summary>Longueur des branches entre deux épingles (m, le long de la route).</summary>
    public float legLengthMin = 22f, legLengthMax = 45f;
    /// <summary>Nombre max d'épingles enchaînées dans des lacets.</summary>
    public int maxLacets = 4;

    /// <summary>Vitesse visée par la voiture (m/s) à une distance s, pour calculer les rayons. Null = 12 m/s.</summary>
    public Func<double, float> speedAt;
    /// <summary>Plus petit rayon que la voiture peut prendre en braquant à fond à une vitesse donnée (m). Null = pas de limite.</summary>
    public Func<float, float> minTurnRadiusAt;

    // ---------- Données ----------

    const float Deg2Rad = (float)(Math.PI / 180.0);

    // Une pièce = un arc de cercle (de th0 à th1, rayon r) puis une portion droite au cap th1
    struct Piece
    {
        public double s0, x0, z0; // début de la pièce
        public double zEnd;       // z à la fin de la pièce (la route ne recule jamais en Z)
        public float th0, th1;    // cap au début / à la fin de l'arc (rad, 0 = vers +Z, + = vers +X)
        public float k;           // courbure signée de l'arc (1/r, + = le cap augmente = virage à droite), 0 = pas d'arc
        public float arcLen;      // longueur de l'arc
        public float len;         // longueur totale (arc + droite)
        public double ex, ez;     // fin de l'arc
    }

    struct SlowZone
    {
        public double start, arcStart, end; // freinage de start à arcStart, vitesse réduite jusqu'à end
        public float factor;
    }

    readonly List<Piece> pieces = new List<Piece>();
    readonly List<SlowZone> slowZones = new List<SlowZone>();
    readonly Random rng;
    int lastIndex;
    int lastDir;              // sens du dernier virage (+1 droite, -1 gauche)
    int lastFigure = -1;
    readonly int[] recentFigures = { -1, -1, -1, -1 }; // dernières figures (anti-répétition)
    int figuresSinceBreak;

    public TougeLayout(int seed)
    {
        rng = seed == 0 ? new Random() : new Random(seed);
        lastDir = rng.NextDouble() < 0.5 ? -1 : 1;
    }

    // ---------- Lecture ----------

    /// <summary>Position (x, z absolus) et cap (rad) du centre de la route à la distance s.</summary>
    public void Sample(double s, out double x, out double z, out float heading)
    {
        if (s <= 0.0) { x = 0.0; z = s; heading = 0f; return; }
        Piece p = pieces[Find(s)];
        PosAt(p, s - p.s0, out x, out z, out heading);
    }

    /// <summary>Courbure de la route (1/rayon, + = virage à droite) à la distance s.</summary>
    public float Curvature(double s)
    {
        if (s <= 0.0) return 0f;
        Piece p = pieces[Find(s)];
        return s - p.s0 < p.arcLen ? p.k : 0f;
    }

    /// <summary>
    /// Vitesse visée à la distance s, en fraction de la vitesse normale : moins de 1 juste avant et dans une épingle à 180°.
    /// </summary>
    public float SpeedFactorAt(double s)
    {
        if (s <= 0.0) return 1f;
        Ensure(s);
        float f = 1f;
        for (int i = slowZones.Count - 1; i >= 0; i--)
        {
            SlowZone z = slowZones[i];
            if (z.end + 2000.0 < s) break; // zones ajoutées dans l'ordre : les suivantes sont toutes derrière
            if (s >= z.start && s <= z.end) f = Math.Min(f, z.factor);
        }
        return f;
    }

    static void PosAt(in Piece p, double u, out double x, out double z, out float heading)
    {
        if (u < p.arcLen)
        {
            double th = p.th0 + p.k * u;
            x = p.x0 + (Math.Cos(p.th0) - Math.Cos(th)) / p.k;
            z = p.z0 + (Math.Sin(th) - Math.Sin(p.th0)) / p.k;
            heading = (float)th;
            return;
        }
        double t = u - p.arcLen;
        x = p.ex + Math.Sin(p.th1) * t;
        z = p.ez + Math.Cos(p.th1) * t;
        heading = p.th1;
    }

    /// <summary>
    /// Point de la route le plus proche de (x, z) (coordonnées absolues), parmi les morceaux de route dont le z est à moins
    /// de range mètres. Renvoie sa distance s, et la distance au carré dans dist2 (infini si rien trouvé).
    /// </summary>
    public double NearestByZ(double x, double z, double range, out double dist2)
    {
        EnsureZ(z + range);
        dist2 = double.PositiveInfinity;
        double best = 0.0;

        // Premier morceau qui atteint z - range (les z de début et de fin sont croissants)
        int lo = 0, hi = pieces.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (pieces[mid].zEnd >= z - range) hi = mid; else lo = mid + 1;
        }
        for (int i = lo; i < pieces.Count && pieces[i].z0 <= z + range; i++)
        {
            double u = NearestU(pieces[i], x, z, 0.0, pieces[i].len, out double d2);
            if (d2 < dist2) { dist2 = d2; best = pieces[i].s0 + u; }
        }
        if (z < 0.0) // avant le départ : la route est droite sur x = 0
        {
            double d2 = x * x;
            if (d2 < dist2) { dist2 = d2; best = z; }
        }
        return best;
    }

    /// <summary>Point de la route le plus proche de (x, z) entre les distances sMin et sMax. Renvoie s (et la distance au carré).</summary>
    public double NearestInRange(double x, double z, double sMin, double sMax, out double dist2)
    {
        dist2 = double.PositiveInfinity;
        double best = Math.Max(0.0, sMin);
        if (sMin < 0.0)
        {
            // Avant le départ : droite sur x = 0
            double zz = Math.Max(sMin, Math.Min(0.0, z));
            dist2 = x * x + (z - zz) * (z - zz);
            best = zz;
            sMin = 0.0;
        }
        if (sMax <= sMin) return best;
        Ensure(sMax);
        for (int i = Find(sMin); i < pieces.Count && pieces[i].s0 <= sMax; i++)
        {
            Piece p = pieces[i];
            double u = NearestU(p, x, z, Math.Max(0.0, sMin - p.s0), Math.Min(p.len, sMax - p.s0), out double d2);
            if (d2 < dist2) { dist2 = d2; best = p.s0 + u; }
        }
        return best;
    }

    // Abscisse (dans la pièce, entre uMin et uMax) du point le plus proche de (qx, qz)
    static double NearestU(in Piece p, double qx, double qz, double uMin, double uMax, out double dist2)
    {
        double bestU = uMin;
        dist2 = double.PositiveInfinity;

        Try(p, uMin, uMin, uMax, qx, qz, ref bestU, ref dist2);
        Try(p, uMax, uMin, uMax, qx, qz, ref bestU, ref dist2);

        if (p.arcLen > 0f && uMin < p.arcLen)
        {
            // Centre du cercle, puis angle du point visé vu du centre
            double cx = p.x0 + Math.Cos(p.th0) / p.k;
            double cz = p.z0 - Math.Sin(p.th0) / p.k;
            double vx = qx - cx, vz = qz - cz;
            if (vx * vx + vz * vz > 1e-8)
            {
                double sg = Math.Sign(p.k);
                double th = Math.Atan2(sg * vz, -sg * vx);
                double delta = th - p.th0;
                while (delta > Math.PI) delta -= 2.0 * Math.PI;
                while (delta < -Math.PI) delta += 2.0 * Math.PI;
                double u = delta / p.k;
                if (u >= 0.0 && u <= p.arcLen) Try(p, u, uMin, uMax, qx, qz, ref bestU, ref dist2);
            }
            Try(p, p.arcLen, uMin, uMax, qx, qz, ref bestU, ref dist2);
        }

        if (uMax > p.arcLen)
        {
            double dx = Math.Sin(p.th1), dz = Math.Cos(p.th1);
            double t = (qx - p.ex) * dx + (qz - p.ez) * dz;
            Try(p, p.arcLen + Math.Max(0.0, t), uMin, uMax, qx, qz, ref bestU, ref dist2);
        }
        return bestU;
    }

    // Essaie le point d'abscisse u (ramenée entre uMin et uMax) : garde le plus proche de (qx, qz)
    static void Try(in Piece p, double u, double uMin, double uMax, double qx, double qz, ref double bestU, ref double bestD)
    {
        u = Math.Max(uMin, Math.Min(uMax, u));
        PosAt(p, u, out double px, out double pz, out _);
        double d = (px - qx) * (px - qx) + (pz - qz) * (pz - qz);
        if (d < bestD) { bestD = d; bestU = u; }
    }

    int Find(double s)
    {
        Ensure(s);

        // La plupart des appels sont proches du précédent
        if (lastIndex < pieces.Count && Contains(lastIndex, s)) return lastIndex;
        if (lastIndex + 1 < pieces.Count && Contains(lastIndex + 1, s)) return ++lastIndex;

        int lo = 0, hi = pieces.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (pieces[mid].s0 <= s) lo = mid; else hi = mid - 1;
        }
        lastIndex = lo;
        return lo;
    }

    bool Contains(int i, double s) => pieces[i].s0 <= s && s < pieces[i].s0 + pieces[i].len;

    // ---------- Génération ----------

    Piece Last => pieces[pieces.Count - 1];
    double EndS => pieces.Count == 0 ? 0.0 : Last.s0 + Last.len;
    double EndX { get { if (pieces.Count == 0) return 0.0; PosAt(Last, Last.len, out double x, out _, out _); return x; } }
    double EndZ => pieces.Count == 0 ? 0.0 : Last.zEnd;
    float EndAngleDeg => pieces.Count == 0 ? 0f : Last.th1 / Deg2Rad;

    void Ensure(double s)
    {
        if (pieces.Count == 0) AddPieceS(0f, 1f, straightStart);
        for (int guard = 0; EndS <= s + 1.0 && guard < 100000; guard++) AddFigure();
    }

    void EnsureZ(double z)
    {
        if (pieces.Count == 0) AddPieceS(0f, 1f, straightStart);
        for (int guard = 0; EndZ <= z + 1.0 && guard < 100000; guard++) AddFigure();
    }

    float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();

    static float Lerp(float a, float b, float t) => a + (b - a) * Math.Max(0f, Math.Min(1f, t));

    /// <summary>Difficulté (0 = départ, 1 = max) à une distance s. Sert aussi à la largeur de la route.</summary>
    public float DifficultyAt(double s)
    {
        double t = Math.Min(1.0, Math.Max(0.0, s / Math.Max(1f, distanceToMaxDifficulty)));
        return (float)Math.Pow(t, Math.Max(0.1f, difficultyCurve));
    }

    float SpeedAt(double s) => speedAt != null ? speedAt(s) : 12f;

    /// <summary>Rayon de virage faisable à la vitesse prévue à cet endroit (de plus en plus serré avec la difficulté).</summary>
    float Radius(double s)
    {
        float d = DifficultyAt(s);
        float v = SpeedAt(s);
        float grip = Lerp(cornerGrip, cornerGripEnd, d);
        float r = Math.Max(Lerp(minRadius, minRadiusEnd, d), v * v / Math.Max(1f, grip));
        if (minTurnRadiusAt != null) r = Math.Max(r, minTurnRadiusAt(v) * 1.1f);
        return r;
    }

    /// <summary>Rayon d'une épingle à 180° : à la vitesse réduite (plus une marge : la voiture ralentit en retard), jamais plus serré que la voiture.</summary>
    float HairpinRadius(double s)
    {
        float v = SpeedAt(s) * Math.Max(0.3f, Math.Min(1f, hairpinSlowdown)) + 1.5f;
        float halfWidth = Lerp(roadWidthStart, roadWidthEnd, DifficultyAt(s)) * 0.5f;
        float r = Math.Max(minHairpinRadius, v * v / Math.Max(1f, hairpinGrip));
        if (minTurnRadiusAt != null) r = Math.Max(r, minTurnRadiusAt(v) * 1.2f);
        return Math.Max(r, halfWidth + 3f); // le bord intérieur reste un virage, pas un point
    }

    // Multiplicateur de rayon au hasard (>= 1) : la plupart des virages restent proches du rayon de base,
    // certains deviennent nettement plus larges (courbes rapides). Tirage biaisé vers 1 (puissance 2).
    float RV()
    {
        float t = (float)rng.NextDouble();
        return 1f + t * t * 2.2f * Math.Max(0f, radiusVariety);
    }

    float Difficulty => DifficultyAt(EndS);
    float MaxAngle => Lerp(maxAngleStart, maxAngleEnd, Difficulty);

    // Longueur d'une portion droite : de plus en plus courte avec la difficulté
    float Hold(float min, float max) => Rand(min, max) * Lerp(holdScaleStart, holdScaleEnd, Difficulty);

    void AddFigure()
    {
        float d = Difficulty;
        float a = MaxAngle;
        float r = Radius(EndS);
        int breakEvery = figuresBetweenBreaks <= 0 ? 0 : (int)Math.Round(Lerp(figuresBetweenBreaks, Math.Max(figuresBetweenBreaks, figuresBetweenBreaksEnd), d));
        bool hairpinsOk = hairpinFrequency > 0f && EndS >= hairpinStartDistance;

        // 0 virage serré, 1 épingle large, 2 double épingle large, 3 S, 4 chicane, 5 virage qui se referme, 6 répit,
        // 7 épingle à 180° (demi-tour), 8 lacets (plusieurs épingles à 180° enchaînées),
        // 9 grande courbe rapide, 10 virage qui s'ouvre (serré puis large, même sens), 11 courbe en plusieurs rayons
        // Avec la difficulté : moins de virages simples, beaucoup plus d'épingles, de S et de chicanes
        float[] w =
        {
            Lerp(2.5f, 0.8f, d),
            Lerp(1.5f, 3.5f, d),
            Lerp(0.2f, 2.8f, d),
            Lerp(1.5f, 3.5f, d),
            Lerp(1.0f, 2.5f, d),
            Lerp(1.0f, 1.5f, d),
            breakEvery > 0 && figuresSinceBreak >= breakEvery ? 6f : 0f,
            hairpinsOk ? hairpinFrequency * Lerp(0.8f, 1.6f, d) : 0f,
            hairpinsOk && maxLacets >= 2 ? hairpinFrequency * Lerp(0.1f, 1.2f, d) : 0f,
            Lerp(1.6f, 1.0f, d) * (0.4f + radiusVariety),
            Lerp(0.8f, 1.4f, d),
            Lerp(0.6f, 1.2f, d) * (0.4f + radiusVariety),
        };
        // Anti-répétition : une figure déjà vue parmi les 4 dernières est nettement moins probable
        foreach (int f in recentFigures)
            if (f >= 0 && f < w.Length && f != 6) w[f] *= 0.4f;
        if (lastFigure == 7 || lastFigure == 8) { w[7] *= 0.4f; w[8] *= 0.4f; }               // pas d'épingles à la chaîne
        int fig = Pick(w);
        lastFigure = fig;
        Array.Copy(recentFigures, 0, recentFigures, 1, recentFigures.Length - 1);
        recentFigures[0] = fig;
        figuresSinceBreak = fig == 6 ? 0 : figuresSinceBreak + 1;

        switch (fig)
        {
            case 0: // virage serré
                Turn(NextDir(), Rand(0.5f, 0.8f) * a, Rand(1.1f, 1.6f) * a, r * Rand(1.05f, 1.3f) * RV(), Hold(3f, 9f));
                break;

            case 1: // épingle large : bascule d'un bord à l'autre (jusqu'à 2 x l'angle max)
                Hairpin(a, r * RV(), Hold(3f, 9f));
                break;

            case 2: // double épingle large : gauche-droite (ou droite-gauche) collées
                Hairpin(a, r * Rand(1f, 1.3f), Hold(2f, 6f));
                Hairpin(a, r * RV(), Hold(3f, 9f));
                break;

            case 3: // enchaînement en S : de 3 virages au début jusqu'à 7 à la difficulté max, presque sans ligne droite
            {
                int n = 3 + rng.Next(1 + (int)Math.Round(4f * d));
                float rs = r * RV(); // un même S garde à peu près son rythme, mais chaque S a le sien
                for (int i = 0; i < n; i++)
                    Turn(NextDir(), Rand(0.4f, 0.7f) * a, Rand(1f, 1.4f) * a, rs * Rand(0.9f, 1.3f), Hold(0.5f, 4f));
                break;
            }

            case 4: // chicane : deux coups de volant secs et rapprochés
                Turn(NextDir(), 0.6f * a, 1.0f * a, r * Rand(0.9f, 1.1f), Hold(0f, 2f));
                Turn(NextDir(), 0.6f * a, 1.0f * a, r * Rand(0.9f, 1.1f), Hold(3f, 8f));
                break;

            case 5: // virage qui se referme : rayon large puis serré, dans le MÊME sens
            {
                int dir = NextDir();
                if (Turn(dir, 0.35f * a, 0.6f * a, r * Rand(1.7f, 2.8f), Hold(0f, 3f)))
                    Turn(dir, 0.35f * a, 1.0f * a, r * Rand(1f, 1.2f), Hold(3f, 8f), keepDir: true, cross: false);
                break;
            }

            case 6: // répit : ligne droite (de plus en plus courte), le cap revient presque dans l'axe
            {
                float len = Lerp(breakLengthStart, breakLengthEnd, d) * Rand(0.8f, 1.2f);
                Turn(-Math.Sign(EndAngleDeg) == 0 ? NextDir() : -Math.Sign(EndAngleDeg), 0f, Math.Abs(EndAngleDeg), r * 1.5f, len, toward: 0f, cross: false);
                break;
            }

            case 7: // épingle à 180° : la route part en travers, fait demi-tour et revient en travers dans l'autre sens
                UTurns(1);
                break;

            case 8: // lacets : 2 à maxLacets épingles à 180° enchaînées, séparées par des branches en travers
                UTurns(2 + rng.Next(Math.Max(1, maxLacets - 1)));
                break;

            case 9: // grande courbe rapide : rayon 2 à 4 fois plus large, changement de cap modéré, sortie longue
                Turn(NextDir(), Rand(0.4f, 0.7f) * a, Rand(0.9f, 1.4f) * a, r * Rand(2f, 4f), Hold(5f, 14f));
                break;

            case 10: // virage qui s'ouvre : serré au début puis large, dans le MÊME sens (l'inverse du virage qui se referme)
            {
                int dir = NextDir();
                if (Turn(dir, 0.35f * a, 0.8f * a, r * Rand(1f, 1.15f), Hold(0f, 2f)))
                    Turn(dir, 0.25f * a, 0.7f * a, r * Rand(2f, 3.2f), Hold(4f, 10f), keepDir: true, cross: false);
                break;
            }

            case 11: // courbe en plusieurs rayons : 2 ou 3 morceaux dans le même sens, chacun à son rayon
            {
                int dir = NextDir();
                int n = 2 + rng.Next(2);
                bool ok = true;
                for (int i = 0; i < n && ok; i++)
                    ok = Turn(dir, 0.2f * a, 0.6f * a, r * Rand(1f, 2.6f), Hold(0f, 3f), keepDir: i > 0, cross: false);
                break;
            }
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
    /// Une ou plusieurs épingles à 180° : virage d'entrée jusqu'au cap ±hairpinHeading (la route roule en travers),
    /// puis chaque épingle fait passer le cap de +H à -H (ou l'inverse) en passant par "tout droit" : la route avance en Z
    /// de 2 x le rayon à chaque demi-tour. Pour finir, un virage ramène la route dans l'axe.
    /// </summary>
    void UTurns(int count)
    {
        float a = MaxAngle;
        float h = Math.Max(45f, Math.Min(90f, hairpinHeading));
        float cur = EndAngleDeg;
        double x = EndX;

        // Côté vers lequel part la première branche (+1 = vers +X) : celui où la route penche déjà, sinon vers le centre
        int side;
        if (Math.Abs(cur) > 15f) side = Math.Sign(cur);
        else if (Math.Abs(x) > 5.0) side = x > 0.0 ? -1 : 1;
        else side = rng.NextDouble() < 0.5 ? -1 : 1;

        // Entrée : virage normal jusqu'au cap de la branche, puis la branche
        AddPieceS(side * h, Radius(EndS) * 1.15f, Leg(side));

        for (int i = 0; i < count; i++)
        {
            side = -side;
            double sStart = EndS;
            float v = SpeedAt(sStart);
            float r = HairpinRadius(sStart);
            bool last = i == count - 1;
            AddPieceS(side * h, r, last ? Rand(4f, 9f) : Leg(side));

            // Ralentissement : freinage avant l'épingle, vitesse réduite jusqu'à la sortie de l'arc
            float f = Math.Max(0.3f, Math.Min(1f, hairpinSlowdown));
            if (f < 0.999f)
            {
                float vh = v * f;
                double brake = (v * v - vh * vh) / (2.0 * Math.Max(0.5f, brakeDecel)) + 0.5 * v;
                slowZones.Add(new SlowZone { start = sStart - brake, arcStart = sStart, end = sStart + Last.arcLen, factor = f });
            }
        }

        // Sortie : retour dans l'axe (petit angle au hasard), puis une courte ligne droite
        float exit = Rand(-0.25f, 0.25f) * a;
        AddPieceS(exit, Radius(EndS) * 1.15f, Hold(4f, 10f) / Math.Max(0.3f, (float)Math.Cos(exit * Deg2Rad)));
        lastDir = -side; // le dernier virage ramenait le cap de side * h vers l'axe
    }

    // Longueur d'une branche en travers : plus courte si elle éloigne la route du centre alors qu'elle en est déjà loin
    float Leg(int side)
    {
        float len = Rand(legLengthMin, Math.Max(legLengthMin, legLengthMax)) * Lerp(1.15f, 0.85f, Difficulty);
        double x = EndX;
        if (side * x > recenterDistance * 0.5) len *= 0.5f;
        else if (side * x < -recenterDistance * 0.5) len *= 1.3f;
        return Math.Max(4f, len);
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

    /// <summary>
    /// Ajoute un arc de rayon r jusqu'au cap angleDeg, puis "holdZ" mètres EN Z tout droit à ce cap
    /// (comme l'ancien tracé x(z) : les diagonales gardent la même longueur à l'écran).
    /// </summary>
    void AddPiece(float angleDeg, float r, float holdZ)
    {
        float th = Math.Min(70f, Math.Abs(angleDeg)) * Deg2Rad;
        AddPieceS(angleDeg, r, holdZ / Math.Max(0.3f, (float)Math.Cos(th)));
    }

    /// <summary>Ajoute un arc de rayon r jusqu'au cap angleDeg (entre -90 et 90), puis "hold" mètres tout droit (le long de la route).</summary>
    void AddPieceS(float angleDeg, float r, float hold)
    {
        float limit = 90f * Deg2Rad;
        var p = new Piece
        {
            s0 = EndS,
            th0 = pieces.Count == 0 ? 0f : Last.th1,
            th1 = Math.Max(-limit, Math.Min(limit, angleDeg * Deg2Rad)),
        };
        if (pieces.Count > 0)
        {
            PosAt(Last, Last.len, out p.x0, out p.z0, out _);
        }
        r = Math.Max(1f, r);
        float delta = p.th1 - p.th0;
        p.arcLen = r * Math.Abs(delta);
        if (p.arcLen < 0.01f) { p.arcLen = 0f; p.k = 0f; p.th1 = p.th0; }
        else p.k = Math.Sign(delta) / r;
        p.len = p.arcLen + Math.Max(0.5f, hold);
        if (p.arcLen > 0f)
        {
            p.ex = p.x0 + (Math.Cos(p.th0) - Math.Cos(p.th1)) / p.k;
            p.ez = p.z0 + (Math.Sin(p.th1) - Math.Sin(p.th0)) / p.k;
        }
        else { p.ex = p.x0; p.ez = p.z0; }
        p.zEnd = p.ez + Math.Cos(p.th1) * (p.len - p.arcLen);
        pieces.Add(p);
    }
}
