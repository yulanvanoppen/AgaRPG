using Cinemachine;
using FishNet.Object;
using System.Threading.Tasks;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM 
[RequireComponent(typeof(PlayerInput))]
#endif
public class PlayerCameraControl : NetworkBehaviour
{
    [Tooltip("Mouse/joystick look sensitivity")]
    public float Sensitivity = 1f;

    [Header("Cinemachine")]
    [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
    public GameObject CinemachineCameraTarget;

    [Tooltip("How far in degrees can you move the camera up")]
    public float TopClamp = 80.0f;

    [Tooltip("How far in degrees can you move the camera down")]
    public float BottomClamp = -45.0f;

    [Tooltip("Additional degress to override the camera. Useful for fine tuning camera position when locked")]
    public float CameraAngleOverride = 0.0f;

    [Tooltip("For locking the camera position on all axis")]
    public bool LockCameraPosition = false;


    // cinemachine
    public float cinemachineTargetYaw;
    public float cinemachineTargetPitch;
    private GameObject _mainCameraGO;
    private CinemachineVirtualCamera _followVirtualCamera;
    private float _targetCameraDistance = 3;

    // input 
#if ENABLE_INPUT_SYSTEM 
    private PlayerInput _playerInput;
#endif
    private InputController _input;
    [HideInInspector] public bool rotateOnMove = true;
    private bool IsCurrentDeviceMouse
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (_playerInput != null)
                return _playerInput.currentControlScheme == "KeyboardMouse";
            else
                return GetComponent<PlayerInput>().currentControlScheme == "KeyboardMouse";
#else
			return false;
#endif
        }
    }

    // player
    private const float _threshold = 0.01f;

    // managers
    private PauseMenu _menuManager;


    private void Awake()
    {
        cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;

        _input = GetComponent<InputController>();
    }


    public override void OnStartClient()
    {
        base.OnStartClient();
        Initialize();
    }


    private void Initialize()
    {
        if (_mainCameraGO != null) return;

        _mainCameraGO = GameObject.FindGameObjectWithTag("MainCamera");
        _followVirtualCamera = GameObject.FindGameObjectWithTag("PlayerFollowCamera").GetComponent<CinemachineVirtualCamera>();

        if (base.IsOwner) _followVirtualCamera.Follow = CinemachineCameraTarget.transform;
    }


    private void Update()
    {
        if (!base.IsOwner) return;
        Zoom();
    }


    private void LateUpdate()
    {
        if (!base.IsOwner) return;
        CameraRotation();
    }


    private void CameraRotation()
    {
        // if there is an input and camera position is not fixed
        if (_input.look.sqrMagnitude >= _threshold && !LockCameraPosition)
        {
            //Don't multiply mouse input by Time.deltaTime;
            float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

            cinemachineTargetYaw += _input.look.x * deltaTimeMultiplier * Sensitivity;
            cinemachineTargetPitch += _input.look.y * deltaTimeMultiplier * Sensitivity;
        }

        // clamp our rotations so our values are limited 360 degrees
        cinemachineTargetYaw = ClampAngle(cinemachineTargetYaw, float.MinValue, float.MaxValue);
        cinemachineTargetPitch = ClampAngle(cinemachineTargetPitch, BottomClamp, TopClamp);

        // Cinemachine will follow this target
        CinemachineCameraTarget.transform.rotation = Quaternion.Euler(cinemachineTargetPitch + CameraAngleOverride,
            cinemachineTargetYaw, 0.0f);
    }


    private void Zoom()
    {
        if (base.IsOwner)
        {
            if (Input.GetAxis("Mouse ScrollWheel") > 0f)
            {
                if (_targetCameraDistance > 2.5f) _targetCameraDistance -= .5f;
            }
            else if (Input.GetAxis("Mouse ScrollWheel") < 0f)
            {
                if (_targetCameraDistance < 10f) _targetCameraDistance += .5f;
            }

            var follow = _followVirtualCamera.GetCinemachineComponent<Cinemachine3rdPersonFollow>();
            follow.CameraDistance = Mathf.Lerp(follow.CameraDistance, _targetCameraDistance, 2f * Time.deltaTime);
            follow.CameraSide = 0.8f + 0.1f * Mathf.Pow(follow.CameraDistance - 10f, 2f) / 56.25f;
        }
    }


    public float GetCameraDistance()
    {
        var follow = _followVirtualCamera.GetCinemachineComponent<Cinemachine3rdPersonFollow>();
        return follow.CameraDistance;
    }


    private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
    {
        if (lfAngle < -360f) lfAngle += 360f;
        if (lfAngle > 360f) lfAngle -= 360f;
        return Mathf.Clamp(lfAngle, lfMin, lfMax);
    }


    public Vector3 EulerAngles() => _mainCameraGO.transform.eulerAngles;

    public Vector3 Forward() => _mainCameraGO.transform.forward;
}