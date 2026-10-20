using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Arc de cercle de l'UI dessiné par code (aucune image) : un anneau qui va de "from" à "to", en tours
/// (0 = en haut, 1 = un tour complet, sens des aiguilles d'une montre).
/// La couleur passe de tailColor (au début de l'arc) à headColor (au bout) : sert au remplissage ET à la traînée.
/// Les bords sont adoucis (anti-aliasing) sur "feather" unités.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RingGraphic : MaskableGraphic
{
    [Tooltip("Début de l'arc, en tours")]
    [SerializeField] float from;
    [Tooltip("Fin de l'arc, en tours (peut dépasser 1 : on dessine au plus un tour)")]
    [SerializeField] float to = 1f;
    [Tooltip("Épaisseur de l'anneau, en fraction du rayon")]
    [Range(0.02f, 1f)] public float thickness = 0.2f;
    [Tooltip("Bord adouci (unités du canvas)")]
    public float feather = 1.5f;
    public Color tailColor = Color.white;
    public Color headColor = Color.white;

    const int SegmentsPerTurn = 128;

    /// <summary>Change l'arc (en tours) ; ne redessine que si ça a bougé.</summary>
    public void SetArc(float start, float end)
    {
        if (Mathf.Approximately(start, from) && Mathf.Approximately(end, to)) return;
        from = start;
        to = end;
        SetVerticesDirty();
    }

    /// <summary>Change le dégradé de l'arc.</summary>
    public void SetColors(Color tail, Color head)
    {
        if (tail == tailColor && head == headColor) return;
        tailColor = tail;
        headColor = head;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        float start = from, end = to;
        if (end < start) { float t = start; start = end; end = t; }
        if (end - start > 1f) start = end - 1f; // un tour au maximum : on garde la partie près du bout
        float span = end - start;
        if (span <= 0.0005f) return;

        Rect r = rectTransform.rect;
        Vector2 center = r.center;
        float outer = Mathf.Min(r.width, r.height) * 0.5f;
        float inner = outer * (1f - thickness);
        float f = Mathf.Max(0f, feather);

        // 4 rayons par pas : bord intérieur transparent, intérieur, extérieur, bord extérieur transparent
        float[] radii = { Mathf.Max(0f, inner - f), inner, Mathf.Max(inner, outer - f), outer };
        float[] alphas = { 0f, 1f, 1f, 0f };

        int steps = Mathf.Max(2, Mathf.CeilToInt(span * SegmentsPerTurn));
        for (int s = 0; s <= steps; s++)
        {
            float k = s / (float)steps;
            float turn = start + span * k;
            float ang = Mathf.PI * 0.5f - turn * Mathf.PI * 2f; // 0 tour = en haut, sens horaire
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Color c = Color.Lerp(tailColor, headColor, k) * color;

            for (int i = 0; i < 4; i++)
            {
                Color vc = c;
                vc.a *= alphas[i];
                vh.AddVert(center + dir * radii[i], vc, Vector4.zero);
            }

            if (s == 0) continue;
            int b = (s - 1) * 4, n = s * 4;
            for (int i = 0; i < 3; i++)
            {
                vh.AddTriangle(b + i, n + i, n + i + 1);
                vh.AddTriangle(b + i, n + i + 1, b + i + 1);
            }
        }
    }
}
