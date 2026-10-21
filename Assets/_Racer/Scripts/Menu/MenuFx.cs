using UnityEngine;

/// <summary>
/// Retours "jeu mobile" du menu, sans aucun fichier à importer :
/// - petits sons synthétisés au lancement (clic, whoosh, pop, carillons, fanfare, pièces, erreur) ;
/// - vibration du téléphone ;
/// - sprite de rayons lumineux (derrière les boosters et les cartes rares).
/// Le volume suit AudioListener.volume (réglage Volume des options).
/// </summary>
public static class MenuFx
{
    public enum Sfx { Tick, Whoosh, Pop, Reveal, Fanfare, Unlock, Coin, Error }

    const int Rate = 44100;
    static AudioClip[] clips;
    static AudioSource[] sources;
    static int nextSource;
    static float nextListenerCheck = -1f;
    static bool hasListener;

    /// <summary>Joue un son (pitch : 1 = normal, plus grand = plus aigu).</summary>
    public static void Play(Sfx sfx, float pitch = 1f, float volume = 1f)
    {
        // Pas d'AudioListener (rechargement de la démo en fond) : on ne joue rien plutôt que de spammer la console
        // (recherche coûteuse : refaite au plus deux fois par seconde)
        if (Time.unscaledTime >= nextListenerCheck)
        {
            nextListenerCheck = Time.unscaledTime + 0.5f;
            hasListener = Object.FindAnyObjectByType<AudioListener>() != null;
        }
        if (!hasListener) return;

        EnsureAudio();
        var src = sources[nextSource];
        nextSource = (nextSource + 1) % sources.Length;
        src.pitch = pitch;
        src.PlayOneShot(clips[(int)sfx], volume);
    }

    /// <summary>Vibration courte du téléphone (rien dans l'éditeur).</summary>
    public static void Vibrate()
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        Handheld.Vibrate();
#endif
    }

    // ---------- Synthèse ----------

    static void EnsureAudio()
    {
        if (sources != null && sources[0] != null) return;

        var go = new GameObject("MenuSfx");
        Object.DontDestroyOnLoad(go);
        sources = new AudioSource[4];
        for (int i = 0; i < sources.Length; i++)
        {
            sources[i] = go.AddComponent<AudioSource>();
            sources[i].playOnAwake = false;
            sources[i].spatialBlend = 0f;
        }

        if (clips != null) return;
        clips = new AudioClip[8];
        clips[(int)Sfx.Tick] = Make("Tick", 0.035f, t => Sine(2300f, t) * Mathf.Exp(-t * 140f) * 0.5f);
        clips[(int)Sfx.Whoosh] = MakeWhoosh();
        clips[(int)Sfx.Pop] = Make("Pop", 0.13f, t => Mathf.Sin(2f * Mathf.PI * (300f * t + 2400f * t * t)) * Mathf.Exp(-t * 28f) * 0.7f);
        clips[(int)Sfx.Reveal] = Make("Reveal", 0.45f, t =>
            (Sine(880f, t) + 0.6f * Sine(1318.5f, t) + 0.25f * Sine(2637f, t)) * Mathf.Exp(-t * 8f) * 0.35f);
        clips[(int)Sfx.Fanfare] = Make("Fanfare", 1.1f, t =>
        {
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            float v = 0f;
            for (int i = 0; i < notes.Length; i++)
            {
                float s = t - i * 0.11f;
                if (s < 0f) continue;
                float decay = i == notes.Length - 1 ? 3f : 9f;
                v += (Sine(notes[i], s) + 0.3f * Sine(notes[i] * 3f, s)) * Mathf.Exp(-s * decay);
            }
            return v * 0.28f;
        });
        clips[(int)Sfx.Unlock] = Make("Unlock", 0.6f, t =>
        {
            float a = Sine(783.99f, t) * Mathf.Exp(-t * 12f);
            float s = t - 0.12f;
            float b = s > 0f ? (Sine(1046.5f, s) + 0.5f * Sine(1568f, s)) * Mathf.Exp(-s * 5f) : 0f;
            return (a + b) * 0.35f;
        });
        clips[(int)Sfx.Coin] = Make("Coin", 0.22f, t =>
            (t < 0.07f ? Sine(1975.5f, t) : Sine(2637f, t)) * Mathf.Exp(-t * 14f) * 0.35f);
        clips[(int)Sfx.Error] = Make("Error", 0.22f, t =>
            Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 140f * t)) * (1f - t / 0.22f) * 0.18f);
    }

    static float Sine(float freq, float t) => Mathf.Sin(2f * Mathf.PI * freq * t);

    static AudioClip Make(string name, float duration, System.Func<float, float> wave)
    {
        int n = Mathf.CeilToInt(duration * Rate);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float fade = Mathf.Clamp01((n - i) / (Rate * 0.005f)); // pas de clic en fin de son
            data[i] = Mathf.Clamp(wave(t) * fade, -1f, 1f);
        }
        var clip = AudioClip.Create("Menu" + name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Souffle : bruit filtré qui monte puis descend
    static AudioClip MakeWhoosh()
    {
        var rng = new System.Random(7);
        float low = 0f;
        return Make("Whoosh", 0.3f, t =>
        {
            float noise = (float)rng.NextDouble() * 2f - 1f;
            float k = Mathf.Lerp(0.04f, 0.25f, Mathf.Sin(Mathf.PI * t / 0.3f));
            low += (noise - low) * k;
            return low * Mathf.Sin(Mathf.PI * t / 0.3f) * 0.9f;
        });
    }

    // ---------- Sprite de rayons ----------

    static Sprite rays;

    /// <summary>Rayons blancs qui partent du centre (à teinter et faire tourner).</summary>
    public static Sprite Rays
    {
        get
        {
            if (rays != null) return rays;
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float ang = Mathf.Atan2(dy, dx);
                    float ray = Mathf.SmoothStep(0.1f, 0.6f, Mathf.Sin(ang * 12f) * 0.5f + 0.5f);
                    float a = ray * Mathf.Clamp01(1f - r) * Mathf.Clamp01(r * 4f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            rays = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            rays.name = "MenuRays";
            return rays;
        }
    }
}
