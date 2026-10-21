using UnityEngine;

/// <summary>
/// Ombre ronde et floue sous la voiture : remplace les ombres en temps réel, trop coûteuses sur téléphone.
/// Un simple sprite transparent posé au sol, à la taille du modèle (calculée au lancement).
/// Ajouté automatiquement par DriftCar.
/// </summary>
public class BlobShadow : MonoBehaviour
{
    [Tooltip("Opacité de l'ombre (0 = invisible, 1 = noir)")]
    [Range(0f, 1f)] public float opacity = 0.55f;
    [Tooltip("Taille de l'ombre par rapport au modèle")]
    public float sizeScale = 1.3f;
    [Tooltip("Hauteur au-dessus du sol (évite que l'ombre clignote avec la route)")]
    public float lift = 0.05f;

    static Sprite sprite;
    Transform shadow;

    void Start()
    {
        // Taille et position du modèle, mesurées avant d'ajouter l'ombre
        bool any = false;
        Bounds b = new Bounds();
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r is SpriteRenderer) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!any) b = new Bounds(transform.position, new Vector3(1.8f, 1.2f, 4.2f));

        var go = new GameObject("Ombre");
        shadow = go.transform;
        shadow.SetParent(transform, false);
        shadow.position = new Vector3(b.center.x, b.min.y + lift, b.center.z);
        shadow.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Vector3 parentScale = transform.lossyScale;
        shadow.localScale = new Vector3(
            b.size.x * sizeScale / Mathf.Max(0.0001f, parentScale.x),
            b.size.z * sizeScale / Mathf.Max(0.0001f, parentScale.z),
            1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSprite();
        sr.color = new Color(0f, 0f, 0f, opacity);
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
    }

    // Disque flou blanc 64 x 64, créé une seule fois par code (taille 1 x 1 m)
    static Sprite GetSprite()
    {
        if (sprite != null) return sprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "BlobShadow",
        };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a); // bord très doux
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n, 0, SpriteMeshType.FullRect);
        return sprite;
    }
}
