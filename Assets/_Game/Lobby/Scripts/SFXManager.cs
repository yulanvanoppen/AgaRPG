using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomAudioPlaylistLoop : MonoBehaviour
{
    public AudioClip[] soundtrack;
    public float volume = 0f;
    private AudioSource _audioSource;
    private DoubleAudioSource _doubleAudioSource;

    private int _previous;

    private void Awake()
    {
        _doubleAudioSource = GetComponent<DoubleAudioSource>();
        DontDestroyOnLoad(this);
    }

    private void Start()
    {
        if (!Application.isBatchMode)
        {
            _previous = 1;
            _doubleAudioSource.CrossFade(soundtrack[_previous], volume, .1f);
        }
    }

    public void StartGame()
    {
        if (!Application.isBatchMode)
        {
            PlayNextTrack();
        }
    }

    private void PlayNextTrack()
    {
        int nextTrack = _previous;
        while (nextTrack == _previous) nextTrack = Random.Range(0, soundtrack.Length);
        _doubleAudioSource.CrossFade(soundtrack[nextTrack], volume, 2f);
        _previous = nextTrack;
    }

    // Update is called once per frame
    private void Update()
    {
        if (soundtrack[_previous].length - _doubleAudioSource.GetTime() < 2f && !Application.isBatchMode)
        {
            PlayNextTrack();
        }
    }
}