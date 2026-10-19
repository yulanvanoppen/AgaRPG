using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace AgaRPG.UI
{
    /// <summary>Loading overlay and error toast for the lobby, faded with DOTween.</summary>
    public class CanvasUtilities : MonoBehaviour
    {
        public static CanvasUtilities Instance { get; private set; }

        [SerializeField] private CanvasGroup _loader;
        [SerializeField] private float _fadeTime;
        [SerializeField] private TMP_Text _loaderText;
        [SerializeField] private TMP_Text _errorText;

        private Tween _tween;

        private void Awake()
        {
            Instance = this;
            Toggle(false, instant: true);
        }

        /// <summary>Shows or hides the loading overlay with an optional message.</summary>
        public void Toggle(bool on, string text = null, bool instant = false)
        {
            _loaderText.text = text;
            _loader.gameObject.SetActive(on);
            _tween?.Kill();
            _tween = _loader.DOFade(on ? 1f : 0f, instant ? 0f : _fadeTime);
        }

        /// <summary>Briefly shows an error message.</summary>
        public void ShowError(string error)
        {
            _errorText.text = error;
            _errorText.DOFade(1f, _fadeTime).OnComplete(() => _errorText.DOFade(0f, _fadeTime).SetDelay(1f));
        }
    }

    /// <summary>
    /// Shows the loading overlay for the lifetime of a <c>using</c> block:
    /// <c>using (new Load("Logging in...")) await ...;</c>
    /// </summary>
    public readonly struct Load : IDisposable
    {
        public Load(string text)
        {
            if (CanvasUtilities.Instance != null) CanvasUtilities.Instance.Toggle(true, text);
        }

        public void Dispose()
        {
            if (CanvasUtilities.Instance != null) CanvasUtilities.Instance.Toggle(false);
        }
    }
}
