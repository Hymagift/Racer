using System;
using System.Collections.Generic;

/// <summary>
/// Profil en long de la route : montées, descentes et replats, le long de la distance s.
///
/// La route est découpée en tronçons de pente constante (tirée au hasard, de plus en plus raide avec la difficulté).
/// Entre deux tronçons, la pente change progressivement sur transitionLength mètres (raccord doux) :
/// pas de cassure brutale au sommet d'une bosse, la voiture reste collée à la route.
/// L'altitude reste entre -maxHeight et +maxHeight : plus la route est haute, plus les descentes sont probables.
///
/// Classe C# simple (pas un MonoBehaviour), utilisée par ZigzagRoadGenerator. Entièrement déterministe pour une seed.
/// </summary>
public class ElevationLayout
{
    // ---------- Réglages (renseignés par ZigzagRoadGenerator) ----------

    /// <summary>Distance plate au départ (m).</summary>
    public float startFlat = 80f;
    /// <summary>Pente max (0.1 = 10 %) au début / à la difficulté max.</summary>
    public float maxGradeStart = 0.07f, maxGradeEnd = 0.14f;
    /// <summary>Longueur d'un tronçon de pente constante (m), au hasard entre ces deux valeurs.</summary>
    public float minSectionLength = 60f, maxSectionLength = 180f;
    /// <summary>Longueur du raccord progressif entre deux pentes (m).</summary>
    public float transitionLength = 45f;
    /// <summary>L'altitude de la route reste entre -maxHeight et +maxHeight (m).</summary>
    public float maxHeight = 35f;
    /// <summary>Probabilité qu'un tronçon soit un replat.</summary>
    public float flatChance = 0.2f;
    /// <summary>Difficulté (0..1) à une distance s. Null = 0.</summary>
    public Func<double, float> difficultyAt;

    // ---------- Données ----------

    struct Section
    {
        public double s0, h0; // début et altitude au début
        public float g0, g1;  // pente au début (celle du tronçon précédent) et pente du tronçon
        public float t;       // longueur du raccord (g0 -> g1)
        public float len;
    }

    readonly List<Section> sections = new List<Section>();
    readonly Random rng;
    int lastIndex;

    public ElevationLayout(int seed)
    {
        rng = seed == 0 ? new Random() : new Random(seed * 31337 + 7);
    }

    /// <summary>Altitude de la route à la distance s.</summary>
    public float Height(double s)
    {
        if (s <= 0.0) return 0f;
        Section c = sections[Find(s)];
        return (float)HeightIn(c, s - c.s0);
    }

    /// <summary>Pente de la route (dy / ds) à la distance s.</summary>
    public float Grade(double s)
    {
        if (s <= 0.0) return 0f;
        Section c = sections[Find(s)];
        double u = s - c.s0;
        if (u >= c.t) return c.g1;
        float x = (float)(u / c.t);
        return c.g0 + (c.g1 - c.g0) * x * x * (3f - 2f * x);
    }

    // Intégrale de la pente : pente g0 -> g1 en "smoothstep" sur t mètres, puis constante
    static double HeightIn(in Section c, double u)
    {
        if (c.t <= 0f || u >= c.t)
        {
            double ht = c.t > 0f ? c.g0 * c.t + (c.g1 - c.g0) * c.t * 0.5 : 0.0;
            return c.h0 + ht + c.g1 * (u - c.t);
        }
        double x = u / c.t;
        double integral = x * x * x - 0.5 * x * x * x * x; // intégrale de 3x² - 2x³
        return c.h0 + c.g0 * u + (c.g1 - c.g0) * c.t * integral;
    }

    int Find(double s)
    {
        Ensure(s);
        if (lastIndex < sections.Count && sections[lastIndex].s0 <= s && s < sections[lastIndex].s0 + sections[lastIndex].len) return lastIndex;

        int lo = 0, hi = sections.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (sections[mid].s0 <= s) lo = mid; else hi = mid - 1;
        }
        lastIndex = lo;
        return lo;
    }

    float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();

    void Ensure(double s)
    {
        if (sections.Count == 0)
            sections.Add(new Section { s0 = 0.0, h0 = 0.0, g0 = 0f, g1 = 0f, t = 0f, len = Math.Max(1f, startFlat) });

        for (int guard = 0; guard < 100000; guard++)
        {
            Section last = sections[sections.Count - 1];
            double end = last.s0 + last.len;
            if (end > s + 1.0) break;

            float d = difficultyAt != null ? difficultyAt(end) : 0f;
            float maxG = maxGradeStart + (maxGradeEnd - maxGradeStart) * Math.Max(0f, Math.Min(1f, d));
            double h = HeightIn(last, last.len);

            // Pente au hasard, poussée vers le bas quand la route est haute (et vers le haut quand elle est basse)
            float pull = (float)(-h / Math.Max(1f, maxHeight));
            float g = rng.NextDouble() < flatChance ? Rand(-0.015f, 0.015f) : Rand(-1f, 1f) * maxG;
            g = Math.Max(-maxG, Math.Min(maxG, g + pull * maxG));

            float t = Math.Max(5f, transitionLength) * (0.5f + Math.Abs(g - last.g1) / Math.Max(0.01f, 2f * maxG));
            float len = Math.Max(t + 10f, Rand(minSectionLength, Math.Max(minSectionLength, maxSectionLength)));

            // Ne pas sortir de [-maxHeight, maxHeight] : on raccourcit le tronçon si besoin
            double reach = h + g * (len - t * 0.5);
            if (Math.Abs(reach) > maxHeight && Math.Abs(g) > 0.001f)
                len = Math.Max(t + 10f, (float)((Math.Sign(g) * maxHeight - h) / g + t * 0.5));

            sections.Add(new Section { s0 = end, h0 = h, g0 = last.g1, g1 = g, t = t, len = len });
        }
    }
}
