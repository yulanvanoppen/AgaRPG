
using FishNet.Component.Animating;
using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class WeaponRecoil : NetworkBehaviour
{
    public Animator rigController;

    [SerializeField] private float _recoilIntensity = 1f;
    [SerializeField] private float _recoilNoise = .25f;
    [SerializeField] private float _recoilDuration = .05f;
    [SerializeField] private Vector2[] _recoilPattern;

    [SerializeField] private float kickback = .1f;
    [SerializeField] private float recoverySpeed = 10f;

    private float _horRecoil;
    private float _vertRecoil;
    private int _index = 0;
    private float _horMean = 0f;

    private int _animIDRecoil;
    private float _time = 0f;

    [HideInInspector] public Unity.Cinemachine.CinemachineImpulseSource _impulseSource;
    private Unity.Cinemachine.Cinemachine3rdPersonFollow _aimVirtualCameraFollow;

    private PlayerMovement _playerMovement;
    private PlayerCameraControl _playerCameraControl;
    private InputController _input;

    private bool _initalized = false;
    

    public void Initialize()
    {
        _animIDRecoil = Animator.StringToHash("Recoil");
        _impulseSource = GetComponentInChildren<Unity.Cinemachine.CinemachineImpulseSource>();
        _playerMovement = transform.root.GetComponent<PlayerMovement>();
        _playerCameraControl = transform.root.GetComponent<PlayerCameraControl>();
        _input = transform.root.GetComponent<InputController>();


        for (var idx = 0; idx < _recoilPattern.Length; idx++)
            _horMean += _recoilPattern[idx].y;

        _horMean /= _recoilPattern.Length;

        if (IsOwner) _aimVirtualCameraFollow = GameObject.FindGameObjectWithTag("PlayerAimCamera")
                                                         .GetComponent<Unity.Cinemachine.CinemachineVirtualCamera>()
                                                         .GetCinemachineComponent<Unity.Cinemachine.Cinemachine3rdPersonFollow>();
        _initalized = true;
    }


    private int NextIndex(int index) => (index + 1) % _recoilPattern.Length;


    public void Recoil()
    {
        //Debug.Log($"RIGCONTROLLER {_rigController}");
        _animIDRecoil = Animator.StringToHash("Recoil");
        rigController.SetTrigger(_animIDRecoil);

        if (base.IsOwner)
        {
            _aimVirtualCameraFollow.CameraDistance = 2f + kickback;
            _impulseSource.GenerateImpulse(_playerCameraControl.Forward());
            RecoilPattern();
        }
    }


    public void ResetPattern() => _index = 0;


    private void RecoilPattern()
    {
        _time = _recoilDuration;

        _vertRecoil = _recoilPattern[_index].x - .75f;
        _horRecoil = _recoilPattern[_index].y - _horMean;

        _index = NextIndex(_index);
    }


    private void LateUpdate()
    {
        if (!_initalized) return;

        if (!_input.shoot) ResetPattern();

        if (_time > 0)
        {
            float multiplier = !_playerMovement.Grounded ? 2.5f
                                                         : 1f + 1.5f * _playerMovement.speed / _playerMovement.MoveSpeed;
            float amplitude = (_recoilIntensity + Random.Range(-_recoilNoise, _recoilNoise)) * multiplier;

            _playerCameraControl.cinemachineTargetPitch -= (amplitude * _vertRecoil * Time.deltaTime) / _recoilDuration;
            _playerCameraControl.cinemachineTargetYaw -= (amplitude * _horRecoil * Time.deltaTime) / _recoilDuration;

            _time -= Time.deltaTime;
        }

        if (base.IsOwner)
        {
            if (_aimVirtualCameraFollow == null)
                _aimVirtualCameraFollow = GameObject.FindGameObjectWithTag("PlayerAimCamera")
                                                    .GetComponent<Unity.Cinemachine.CinemachineVirtualCamera>()
                                                    .GetCinemachineComponent<Unity.Cinemachine.Cinemachine3rdPersonFollow>();
            if (_aimVirtualCameraFollow.CameraDistance > 2)
                _aimVirtualCameraFollow.CameraDistance = Mathf.Lerp(_aimVirtualCameraFollow.CameraDistance, 
                                                                    2, recoverySpeed * Time.deltaTime);
        }
    }
}
