using UnityEngine;
using UnityEngine.InputSystem;

namespace CrazyElevator.Managers
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class LocalPlayerInput : MonoBehaviour
    {
        PlayerInput playerInput;
        bool registered;

        void Awake()
        {
            playerInput = GetComponent<PlayerInput>();
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            if (registered || InputManager.Instance == null) return;
            InputManager.Instance.RegisterPlayerInput(playerInput);
            registered = true;
        }
    }
}