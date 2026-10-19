using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Une ligne du shop (à mettre sur un prefab) : nom, prix et bouton Acheter / Équiper.
/// Le prefab doit contenir 2-3 textes TextMeshPro et un Button.
/// </summary>
public class ShopItemUI : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text priceText;
    public Button button;
    public TMP_Text buttonText;

    public void Setup(string title, string price, string buttonLabel, bool interactable, Action onClick)
    {
        if (nameText != null) nameText.text = title;
        if (priceText != null) priceText.text = price;
        if (buttonText != null) buttonText.text = buttonLabel;

        if (button != null)
        {
            button.interactable = interactable;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}