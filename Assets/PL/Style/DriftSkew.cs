using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Penche une image ou un texte de l'UI (effet "vitesse" : parallélogramme au lieu d'un rectangle).
/// Le haut part vers la droite, le bas vers la gauche, autour du centre du rect.
/// Utilisé par le HUD de la partie (même effet que les boutons penchés du menu).
/// </summary>
public class DriftSkew : BaseMeshEffect
{
    [Tooltip("Inclinaison en degrés (positif = penché vers la droite)")]
    public float angle = DriftTheme.SkewAngle;

    static readonly List<UIVertex> verts = new List<UIVertex>();

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || Mathf.Approximately(angle, 0f)) return;

        float k = Mathf.Tan(angle * Mathf.Deg2Rad);
        float cy = graphic.rectTransform.rect.center.y;

        verts.Clear();
        vh.GetUIVertexStream(verts);
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            v.position.x += (v.position.y - cy) * k;
            verts[i] = v;
        }
        vh.Clear();
        vh.AddUIVertexTriangleStream(verts);
    }
}
