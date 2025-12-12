using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using SprocketMultiplayer.Patches;

namespace SprocketMultiplayer.Core {
    public class MultiplayerManager {
        public static MultiplayerManager Instance = new MultiplayerManager();

        // Player -> Tank assignment (host authoritative)
        public Dictionary<string, string> PlayerChosenTanks = new Dictionary<string, string>();
        public Dictionary<string, GameObject> SpawnedVehicles = new Dictionary<string, GameObject>();

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
                if (BlueprintSpawner.HasTank(tankName))
                    return tankName;
                    
                MelonLogger.Warning($"[MP] Player {nickname} has invalid tank '{tankName}', using default.");
            }

            // Return default tank
            return BlueprintSpawner.GetDefaultTankId();
        }


        /// Get all available tanks from the database
        public List<string> GetAvailableTanks()
        {
            // Use BlueprintSpawner's registry
            return BlueprintSpawner.GetAvailableTankIds();
        }

        // ===== SCENE INITIALIZATION =====


        /// Called when a multiplayer scene loads
        public void OnSceneLoaded() {
            MelonLogger.Msg("[MP] OnSceneLoaded - Starting delayed initialization...");
            MelonCoroutines.Start(DelayedSceneInit());
        }

        private IEnumerator DelayedSceneInit() {
            // Wait for scene to fully load
            yield return new WaitForSeconds(1.5f);

            // Ensure BlueprintSpawner is ready
            BlueprintSpawner.EnsureInitialized();

            sceneReady = true;
            MelonLogger.Msg("[MP] Scene ready for spawning.");

            if (NetworkManager.Instance == null) {
                MelonLogger.Warning("[MP] NetworkManager.Instance is null!");
                yield break;
            }

            // Only host starts spawn process automatically
            if (NetworkManager.Instance.IsHost) {
                MelonLogger.Msg("[MP] Host detected - starting spawn process...");
                StartSpawnProcess();
            } else {
                MelonLogger.Msg("[MP] Client detected - waiting for spawn commands from host...");
            }
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
                    MelonLogger.Warning($"[MP] No tank for {nickname}, skipping.");
                    continue;
                }

                MelonLogger.Msg($"[MP] Spawning '{tankName}' for {nickname}...");

                GameObject tank = SpawnTankForPlayer(nickname, tankName);

                if (tank != null) {
                    SpawnedVehicles[nickname] = tank;

                    // Assign vehicle control
                    VehicleControl.AssignVehicleToPlayer(tank);

                    // Notify clients
                    if (NetworkManager.Instance != null)
                        NetworkManager.Instance.Send($"SPAWN:{nickname}:{tankName}");
                } else {
                    MelonLogger.Error($"[MP] Failed to spawn tank for {nickname}!");
                }
                
                yield return new WaitForSeconds(0.3f);
            }

            MelonLogger.Msg("[MP] ✓ Spawn queue complete!");
        }

        // ===== SPAWN LOGIC =====


        /// Spawn a tank for a specific player
        private GameObject SpawnTankForPlayer(string nickname, string tankName) {
            Vector3 spawnPos = GetSpawnPoint();
            Quaternion spawnRot = Quaternion.identity;

            // Use BlueprintSpawner to spawn the tank
            GameObject tank = BlueprintSpawner.SpawnTank(tankName, spawnPos, spawnRot);

            if (tank == null) {
                MelonLogger.Error($"[MP] BlueprintSpawner failed to spawn '{tankName}'");
                return null;
            }

            // Ensure physics components
            if (tank.GetComponent<Rigidbody>() == null)
                tank.AddComponent<Rigidbody>();
            
            if (tank.GetComponent<Collider>() == null)
                tank.AddComponent<BoxCollider>();

            // Set name for identification
            tank.name = $"Vehicle_{tankName}_{nickname}";

            MelonLogger.Msg($"[MP] ✓ Spawned '{tank.name}' at {spawnPos}");
            return tank;
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
            GameObject tank = BlueprintSpawner.SpawnTank(tankName, spawnPos, Quaternion.identity);

            if (tank == null) {
                MelonLogger.Error($"[MP] Client failed to spawn '{tankName}'");
                return;
            }

            // Ensure physics
            if (tank.GetComponent<Rigidbody>() == null)
                tank.AddComponent<Rigidbody>();
            
            if (tank.GetComponent<Collider>() == null)
                tank.AddComponent<BoxCollider>();

            tank.name = $"Vehicle_{tankName}_{nickname}";
            SpawnedVehicles[nickname] = tank;

            // If this is the local player's tank, assign control
            if (NetworkManager.Instance != null && nickname == NetworkManager.Instance.LocalNickname) {
                MelonLogger.Msg($"[MP] This is local player's tank, assigning control...");
                MelonCoroutines.Start(AssignAfterDelay(tank));
            }
        }

        private IEnumerator AssignAfterDelay(GameObject tank) {
            yield return new WaitForSeconds(0.25f);
            VehicleControl.AssignVehicleToPlayer(tank);
        }

        // ===== CLEANUP =====


        /// Reset multiplayer state (called when leaving session)
        public void Reset() {
            MelonLogger.Msg("[MP] Resetting MultiplayerManager state...");
            
            // Destroy all spawned vehicles
            foreach (var kv in SpawnedVehicles) {
                if (kv.Value != null)
                    Object.Destroy(kv.Value);
            }

            SpawnedVehicles.Clear();
            PlayerChosenTanks.Clear();
            sceneReady = false;
            spawnStarted = false;

            MelonLogger.Msg("[MP] MultiplayerManager reset complete.");
        }
    }
}