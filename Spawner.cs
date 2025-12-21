using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using Il2CppSprocket.Vehicles;

namespace SprocketMultiplayer.Core
{
    /// <summary>
    /// High-level spawner interface using proper Sprocket API
    /// Now works with IVehicleEditGateway
    /// </summary>
    public class Spawner : MonoBehaviour
    {
        public static Spawner Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            MelonLogger.Msg("[Spawner] Initialized with proper Sprocket API");
        }

        private void Start()
        {
            // Initialize VehicleSpawner when Spawner starts
            try
            {
                VehicleSpawner.EnsureInitialized();
                MelonLogger.Msg($"[Spawner] VehicleSpawner ready with {VehicleSpawner.GetTankCount()} vehicles");
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[Spawner] Failed to initialize VehicleSpawner: {e}");
            }
        }

        /// <summary>
        /// Spawn a vehicle using proper Sprocket API
        /// Returns IVehicleEditGateway
        /// </summary>
        public IVehicleEditGateway SpawnVehicle(string blueprintName, int team = 0, bool attachAI = false, Vector3? position = null)
        {
            Vector3 spawnPos = position ?? new Vector3(0, 2, 0);
            Quaternion spawnRot = Quaternion.identity;

            MelonLogger.Msg($"[Spawner] Spawning '{blueprintName}' at {spawnPos}");

            try
            {
                // Use VehicleSpawner with proper API - returns IVehicleEditGateway
                IVehicleEditGateway gateway = VehicleSpawner.SpawnVehicle(blueprintName, spawnPos, spawnRot);

                if (gateway != null)
                {
                    MelonLogger.Msg($"[Spawner] ✓ Successfully spawned: {blueprintName}");
                    
                    // TODO: Apply team assignment if needed
                    if (team != 0)
                    {
                        MelonLogger.Msg($"[Spawner] TODO: Assign vehicle to team {team}");
                    }
                    
                    // TODO: Attach AI if needed
                    if (attachAI)
                    {
                        MelonLogger.Msg("[Spawner] TODO: Attach AI to vehicle");
                    }

                    return gateway;
                }
                else
                {
                    MelonLogger.Error($"[Spawner] Failed to spawn '{blueprintName}' - VehicleSpawner returned null");
                    return null;
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[Spawner] Exception during spawn: {e.Message}");
                MelonLogger.Error($"[Spawner] Stack trace: {e.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Spawn vehicle synchronously and return the gateway
        /// </summary>
        public IVehicleEditGateway SpawnVehicleSync(string blueprintName, Vector3 position, Quaternion rotation)
        {
            try
            {
                IVehicleEditGateway gateway = VehicleSpawner.SpawnVehicle(blueprintName, position, rotation);
                
                if (gateway != null)
                {
                    MelonLogger.Msg($"[Spawner] Sync spawned: {blueprintName}");
                }
                
                return gateway;
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[Spawner] Sync spawn failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Spawn vehicle and assign control to local player
        /// </summary>
        public IVehicleEditGateway SpawnAndControl(string blueprintName, Vector3? position = null)
        {
            Vector3 spawnPos = position ?? new Vector3(0, 2, 0);
            
            IVehicleEditGateway gateway = SpawnVehicle(blueprintName, position: spawnPos);
            
            if (gateway != null)
            {
                try
                {
                    VehicleSpawner.AssignVehicleControl(gateway);
                    MelonLogger.Msg($"[Spawner] ✓ Assigned control of vehicle to local player");
                }
                catch (Exception e)
                {
                    MelonLogger.Error($"[Spawner] Failed to assign control: {e.Message}");
                }
            }
            
            return gateway;
        }

        /// <summary>
        /// Get list of all available vehicle blueprints
        /// </summary>
        public string[] GetAvailableVehicles()
        {
            try
            {
                VehicleSpawner.EnsureInitialized();
                var vehicles = VehicleSpawner.GetAvailableTankIds();
                MelonLogger.Msg($"[Spawner] Found {vehicles.Count} available vehicles");
                return vehicles.ToArray();
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[Spawner] Failed to get vehicles: {e.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Get list of available vehicles as List
        /// </summary>
        public List<string> GetAvailableVehiclesList()
        {
            try
            {
                VehicleSpawner.EnsureInitialized();
                return VehicleSpawner.GetAvailableTankIds();
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[Spawner] Failed to get vehicles: {e.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Spawn a random vehicle from available blueprints
        /// </summary>
        public IVehicleEditGateway SpawnRandomVehicle(Vector3? position = null)
        {
            var vehicles = GetAvailableVehicles();
            
            if (vehicles.Length == 0)
            {
                MelonLogger.Warning("[Spawner] No vehicles available to spawn");
                return null;
            }

            string randomVehicle = vehicles[UnityEngine.Random.Range(0, vehicles.Length)];
            MelonLogger.Msg($"[Spawner] Spawning random vehicle: {randomVehicle}");
            return SpawnVehicle(randomVehicle, position: position);
        }

        /// <summary>
        /// Check if VehicleSpawner has any vehicles loaded
        /// </summary>
        public bool HasVehiclesAvailable()
        {
            try
            {
                VehicleSpawner.EnsureInitialized();
                return VehicleSpawner.GetTankCount() > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a specific vehicle exists
        /// </summary>
        public bool HasVehicle(string blueprintName)
        {
            try
            {
                VehicleSpawner.EnsureInitialized();
                return VehicleSpawner.HasTank(blueprintName);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get the default vehicle name
        /// </summary>
        public string GetDefaultVehicle()
        {
            try
            {
                VehicleSpawner.EnsureInitialized();
                return VehicleSpawner.GetDefaultTankId();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Despawn (destroy and deregister) a vehicle via gateway
        /// </summary>
        public void DespawnVehicle(IVehicleEditGateway gateway)
        {
            if (gateway == null)
            {
                MelonLogger.Warning("[Spawner] Cannot despawn null gateway");
                return;
            }

            try
            {
                // Deregister first
                VehicleSpawner.DeregisterVehicle(gateway);
                
                // Try to get GameObject and destroy it
                var gatewayType = gateway.GetType();
                var vehicleProp = gatewayType.GetProperty("Vehicle");
                if (vehicleProp != null)
                {
                    var vehicle = vehicleProp.GetValue(gateway);
                    if (vehicle != null)
                    {
                        var vehicleType = vehicle.GetType();
                        var goProp = vehicleType.GetProperty("gameObject");
                        if (goProp != null)
                        {
                            var go = goProp.GetValue(vehicle) as GameObject;
                            if (go != null)
                            {
                                string name = go.name;
                                Destroy(go);
                                MelonLogger.Msg($"[Spawner] ✓ Despawned vehicle: {name}");
                                return;
                            }
                        }
                    }
                }
                
                MelonLogger.Warning("[Spawner] Could not find GameObject to destroy");
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[Spawner] Failed to despawn vehicle: {e.Message}");
            }
        }

        /// <summary>
        /// Check if the proper spawning API is available
        /// </summary>
        public bool IsSpawnerReady()
        {
            try
            {
                VehicleSpawner.EnsureInitialized();
                return VehicleSpawner.GetTankCount() > 0;
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[Spawner] Spawner not ready: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Force re-initialization of VehicleSpawner
        /// </summary>
        public void Reinitialize()
        {
            try
            {
                MelonLogger.Msg("[Spawner] Reinitializing VehicleSpawner...");
                VehicleSpawner.Initialize();
                MelonLogger.Msg($"[Spawner] ✓ Reinitialized with {VehicleSpawner.GetTankCount()} vehicles");
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[Spawner] Reinitialization failed: {e.Message}");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                MelonLogger.Msg("[Spawner] Instance destroyed");
            }
        }
    }
}