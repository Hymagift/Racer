using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Petite musique de fond qui tourne en boucle pendant tout le jeu (menu, compte à rebours, partie, écran de fin, fondus).
/// Créée toute seule au lancement et gardée d'une scène à l'autre : rien à mettre dans les scènes.
///
/// La musique est composée par code, sans fichier audio : boucle de 16 mesures à 100 BPM (~38 s) façon synthwave,
/// en la mineur (la mineur - fa - do - sol) : nappe d'accords, basse, arpège, kick, caisse claire, charleston.
/// La boucle est calculée une seule fois au lancement, dans un thread à part (aucun ralentissement),
/// puis jouée comme un son normal : elle ne coûte plus rien ensuite.
///
/// Le volume suit AudioListener.volume (réglage Volume des options).
/// </summary>
public class BackgroundMusic : MonoBehaviour
{
    public static BackgroundMusic Instance { get; private set; }

    [Tooltip("Volume de la musique (le volume général des options s'applique en plus)")]
    [Range(0f, 1f)] public float volume = 0.5f;
    [Tooltip("Durée du fondu d'entrée au lancement (secondes)")]
    public float fadeIn = 2.5f;

    const int Rate = 44100;
    const float Bpm = 100f;
    const int Bars = 16;
    const float Beat = 60f / Bpm;   // 0.6 s
    const float Bar = Beat * 4f;    // 2.4 s
    const int LoopSamples = (int)(Bars * Bar * Rate);

    // Accords (notes MIDI) et basses : la mineur, fa, do, sol (2 mesures chacun)
    static readonly int[][] Chords =
    {
        new[] { 57, 60, 64 }, // la do mi
        new[] { 53, 57, 60 }, // fa la do
        new[] { 55, 60, 64 }, // sol do mi (do)
        new[] { 55, 59, 62 }, // sol si ré
    };
    static readonly int[] BassNotes = { 45, 41, 48, 43 };
    static readonly int[] ArpPattern = { 0, 1, 2, 1, 2, 1, 0, 2 };

    AudioSource source;
    AudioListener ownListener;
    Task<float[]> rendering;
    float listenerCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("Musique");
        DontDestroyOnLoad(go);
        go.AddComponent<BackgroundMusic>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Écouteur de secours : la scène du menu n'a pas de caméra pendant le rechargement de la démo en fond
        ownListener = gameObject.AddComponent<AudioListener>();
        ownListener.enabled = false;

        // Calcul de la boucle en arrière-plan (environ 1 s sur PC, quelques secondes sur téléphone)
        rendering = Task.Run(Render);
        source = gameObject.AddComponent<AudioSource>();
        source.loop = true;
        source.spatialBlend = 0f;
        source.priority = 0;
        source.volume = 0f;
        source.ignoreListenerPause = true;
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneChanged;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneChanged;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    // Une scène arrive ou part : on revérifie tout de suite qui doit écouter
    void OnSceneChanged(Scene s, LoadSceneMode m) => listenerCheck = 0f;
    void OnSceneUnloaded(Scene s) => listenerCheck = 0f;

    void Update()
    {
        // Boucle prête : on la joue (avec un fondu d'entrée)
        if (rendering != null && rendering.IsCompleted)
        {
            if (rendering.Status == TaskStatus.RanToCompletion)
            {
                var clip = AudioClip.Create("Musique", LoopSamples, 1, Rate, false);
                clip.SetData(rendering.Result, 0);
                source.clip = clip;
                source.Play();
            }
            else Debug.LogWarning("[Musique] La musique n'a pas pu être calculée : " + rendering.Exception);
            rendering = null;
        }
        if (source.isPlaying)
            source.volume = Mathf.MoveTowards(source.volume, volume, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeIn) * volume);

        // Notre écouteur ne sert que s'il n'y en a aucun autre (sinon Unity se plaint de deux écouteurs)
        listenerCheck -= Time.unscaledDeltaTime;
        if (listenerCheck > 0f) return;
        listenerCheck = 0.25f;
        bool other = false;
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (l != ownListener && l.isActiveAndEnabled) { other = true; break; }
        ownListener.enabled = !other;
    }

    // ---------- Synthèse (appelée hors du thread principal : uniquement des calculs, aucune API Unity) ----------

    static float[] Render()
    {
        var data = new float[LoopSamples];
        for (int i = 0; i < data.Length; i++) data[i] = Sample(i);
        return data;
    }

    static float Sample(int index)
    {
        double td = index / (double)Rate; // en double : phases précises même en fin de boucle
        float t = (float)td;
        int bar = (int)(t / Bar);
        float tBeat = t % Beat;
        int beat = (int)(t / Beat) % 4;
        int chord = (bar / 2) % Chords.Length;
        float tChord = t % (Bar * 2f);
        bool intro = bar < 2;            // 2 premières mesures : sans batterie
        bool arpOn = bar >= 8;           // seconde moitié : arpège en plus

        // "Pompe" : la nappe et la basse s'effacent un peu à chaque temps (effet sidechain)
        float pump = intro ? 1f : 1f - 0.45f * Mathf.Exp(-tBeat * 7f);

        float mix = 0f;

        // Nappe : les 3 notes de l'accord, deux oscillateurs légèrement désaccordés, entrée et sortie douces
        float padEnv = SmoothStep(0f, 0.5f, tChord) * SmoothStep(0f, 0.25f, Bar * 2f - tChord);
        var notes = Chords[chord];
        float pad = 0f;
        for (int n = 0; n < notes.Length; n++)
        {
            float f = Midi(notes[n]);
            pad += Osc(f * 1.0015, td) + Osc(f * 0.9985, td) + 0.3f * Osc(f * 2.0, td);
        }
        mix += pad * padEnv * pump * 0.045f;

        // Basse : croches sur la fondamentale (une octave plus haut une croche sur quatre)
        float eighth = Beat * 0.5f;
        int step8 = (int)(t / eighth);
        float t8 = t % eighth;
        float bf = Midi(BassNotes[chord] + (step8 % 4 == 3 ? 12 : 0));
        float bassEnv = Mathf.Min(1f, t8 * 200f) * Mathf.Exp(-t8 * 5f);
        float bass = Osc(bf, td) + 0.4f * Osc(bf * 2.0, td) + 0.2f * Osc(bf * 3.0, td);
        mix += bass * bassEnv * pump * 0.16f;

        // Arpège : doubles croches sur les notes de l'accord, une octave au-dessus
        if (arpOn)
        {
            float sixteenth = Beat * 0.25f;
            int step16 = (int)(t / sixteenth);
            float t16 = t % sixteenth;
            float af = Midi(notes[ArpPattern[step16 % ArpPattern.Length]] + 12);
            float tri = Mathf.Asin(Osc(af, td)) * 0.6366f;
            mix += tri * Mathf.Exp(-t16 * 16f) * 0.07f;
        }

        if (!intro)
        {
            // Kick sur les temps 1 et 3 (son grave qui descend)
            if (beat == 0 || beat == 2)
            {
                float phase = Tau * (45f * tBeat + 75f * (1f - Mathf.Exp(-30f * tBeat)) / 30f);
                mix += Mathf.Sin(phase) * Mathf.Exp(-tBeat * 8f) * 0.42f;
            }
            // Caisse claire sur les temps 2 et 4
            if (beat == 1 || beat == 3)
            {
                float snare = Noise(index) * Mathf.Exp(-tBeat * 16f) * 0.11f
                            + Mathf.Sin(Tau * 190f * tBeat) * Mathf.Exp(-tBeat * 22f) * 0.08f;
                mix += snare;
            }
            // Charleston sur les contretemps (bruit aigu très court)
            if (step8 % 2 == 1)
                mix += (Noise(index) - Noise(index - 1)) * Mathf.Exp(-t8 * 70f) * 0.035f;
        }

        // Limiteur doux (pas de saturation brutale)
        return (float)System.Math.Tanh(mix * 1.9f) * 0.8f;
    }

    const float Tau = Mathf.PI * 2f;

    // Sinus d'une fréquence (Hz) au temps t (s), calculé sur la partie fractionnaire des cycles (précis)
    static float Osc(double freq, double t)
    {
        double cycles = freq * t;
        return (float)System.Math.Sin(2.0 * System.Math.PI * (cycles - System.Math.Floor(cycles)));
    }

    static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

    static float SmoothStep(float a, float b, float x)
    {
        float k = Mathf.Clamp01((x - a) / (b - a));
        return k * k * (3f - 2f * k);
    }

    // Bruit blanc déterministe (même valeur pour le même échantillon : la boucle est identique à chaque tour)
    static float Noise(int i)
    {
        unchecked
        {
            uint h = (uint)i * 747796405u + 2891336453u;
            h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
            h = (h >> 22) ^ h;
            return (h & 0xFFFF) / 32767.5f - 1f;
        }
    }
}
