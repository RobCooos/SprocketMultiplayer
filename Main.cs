using System;
using MelonLoader;
using UnityEngine;
using System.Collections;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using SprocketMultiplayer.Core;
using SprocketMultiplayer.Patches;
using SprocketMultiplayer.UI;
using UnityEngine.SceneManagement;

namespace SprocketMultiplayer
{
    public class Main : MelonMod
    {
        public static string GetPlayerFaction() => "AllowedVehicles";
        
        private static NetworkManager network;
        private static bool handlerSpawned = false;
        private const int MaxRetryAttempts = 3;
        private bool consoleSpawned = false;
        private static bool photomodeWarning = false;
        
        // Scene change detection
        private string lastSceneName = "";
        
        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("========================================");
            MelonLogger.Msg("Sprocket Multiplayer Mod Initializing...");
            MelonLogger.Msg("========================================");
            
            // Register IL2CPP types
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<Menu.HandleClicks>();
                ClassInjector.RegisterTypeInIl2Cpp<InputHandler>();
                ClassInjector.RegisterTypeInIl2Cpp<UI.Console>();
                MelonLogger.Msg("✓ IL2CPP types registered");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to register IL2CPP types: {ex.Message}");
            }
            
            // Initialize NetworkManager
            try
            {
                network = new NetworkManager();
                if (network == null)
                {
                    throw new Exception("NetworkManager constructor returned null");
                }
                MelonLogger.Msg("✓ NetworkManager initialized");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"NetworkManager initialization failed: {ex.Message}");
                network = null;
            }
            
            // Apply Harmony patches
            try
            {
                new HarmonyLib.Harmony("SprocketMultiplayer").PatchAll();
                MelonLogger.Msg("✓ Harmony patches applied");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Some Harmony patches failed: {ex.Message}");
            }
            
            // Show photomode warning once
            if (!photomodeWarning)
            {
                ShowPhotomodeWarning();
                photomodeWarning = true;
            }
            
            // We use OnUpdate for scene detection instead of events
            MelonLogger.Msg("Scene detection via OnUpdate enabled");
            
            // Test VehicleManager
            MelonLogger.Msg("[VehicleManager] Game started. Testing VehicleManager...");
            string currentFaction = GetPlayerFaction();
            bool canPickVehicle = VehicleManager.CheckFaction(currentFaction);
            MelonLogger.Msg($"[VehicleManager] VehicleManager.CheckFaction returned {canPickVehicle}");
            
            MelonLogger.Msg("========================================");
            MelonLogger.Msg("✓ Initialization complete");
            MelonLogger.Msg("========================================");
        }

        private static void ShowPhotomodeWarning()
        {
            MelonLogger.Warning(
                "\n========================================================\n" +
                "PLEASE NOTE!\n" +
                "Photomode is disabled while using Sprocket Multiplayer Sessions.\n" +
                "This is done to prevent users stopping time while in-game.\n" +
                "Photomode not working IS NOT a bug, but an intended block.\n" +
                "Thank you for trying the mod, and have fun!\n" +
                "========================================================"
            );
        }

        public static class VehicleManager
        {
            private const string AllowedFaction = "AllowedVehicles";

            public static bool CheckFaction(string playerFaction)
            {
                MelonLogger.Msg($"[VehicleManager] CheckFaction called with faction: {playerFaction}");
                if (playerFaction == AllowedFaction)
                {
                    MelonLogger.Msg("[VehicleManager] Faction allowed.");
                    return true;
                }
                else
                {
                    MelonLogger.Msg("[VehicleManager] Your current faction is not allowed. Select AllowedVehicles faction to pick a tank.");
                    return false;
                }
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (handlerSpawned) return;
            handlerSpawned = true;

            try
            {
                SpawnInputHandler();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Spawn failed in scene {sceneName} (buildIndex {buildIndex}): {ex.Message}");
                MelonCoroutines.Start(DelayedSpawn(0));
            }
        }

        private void SpawnInputHandler()
        {
            var go = new GameObject("MultiplayerInputHandler");
            GameObject.DontDestroyOnLoad(go);

            var il2cppType = Il2CppType.From(typeof(InputHandler));
            var comp = go.AddComponent(il2cppType)?.Cast<InputHandler>();
            if (comp == null)
                throw new Exception("Failed to instantiate InputHandler component.");
            
            MelonLogger.Msg("Input handler spawned successfully.");
        }

        private IEnumerator DelayedSpawn(int attempt)
        {
            if (attempt >= MaxRetryAttempts)
            {
                MelonLogger.Error("Max retry attempts reached. Input handler not spawned.");
                yield break;
            }

            yield return new WaitForSeconds(0.1f);
            try
            {
                SpawnInputHandler();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Delayed spawn (attempt {attempt + 1}) failed: {ex.Message}");
                MelonCoroutines.Start(DelayedSpawn(attempt + 1));
            }
        }

        public override void OnUpdate()
        {
            // Poll network events
            if (network != null)
            {
                network.PollEvents();
            }
            
            // Try to spawn console if not yet spawned
            if (!consoleSpawned)
            {
                TrySpawnConsole();
            }
            
            // Check for scene changes
            CheckSceneChange();
        }

        private void CheckSceneChange()
        {
            var activeScene = SceneManager.GetActiveScene();
            
            // Check if scene changed
            if (activeScene.name != lastSceneName && !string.IsNullOrEmpty(activeScene.name))
            {
                string oldScene = lastSceneName;
                lastSceneName = activeScene.name;
                
                // Only process if we had a previous scene (skip initial load)
                if (!string.IsNullOrEmpty(oldScene))
                {
                    OnSceneChanged(activeScene);
                }
            }
        }

        private void OnSceneChanged(Scene scene)
        {
            MelonLogger.Msg("========================================");
            MelonLogger.Msg($"Scene Changed: {scene.name}");
            MelonLogger.Msg("========================================");
            
            // Check if multiplayer is active
            if (NetworkManager.Instance == null)
            {
                MelonLogger.Msg("[SceneLoad] NetworkManager.Instance is null");
                return;
            }
            
            bool isMultiplayer = NetworkManager.Instance.IsActiveMultiplayer;
            MelonLogger.Msg($"[SceneLoad] Is multiplayer active? {isMultiplayer}");
            
            if (!isMultiplayer)
            {
                MelonLogger.Msg("[SceneLoad] Not in multiplayer mode, ignoring scene load");
                return;
            }
            
            // Check if this is a gameplay scene
            bool isGameplayScene = IsGameplayScene(scene.name);
            MelonLogger.Msg($"[SceneLoad] Is gameplay scene? {isGameplayScene}");
            
            if (isGameplayScene)
            {
                MelonLogger.Msg($"[SceneLoad] ✓ Multiplayer gameplay scene detected: {scene.name}");
                
                // Notify MultiplayerManager
                if (MultiplayerManager.Instance == null)
                {
                    MelonLogger.Error("[SceneLoad] ✗ MultiplayerManager.Instance is NULL!");
                    return;
                }
                
                MelonLogger.Msg("[SceneLoad] Calling MultiplayerManager.OnSceneLoaded()...");
                MultiplayerManager.Instance.OnSceneLoaded();
                MelonLogger.Msg("[SceneLoad] ✓ MultiplayerManager notified");
            }
            else
            {
                MelonLogger.Msg($"[SceneLoad] Scene '{scene.name}' is not a gameplay scene");
            }
        }

        private bool IsGameplayScene(string sceneName) {
            string[] gameplayScenes = new string[] {
                "Railway",
                "Sandbox"
            };
            
            // Check exact matches
            foreach (string name in gameplayScenes)
            {
                if (sceneName == name)
                    return true;
            }
            
            // Check if it contains railway
            // TODO: currently hardcoded.Can be dynamic
            if (sceneName.Contains("Mission"))
                return true;
            
            return false;
        }

        private void TrySpawnConsole()
        {
            try
            {
                GameObject consoleGO = new GameObject("SprocketConsole");
                GameObject.DontDestroyOnLoad(consoleGO);

                var il2cppType = Il2CppType.From(typeof(UI.Console));
                var comp = consoleGO.AddComponent(il2cppType)?.Cast<UI.Console>();

                if (comp != null)
                {
                    MelonLogger.Msg("Console spawned as IL2CPP component successfully.");
                    consoleSpawned = true;
                }
                else
                {
                    MelonLogger.Error("Failed to cast or attach Console component.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to spawn console: {ex.Message}");
            }
        }

        public override void OnApplicationQuit()
        {
            MelonLogger.Msg("Sprocket Multiplayer shutting down...");
            
            if (network != null)
            {
                network.Shutdown();
            }
            
            MelonLogger.Msg("✓ Sprocket Multiplayer shut down.");
        }
    }
}