using UnityEngine;
using MelonLoader;
using UnityEngine.InputSystem;
using SprocketMultiplayer.UI;

namespace SprocketMultiplayer {
    public class InputHandler : MonoBehaviour {
        public NetworkManager network;
        private Console consoleInstance;

        private void Start() {
            consoleInstance = FindObjectOfType<Console>();
        }

        private void Update() {
            // Skip custom hotkeys if console is open AND focused
            var console = Console.Instance;
            if (console != null && Console.IsOpen && console.IsFocused())
                return;

            if (Keyboard.current == null)
                return;

            if (Keyboard.current.hKey.wasPressedThisFrame) {
                MelonLogger.Msg("Starting host...");
                network?.StartHost(7777);
            }

            if (Keyboard.current.cKey.wasPressedThisFrame) {
                MelonLogger.Msg("Connecting to host...");
                network?.ConnectToHost("127.0.0.1", 7777);
            }

            if (Keyboard.current.pKey.wasPressedThisFrame) {
                MelonLogger.Msg("Pinging host...");
                network?.Send("Ping!");
            }
        }
    }
}