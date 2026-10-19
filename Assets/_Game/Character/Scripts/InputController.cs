using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AgaRPG.Player
{
    /// <summary>
    /// Receives <see cref="PlayerInput"/> "Send Messages" callbacks and exposes the current
    /// input as read-only values. Only the owning client has its <c>PlayerInput</c> enabled,
    /// so on every other instance all values stay at their defaults.
    /// </summary>
    public class InputController : MonoBehaviour
    {
        [Header("Movement")]
        [FormerlySerializedAs("analogMovement")]
        [SerializeField] private bool _analogMovement;

        [Header("Mouse Cursor")]
        [FormerlySerializedAs("cursorLocked")]
        [SerializeField] private bool _cursorLocked = true;

        [FormerlySerializedAs("cursorInputForLook")]
        [SerializeField] private bool _cursorInputForLook = true;

        private bool _jumpPressed;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool Sprint { get; private set; }
        public bool Aim { get; private set; }

        /// <summary>True while the attack/shoot button is held.</summary>
        public bool Shoot { get; private set; }

        /// <summary>Whether <see cref="Move"/> comes from an analog stick (magnitude matters).</summary>
        public bool AnalogMovement => _analogMovement;

        /// <summary>Returns true once per jump press and clears the latch.</summary>
        public bool ConsumeJump()
        {
            bool pressed = _jumpPressed;
            _jumpPressed = false;
            return pressed;
        }

#if ENABLE_INPUT_SYSTEM
        // Message handlers: names must match the actions in the input action asset.
        private void OnMove(InputValue value) => Move = value.Get<Vector2>();

        private void OnLook(InputValue value)
        {
            if (_cursorInputForLook) Look = value.Get<Vector2>();
        }

        private void OnJump(InputValue value)
        {
            if (value.isPressed) _jumpPressed = true;
        }

        private void OnSprint(InputValue value) => Sprint = value.isPressed;
        private void OnAim(InputValue value) => Aim = value.isPressed;
        private void OnShoot(InputValue value) => Shoot = value.isPressed;
#endif

        private void OnApplicationFocus(bool hasFocus) => SetCursorLocked(_cursorLocked);

        private static void SetCursorLocked(bool locked) =>
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
    }
}
