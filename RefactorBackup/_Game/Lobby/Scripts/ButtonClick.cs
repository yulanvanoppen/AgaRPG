using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;

/// <summary>
///     Play some satisfying click sounds
/// </summary>
public class ButtonClick : MonoBehaviour, IPointerDownHandler {
    public AudioClip buttonClick;

    public void OnPointerDown(PointerEventData eventData) {
        AudioSource.PlayClipAtPoint(buttonClick, Vector3.zero, 1f);
    }
}