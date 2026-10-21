using System;
using System.Collections.Generic;

/// <summary>
/// De quel côté de la route se trouve le vide (la falaise), le long de la route.
///
/// La route alterne des zones "vide à gauche / montagne à droite" et "vide à droite / montagne à gauche".
/// Entre deux zones, un PONT franchit une gorge : du vide des deux côtés.
///   ... vide à gauche ... | la montagne de droite s'enfonce | PONT (vide des deux côtés) | la montagne remonte à gauche | ... vide à droite ...
///
/// Classe C# simple (pas un MonoBehaviour), utilisée par ZigzagRoadGenerator. Entièrement déterministe pour une seed.
/// Les distances sont des z absolus (depuis le départ).
/// </summary>
public class CliffLayout
{
    // ---------- Réglages (renseignés par ZigzagRoadGenerator) ----------

    /// <summary>Côté du vide au départ : -1 = gauche, 1 = droite.</summary>
    public int startSide = -1;
    /// <summary>Faux = le vide reste toujours du même côté, sans pont.</summary>
    public bool switchSides = true;
    /// <summary>Longueur de la première zone (m).</summary>
    public float firstZoneLength = 450f;
    /// <summary>Longueur des zones suivantes (m), tirée au hasard entre ces deux valeurs.</summary>
    public float minZoneLength = 350f, maxZoneLength = 800f;
    /// <summary>Longueur des ponts (m).</summary>
    public float minBridgeLength = 50f, maxBridgeLength = 90f;
    /// <summary>Distance (m) sur laquelle la montagne s'enfonce avant le pont / remonte après.</summary>
    public float rampLength = 30f;
    /// <summary>Distance (m) aux extrémités du pont sur laquelle la gorge se creuse sous la route.</summary>
    public float abutmentLength = 8f;

    // ---------- Données ----------

    struct Zone
    {
        public double start, end; // la zone ; le pont qui la suit va de end au start de la zone suivante
        public int side;          // côté du vide : -1 gauche, 1 droite
    }

    readonly List<Zone> zones = new List<Zone>();
    readonly Random rng;
    int lastIndex;

    public CliffLayout(int seed)
    {
        rng = seed == 0 ? new Random() : new Random(seed * 7919 + 13);
    }

    /// <summary>
    /// voidLeft / voidRight : 0 = montagne contre la route, 1 = vide (falaise). bridge : 1 sur un pont, 0 ailleurs.
    /// </summary>
    public void Sample(double z, out float voidLeft, out float voidRight, out float bridge)
    {
        int side = startSide >= 0 ? 1 : -1;
        if (!switchSides || z <= 0.0)
        {
            Set(side, 0f, out voidLeft, out voidRight); // vide d'un côté, montagne de l'autre
            bridge = 0f;
            return;
        }

        int i = Find(z);
        var zone = zones[i];

        if (z >= zone.end)
        {
            // Sur le pont : vide des deux côtés, la gorge se creuse aux deux extrémités
            double bridgeEnd = zones[i + 1].start;
            float a = (float)Math.Min(z - zone.end, bridgeEnd - z) / Math.Max(0.01f, abutmentLength);
            bridge = Smooth(a);
            voidLeft = voidRight = 1f;
            return;
        }

        // Dans une zone : vide d'un côté ; de l'autre, la montagne s'enfonce près des ponts
        bridge = 0f;
        float mountainVoid = 0f;
        if (i > 0) mountainVoid = Math.Max(mountainVoid, Smooth(1f - (float)(z - zone.start) / rampLength));
        mountainVoid = Math.Max(mountainVoid, Smooth(1f - (float)(zone.end - z) / rampLength));
        Set(zone.side, mountainVoid, out voidLeft, out voidRight);
    }

    // Côté du vide = 1, l'autre côté = mountainVoid
    static void Set(int voidSide, float mountainVoid, out float left, out float right)
    {
        left = voidSide < 0 ? 1f : mountainVoid;
        right = voidSide > 0 ? 1f : mountainVoid;
    }

    static float Smooth(float t)
    {
        t = Math.Max(0f, Math.Min(1f, t));
        return t * t * (3f - 2f * t);
    }

    float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();

    // Index de la zone qui contient z, ou dont le pont contient z
    int Find(double z)
    {
        Ensure(z);
        if (lastIndex < zones.Count - 1 && zones[lastIndex].start <= z && z < zones[lastIndex + 1].start) return lastIndex;

        int lo = 0, hi = zones.Count - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (zones[mid].start <= z) lo = mid; else hi = mid - 1;
        }
        lastIndex = lo;
        return lo;
    }

    // Il faut toujours une zone après celle qui contient z (pour connaître la fin du pont)
    void Ensure(double z)
    {
        if (zones.Count == 0)
            zones.Add(new Zone { start = 0.0, end = firstZoneLength, side = startSide >= 0 ? 1 : -1 });

        while (zones.Count < 2 || zones[zones.Count - 2].end <= z + 1.0 || zones[zones.Count - 1].start <= z)
        {
            var last = zones[zones.Count - 1];
            double start = last.end + Rand(minBridgeLength, maxBridgeLength);
            double length = Math.Max(rampLength * 2.5f, Rand(minZoneLength, maxZoneLength));
            zones.Add(new Zone { start = start, end = start + length, side = -last.side });
        }
    }
}
