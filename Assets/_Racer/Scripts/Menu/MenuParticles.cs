using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Particules d'UI légères (petits ronds colorés) : éclats, étincelles, confettis.
/// À mettre sur un RectTransform qui couvre la zone (positions en coordonnées locales de ce rect).
/// Les images sont recyclées. Utilisé par BoosterShop et le ComboHUD.
/// </summary>
public class MenuParticles : MonoBehaviour
{
    class Particle
    {
        public RectTransform rt;
        public Image img;
        public Vector2 velocity;
        public float life, maxLife, gravity, drag, size, spin;
        public Color color;
        public bool shrink;
    }

    [Tooltip("Image des particules (vide = petit rond)")]
    public Sprite sprite;

    readonly List<Particle> alive = new List<Particle>();
    readonly Stack<Particle> pool = new Stack<Particle>();

    /// <summary>Explosion : count particules projetées dans toutes les directions.</summary>
    public void Burst(Vector2 position, Color color, int count, float speed = 900f, float size = 22f, float gravity = -1400f)
    {
        for (int i = 0; i < count; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float s = speed * Random.Range(0.35f, 1f);
            var c = Random.value < 0.25f ? Color.white : color;
            Spawn(position, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s, c,
                  size * Random.Range(0.5f, 1.2f), Random.Range(0.5f, 1.1f), gravity, 2.5f, true);
        }
    }

    /// <summary>Petite étincelle qui monte doucement et s'éteint.</summary>
    public void Sparkle(Vector2 position, Color color, float size = 16f)
    {
        Spawn(position, new Vector2(Random.Range(-30f, 30f), Random.Range(40f, 120f)), color,
              size * Random.Range(0.6f, 1.3f), Random.Range(0.7f, 1.4f), 0f, 0.5f, false);
    }

    /// <summary>Une particule, entièrement réglée par l'appelant (gravity en unités/s², drag = freinage par seconde).</summary>
    public void Emit(Vector2 position, Vector2 velocity, Color color, float size, float life, float gravity, float drag, bool shrink) =>
        Spawn(position, velocity, color, size, life, gravity, drag, shrink);

    void Spawn(Vector2 pos, Vector2 vel, Color color, float size, float life, float gravity, float drag, bool shrink)
    {
        var p = pool.Count > 0 ? pool.Pop() : Create();
        p.rt.gameObject.SetActive(true);
        p.rt.SetAsLastSibling();
        p.rt.anchoredPosition = pos;
        p.velocity = vel;
        p.color = color;
        p.size = size;
        p.life = p.maxLife = life;
        p.gravity = gravity;
        p.drag = drag;
        p.shrink = shrink;
        p.spin = Random.Range(-360f, 360f);
        alive.Add(p);
    }

    Particle Create()
    {
        var rt = MenuStyle.Node("Particule", transform);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        var img = MenuStyle.Fill(rt, Color.white);
        img.sprite = sprite != null ? sprite : MenuStyle.Round;
        return new Particle { rt = rt, img = img };
    }

    void LateUpdate()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            var p = alive[i];
            p.life -= dt;
            if (p.life <= 0f)
            {
                p.rt.gameObject.SetActive(false);
                pool.Push(p);
                alive.RemoveAt(i);
                continue;
            }

            p.velocity.y += p.gravity * dt;
            p.velocity *= Mathf.Exp(-p.drag * dt);
            p.rt.anchoredPosition += p.velocity * dt;
            p.rt.localRotation = Quaternion.Euler(0f, 0f, p.rt.localEulerAngles.z + p.spin * dt);

            float k = p.life / p.maxLife;
            float s = p.shrink ? p.size * k : p.size * Mathf.Sin(k * Mathf.PI); // les étincelles grossissent puis s'éteignent
            p.rt.sizeDelta = new Vector2(s, s);
            var c = p.color;
            c.a *= Mathf.Clamp01(k * 2f);
            p.img.color = c;
        }
    }

    void OnDisable()
    {
        foreach (var p in alive)
        {
            p.rt.gameObject.SetActive(false);
            pool.Push(p);
        }
        alive.Clear();
    }
}
