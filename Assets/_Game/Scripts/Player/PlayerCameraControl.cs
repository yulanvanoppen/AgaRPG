using FishNet.Object;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AgaRPG.Player
{
    /// <summary>
    /// Third-person orbit camera for the owning player: rotates the Cinemachine follow target
    /// from look input and handles scroll-wheel zoom. Does nothing on non-owned instances.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class PlayerCameraControl : NetworkBehaviour
    {
        [Tooltip("Mouse/joystick look sensitivity")]
        [FormerlySerializedAs("Sensitivity")]
        [SerializeField] private float _sensitivity = 1f;

        [Header("Cinemachine")]
        [Tooltip("The follow target set in the Cinemachine camera that the camera will follow")]
        [FormerlySerializedAs("CinemachineCameraTarget")]
        [SerializeField] private GameObject _cameraTarget;

        [Tooltip("How far in degrees the camera can look up")]
        [FormerlySerializedAs("TopClamp")]
        [SerializeField] private float _topClamp = 80f;

        [Tooltip("How far in degrees the camera can look down")]
        [FormerlySerializedAs("BottomClamp")]
        [SerializeField] private float _bottomClamp = -45f;

        [Tooltip("Additional pitch offset, useful for fine tuning the camera when locked")]
        [FormerlySerializedAs("CameraAngleOverride")]
        [SerializeField] private float _cameraAngleOverride;

        [Tooltip("Lock the camera rotation on all axes")]
        [FormerlySerializedAs("LockCameraPosition")]
        [SerializeField] private bool _lockCameraPosition;

        [Header("Zoom")]
        [SerializeField] private float _minZoomDistance = 2.5f;
        [SerializeField] private float _maxZoomDistance = 10f;
        [SerializeField] private float _zoomStep = 0.5f;
        [SerializeField] private float _zoomSmoothing = 2f;

        private const string FollowCameraTag = "PlayerFollowCamera";
        private const float LookThreshold = 0.01f;

        private float _yaw;
        private float _pitch;
        private float _targetCameraDistance = 3f;

        private Transform _mainCamera;
        private CinemachineCamera _followCamera;
        private CinemachineThirdPersonFollow _follow;
        private InputController _input;
#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif

        /// <summary>Transform the Cinemachine cameras follow (the player's camera root).</summary>
        public Transform CameraTarget => _cameraTarget.transform;

        /// <summary>Current look sensitivity; aiming lowers it.</summary>
        public float Sensitivity
        {
            get => _sensitivity;
            set => _sensitivity = value;
        }

        /// <summary>World yaw of the main camera, used to make movement camera-relative.</summary>
        public float CameraYaw => _mainCamera != null ? _mainCamera.eulerAngles.y : 0f;

        /// <summary>Forward direction of the main camera.</summary>
        public Vector3 CameraForward => _mainCamera != null ? _mainCamera.forward : transform.forward;

        /// <summary>Distance of the default follow camera to its target.</summary>
        public float FollowDistance => _follow != null ? _follow.CameraDistance : _targetCameraDistance;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return _playerInput != null && _playerInput.currentControlScheme == "KeyboardMouse";
#else
                return false;
#endif
            }
        }

        private void Awake()
        {
            _input = GetComponent<InputController>();
#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
#endif
            _yaw = _cameraTarget.transform.rotation.eulerAngles.y;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (_mainCamera != null) return;

            Camera main = Camera.main;
            if (main != null) _mainCamera = main.transform;

            GameObject followCameraObject = GameObject.FindGameObjectWithTag(FollowCameraTag);
            _followCamera = followCameraObject.GetComponent<CinemachineCamera>();
            _follow = followCameraObject.GetComponent<CinemachineThirdPersonFollow>();
            if (_follow == null)
                Debug.LogError($"{FollowCameraTag} has no {nameof(CinemachineThirdPersonFollow)} component.", followCameraObject);

            if (base.IsOwner) _followCamera.Follow = _cameraTarget.transform;
        }

        /// <summary>Offsets the camera orientation, e.g. for weapon recoil (degrees).</summary>
        public void AddRotationOffset(float pitch, float yaw)
        {
            _pitch += pitch;
            _yaw += yaw;
        }

        private void Update()
        {
            if (base.IsOwner) Zoom();
        }

        private void LateUpdate()
        {
            if (base.IsOwner) RotateCamera();
        }

        private void RotateCamera()
        {
            if (_input.Look.sqrMagnitude >= LookThreshold && !_lockCameraPosition)
            {
                // Mouse delta is already frame-rate independent; stick input is not.
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1f : Time.deltaTime;
                _yaw += _input.Look.x * deltaTimeMultiplier * _sensitivity;
                _pitch += _input.Look.y * deltaTimeMultiplier * _sensitivity;
            }

            _yaw = ClampAngle(_yaw, float.MinValue, float.MaxValue);
            _pitch = ClampAngle(_pitch, _bottomClamp, _topClamp);

            _cameraTarget.transform.rotation = Quaternion.Euler(_pitch + _cameraAngleOverride, _yaw, 0f);
        }

        private void Zoom()
        {
            if (_follow == null) return;

            float scroll = ReadScroll();
            if (scroll > 0f) _targetCameraDistance = Mathf.Max(_minZoomDistance, _targetCameraDistance - _zoomStep);
            else if (scroll < 0f) _targetCameraDistance = Mathf.Min(_maxZoomDistance, _targetCameraDistance + _zoomStep);

            _follow.CameraDistance = Mathf.Lerp(_follow.CameraDistance, _targetCameraDistance, _zoomSmoothing * Time.deltaTime);

            // Move the camera further over the shoulder when zoomed in.
            float zoomedOut = _follow.CameraDistance - _maxZoomDistance;
            _follow.CameraSide = 0.8f + 0.1f * zoomedOut * zoomedOut / 56.25f;
        }

        private static float ReadScroll()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
            return Input.GetAxis("Mouse ScrollWheel");
#endif
        }

        private static float ClampAngle(float angle, float min, float max)
        {
            if (angle < -360f) angle += 360f;
            if (angle > 360f) angle -= 360f;
            return Mathf.Clamp(angle, min, max);
        }
    }
}
