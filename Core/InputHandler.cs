using System.Linq;
using Il2CppSprocket.Gameplay.VehicleControl;
using Il2CppSprocket.PlayerControl;
using UnityEngine;
using MelonLoader;
using UnityEngine.InputSystem;
using SprocketMultiplayer.UI;

namespace SprocketMultiplayer.Core {
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
            
            if (Keyboard.current.f6Key.wasPressedThisFrame) {
                if (Keyboard.current.f6Key.wasPressedThisFrame) {
                    

                    MelonLogger.Msg("=== SCENE INSPECTION ===");
                    SceneLogger.LogSceneDetails();
                    SceneLogger.LogVehiclesAdvanced();
                    SceneLogger.LogControlAssignments();
                    SceneLogger.LogSpawners();
                    SceneLogger.LogPlayerLinks();
                    SceneLogger.LogPlayerFields();
                    

                    var vehiclesWithControl = GameObject.FindObjectsOfType<GameObject>(true)
                        .Where(go =>
                        {
                            var comps = go.GetComponents<MonoBehaviour>();
                            if (comps == null) return false;
                            return comps.Any(c => c != null && (c.GetType().Name.Contains("Control") || c.GetType().Name.Contains("Driver")));
                        });

                    foreach (var vehicle in vehiclesWithControl)
                    {
                        SceneLogger.LogVehicleComponents(vehicle);
                    }
                    
                    SceneLogger.TrackNewVehicles();

                    MelonLogger.Msg("=========================");
                }
            }
        }
    }
}