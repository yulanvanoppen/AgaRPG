using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

namespace AgaRPG.UI
{
    /// <summary>Plays a click sound when the button is pressed.</summary>
    public class ButtonClick : MonoBehaviour, IPointerDownHandler
    {
        [FormerlySerializedAs("buttonClick")]
        [SerializeField] private AudioClip _clickSound;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_clickSound != null) AudioSource.PlayClipAtPoint(_clickSound, Vector3.zero, 1f);
        }
    }
}
