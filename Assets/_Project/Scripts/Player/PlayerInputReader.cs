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
        private bool inputEnabled = true;
        private bool hasFocus;
        private bool paused;

        public Vector2 Movement { get; private set; }
        public bool CanRead => isActiveAndEnabled && inputEnabled && hasFocus && !paused && move != null;
        public event Action ControlsSuppressed;

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
            move = runtimeActions.FindAction("Player/Move", false);
            if (move == null)
            {
                Debug.LogError("[PlayerInputReader] Falta la acción Player/Move.", this);
                enabled = false;
                return;
            }
            move.performed += ReadMovement;
            move.canceled += ReadMovement;
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
            if (runtimeActions != null)
            {
                runtimeActions.Disable();
                Destroy(runtimeActions);
            }
        }

        public void SetInputEnabled(bool value)
        {
            inputEnabled = value;
            RefreshInput();
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
            if (CanRead)
                move.Enable();
            else
                SuppressInput();
        }

        private void SuppressInput()
        {
            move?.Disable();
            Movement = Vector2.zero;
            ControlsSuppressed?.Invoke();
        }

        private void ReadMovement(InputAction.CallbackContext context)
        {
            Movement = CanRead && !context.canceled
                ? Vector2.ClampMagnitude(context.ReadValue<Vector2>(), 1f)
                : Vector2.zero;
        }
    }
}
