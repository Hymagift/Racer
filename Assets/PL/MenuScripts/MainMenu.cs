using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menu principal : Jouer, Shop, Options, Quitter.
/// À mettre sur un objet vide "MainMenu" dans la scène du menu, puis brancher les boutons
/// (OnClick -> MainMenu -> méthode correspondante).
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Panneaux")]
    public GameObject mainPanel;
    public GameObject shopPanel;
    public GameObject optionsPanel;

    [Header("Scène de jeu")]
    [Tooltip("Nom exact de la scène de jeu (elle doit être dans Build Profiles > Scene List)")]
    public string gameSceneName = "Game";

    [Header("Textes (optionnel)")]
    public TMP_Text bestScoreText;
    public TMP_Text coinsText;

    [Header("Options")]
    public Slider volumeSlider;

    void Start()
    {
        Time.timeScale = 1f;

        float v = PlayerData.Volume;
        AudioListener.volume = v;
        if (volumeSlider != null)
        {
            volumeSlider.SetValueWithoutNotify(v);
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }

        PlayerData.OnChanged += RefreshTexts;
        RefreshTexts();
        ShowMain();
    }

    void OnDestroy()
    {
        PlayerData.OnChanged -= RefreshTexts;
    }

    void RefreshTexts()
    {
        if (bestScoreText != null) bestScoreText.text = "Record : " + PlayerData.BestScore;
        if (coinsText != null) coinsText.text = PlayerData.Coins.ToString();
    }

    // ---------- Navigation (à brancher sur les boutons) ----------

    public void Play() => SceneManager.LoadScene(gameSceneName);

    public void ShowMain() => ShowOnly(mainPanel);
    public void OpenShop() => ShowOnly(shopPanel);
    public void OpenOptions() => ShowOnly(optionsPanel);

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void ShowOnly(GameObject panel)
    {
        if (mainPanel != null) mainPanel.SetActive(mainPanel == panel);
        if (shopPanel != null) shopPanel.SetActive(shopPanel == panel);
        if (optionsPanel != null) optionsPanel.SetActive(optionsPanel == panel);
    }

    // ---------- Options ----------

    public void SetVolume(float value)
    {
        AudioListener.volume = value;
        PlayerData.Volume = value;
    }

    /// <summary>Efface pièces, achats, record et volume. À brancher sur un bouton "Réinitialiser".</summary>
    public void ResetProgress()
    {
        PlayerData.ResetAll();
        AudioListener.volume = 1f;
        if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(1f);
        RefreshTexts();
    }
}