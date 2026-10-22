using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Photos des voitures pour le Garage, prises par code à partir des modèles 3D du catalogue (aucune image à faire).
///
/// Chaque modèle est posé loin du décor, sur un calque à part, et photographié de trois quarts avant
/// par une caméra temporaire. Deux photos (fond noir puis fond blanc) donnent la transparence exacte,
/// quels que soient les réglages URP. Rendu en double taille puis réduit : bords lisses sans MSAA.
/// Une seule fois par voiture, gardé en mémoire ; null si la voiture n'a pas de modèle.
/// </summary>
public static class CarThumbnails
{
    const int Width = 640, Height = 400;
    const int Layer = 31;                        // calque réservé aux photos (rien d'autre dessus)
    static readonly Vector3 Far = new Vector3(0f, -5000f, 0f);
    static readonly int LineBoost = Shader.PropertyToID("_RacerLineBoost"); // voir Racer/Outlined
    static readonly Vector3 ViewDir = new Vector3(-1f, 0.62f, 1f); // trois quarts avant gauche : l'avant pointe à gauche

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    static readonly Dictionary<string, Sprite> masks = new Dictionary<string, Sprite>();

    public static Sprite Get(CarCatalog.Car car)
    {
        if (car == null || car.model == null) return null;
        if (cache.TryGetValue(car.id, out var s) && s != null) return s;
        s = Take(car);
        cache[car.id] = s;
        return s;
    }

    /// <summary>Même photo en blanc uni (garde la forme) : liseré blanc derrière la voiture.</summary>
    public static Sprite GetMask(CarCatalog.Car car)
    {
        if (Get(car) == null) return null;
        return masks.TryGetValue(car.id, out var m) ? m : null;
    }

    static Sprite Take(CarCatalog.Car car)
    {
        var root = new GameObject("PhotoVoiture");
        root.transform.position = Far;
        GameObject camGo = null;
        RenderTexture rt = null;
        try
        {
            var model = Object.Instantiate(car.model, root.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, car.modelYaw, 0f);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            InGameLook.ApplyCarModel(model); // DA du jeu : blanche, contour et arêtes noirs (verrouillée : teintée en noir)

            bool any = false;
            var bounds = new Bounds();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any) return null;

            // Caméra orthographique cadrée sur la voiture
            camGo = new GameObject("PhotoCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1000f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.aspect = Width / (float)Height;

            float dist = bounds.extents.magnitude * 4f + 1f;
            cam.transform.position = bounds.center + ViewDir.normalized * dist;
            cam.transform.LookAt(bounds.center);

            // Cadrage : boîte englobante vue par la caméra
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, 0f), max = new Vector3(float.MinValue, float.MinValue, 0f);
            for (int i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var p = cam.transform.InverseTransformPoint(corner);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            Vector3 mid = (min + max) * 0.5f;
            cam.transform.position += cam.transform.right * mid.x + cam.transform.up * mid.y;
            float halfW = (max.x - min.x) * 0.5f, halfH = (max.y - min.y) * 0.5f;
            cam.orthographicSize = Mathf.Max(halfH, halfW / cam.aspect) * 1.06f;

            int w = Width * 2, h = Height * 2;
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;

            Shader.SetGlobalFloat(LineBoost, 1.5f); // traits plus épais : la photo est réduite ensuite
            Color32[] onBlack = Shoot(cam, rt, Color.black);
            Color32[] onWhite = Shoot(cam, rt, Color.white);
            Shader.SetGlobalFloat(LineBoost, 0f);
            if (onBlack == null || onWhite == null) return null;

            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, true)
            {
                name = "Photo " + car.id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = Matte(onBlack, onWhite, w, h);
            tex.SetPixels32(pixels);
            tex.Apply(true, true); // plus lisible par le CPU : économise la mémoire

            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, pixels[i].a);
            var mask = new Texture2D(Width, Height, TextureFormat.RGBA32, true)
            {
                name = "Silhouette " + car.id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            mask.SetPixels32(pixels);
            mask.Apply(true, true);
            masks[car.id] = Sprite.Create(mask, new Rect(0, 0, Width, Height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return Sprite.Create(tex, new Rect(0, 0, Width, Height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Garage] Photo de la voiture " + car.id + " impossible : " + e.Message);
            return null;
        }
        finally
        {
            Shader.SetGlobalFloat(LineBoost, 0f);
            if (camGo != null) Object.DestroyImmediate(camGo);
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            Object.DestroyImmediate(root);
        }
    }

    static Color32[] Shoot(Camera cam, RenderTexture rt, Color background)
    {
        cam.backgroundColor = background;
        var request = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
        else cam.Render();

        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        RenderTexture.active = previous;
        var px = read.GetPixels32();
        Object.DestroyImmediate(read);
        return px;
    }

    // Transparence = différence entre les deux fonds ; couleur = photo sur fond noir / transparence.
    // Réduit de moitié en même temps (moyenne de 2 x 2 pixels).
    static Color32[] Matte(Color32[] black, Color32[] white, int w, int h)
    {
        int ow = w / 2, oh = h / 2;
        var result = new Color32[ow * oh];
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int k = (y * 2 + j) * w + x * 2 + i;
                        Color32 cb = black[k], cw = white[k];
                        float diff = ((cw.r - cb.r) + (cw.g - cb.g) + (cw.b - cb.b)) / (3f * 255f);
                        float alpha = Mathf.Clamp01(1f - diff);
                        a += alpha;
                        r += cb.r; g += cb.g; b += cb.b; // couleur prémultipliée par la transparence
                    }
                a *= 0.25f;
                float inv = a > 0.004f ? 0.25f / a : 0f;
                result[y * ow + x] = new Color32(
                    (byte)Mathf.Clamp(r * inv, 0f, 255f),
                    (byte)Mathf.Clamp(g * inv, 0f, 255f),
                    (byte)Mathf.Clamp(b * inv, 0f, 255f),
                    (byte)(a * 255f));
            }
        return result;
    }
}
