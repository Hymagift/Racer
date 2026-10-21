using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Petit rebond quand on appuie sur un bouton (il s'écrase un peu puis revient).
/// Ajouté automatiquement par MenuStyle.CandyButton.
/// </summary>
public class ButtonBounce : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("Taille quand le doigt appuie (1 = taille normale)")]
    public float pressedScale = 0.92f;
    [Tooltip("Vitesse du retour à la taille normale")]
    public float speed = 18f;

    /// <summary>Multiplicateur de taille piloté par un autre script (ex : pulsation du bouton Jouer, onglet actif).</summary>
    [HideInInspector] public float extraScale = 1f;

    Selectable selectable;
    bool pressed;
    float current = 1f;

    void Awake() => selectable = GetComponent<Selectable>();

    void OnDisable()
    {
        pressed = false;
        current = 1f;
        transform.localScale = Vector3.one * extraScale;
        applied = extraScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (selectable == null || selectable.IsInteractable()) pressed = true;
    }

    public void OnPointerUp(PointerEventData eventData) => pressed = false;
    public void OnPointerExit(PointerEventData eventData) => pressed = false;

    float applied = -1f;

    void Update()
    {
        float target = pressed ? pressedScale : 1f;
        current = Mathf.Abs(current - target) < 0.0005f ? target
                : Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
        // N'écrit la taille que si elle change : sinon l'UI est recalculée à chaque image pour rien
        float s = current * extraScale;
        if (Mathf.Abs(s - applied) < 0.0001f) return;
        applied = s;
        transform.localScale = Vector3.one * s;
    }
}
