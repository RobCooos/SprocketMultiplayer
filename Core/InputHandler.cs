using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using MelonLoader;
using SprocketMultiplayer.Core;
using Il2CppSprocket.Vehicles;

namespace SprocketMultiplayer.Core
{
    public class InputHandler : MonoBehaviour
    {
        private void Update()
        {
            if (Keyboard.current == null) return;

            // F7: Initialize vehicle spawner
            if (Keyboard.current.f7Key.wasPressedThisFrame)
            {
                InitializeVehicleSpawner();
            }

            // F8: Spawn tank next to player
            if (Keyboard.current.f8Key.wasPressedThisFrame)
            {
                SpawnTankNextToPlayer();
            }

            // F9: List available tanks
            if (Keyboard.current.f9Key.wasPressedThisFrame)
            {
                ListAvailableTanks();
            }

            // F10: Debug scene info
            if (Keyboard.current.f10Key.wasPressedThisFrame)
            {
                DebugSceneInfo();
            }

            // F11: Try alternative spawn method
            if (Keyboard.current.f11Key.wasPressedThisFrame)
            {
                TryAlternativeSpawn();
            }

            // F5: Find existing vehicles
            if (Keyboard.current.f5Key.wasPressedThisFrame)
            {
                FindExistingVehicles();
            }
            if (Keyboard.current.f6Key.wasPressedThisFrame)
            {
                ManuallyTriggerMultiplayerSpawn();
            }
        }

        private void ManuallyTriggerMultiplayerSpawn()
        {
            MelonLogger.Msg("=== F6: MANUALLY TRIGGERING MULTIPLAYER SPAWN ===");
    
            try
            {
                if (NetworkManager.Instance == null)
                {
                    MelonLogger.Warning("NetworkManager not initialized!");
                    return;
                }
        
                if (!NetworkManager.Instance.IsHost)
                {
                    MelonLogger.Warning("Only host can trigger multiplayer spawn!");
                    return;
                }
        
                if (MultiplayerManager.Instance == null)
                {
                    MelonLogger.Warning("MultiplayerManager not initialized!");
                    return;
                }
        
                MelonLogger.Msg("Calling MultiplayerManager.ManuallyStartSpawn()...");
                MultiplayerManager.Instance.ManuallyStartSpawn();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to trigger spawn: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }
        private void InitializeVehicleSpawner()
        {
            MelonLogger.Msg("=== F7: INITIALIZING VEHICLE SPAWNER ===");
            
            try
            {
                VehicleSpawner.Initialize();
                
                int count = VehicleSpawner.GetTankCount();
                MelonLogger.Msg($"✓ Vehicle spawner ready! {count} tanks loaded.");
                
                if (count > 0)
                {
                    string defaultTank = VehicleSpawner.GetDefaultTankId();
                    MelonLogger.Msg($"Default tank: {defaultTank}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"✗ Initialization failed: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private void SpawnTankNextToPlayer()
        {
            MelonLogger.Msg("=== F8: SPAWNING TANK NEXT TO PLAYER ===");
            
            try
            {
                // Find player
                var player = GameObject.Find("Player");
                if (player == null)
                {
                    MelonLogger.Warning("✗ Player object not found!");
                    return;
                }

                // Get default tank
                string tankId = VehicleSpawner.GetDefaultTankId();
                if (string.IsNullOrEmpty(tankId))
                {
                    MelonLogger.Warning("✗ No tanks available! Press F7 first.");
                    return;
                }

                // Calculate spawn position
                Vector3 spawnPos = player.transform.position + player.transform.right * 10f + Vector3.up * 2f;
                Quaternion spawnRot = Quaternion.LookRotation(player.transform.forward, Vector3.up);

                MelonLogger.Msg($"Spawning '{tankId}' at {spawnPos}...");
                
                // Spawn using proper API - returns IVehicleEditGateway
                IVehicleEditGateway gateway = VehicleSpawner.SpawnVehicle(tankId, spawnPos, spawnRot);
                
                if (gateway != null)
                {
                    MelonLogger.Msg($"✓ Tank '{tankId}' spawned successfully!");
                    
                    // Debug: Log gateway type and properties
                    LogGatewayInfo(gateway);
                    
                    // Optional: Assign control to player
                    MelonLogger.Msg("Assigning control to local player...");
                    VehicleSpawner.AssignVehicleControl(gateway);
                    MelonLogger.Msg("✓ Control assigned!");
                }
                else
                {
                    MelonLogger.Error("✗ Tank spawn returned null!");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"✗ Spawn failed: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private void ListAvailableTanks()
        {
            MelonLogger.Msg("=== F9: LISTING AVAILABLE TANKS ===");
            
            try
            {
                VehicleSpawner.EnsureInitialized();
                
                var tanks = VehicleSpawner.GetAvailableTankIds();
                
                if (tanks.Count == 0)
                {
                    MelonLogger.Msg("No tanks available. Press F7 to initialize.");
                    return;
                }
                
                MelonLogger.Msg($"Available tanks ({tanks.Count} total):");
                foreach (var tank in tanks)
                {
                    MelonLogger.Msg($"  - {tank}");
                }
                
                string defaultTank = VehicleSpawner.GetDefaultTankId();
                MelonLogger.Msg($"Default: {defaultTank}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"✗ Failed to list tanks: {ex.Message}");
            }
        }

        /// <summary>
        /// Debug helper: Log information about the gateway and vehicle
        /// </summary>
        private void LogGatewayInfo(IVehicleEditGateway gateway)
        {
            try
            {
                MelonLogger.Msg($"[DEBUG] Gateway type: {gateway.GetType().FullName}");
                
                // Try to get Vehicle property
                var gatewayType = gateway.GetType();
                var vehicleProp = gatewayType.GetProperty("Vehicle");
                if (vehicleProp != null)
                {
                    var vehicle = vehicleProp.GetValue(gateway);
                    if (vehicle != null)
                    {
                        MelonLogger.Msg($"[DEBUG] Vehicle type: {vehicle.GetType().FullName}");
                        
                        // Try to get GameObject
                        var vehicleType = vehicle.GetType();
                        var goProp = vehicleType.GetProperty("gameObject");
                        if (goProp != null)
                        {
                            var go = goProp.GetValue(vehicle) as GameObject;
                            if (go != null)
                            {
                                MelonLogger.Msg($"[DEBUG] GameObject: {go.name}");
                                MelonLogger.Msg($"[DEBUG] Position: {go.transform.position}");
                            }
                        }
                        
                        // Try to get Transform
                        var transformProp = vehicleType.GetProperty("transform");
                        if (transformProp != null)
                        {
                            var transform = transformProp.GetValue(vehicle) as Transform;
                            if (transform != null)
                            {
                                MelonLogger.Msg($"[DEBUG] Transform position: {transform.position}");
                            }
                        }
                    }
                    else
                    {
                        MelonLogger.Warning("[DEBUG] Vehicle property is null");
                    }
                }
                else
                {
                    MelonLogger.Warning("[DEBUG] No Vehicle property found on gateway");
                }
                
                // List all properties
                MelonLogger.Msg("[DEBUG] Gateway properties:");
                foreach (var prop in gatewayType.GetProperties())
                {
                    try
                    {
                        var value = prop.GetValue(gateway);
                        MelonLogger.Msg($"  - {prop.Name}: {(value != null ? value.GetType().Name : "null")}");
                    }
                    catch
                    {
                        MelonLogger.Msg($"  - {prop.Name}: <cannot read>");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DEBUG] Failed to log gateway info: {ex.Message}");
            }
        }

        /// <summary>
        /// Debug scene information - press F10
        /// </summary>
        private void DebugSceneInfo()
        {
            MelonLogger.Msg("=== F10: DEBUG SCENE INFO ===");
            
            try
            {
                // Current scene
                var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                MelonLogger.Msg($"Active scene: {activeScene.name}");
                MelonLogger.Msg($"Scene path: {activeScene.path}");
                MelonLogger.Msg($"Scene loaded: {activeScene.isLoaded}");
                
                // All loaded scenes
                int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
                MelonLogger.Msg($"Total loaded scenes: {sceneCount}");
                for (int i = 0; i < sceneCount; i++)
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    MelonLogger.Msg($"  [{i}] {scene.name} (loaded: {scene.isLoaded})");
                }
                
                // Vehicle-related objects
                var allObjects = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                MelonLogger.Msg($"Total MonoBehaviours in scene: {allObjects.Length}");
                
                int vehicleCount = 0;
                int factoryCount = 0;
                int controllerCount = 0;
                
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    
                    string typeName = obj.GetType().FullName;
                    
                    if (typeName.Contains("Vehicle"))
                    {
                        vehicleCount++;
                        if (typeName.Contains("Factory"))
                        {
                            factoryCount++;
                            MelonLogger.Msg($"  FACTORY: {typeName}");
                        }
                        else if (typeName.Contains("Controller"))
                        {
                            controllerCount++;
                            MelonLogger.Msg($"  CONTROLLER: {typeName}");
                        }
                        else if (vehicleCount <= 10) // Limit output
                        {
                            MelonLogger.Msg($"  VEHICLE: {typeName}");
                        }
                    }
                }
                
                MelonLogger.Msg($"Summary:");
                MelonLogger.Msg($"  - Vehicle-related objects: {vehicleCount}");
                MelonLogger.Msg($"  - Factory objects: {factoryCount}");
                MelonLogger.Msg($"  - Controller objects: {controllerCount}");
                
                // Check for specific important types
                MelonLogger.Msg("Checking for key Sprocket types...");
                
                // IVehicleFactory assembly info
                var factoryType = typeof(Il2CppSprocket.Vehicles.IVehicleFactory);
                MelonLogger.Msg($"IVehicleFactory assembly: {factoryType.Assembly.GetName().Name}");
                MelonLogger.Msg($"IVehicleFactory full name: {factoryType.FullName}");
                
                // Try to find implementing types
                var assembly = factoryType.Assembly;
                var types = assembly.GetTypes();
                MelonLogger.Msg($"Types in assembly: {types.Length}");
                
                int implementingTypes = 0;
                foreach (var type in types)
                {
                    if (!type.IsInterface && !type.IsAbstract && 
                        typeof(Il2CppSprocket.Vehicles.IVehicleFactory).IsAssignableFrom(type))
                    {
                        implementingTypes++;
                        MelonLogger.Msg($"  Implements IVehicleFactory: {type.FullName}");
                    }
                }
                MelonLogger.Msg($"Found {implementingTypes} types implementing IVehicleFactory");
                
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to debug scene: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Try alternative spawn methods - press F11
        /// </summary>
        private void TryAlternativeSpawn()
        {
            MelonLogger.Msg("=== F11: TRYING ALTERNATIVE SPAWN ===");
            
            try
            {
                // Search for ALL objects that might help spawn vehicles
                var allObjects = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                MelonLogger.Msg($"Searching {allObjects.Length} objects...");
                
                // Look for any spawn-related objects
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    
                    string typeName = obj.GetType().FullName;
                    
                    // Look for spawn, instantiate, create, or manager related types
                    if (typeName.Contains("Spawn") || 
                        typeName.Contains("Manager") || 
                        typeName.Contains("Creator") ||
                        typeName.Contains("Instantiator"))
                    {
                        MelonLogger.Msg($"Found: {typeName}");
                        
                        // List all methods
                        var methods = obj.GetType().GetMethods(
                            System.Reflection.BindingFlags.Public | 
                            System.Reflection.BindingFlags.Instance);
                        
                        foreach (var method in methods)
                        {
                            if (method.Name.Contains("Spawn") || 
                                method.Name.Contains("Create") || 
                                method.Name.Contains("Instantiate"))
                            {
                                MelonLogger.Msg($"  -> Method: {method.Name}");
                                var parameters = method.GetParameters();
                                foreach (var param in parameters)
                                {
                                    MelonLogger.Msg($"     - {param.ParameterType.Name} {param.Name}");
                                }
                            }
                        }
                    }
                }
                
                // Try to find VehicleRegister and see what it has
                MelonLogger.Msg("Searching for VehicleRegister...");
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    
                    if (obj.GetType().Name == "VehicleRegister")
                    {
                        MelonLogger.Msg($"Found VehicleRegister: {obj.GetType().FullName}");
                        
                        // List all methods and properties
                        var type = obj.GetType();
                        
                        MelonLogger.Msg("Properties:");
                        foreach (var prop in type.GetProperties())
                        {
                            MelonLogger.Msg($"  - {prop.PropertyType.Name} {prop.Name}");
                        }
                        
                        MelonLogger.Msg("Methods:");
                        foreach (var method in type.GetMethods(
                            System.Reflection.BindingFlags.Public | 
                            System.Reflection.BindingFlags.Instance))
                        {
                            if (!method.Name.StartsWith("get_") && 
                                !method.Name.StartsWith("set_") &&
                                !method.Name.StartsWith("add_") &&
                                !method.Name.StartsWith("remove_"))
                            {
                                var parameters = method.GetParameters();
                                string paramStr = string.Join(", ", 
                                    parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                                MelonLogger.Msg($"  - {method.ReturnType.Name} {method.Name}({paramStr})");
                            }
                        }
                    }
                }
                
                // Check all assemblies for vehicle-related types
                MelonLogger.Msg("Checking assemblies for vehicle factories...");
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                
                foreach (var assembly in assemblies)
                {
                    if (assembly.GetName().Name.Contains("Sprocket"))
                    {
                        MelonLogger.Msg($"Assembly: {assembly.GetName().Name}");
                        
                        var types = assembly.GetTypes();
                        foreach (var type in types)
                        {
                            if (type.Name.Contains("Factory") && type.Name.Contains("Vehicle"))
                            {
                                MelonLogger.Msg($"  Found type: {type.FullName}");
                                MelonLogger.Msg($"    Is interface: {type.IsInterface}");
                                MelonLogger.Msg($"    Is abstract: {type.IsAbstract}");
                                
                                if (!type.IsInterface && !type.IsAbstract)
                                {
                                    // Check for static instance
                                    var instanceProp = type.GetProperty("Instance",
                                        System.Reflection.BindingFlags.Public | 
                                        System.Reflection.BindingFlags.Static);
                                    
                                    if (instanceProp != null)
                                    {
                                        MelonLogger.Msg($"    Has Instance property!");
                                        var instance = instanceProp.GetValue(null);
                                        MelonLogger.Msg($"    Instance is null: {instance == null}");
                                    }
                                    
                                    // Check for methods
                                    var methods = type.GetMethods(
                                        System.Reflection.BindingFlags.Public | 
                                        System.Reflection.BindingFlags.Instance);
                                    
                                    foreach (var method in methods)
                                    {
                                        if (method.Name.Contains("Create") || method.Name.Contains("Spawn"))
                                        {
                                            var parameters = method.GetParameters();
                                            string paramStr = string.Join(", ", 
                                                parameters.Select(p => p.ParameterType.Name));
                                            MelonLogger.Msg($"    Method: {method.Name}({paramStr})");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Alternative spawn search failed: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Find existing vehicles in the scene - press F12
        /// </summary>
        private void FindExistingVehicles()
        {
            MelonLogger.Msg("=== F12: FINDING EXISTING VEHICLES ===");
            
            try
            {
                var allObjects = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                MelonLogger.Msg($"Searching {allObjects.Length} objects for vehicles...");
                
                int vehicleCount = 0;
                
                // Search for Vehicle objects
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    
                    var type = obj.GetType();
                    
                    // Check if this might be a vehicle
                    if (type.Name.Contains("Vehicle") && !type.Name.Contains("UI") && !type.Name.Contains("Menu"))
                    {
                        vehicleCount++;
                        MelonLogger.Msg($"[{vehicleCount}] Found: {type.FullName}");
                        MelonLogger.Msg($"    GameObject: {obj.gameObject.name}");
                        MelonLogger.Msg($"    Position: {obj.transform.position}");
                        
                        // List all interfaces
                        var interfaces = type.GetInterfaces();
                        if (interfaces.Length > 0)
                        {
                            MelonLogger.Msg("    Interfaces:");
                            foreach (var iface in interfaces)
                            {
                                MelonLogger.Msg($"      - {iface.Name}");
                            }
                        }
                        
                        // Check if it's an IVehicleEditGateway
                        try
                        {
                            var il2cppObj = obj as Il2CppSystem.Object;
                            if (il2cppObj != null)
                            {
                                var gateway = il2cppObj.TryCast<IVehicleEditGateway>();
                                if (gateway != null)
                                {
                                    MelonLogger.Msg("    ✓ This is an IVehicleEditGateway!");
                                    
                                    // Try to get the factory from it
                                    var props = type.GetProperties();
                                    foreach (var prop in props)
                                    {
                                        if (prop.Name.Contains("Factory"))
                                        {
                                            try
                                            {
                                                var factory = prop.GetValue(obj);
                                                if (factory != null)
                                                {
                                                    MelonLogger.Msg($"    ✓ Found factory property: {prop.Name}");
                                                    MelonLogger.Msg($"      Type: {factory.GetType().FullName}");
                                                    
                                                    // Try to cast to IVehicleFactory
                                                    var il2cppFactory = factory as Il2CppSystem.Object;
                                                    if (il2cppFactory != null)
                                                    {
                                                        var vehicleFactory = il2cppFactory.TryCast<IVehicleFactory>();
                                                        if (vehicleFactory != null)
                                                        {
                                                            MelonLogger.Msg("      ✓✓✓ THIS IS AN IVehicleFactory! ✓✓✓");
                                                        }
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                MelonLogger.Warning($"      Failed to get {prop.Name}: {ex.Message}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                        
                        // List all properties
                        MelonLogger.Msg("    Properties:");
                        var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        foreach (var prop in properties)
                        {
                            try
                            {
                                var value = prop.GetValue(obj);
                                string valueStr = value != null ? value.GetType().Name : "null";
                                MelonLogger.Msg($"      {prop.PropertyType.Name} {prop.Name} = {valueStr}");
                            }
                            catch
                            {
                                MelonLogger.Msg($"      {prop.PropertyType.Name} {prop.Name} = <error reading>");
                            }
                        }
                        
                        MelonLogger.Msg(""); // Empty line for readability
                    }
                }
                
                MelonLogger.Msg($"Total vehicle-like objects found: {vehicleCount}");
                
                if (vehicleCount == 0)
                {
                    MelonLogger.Msg("No vehicles found. Trying broader search...");
                    
                    // Try to find ANY object with "tank" in the name
                    var allGameObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
                    MelonLogger.Msg($"Searching {allGameObjects.Length} GameObjects...");
                    
                    foreach (var go in allGameObjects)
                    {
                        if (go == null) continue;
                        
                        string name = go.name.ToLower();
                        if (name.Contains("tank") || name.Contains("vehicle") || 
                            name.Contains("pz") || name.Contains("char") || 
                            name.Contains("t-34") || name.Contains("cromwell"))
                        {
                            MelonLogger.Msg($"  GameObject: {go.name}");
                            MelonLogger.Msg($"    Position: {go.transform.position}");
                            MelonLogger.Msg($"    Components:");
                            
                            var components = go.GetComponents<MonoBehaviour>();
                            foreach (var comp in components)
                            {
                                if (comp != null)
                                {
                                    MelonLogger.Msg($"      - {comp.GetType().FullName}");
                                }
                            }
                        }
                    }
                }
                
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to find vehicles: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}