using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FloatingHealthBar : NetworkBehaviour
{
    [SerializeField] private Slider _slider;
    [SerializeField] private GameObject _mainCameraGO;

    private bool _initialized;
    private float _targetValue = 1;

    private void Start()
    {
        Initialize();
    }

    public override void OnStartClient()
    {
        Initialize();
        base.OnStartClient();
    }

    private void Initialize()
    {
        if (!_initialized)
            if (_mainCameraGO == null)
                _mainCameraGO = GameObject.FindGameObjectWithTag("MainCamera");
    }


    public void UpdateHealthBar(float currentValue, float maxValue)
    {
        _targetValue = currentValue / maxValue;
    }

    // Update is called once per frame
    void Update()
    {
        if (_mainCameraGO == null)
            _mainCameraGO = GameObject.FindGameObjectWithTag("MainCamera");
        transform.rotation = _mainCameraGO.transform.rotation;
        _slider.value = Mathf.Lerp(_slider.value, _targetValue, .1f);
    }
}
