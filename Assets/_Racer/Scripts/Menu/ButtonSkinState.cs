using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Retient l'apparence dessinée d'un bouton passé en image par MenuStyle.Skin, pour pouvoir la remettre
/// (ex. bouton "continuer" : image pour 10 rubis, bouton dessiné pour un prix qui n'a pas d'image).
/// Ajouté automatiquement, rien à régler.
/// </summary>
public class ButtonSkinState : MonoBehaviour
{
    public bool Skinned { get; private set; }

    RectTransform face;
    Image faceImage, side;
    Vector2 offsetMin, offsetMax;
    Sprite sprite;
    Image.Type type;
    float pixelsPerUnit;
    bool preserveAspect;
    Color disabledColor;
    readonly List<GameObject> hidden = new List<GameObject>();

    public void Capture(Button b, RectTransform face, Image faceImage, Image side)
    {
        this.face = face;
        this.faceImage = faceImage;
        this.side = side;
        offsetMin = face.offsetMin;
        offsetMax = face.offsetMax;
        sprite = faceImage.sprite;
        type = faceImage.type;
        pixelsPerUnit = faceImage.pixelsPerUnitMultiplier;
        preserveAspect = faceImage.preserveAspect;
        disabledColor = b.colors.disabledColor;
    }

    /// <summary>Cache un enfant de la face (texte, icône) ; il revient avec Restore.</summary>
    public void Hide(GameObject child)
    {
        if (!child.activeSelf) return;
        child.SetActive(false);
        hidden.Add(child);
    }

    public void MarkSkinned() => Skinned = true;

    /// <summary>Remet le bouton dessiné d'origine.</summary>
    public void Restore(Button b)
    {
        if (!Skinned) return;
        Skinned = false;
        face.offsetMin = offsetMin;
        face.offsetMax = offsetMax;
        faceImage.sprite = sprite;
        faceImage.type = type;
        faceImage.pixelsPerUnitMultiplier = pixelsPerUnit;
        faceImage.preserveAspect = preserveAspect;
        if (side != null) side.enabled = true;
        foreach (var go in hidden) if (go != null) go.SetActive(true);
        hidden.Clear();
        var colors = b.colors;
        colors.disabledColor = disabledColor;
        b.colors = colors;
    }
}
