using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.Audio
{
    /// <summary>
    /// Background music: plays the lobby track, then random tracks (never the same twice in a
    /// row) cross-faded through a <see cref="DoubleAudioSource"/>. Silent in headless builds.
    /// </summary>
    [RequireComponent(typeof(DoubleAudioSource))]
    public class RandomAudioPlaylistLoop : MonoBehaviour
    {
        [FormerlySerializedAs("soundtrack")]
        [SerializeField] private AudioClip[] _soundtrack;

        [FormerlySerializedAs("volume")]
        [SerializeField] private float _volume;

        [Tooltip("Index of the track played in the lobby")]
        [SerializeField] private int _lobbyTrack = 1;

        [SerializeField] private float _crossFadeTime = 2f;

        private DoubleAudioSource _audio;
        private int _currentTrack;

        private void Awake()
        {
            _audio = GetComponent<DoubleAudioSource>();
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (Application.isBatchMode || _soundtrack.Length == 0) return;

            _currentTrack = Mathf.Clamp(_lobbyTrack, 0, _soundtrack.Length - 1);
            _audio.CrossFade(_soundtrack[_currentTrack], _volume, 0.1f);
        }

        /// <summary>Switches from the lobby track to the in-game playlist.</summary>
        public void StartGame()
        {
            if (!Application.isBatchMode) PlayNextTrack();
        }

        private void Update()
        {
            if (Application.isBatchMode || _soundtrack.Length == 0) return;
            if (_soundtrack[_currentTrack].length - _audio.GetTime() < _crossFadeTime) PlayNextTrack();
        }

        private void PlayNextTrack()
        {
            if (_soundtrack.Length < 2) return;

            int next = _currentTrack;
            while (next == _currentTrack) next = Random.Range(0, _soundtrack.Length);

            _audio.CrossFade(_soundtrack[next], _volume, _crossFadeTime);
            _currentTrack = next;
        }
    }
}
