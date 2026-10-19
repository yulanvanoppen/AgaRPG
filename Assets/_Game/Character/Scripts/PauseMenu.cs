using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AgaRPG.UI
{
    /// <summary>
    /// In-game pause menu (Escape) with a player colour picker. The colour options are the
    /// dropdown labels, written as HTML colour names or hex codes.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        [FormerlySerializedAs("reticle")]
        [SerializeField] private CanvasGroup _reticle;

        [FormerlySerializedAs("pauseMenuGO")]
        [SerializeField] private GameObject _menuRoot;

        [FormerlySerializedAs("colorPicker")]
        [SerializeField] private Dropdown _colorPicker;

        private float _reticleAlpha;

        public bool IsPaused { get; private set; }
        public Color Color { get; private set; } = Color.white;

        /// <summary>Raised when the player picks a new colour.</summary>
        public event Action<Color> ColorChanged;

        private void Start()
        {
            _menuRoot.SetActive(false);
            Cursor.visible = false;
        }

        private void Update()
        {
            if (EscapePressed())
            {
                if (IsPaused) ResumeGame();
                else PauseGame();
            }
        }

        public void PauseGame()
        {
            _menuRoot.SetActive(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            _reticleAlpha = _reticle.alpha;
            _reticle.alpha = 0f;
            IsPaused = true;
        }

        /// <summary>Bound to the Resume button.</summary>
        public void ResumeGame()
        {
            _menuRoot.SetActive(false);
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            _reticle.alpha = _reticleAlpha;
            IsPaused = false;
        }

        /// <summary>Shows <paramref name="color"/> as the current selection without raising <see cref="ColorChanged"/>.</summary>
        public void SetColor(Color color)
        {
            Color = color;
            int index = _colorPicker.options.FindIndex(option => LabelMatches(color, option.text));
            if (index >= 0) _colorPicker.SetValueWithoutNotify(index);
        }

        /// <summary>Bound to the colour dropdown's OnValueChanged.</summary>
        public void ChangeColor(Dropdown dropdown)
        {
            if (!ColorUtility.TryParseHtmlString(dropdown.options[dropdown.value].text, out Color color)) return;
            Color = color;
            ColorChanged?.Invoke(color);
        }

        /// <summary>Bound to the Quit button.</summary>
        public void Desktop()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
            Application.Quit();
        }

        private static bool LabelMatches(Color color, string label) =>
            ColorUtility.TryParseHtmlString(label, out Color labelColor) && color.Equals(labelColor);

        private static bool EscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
