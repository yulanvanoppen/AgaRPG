using UnityEngine;

namespace AgaRPG.Audio
{
    /// <summary>Mutes all audio in headless (batch mode) server builds.</summary>
    public class SilenceOnServer : MonoBehaviour
    {
        private void Start() => AudioListener.volume = Application.isBatchMode ? 0f : 1f;
    }
}
