using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Net;


public class PauseMenu : MonoBehaviour
{
    public CanvasGroup reticle;
    public GameObject pauseMenuGO;
    public Button resumeButton;
    public Dropdown colorPicker;
    public Button desktopButton;

    private float _reticleAlpha;
    private CloudSaveManager _cloudSaveManager;
    
    [HideInInspector] public bool isPaused = false;

    [HideInInspector] public Color color = Color.white;


    private void Awake()
    {
        _cloudSaveManager = GameObject.FindGameObjectWithTag("CloudSaveManager").GetComponent<CloudSaveManager>();
        color = _cloudSaveManager.playerColor;
        colorPicker.value = colorPicker.options.FindIndex(option => CheckLabel(color, option.text));
    }


    private void Start()
    {
        pauseMenuGO.SetActive(false);
        Cursor.visible = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            if (isPaused) ResumeGame(); else PauseGame();
    }

    public void PauseGame()
    {
        pauseMenuGO.SetActive(true);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        _reticleAlpha = reticle.alpha;
        reticle.alpha = 0f;
        isPaused = true;
    }

    public void ResumeGame()
    {
        pauseMenuGO.SetActive(false);
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        reticle.alpha = _reticleAlpha;
        isPaused = false;
    }

    public void ChangeColor(Dropdown change) => ColorUtility.TryParseHtmlString(change.options[change.value].text, out color);

    public bool CheckLabel(Color color, string label)
    {
        Color labeled_color;
        ColorUtility.TryParseHtmlString(label, out labeled_color);
        return color.Equals(labeled_color);
    }

    public void Desktop()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
        Application.Quit();
    }
    
}
