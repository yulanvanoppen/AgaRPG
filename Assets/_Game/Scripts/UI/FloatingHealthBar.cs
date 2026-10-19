using FishNet.Object;
using UnityEngine;
using UnityEngine.UI;

namespace AgaRPG.UI
{
    /// <summary>World-space health bar above a player that faces the camera and eases to new values.</summary>
    public class FloatingHealthBar : NetworkBehaviour
    {
        [SerializeField] private Slider _slider;
        [SerializeField] private float _smoothing = 6f;

        private float _targetValue = 1f;

        public void UpdateHealthBar(float current, float max) => _targetValue = max > 0f ? current / max : 0f;

        private void LateUpdate()
        {
            Billboard.FaceMainCamera(transform);
            _slider.value = Mathf.Lerp(_slider.value, _targetValue, 1f - Mathf.Exp(-_smoothing * Time.deltaTime));
        }
    }
}
