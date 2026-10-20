using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Pages du menu côte à côte, qu'on fait défiler en glissant le doigt vers la gauche ou la droite.
/// La page suit le doigt, puis se cale sur la page la plus proche (ou la suivante si on a "lancé" le glissé).
/// Créé par MainMenu (UI automatique) : AddPage() pour chaque page, puis GoTo() pour changer de page par code.
///
/// Les pages ont toutes la taille du MenuPager ; la page i est placée à i largeurs d'écran vers la droite.
/// </summary>
public class MenuPager : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Tooltip("Durée approximative du calage sur une page (secondes)")]
    public float snapTime = 0.14f;
    [Tooltip("Vitesse (pages par seconde) au-delà de laquelle un glissé rapide passe à la page suivante")]
    public float flickSpeed = 1.2f;
    [Tooltip("Résistance au bord (première et dernière page) : 0 = bloqué, 1 = aucune résistance")]
    public float edgeResistance = 0.3f;

    /// <summary>Page visée (0 = la première).</summary>
    public int Current { get; private set; }
    /// <summary>Position continue en pages (1.5 = à mi-chemin entre la 2e et la 3e page). Sert à animer les onglets.</summary>
    public float Position { get; private set; }
    public int PageCount => content != null ? content.childCount : 0;

    /// <summary>Appelé quand la page visée change.</summary>
    public event Action<int> OnPageChanged;

    RectTransform rect, content;
    bool dragging, ignoreDrag;
    float dragStartPosition, dragVelocity, lastDragPosition, snapVelocity, lastWidth;

    RectTransform Rect => rect != null ? rect : rect = (RectTransform)transform;
    float Width => Mathf.Max(1f, Rect.rect.width);

    RectTransform Content
    {
        get
        {
            if (content != null) return content;
            content = MenuStyle.Node("Pages", transform);
            MenuStyle.Stretch(content);
            return content;
        }
    }

    /// <summary>Ajoute une page (à droite de la précédente) et la renvoie, vide, prête à remplir.</summary>
    public RectTransform AddPage(string name)
    {
        int i = Content.childCount;
        var page = MenuStyle.Node(name, Content);
        page.anchorMin = new Vector2(i, 0f);
        page.anchorMax = new Vector2(i + 1, 1f);
        page.offsetMin = page.offsetMax = Vector2.zero;
        return page;
    }

    /// <summary>Va à une page (animé, ou tout de suite si instant).</summary>
    public void GoTo(int page, bool instant = false)
    {
        page = Mathf.Clamp(page, 0, Mathf.Max(0, PageCount - 1));
        if (instant)
        {
            Position = page;
            snapVelocity = 0f;
        }
        if (page == Current) { Apply(); return; }
        Current = page;
        OnPageChanged?.Invoke(page);
        Apply();
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        eventData.useDragThreshold = true;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Glissé plutôt vertical : on laisse faire (rien à faire défiler verticalement ici)
        Vector2 d = eventData.position - eventData.pressPosition;
        ignoreDrag = Mathf.Abs(d.y) > Mathf.Abs(d.x);
        if (ignoreDrag) return;

        dragging = true;
        dragStartPosition = Position;
        lastDragPosition = Position;
        dragVelocity = 0f;
        snapVelocity = 0f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging || ignoreDrag) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, eventData.position, eventData.pressEventCamera, out var now) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, eventData.pressPosition, eventData.pressEventCamera, out var start))
            return;

        float p = dragStartPosition - (now.x - start.x) / Width;
        float max = Mathf.Max(0, PageCount - 1);
        if (p < 0f) p *= edgeResistance;
        else if (p > max) p = max + (p - max) * edgeResistance;
        Position = p;

        // Vitesse lissée du glissé (pages par seconde), pour détecter un "lancer"
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        dragVelocity = Mathf.Lerp(dragVelocity, (p - lastDragPosition) / dt, 0.5f);
        lastDragPosition = p;
        Apply();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        dragging = false;

        int target = Mathf.RoundToInt(Position);
        if (dragVelocity > flickSpeed) target = Mathf.FloorToInt(Position) + 1;
        else if (dragVelocity < -flickSpeed) target = Mathf.CeilToInt(Position) - 1;
        // Pas plus d'une page à la fois depuis le début du glissé
        target = Mathf.Clamp(target, Mathf.FloorToInt(dragStartPosition + 0.001f) - 1, Mathf.CeilToInt(dragStartPosition - 0.001f) + 1);
        GoTo(target);
    }

    void Update()
    {
        // Taille de l'écran changée (rotation, fenêtre de l'éditeur) : on recale les pages
        if (!Mathf.Approximately(Width, lastWidth)) Apply();
        if (dragging || Mathf.Approximately(Position, Current)) return;
        Position = Mathf.SmoothDamp(Position, Current, ref snapVelocity, snapTime, Mathf.Infinity, Time.unscaledDeltaTime);
        if (Mathf.Abs(Position - Current) < 0.0005f)
        {
            Position = Current;
            snapVelocity = 0f;
        }
        Apply();
    }

    void Apply()
    {
        if (content == null) return;
        lastWidth = Width;
        content.anchoredPosition = new Vector2(-Position * Width, 0f);
    }
}
