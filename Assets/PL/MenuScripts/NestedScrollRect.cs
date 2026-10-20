using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ScrollRect vertical placé dans une page du MenuPager : un glissé plutôt horizontal est donné au MenuPager
/// (changement de page), un glissé plutôt vertical fait défiler la liste comme d'habitude.
/// Créé par MenuStyle.VerticalScroll.
/// </summary>
public class NestedScrollRect : ScrollRect
{
    bool routeToParent;

    public override void OnInitializePotentialDrag(PointerEventData eventData)
    {
        Parent<IInitializePotentialDragHandler>(eventData, ExecuteEvents.initializePotentialDrag);
        base.OnInitializePotentialDrag(eventData);
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        Vector2 d = eventData.position - eventData.pressPosition;
        routeToParent = (!horizontal && Mathf.Abs(d.x) > Mathf.Abs(d.y))
                     || (!vertical && Mathf.Abs(d.y) > Mathf.Abs(d.x));

        if (routeToParent) Parent<IBeginDragHandler>(eventData, ExecuteEvents.beginDragHandler);
        else base.OnBeginDrag(eventData);
    }

    public override void OnDrag(PointerEventData eventData)
    {
        if (routeToParent) Parent<IDragHandler>(eventData, ExecuteEvents.dragHandler);
        else base.OnDrag(eventData);
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        if (routeToParent) Parent<IEndDragHandler>(eventData, ExecuteEvents.endDragHandler);
        else base.OnEndDrag(eventData);
        routeToParent = false;
    }

    // Envoie l'événement au premier parent qui sait le gérer (le MenuPager)
    void Parent<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> function) where T : IEventSystemHandler
    {
        if (transform.parent == null) return;
        var target = ExecuteEvents.GetEventHandler<T>(transform.parent.gameObject);
        if (target != null) ExecuteEvents.Execute(target, eventData, function);
    }
}
