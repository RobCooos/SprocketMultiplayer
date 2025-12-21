using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using SprocketMultiplayer.Patches;
using Il2CppSprocket.Vehicles;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SprocketMultiplayer.Core {
    public class MultiplayerManager {
        public static MultiplayerManager Instance = new MultiplayerManager();
        
        public Dictionary<string, string> PlayerChosenTanks = new Dictionary<string, string>();
        public Dictionary<string, IVehicleEditGateway> SpawnedVehicles = new Dictionary<string, IVehicleEditGateway>();

        private bool sceneReady;
        private bool spawnStarted;

        // ===== TANK SELECTION =====

        /// Set player's chosen tank (host only)
        public void SetPlayerTank(string nickname, string tankName) {
            if (string.IsNullOrEmpty(nickname) || string.IsNullOrEmpty(tankName)) return;

            MelonLogger.Msg($"[MP] SetPlayerTank: {nickname} -> {tankName}");
            PlayerChosenTanks[nickname] = tankName;

            // Update lobby UI
            if (Lobby.Panel != null)
                Lobby.SetPlayerTank(nickname, tankName);
        }

        /// Get player's chosen tank, or default if none chosen
        public string GetPlayerTank(string nickname) {
            if (PlayerChosenTanks.TryGetValue(nickname, out var tankName) && !string.IsNullOrEmpty(tankName))
            {
                // Verify this tank exists
                if (VehicleSpawner.HasTank(tankName))
                    return tankName;
                    
                MelonLogger.Warning($"[MP] Player {nickname} has invalid tank '{tankName}', using default.");
            }

            // Return default tank
            return VehicleSpawner.GetDefaultTankId();
        }

        /// Get all available tanks from the database
        public List<string> GetAvailableTanks()
        {
            return VehicleSpawner.GetAvailableTankIds();
        }

        // ===== SCENE INITIALIZATION =====

        /// Called when a multiplayer scene loads
        public void OnSceneLoaded() {
            MelonLogger.Msg("[MP] OnSceneLoaded - Starting delayed initialization...");
            MelonCoroutines.Start(DelayedSceneInit());
        }

        private IEnumerator DelayedSceneInit() {
        MelonLogger.Msg("========================================");
        MelonLogger.Msg("[MP] DELAYED SCENE INIT STARTED");
        MelonLogger.Msg("========================================");
        MelonLogger.Msg("[MP] Waiting for scene to stabilize...");
        
        // Wait for scene to fully load
        yield return new WaitForSeconds(1.5f);
        MelonLogger.Msg("[MP] ✓ Scene stabilized");

        MelonLogger.Msg("[MP] ✓ Scene stabilized");

        MelonLogger.Msg("[MP] Initializing VehicleSpawner...");
        
        // Ensure VehicleSpawner is ready
        try {
            VehicleSpawner.EnsureInitialized();
            int tankCount = VehicleSpawner.GetTankCount();
            MelonLogger.Msg($"[MP] ✓ VehicleSpawner ready with {tankCount} tanks");
        } catch (Exception ex) {
            MelonLogger.Error($"[MP] ✗ VehicleSpawner initialization failed: {ex.Message}");
            yield break;
        }
        
        // Wait a bit more for factory to be available
        yield return new WaitForSeconds(0.5f);

        sceneReady = true;
        MelonLogger.Msg("[MP] ✓ Scene ready flag set");

        if (NetworkManager.Instance == null) {
            MelonLogger.Error("[MP] ✗ NetworkManager.Instance is null!");
            yield break;
        }
        
        MelonLogger.Msg($"[MP] Network Status: IsHost={NetworkManager.Instance.IsHost}, IsClient={NetworkManager.Instance.IsClient}");

        // Only host starts spawn process automatically
        if (NetworkManager.Instance.IsHost) {
            MelonLogger.Msg("[MP] HOST MODE - Waiting for factory to be available...");
            MelonLogger.Msg("[MP] Note: Factory requires an existing vehicle in scene");
            
            // Critical: Wait for IVehicleFactory to exist in scene
            // The factory only exists after the first vehicle is spawned by the game
            bool factoryFound = false;
            int attempts = 0;
            
            while (!factoryFound && attempts < 20) {
                attempts++;
                MelonLogger.Msg($"[MP] Factory search attempt {attempts}/20...");
                
                // Try to find any existing vehicle (game may have spawned one)
                var existingVehicles = Object.FindObjectsOfType<MonoBehaviour>();
                int vehicleCount = 0;
                
                foreach (var obj in existingVehicles) {
                    if (obj == null) continue;
                    
                    string typeName = obj.GetType().FullName;
                    if (typeName.Contains("Vehicle") && !typeName.Contains("UI") && !typeName.Contains("Menu")) {
                        vehicleCount++;
                        
                        var il2cppObj = obj as Il2CppSystem.Object;
                        if (il2cppObj != null) {
                            var gateway = il2cppObj.TryCast<IVehicleEditGateway>();
                            if (gateway != null) {
                                MelonLogger.Msg($"[MP] ✓ Found existing vehicle gateway (type: {typeName})");
                                MelonLogger.Msg("[MP] ✓ Factory should now be available!");
                                factoryFound = true;
                                break;
                            }
                        }
                    }
                }
                
                if (!factoryFound) {
                    if (vehicleCount > 0) {
                        MelonLogger.Msg($"[MP] Found {vehicleCount} vehicle-like objects but no gateways");
                    } else {
                        MelonLogger.Msg($"[MP] No vehicles found in scene yet");
                    }
                    yield return new WaitForSeconds(1f);
                }
            }
            
            if (!factoryFound) {
                MelonLogger.Warning("[MP] ========================================");
                MelonLogger.Warning("[MP] ⚠ FACTORY NOT FOUND AFTER WAITING!");
                MelonLogger.Warning("[MP] ========================================");
                MelonLogger.Warning("[MP] The game hasn't spawned any vehicles yet.");
                MelonLogger.Warning("[MP] ");
                MelonLogger.Warning("[MP] MANUAL WORKAROUND:");
                MelonLogger.Warning("[MP] 1. Press F8 to spawn a test tank");
                MelonLogger.Warning("[MP] 2. Press F6 to start multiplayer spawn");
                MelonLogger.Warning("[MP] ");
                MelonLogger.Warning("[MP] Or wait for the game to spawn its initial vehicle");
                MelonLogger.Warning("[MP] ========================================");
                // Don't start spawn process yet
                yield break;
            }
            
            MelonLogger.Msg("[MP] ========================================");
            MelonLogger.Msg("[MP] ✓ FACTORY READY - STARTING SPAWN PROCESS");
            MelonLogger.Msg("[MP] ========================================");
            StartSpawnProcess();
        } else {
            MelonLogger.Msg("[MP] CLIENT MODE - Waiting for spawn commands from host...");
        }
        
        MelonLogger.Msg("[MP] DelayedSceneInit complete");
    }

        public void ManuallyStartSpawn() {
            if (!sceneReady) {
                MelonLogger.Warning("[MP] Scene not ready yet!");
                return;
            }
    
            if (!NetworkManager.Instance.IsHost) {
                MelonLogger.Warning("[MP] Only host can start spawn!");
                return;
            }
    
            if (spawnStarted) {
                MelonLogger.Warning("[MP] Spawn already started!");
                return;
            }
    
            MelonLogger.Msg("[MP] Manually starting spawn process...");
            StartSpawnProcess();
        }


        // ===== SPAWN PROCESS (HOST) =====

        /// Start spawning tanks for all players (host only)
        public void StartSpawnProcess() {
            if (spawnStarted) {
                MelonLogger.Msg("[MP] Spawn already started.");
                return;
            }

            if (!sceneReady) {
                MelonLogger.Warning("[MP] Scene not ready yet!");
                return;
            }

            if (!NetworkManager.Instance.IsHost) {
                MelonLogger.Warning("[MP] Only host can start spawn process!");
                return;
            }

            spawnStarted = true;

            // Get all players from Lobby
            var playerNicknames = new List<string>();
            
            // Add host first
            if (!string.IsNullOrEmpty(NetworkManager.Instance.HostNickname))
                playerNicknames.Add(NetworkManager.Instance.HostNickname);

            // Add all lobby players
            foreach (var kv in Lobby.Players) {
                string nick = kv.Key;
                if (!playerNicknames.Contains(nick))
                    playerNicknames.Add(nick);
            }

            MelonLogger.Msg($"[MP] Starting spawn process for {playerNicknames.Count} players...");
            MelonCoroutines.Start(SpawnQueueRoutine(playerNicknames));
        }

        private IEnumerator SpawnQueueRoutine(List<string> players) {
                foreach (string nickname in players) {
                    string tankName = GetPlayerTank(nickname);

                    if (string.IsNullOrEmpty(tankName)) {
                        MelonLogger.Warning($"[MP] No tank for {nickname}, using default.");
                        tankName = VehicleSpawner.GetDefaultTankId();
                
                        if (string.IsNullOrEmpty(tankName)) {
                            MelonLogger.Error($"[MP] No default tank available! Skipping {nickname}.");
                            continue;
                        }
                    }

                    MelonLogger.Msg($"[MP] Spawning '{tankName}' for {nickname}...");

                    IVehicleEditGateway gateway = SpawnTankForPlayer(nickname, tankName);

                    if (gateway != null) {
                        SpawnedVehicles[nickname] = gateway;

                        // Assign vehicle control if this is the local player (host)
                        if (nickname == NetworkManager.Instance.LocalNickname) {
                            MelonLogger.Msg($"[MP] Assigning control to host ({nickname})");
                            VehicleSpawner.AssignVehicleControl(gateway);
                        }

                        // Notify clients
                        if (NetworkManager.Instance != null)
                            NetworkManager.Instance.Send($"SPAWN:{nickname}:{tankName}");
                    } else {
                        MelonLogger.Error($"[MP] ✗ Failed to spawn tank for {nickname}!");
                    }
            
                    // Longer delay between spawns to let game process each vehicle
                    yield return new WaitForSeconds(1f);
                }

                MelonLogger.Msg("[MP] ✓ Spawn queue complete!");
            }

        // ===== SPAWN LOGIC =====

        /// Spawn a tank for a specific player using proper Sprocket API
        private IVehicleEditGateway SpawnTankForPlayer(string nickname, string tankName) {
            Vector3 spawnPos = GetSpawnPoint();
            Quaternion spawnRot = Quaternion.identity;

            // Use VehicleSpawner - returns IVehicleEditGateway
            IVehicleEditGateway gateway = VehicleSpawner.SpawnVehicle(tankName, spawnPos, spawnRot);

            if (gateway == null) {
                MelonLogger.Error($"[MP] VehicleSpawner failed to spawn '{tankName}'");
                return null;
            }

            MelonLogger.Msg($"[MP] ✓ Spawned '{tankName}' for {nickname} at {spawnPos}");
            return gateway;
        }

        /// Find a spawn point in the scene
        private Vector3 GetSpawnPoint() {
            // Try to find spawn point objects
            var spawnPoint = GameObject.Find("SpawnPoint");
            if (spawnPoint != null) return spawnPoint.transform.position;

            var playerSpawn = GameObject.Find("PlayerSpawn");
            if (playerSpawn != null) return playerSpawn.transform.position;
            
            // Fallback: random position
            return new Vector3(
                Random.Range(-10f, 10f),
                2f,
                Random.Range(-10f, 10f)
            );
        }
    
        // ===== CLIENT SPAWN HANDLING =====

        /// Handle spawn message from host (client side)
        /// Format: "SPAWN:{nickname}:{tankName}"
        public void OnClientSpawnMessage(string nickname, string tankName) {
            MelonLogger.Msg($"[MP] Client received spawn command: {nickname} -> {tankName}");

            // Check if already spawned
            if (SpawnedVehicles.ContainsKey(nickname) && SpawnedVehicles[nickname] != null) {
                MelonLogger.Msg($"[MP] Already spawned vehicle for {nickname}, skipping.");
                return;
            }

            // Spawn the tank
            Vector3 spawnPos = GetSpawnPoint();
            IVehicleEditGateway gateway = VehicleSpawner.SpawnVehicle(tankName, spawnPos, Quaternion.identity);

            if (gateway == null) {
                MelonLogger.Error($"[MP] Client failed to spawn '{tankName}'");
                return;
            }

            SpawnedVehicles[nickname] = gateway;

            // If this is the local player's tank, assign control
            if (NetworkManager.Instance != null && nickname == NetworkManager.Instance.LocalNickname) {
                MelonLogger.Msg($"[MP] This is local player's tank, assigning control...");
                MelonCoroutines.Start(AssignAfterDelay(gateway));
            }
        }

        private IEnumerator AssignAfterDelay(IVehicleEditGateway gateway) {
            yield return new WaitForSeconds(0.25f);
            VehicleSpawner.AssignVehicleControl(gateway);
        }

        // ===== CLEANUP =====

        /// Reset multiplayer state (called when leaving session)
        public void Reset() {
            MelonLogger.Msg("[MP] Resetting MultiplayerManager state...");
            
            // Destroy and deregister all spawned vehicles
            foreach (var kv in SpawnedVehicles) {
                if (kv.Value != null) {
                    // Deregister from VehicleRegister
                    VehicleSpawner.DeregisterVehicle(kv.Value);
                    
                    // Destroy the GameObject
                    try {
                        var gameObject = VehicleSpawner.GetGameObjectFromGateway(kv.Value);
                        if (gameObject != null) {
                            Object.Destroy(gameObject);
                            MelonLogger.Msg($"[MP] Destroyed vehicle for {kv.Key}");
                        }
                    }
                    catch (System.Exception ex) {
                        MelonLogger.Warning($"[MP] Failed to destroy vehicle GameObject: {ex.Message}");
                    }
                }
            }

            SpawnedVehicles.Clear();
            PlayerChosenTanks.Clear();
            sceneReady = false;
            spawnStarted = false;

            MelonLogger.Msg("[MP] MultiplayerManager reset complete.");
        }
    }
}