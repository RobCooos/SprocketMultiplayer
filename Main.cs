using System;
using MelonLoader;
using UnityEngine;
using System.Collections;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using SprocketMultiplayer.UI;

namespace SprocketMultiplayer{
    public class Main : MelonMod {
        private static NetworkManager network;
        private static bool handlerSpawned = false;
        private const int MaxRetryAttempts = 3;
        private bool consoleSpawned = false;
        private int frames = 0;
        
        
        public override void OnInitializeMelon() {
            try {
                network = new NetworkManager() ??
                          throw new System.Exception("NetworkManager constructor returned null");
                ClassInjector.RegisterTypeInIl2Cpp<InputHandler>();
                ClassInjector.RegisterTypeInIl2Cpp<SprocketMultiplayer.UI.Console>();
                MelonLogger.Msg("Sprocket Multiplayer initialized successfully.");
            }
            catch (System.Exception ex) {
                MelonLogger.Error($"Initialization failed: {ex.Message}\nStackTrace: {ex.StackTrace}");
                network = null; // explicit null so OnUpdate knows not to use it
            }
            new HarmonyLib.Harmony("SprocketMultiplayer").PatchAll();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName) {
            if (handlerSpawned) return;
            handlerSpawned = true;

            try {
                SpawnInputHandler();
            }
            catch (System.Exception ex) {
                MelonLogger.Error(
                    $"Spawn failed in scene {sceneName} (buildIndex {buildIndex}): {ex.Message}\nStackTrace: {ex.StackTrace}");
                MelonCoroutines.Start(DelayedSpawn(0));
            }

        }

        private void SpawnInputHandler() {
            var go = new GameObject("MultiplayerInputHandler");
            GameObject.DontDestroyOnLoad(go);

            // Add InputHandler component via IL2CPP interop
            var il2cppType = Il2CppType.From(typeof(InputHandler));
            var comp = go.AddComponent(il2cppType)?.Cast<InputHandler>();
            if (comp == null)
                throw new System.Exception("Failed to instantiate InputHandler component.");

            // Assign network via public field instead of Init()
            comp.network = network;

            MelonLogger.Msg("Input handler spawned successfully.");
        }


        private IEnumerator DelayedSpawn(int attempt) {
            if (attempt >= MaxRetryAttempts) {
                MelonLogger.Error("Max retry attempts reached. Input handler not spawned.");
                yield break;
            }

            yield return new WaitForSeconds(0.1f);
            try {
                SpawnInputHandler();
            }
            catch (System.Exception ex) {
                MelonLogger.Error(
                    $"Delayed spawn (attempt {attempt + 1}) failed: {ex.Message}\nStackTrace: {ex.StackTrace}");
                MelonCoroutines.Start(DelayedSpawn(attempt + 1));
            }
        }

        public override void OnUpdate() {
            if (network == null) return;
            network.PollEvents();
            if (!consoleSpawned) {
                TrySpawnConsole(); //keep trying until succeed
            }
        }

        private void TrySpawnConsole() {
            try {
                GameObject consoleGO = new GameObject("SprocketConsole");
                GameObject.DontDestroyOnLoad(consoleGO);

                var il2cppType = Il2CppType.From(typeof(SprocketMultiplayer.UI.Console));
                var comp = consoleGO.AddComponent(il2cppType)?.Cast<SprocketMultiplayer.UI.Console>();

                if (comp != null) {
                    MelonLogger.Msg("Console spawned as IL2CPP component successfully.");
                    consoleSpawned = true;
                }
                else {
                    MelonLogger.Error("Failed to cast or attach IL2CPP Console component.");
                }
            }
            catch (Exception ex) {
                MelonLogger.Error($"Failed to spawn console: {ex}");
            }
        }


        public override void OnApplicationQuit() {
            network?.Shutdown();
            MelonLogger.Msg("Sprocket Multiplayer shut down.");
        }
    }
}