using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
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
        private bool factoryReady;

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

        // Get all available tanks from the database
        public List<string> GetAvailableTanks()
        {
            return VehicleSpawner.GetAvailableTankIds();
        }

        // ===== SCENE INITIALIZATION =====

        // Called when a multiplayer scene loads
        public void OnSceneLoaded() {
            MelonLogger.Msg("[MP] OnSceneLoaded - Starting delayed initialization...");
            MelonCoroutines.Start(DelayedSceneInit());
        }

        private IEnumerator DelayedSceneInit() {
        MelonLogger.Msg("========================================");
        MelonLogger.Msg("[MP] DELAYED SCENE INIT STARTED");
        MelonLogger.Msg("========================================");
        MelonLogger.Msg("[MP] Waiting for scene to stabilize...");
        
        // Wait for scene to load
        yield return new WaitForSeconds(1.5f);
        MelonLogger.Msg("[MP] ✓ Scene stabilized");

        // Initialize VehicleSpawner
        MelonLogger.Msg("[MP] Initializing VehicleSpawner...");
        try {
            VehicleSpawner.EnsureInitialized();
            int tankCount = VehicleSpawner.GetTankCount();
            MelonLogger.Msg($"[MP] ✓ VehicleSpawner ready with {tankCount} tanks");
        } catch (Exception ex) {
            MelonLogger.Error($"[MP] ✗ VehicleSpawner initialization failed: {ex.Message}");
            yield break;
        }
        
        sceneReady = true;
        MelonLogger.Msg("[MP] ✓ Scene ready flag set");

        if (NetworkManager.Instance == null) {
            MelonLogger.Error("[MP] ✗ NetworkManager.Instance is null!");
            yield break;
        }
        
        MelonLogger.Msg($"[MP] Network Status: IsHost={NetworkManager.Instance.IsHost}, IsClient={NetworkManager.Instance.IsClient}");

        if (NetworkManager.Instance.IsHost) {
            MelonLogger.Msg("[MP] HOST MODE - Waiting for factory...");
            
            // Try to initialize the factory through game systems
            yield return new WaitForSeconds(1f);
            
            MelonLogger.Msg("[MP] ========================================");
            MelonLogger.Msg("[MP] INITIALIZING FACTORY");
            MelonLogger.Msg("[MP] ========================================");
            
            // Call the initialization method
            VehicleSpawner.TryInitializeFactory();
            
            // Wait for factory to become available
            yield return VehicleSpawner.WaitForFactory(10);
            
            if (!VehicleSpawner.IsFactoryAvailable())
            {
                MelonLogger.Error("[MP] ✗ Factory is not available! Cannot spawn vehicles.");
                MelonLogger.Error("[MP] Please spawn a vehicle through the game UI first (ESC > Spawn Vehicle)");
                yield break;
            }
            
            MelonLogger.Msg("[MP] ✓ Factory is ready!");
            
            // Now spawn player vehicles
            MelonLogger.Msg("[MP] ========================================");
            MelonLogger.Msg("[MP] STARTING PLAYER VEHICLE SPAWN");
            MelonLogger.Msg("[MP] ========================================");
            StartSpawnProcess();
        } else {
            MelonLogger.Msg("[MP] CLIENT MODE - Waiting for spawn commands from host...");
        }
        
        MelonLogger.Msg("[MP] DelayedSceneInit complete");
    }

        // Attempt to trigger the game's normal vehicle spawn
        // This should initialize the factory properly

        private IEnumerator AttemptGameSpawn() {
            MelonLogger.Msg("[MP] Attempting to trigger game's vehicle spawn system...");
            factoryReady = false;
            
            try {
                // Look for game's spawn system
                var allObjects = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                
                foreach (var obj in allObjects) {
                    if (obj == null) continue;
                    
                    string typeName = obj.GetType().FullName;
                    
                    // Look for ScenarioGameState or similar
                    if (typeName.Contains("ScenarioGameState")) {
                        MelonLogger.Msg($"[MP] Found game state: {typeName}");
                        
                        // Try to find and call spawn methods
                        var methods = obj.GetType().GetMethods(
                            System.Reflection.BindingFlags.Public | 
                            System.Reflection.BindingFlags.Instance);
                        
                        foreach (var method in methods) {
                            if (method.Name.Contains("Spawn") || 
                                method.Name.Contains("Initialize") ||
                                method.Name.Contains("Start")) {
                                
                                MelonLogger.Msg($"[MP] Found method: {method.Name}");
                                
                                // Try calling methods with no parameters
                                if (method.GetParameters().Length == 0) {
                                    try {
                                        MelonLogger.Msg($"[MP] Attempting to call {method.Name}()...");
                                        method.Invoke(obj, null);
                                        
                                        // Wait to see if it worked
                                        new WaitForSeconds(1f);
                                        factoryReady = true; // mark as ready when done
                                        
                                        // Check if vehicles spawned
                                        var vehicles = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                                            .Where(o => o != null && o.GetType().FullName.Contains("Vehicle"));
                                        
                                        if (vehicles.Any()) {
                                            MelonLogger.Msg("[MP] ✓ Game spawned vehicles!");
                                        }
                                    } catch (Exception ex) {
                                        MelonLogger.Msg($"[MP] Method call failed: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                MelonLogger.Error($"[MP] AttemptGameSpawn error: {ex.Message}");
            } 
            
            MelonLogger.Warning("[MP] Could not trigger game spawn");
            yield return false;
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
                        MelonLogger.Error($"[MP] Failed to spawn tank for {nickname}!");
                    }
            
                    // Longer delay between spawns to let game process each vehicle
                    yield return new WaitForSeconds(1f);
                }

                MelonLogger.Msg("[MP] Spawn queue complete!");
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
                    catch (Exception ex) {
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