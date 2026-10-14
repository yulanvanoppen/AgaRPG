using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;
using UnityEngine.UI;

public class ReticleControl : NetworkBehaviour
{
    private RectTransform _reticle;
    private CanvasGroup _reticleGroup;
    private CharacterController _characterController;
    private PlayerState _playerState;
    private PlayerAttributes _playerAttributes;
    private PlayerWeapons _playerWeapons;

    public float aimSize;
    public float restingSize;
    public float lookSize;
    public float maxSize;
    public float speed;
    private float currentSize;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _playerState = GetComponent<PlayerState>();
        _playerAttributes = GetComponent<PlayerAttributes>();
        _playerWeapons = GetComponent<PlayerWeapons>();
    }

    public override void OnStartClient()
    {
        _reticle = GameObject.Find("Reticle").GetComponent<RectTransform>();
        _reticleGroup = GameObject.Find("Reticle").GetComponent<CanvasGroup>();
    }

    private void Update()
    {
        if (IsOwner)
        {
            bool hide = _playerAttributes.dead; // || _playerWeapons.rangedWeapon == null;
            _reticleGroup.alpha = hide ? 0f : 1f;
            
            if (isMoving && isAiming || !_playerState.Is(MeleeState.Inactive))
            {
                currentSize = Mathf.Lerp(currentSize, restingSize, Time.deltaTime * speed);
            }
            else if (isMoving)
            {
                currentSize = Mathf.Lerp(currentSize, maxSize, Time.deltaTime * speed);
            }
            else if (isAiming)
            {
                currentSize = Mathf.Lerp(currentSize, aimSize, Time.deltaTime * speed);
            }
            else if (isLooking)
            {
                currentSize = Mathf.Lerp(currentSize, lookSize, Time.deltaTime * speed);
            }
            else
            {
                currentSize = Mathf.Lerp(currentSize, restingSize, Time.deltaTime * speed);
            }
            _reticle.sizeDelta = new Vector2(currentSize, currentSize);
        }

    }

    bool isMoving
    {
        get
        {
            return Input.GetAxis("Horizontal") != 0 ||
                Input.GetAxis("Vertical") != 0 ||
                _characterController.velocity.sqrMagnitude > 1.9;
        }
    }

    bool isLooking
    {
        get
        {
            return Input.GetAxis("Mouse X") != 0 ||Input.GetAxis("Mouse Y") != 0;
        }
    }


    bool isAiming
    {
        get
        {
            return GetComponent<InputController>().aim;
        }
    }
}
