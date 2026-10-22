using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EchoScrap.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;

        private InputActionAsset runtimeActions;
        private InputAction move;
        private InputAction dash;
        private InputAction pause;
        private bool inputEnabled = true;
        private bool hasFocus;
        private bool paused;
        private bool dashRequested;
        private bool suppressDashUntilRelease;

        public Vector2 Movement { get; private set; }
        public bool CanRead => isActiveAndEnabled && inputEnabled && hasFocus && !paused && move != null;
        public bool IsDashQueued => dashRequested && CanRead && !suppressDashUntilRelease;

        public event Action ControlsSuppressed;
        public event Action DashRequested;
        public event Action PauseRequested;

        private void Awake()
        {
            hasFocus = Application.isFocused;
            if (actions == null)
            {
                Debug.LogError("[PlayerInputReader] Asigna el asset de controles del jugador.", this);
                enabled = false;
                return;
            }

            // Cada instancia controla y libera su copia, sin modificar el asset compartido.
            runtimeActions = Instantiate(actions);
            InputActionMap map = runtimeActions.FindActionMap("Player", false);
            move = runtimeActions.FindAction("Player/Move", false);
            if (move == null)
            {
                Debug.LogError("[PlayerInputReader] Falta la acción Player/Move.", this);
                enabled = false;
                return;
            }

            dash = runtimeActions.FindAction("Player/Dash", false);
            if (dash == null && map != null)
            {
                dash = map.AddAction("Dash", InputActionType.Button);
                dash.AddBinding("<Keyboard>/space");
                dash.AddBinding("<Gamepad>/buttonSouth");
            }

            pause = runtimeActions.FindAction("Player/Pause", false);
            if (pause == null && map != null)
            {
                pause = map.AddAction("Pause", InputActionType.Button);
                pause.AddBinding("<Keyboard>/escape");
                pause.AddBinding("<Gamepad>/start");
            }

            move.performed += ReadMovement;
            move.canceled += ReadMovement;
            if (dash != null)
            {
                dash.performed += OnDashPerformed;
                dash.canceled += OnDashCanceled;
            }
            if (pause != null)
            {
                pause.performed += OnPausePerformed;
            }
        }

        private void OnEnable() => RefreshInput();

        private void OnDisable() => SuppressInput();

        private void OnDestroy()
        {
            if (move != null)
            {
                move.performed -= ReadMovement;
                move.canceled -= ReadMovement;
            }
            if (dash != null)
            {
                dash.performed -= OnDashPerformed;
                dash.canceled -= OnDashCanceled;
            }
            if (pause != null)
            {
                pause.performed -= OnPausePerformed;
            }
            if (runtimeActions != null)
            {
                runtimeActions.Disable();
                Destroy(runtimeActions);
            }
        }

        public void SetInputEnabled(bool value)
        {
            bool wasDisabled = !inputEnabled;
            inputEnabled = value;
            if (value && wasDisabled)
            {
                dashRequested = false;
                if (dash != null && dash.IsPressed())
                    suppressDashUntilRelease = true;
            }
            RefreshInput();
        }

        /// <summary>
        /// Suprime la pulsación usada para confirmar una carta o cerrar una interfaz
        /// para que no se convierta accidentalmente en un dash al regresar al combate.
        /// </summary>
        public void SuppressDashFromUiConfirmation()
        {
            dashRequested = false;
            suppressDashUntilRelease = true;
        }

        public bool ConsumeDashRequest()
        {
            if (!CanRead || suppressDashUntilRelease || !dashRequested)
                return false;

            dashRequested = false;
            return true;
        }

        public void ClearPendingDash()
        {
            dashRequested = false;
        }

        private void OnApplicationFocus(bool value)
        {
            hasFocus = value;
            RefreshInput();
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            RefreshInput();
        }

        private void RefreshInput()
        {
            if (isActiveAndEnabled && hasFocus && !paused)
                pause?.Enable();
            else
                pause?.Disable();

            if (CanRead)
            {
                move.Enable();
                dash?.Enable();
                if (dash != null && dash.IsPressed() && suppressDashUntilRelease)
                {
                    dashRequested = false;
                }
                else if (dash != null && !dash.IsPressed())
                {
                    suppressDashUntilRelease = false;
                }
            }
            else
            {
                SuppressInput();
            }
        }

        private void SuppressInput()
        {
            move?.Disable();
            dash?.Disable();
            Movement = Vector2.zero;
            dashRequested = false;
            ControlsSuppressed?.Invoke();
        }

        private void ReadMovement(InputAction.CallbackContext context)
        {
            Movement = CanRead && !context.canceled
                ? Vector2.ClampMagnitude(context.ReadValue<Vector2>(), 1f)
                : Vector2.zero;
        }

        private void OnDashPerformed(InputAction.CallbackContext context)
        {
            if (!CanRead)
                return;

            if (suppressDashUntilRelease)
                return;

            dashRequested = true;
            DashRequested?.Invoke();
        }

        private void OnDashCanceled(InputAction.CallbackContext context)
        {
            suppressDashUntilRelease = false;
        }

        private void OnPausePerformed(InputAction.CallbackContext context)
        {
            if (!isActiveAndEnabled || !hasFocus || paused)
                return;

            PauseRequested?.Invoke();
        }
    }
}
