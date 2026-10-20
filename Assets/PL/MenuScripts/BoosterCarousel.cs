using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Carrousel horizontal : on fait glisser les éléments du doigt, ils se calent sur le plus proche.
/// Ne fait que la position (Position, Current) : l'affichage (taille, transparence...) est fait par BoosterShop.
///
/// Placé dans une page du MenuPager : un glissé vertical, ou un glissé qui sort du carrousel
/// (vers la droite sur le premier élément, vers la gauche sur le dernier) est donné au MenuPager pour changer de page.
/// </summary>
public class BoosterCarousel : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Tooltip("Distance entre deux éléments (unités du canvas) : un glissé de cette longueur = un élément")]
    public float spacing = 560f;
    [Tooltip("Durée approximative du calage (secondes)")]
    public float snapTime = 0.16f;
    [Tooltip("Vitesse (éléments par seconde) au-delà de laquelle un glissé rapide passe à l'élément suivant")]
    public float flickSpeed = 1.4f;
    [Tooltip("Résistance aux bords : 0 = bloqué, 1 = aucune résistance")]
    public float edgeResistance = 0.25f;

    public int Count { get; set; }
    /// <summary>Élément visé (0 = le premier).</summary>
    public int Current { get; private set; }
    /// <summary>Position continue (1.4 = entre le 2e et le 3e élément, plus près du 2e).</summary>
    public float Position { get; private set; }
    public bool Dragging => dragging;

    /// <summary>Appelé quand l'élément visé change.</summary>
    public event Action<int> OnChanged;

    bool dragging, toParent;
    float dragStart, lastPosition, velocity, snapVelocity;
    RectTransform rect;

    RectTransform Rect => rect != null ? rect : rect = (RectTransform)transform;

    public void GoTo(int index, bool instant = false)
    {
        index = Mathf.Clamp(index, 0, Mathf.Max(0, Count - 1));
        if (instant)
        {
            Position = index;
            snapVelocity = 0f;
        }
        if (index == Current) return;
        Current = index;
        OnChanged?.Invoke(index);
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        eventData.useDragThreshold = true;
        Parent<IInitializePotentialDragHandler>(eventData, ExecuteEvents.initializePotentialDrag);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        Vector2 d = eventData.position - eventData.pressPosition;
        bool vertical = Mathf.Abs(d.y) > Mathf.Abs(d.x);
        bool towardPrevious = d.x > 0f; // le doigt part vers la droite : élément précédent
        toParent = vertical || (towardPrevious && Current <= 0) || (!towardPrevious && Current >= Count - 1);

        if (toParent)
        {
            Parent<IBeginDragHandler>(eventData, ExecuteEvents.beginDragHandler);
            return;
        }

        dragging = true;
        dragStart = lastPosition = Position;
        velocity = snapVelocity = 0f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (toParent) { Parent<IDragHandler>(eventData, ExecuteEvents.dragHandler); return; }
        if (!dragging) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, eventData.position, eventData.pressEventCamera, out var now) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, eventData.pressPosition, eventData.pressEventCamera, out var start))
            return;

        float p = dragStart - (now.x - start.x) / Mathf.Max(1f, spacing);
        float max = Mathf.Max(0, Count - 1);
        if (p < 0f) p *= edgeResistance;
        else if (p > max) p = max + (p - max) * edgeResistance;
        Position = p;

        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        velocity = Mathf.Lerp(velocity, (p - lastPosition) / dt, 0.5f);
        lastPosition = p;

        // L'élément visé suit le doigt (le retour visuel et le son arrivent pendant le glissé)
        int nearest = Mathf.Clamp(Mathf.RoundToInt(p), 0, Mathf.Max(0, Count - 1));
        if (nearest != Current)
        {
            Current = nearest;
            OnChanged?.Invoke(nearest);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (toParent)
        {
            Parent<IEndDragHandler>(eventData, ExecuteEvents.endDragHandler);
            toParent = false;
            return;
        }
        if (!dragging) return;
        dragging = false;

        int target = Mathf.RoundToInt(Position);
        if (velocity > flickSpeed) target = Mathf.FloorToInt(Position) + 1;
        else if (velocity < -flickSpeed) target = Mathf.CeilToInt(Position) - 1;
        target = Mathf.Clamp(target, Mathf.FloorToInt(dragStart + 0.001f) - 1, Mathf.CeilToInt(dragStart - 0.001f) + 1);
        GoTo(target);
    }

    void Update()
    {
        if (dragging || Mathf.Approximately(Position, Current)) return;
        Position = Mathf.SmoothDamp(Position, Current, ref snapVelocity, snapTime, Mathf.Infinity, Time.unscaledDeltaTime);
        if (Mathf.Abs(Position - Current) < 0.0005f)
        {
            Position = Current;
            snapVelocity = 0f;
        }
    }

    // Envoie l'événement au premier parent qui sait le gérer (le MenuPager)
    void Parent<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> function) where T : IEventSystemHandler
    {
        if (transform.parent == null) return;
        var target = ExecuteEvents.GetEventHandler<T>(transform.parent.gameObject);
        if (target != null) ExecuteEvents.Execute(target, eventData, function);
    }
}
