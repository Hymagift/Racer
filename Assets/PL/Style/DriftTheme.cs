using UnityEngine;

/// <summary>
/// DA "drift" rouge / blanc / bleu, en UN SEUL endroit : couleurs et réglages de style lus par le HUD de la partie
/// (et à brancher sur le menu : MenuStyle peut reprendre ces couleurs au lieu de les redéfinir).
/// Pour faire évoluer la DA, on change les valeurs ici et tout ce qui les lit suit.
/// </summary>
public static class DriftTheme
{
    // ---------- Couleurs (mêmes valeurs que le menu) ----------

    public static readonly Color Red = Hex(0xE0243C);       // couleur principale
    public static readonly Color DarkRed = Hex(0xA3122A);   // fonds de pastilles et panneaux
    public static readonly Color Blue = Hex(0x1F6BFF);      // actions, remplissage de l'anneau
    public static readonly Color LightBlue = Hex(0x4FA8FF); // traînée de l'anneau
    public static readonly Color Yellow = Hex(0xFFD23F);    // pièces, record
    public static readonly Color Ink = Hex(0x0A1A44);       // contours des textes (bleu nuit)
    public static readonly Color White = Color.white;

    // ---------- Formes ----------

    /// <summary>Inclinaison des pastilles, boutons et textes "vitesse" (degrés).</summary>
    public const float SkewAngle = 10f;
    /// <summary>Rayon des coins des pastilles (unités du canvas, base 1080x1920).</summary>
    public const float PillRadius = 16f;
    /// <summary>Épaisseur du liseré blanc autour des pastilles et de l'anneau.</summary>
    public const float RimWidth = 5f;
    /// <summary>Contour sombre des textes.</summary>
    public const float TextOutline = 3f;
    /// <summary>Police : gras italique.</summary>
    public const FontStyle TextStyle = FontStyle.BoldAndItalic;

    // ---------- Outils ----------

    public static Color Hex(int rgb, float a = 1f)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
    }

    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
}
