using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Il2CppInterop.Runtime;
using Il2CppSprocket.Gameplay.VehicleControl;
using Il2CppSprocket.TechTrees;
using UnityEngine;
using MelonLoader;
using Il2CppSprocket.Vehicles;
using Il2CppSprocket.Vehicles.Serialization;

namespace SprocketMultiplayer.Core
{
    /// <summary>
    /// Vehicle spawner using proper Sprocket API
    /// Based on Hamish's guidance about IVehicleFactory.Create() and VehicleController
    /// </summary>
    public static class VehicleSpawner
    {
        private static bool isInitialized = false;
        private static Dictionary<string, string> blueprintPaths = new Dictionary<string, string>();
        private static List<string> availableTankIds = new List<string>();
        private static string defaultTankId = null;
        
        // Cache for factory and register
        private static IVehicleFactory cachedFactory = null;
        private static VehicleRegister cachedRegister = null;
        private static VehicleController cachedController = null;
        
        private static readonly string vehiclesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "Sprocket", "Factions", "AllowedVehicles", "Blueprints", "Vehicles"
        );

        /// <summary>
        /// Initialize the spawner by scanning available blueprints
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized)
            {
                MelonLogger.Msg("[VehicleSpawner] Already initialized.");
                return;
            }

            MelonLogger.Msg("[VehicleSpawner] ===== INITIALIZING VEHICLE SPAWNER =====");

            if (!Directory.Exists(vehiclesPath))
            {
                MelonLogger.Warning($"[VehicleSpawner] Vehicles folder not found: {vehiclesPath}");
                isInitialized = true;
                return;
            }

            // Find all .blueprint files
            var files = Directory.GetFiles(vehiclesPath, "*.blueprint", SearchOption.AllDirectories);
            MelonLogger.Msg($"[VehicleSpawner] Found {files.Length} blueprint files.");

            foreach (var file in files)
            {
                string tankName = Path.GetFileNameWithoutExtension(file);
                blueprintPaths[tankName] = file;
                availableTankIds.Add(tankName);
                
                if (defaultTankId == null)
                    defaultTankId = tankName;
                
                MelonLogger.Msg($"[VehicleSpawner] ✓ Loaded: {tankName}");
            }

            isInitialized = true;
            MelonLogger.Msg($"[VehicleSpawner] ✓ Initialized with {files.Length} blueprints");
            MelonLogger.Msg($"[VehicleSpawner] Default tank: {defaultTankId ?? "None"}");
            MelonLogger.Msg("[VehicleSpawner] ===== INITIALIZATION COMPLETE =====");
        }

        /// <summary>
        /// Ensure the spawner is initialized
        /// </summary>
        public static void EnsureInitialized()
        {
            if (!isInitialized)
                Initialize();
        }

        /// <summary>
        /// Load a blueprint from file
        /// Returns IVehicleBlueprint using Sprocket's own deserialization
        /// </summary>
        private static IVehicleBlueprint LoadBlueprint(string tankName)
        {
            if (!blueprintPaths.TryGetValue(tankName, out string filePath))
            {
                MelonLogger.Error($"[VehicleSpawner] Blueprint path not found for: {tankName}");
                return null;
            }

            try
            {
                // Read blueprint file as JSON
                string json = File.ReadAllText(filePath);
                
                // Use VehicleBlueprint (concrete class) which implements IVehicleBlueprint
                var blueprint = JsonUtility.FromJson<VehicleBlueprint>(json);
                
                if (blueprint == null)
                {
                    MelonLogger.Error($"[VehicleSpawner] Failed to deserialize blueprint: {tankName}");
                    return null;
                }
                
                // Cast to interface
                return blueprint.Cast<IVehicleBlueprint>();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[VehicleSpawner] Error loading blueprint '{tankName}': {ex.Message}");
                MelonLogger.Error($"[VehicleSpawner] Stack: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Spawn a vehicle using proper Sprocket API
        /// Per Hamish: VehicleBlueprint is handed to IVehicleFactory through Create()
        /// </summary>
        public static IVehicleEditGateway SpawnVehicle(string tankName, Vector3 position, Quaternion rotation)
        {
            EnsureInitialized();

            MelonLogger.Msg($"[VehicleSpawner] ========== SPAWNING '{tankName}' ==========");

            // Validate tank exists
            if (!blueprintPaths.ContainsKey(tankName))
            {
                MelonLogger.Warning($"[VehicleSpawner] Tank '{tankName}' not found. Using default...");
                
                if (defaultTankId == null)
                {
                    MelonLogger.Error("[VehicleSpawner] No default tank available!");
                    return null;
                }
                
                tankName = defaultTankId;
            }

            try
            {
                // Step 1: Get IVehicleFactory instance
                IVehicleFactory factory = GetVehicleFactory();
                if (factory == null)
                {
                    MelonLogger.Error("[VehicleSpawner] ✗ Could not get IVehicleFactory! Factory must exist in scene.");
                    MelonLogger.Error("[VehicleSpawner] Try spawning a vehicle through the game first.");
                    return null;
                }
                MelonLogger.Msg("[VehicleSpawner] ✓ Got IVehicleFactory");

                // Step 2: Get ITechFrame
                ITechFrame techFrame = GetTechFrame();
                if (techFrame == null)
                {
                    MelonLogger.Error("[VehicleSpawner] ✗ Could not get ITechFrame!");
                    return null;
                }
                MelonLogger.Msg("[VehicleSpawner] ✓ Got ITechFrame");

                // Step 3: Load blueprint
                IVehicleBlueprint blueprint = LoadBlueprint(tankName);
                if (blueprint == null)
                {
                    MelonLogger.Error($"[VehicleSpawner] ✗ Failed to load blueprint for '{tankName}'");
                    return null;
                }
                MelonLogger.Msg($"[VehicleSpawner] ✓ Loaded blueprint for '{tankName}'");

                // Step 4: Create cancellation token
                var cts = new Il2CppSystem.Threading.CancellationTokenSource();
                Il2CppSystem.Threading.CancellationToken ct = cts.Token;

                // Step 5: Call factory.Create() - this is async
                VehicleSpawnFlags flags = VehicleSpawnFlags.None;
                
                MelonLogger.Msg("[VehicleSpawner] Calling factory.Create()...");
                var task = factory.Create(blueprint, techFrame, flags, ct);

                // Step 6: Wait for task completion
                int timeout = 0;
                while (!task.IsCompleted && !task.IsFaulted && !task.IsCanceled && timeout < 100)
                {
                    System.Threading.Thread.Sleep(50);
                    timeout++;
                }

                if (timeout >= 100)
                {
                    MelonLogger.Error("[VehicleSpawner] ✗ Factory task timeout!");
                    cts.Cancel();
                    return null;
                }

                if (task.IsFaulted)
                {
                    MelonLogger.Error("[VehicleSpawner] ✗ Factory task faulted!");
                    if (task.Exception != null)
                    {
                        MelonLogger.Error($"[VehicleSpawner] Exception: {task.Exception.Message}");
                    }
                    return null;
                }

                if (task.IsCanceled)
                {
                    MelonLogger.Error("[VehicleSpawner] ✗ Factory task was canceled!");
                    return null;
                }

                // Step 7: Get result - this is IVehicleEditGateway
                IVehicleEditGateway gateway = task.Result;
                
                if (gateway == null)
                {
                    MelonLogger.Error("[VehicleSpawner] ✗ Factory returned null gateway!");
                    return null;
                }

                MelonLogger.Msg("[VehicleSpawner] ✓ Factory created vehicle gateway");

                // Step 8: Position the vehicle
                try
                {
                    var vehicle = GetVehicleFromGateway(gateway);
                    if (vehicle != null)
                    {
                        // IVehicleBehaviour should be a MonoBehaviour, so gameObject should be accessible
                        var monoBehaviour = vehicle.TryCast<MonoBehaviour>();
                        if (monoBehaviour != null && monoBehaviour.gameObject != null)
                        {
                            monoBehaviour.gameObject.transform.position = position;
                            monoBehaviour.gameObject.transform.rotation = rotation;
                            MelonLogger.Msg($"[VehicleSpawner] ✓ Positioned vehicle at {position}");
                        }
                        else
                        {
                            MelonLogger.Warning("[VehicleSpawner] Could not access vehicle GameObject");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[VehicleSpawner] Could not position vehicle: {ex.Message}");
                }

                // Step 9: Register vehicle
                // Per Hamish: "You register tanks through its Register() and Deregister() methods"
                RegisterVehicle(gateway);

                MelonLogger.Msg($"[VehicleSpawner] ========== SPAWN COMPLETE ==========");
                return gateway;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[VehicleSpawner] ✗ Exception during spawn: {ex.Message}");
                MelonLogger.Error($"[VehicleSpawner] Stack: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Assign vehicle control to the local player
        /// Per Hamish: "Vehicle control is assigned to VehicleController.ControlledVehicle 
        /// in the player's VehicleController instance"
        /// ControlledVehicle expects IVehicleBehaviour (the vehicle), not IVehicleEditGateway (the gateway)
        /// </summary>
        public static void AssignVehicleControl(IVehicleEditGateway gateway)
        {
            if (gateway == null)
            {
                MelonLogger.Warning("[VehicleSpawner] Cannot assign control - gateway is null");
                return;
            }

            try
            {
                var controller = GetVehicleController();
                if (controller == null)
                {
                    MelonLogger.Error("[VehicleSpawner] ✗ Could not get VehicleController!");
                    return;
                }

                // Extract the vehicle from the gateway
                var vehicle = GetVehicleFromGateway(gateway);
                if (vehicle == null)
                {
                    MelonLogger.Error("[VehicleSpawner] ✗ Gateway has no vehicle!");
                    return;
                }

                // Assign the vehicle (IVehicleBehaviour) to ControlledVehicle
                controller.ControlledVehicle = vehicle;
                
                MelonLogger.Msg($"[VehicleSpawner] ✓ Assigned vehicle control to local player");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[VehicleSpawner] Error assigning control: {ex.Message}");
            }
        }

        /// <summary>
        /// Register vehicle in the dynamic register
        /// Per Hamish: "The dynamic register is just a wrapped list, a VehicleRegister instance"
        /// Update: VehicleRegister expects IVehicleEditGateway, not the vehicle itself
        /// </summary>
        private static void RegisterVehicle(IVehicleEditGateway gateway)
        {
            try
            {
                var register = GetVehicleRegister();
                if (register == null)
                {
                    MelonLogger.Warning("[VehicleSpawner] Could not get VehicleRegister - vehicle won't be registered");
                    return;
                }

                if (gateway == null)
                {
                    MelonLogger.Warning("[VehicleSpawner] Gateway is null, cannot register");
                    return;
                }

                // Register the gateway itself, not the vehicle
                register.Register(gateway);
                MelonLogger.Msg("[VehicleSpawner] ✓ Registered vehicle");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[VehicleSpawner] Could not register vehicle: {ex.Message}");
            }
        }

        /// <summary>
        /// Deregister vehicle from the register
        /// VehicleRegister expects IVehicleEditGateway, not the vehicle itself
        /// </summary>
        public static void DeregisterVehicle(IVehicleEditGateway gateway)
        {
            try
            {
                var register = GetVehicleRegister();
                if (register == null) return;

                if (gateway == null) return;

                // Deregister the gateway itself
                register.Deregister(gateway);
                MelonLogger.Msg("[VehicleSpawner] ✓ Deregistered vehicle");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[VehicleSpawner] Could not deregister vehicle: {ex.Message}");
            }
        }

        // ========== SYSTEM COMPONENT GETTERS ==========

        /// <summary>
        /// Get the Vehicle from IVehicleEditGateway using reflection
        /// IL2CPP interfaces don't directly expose properties, need to use reflection
        /// Returns IVehicleBehaviour which is what VehicleController and VehicleRegister expect
        /// </summary>
        private static IVehicleBehaviour GetVehicleFromGateway(IVehicleEditGateway gateway)
        {
            if (gateway == null) return null;

            try
            {
                // Method 1: Try direct property access via reflection
                var gatewayType = gateway.GetType();
                var vehicleProp = gatewayType.GetProperty("Vehicle", 
                    BindingFlags.Public | BindingFlags.Instance);
                
                if (vehicleProp != null)
                {
                    var vehicle = vehicleProp.GetValue(gateway);
                    if (vehicle != null)
                    {
                        // Cast to IVehicleBehaviour through IL2CPP
                        var il2cppObj = vehicle as Il2CppSystem.Object;
                        if (il2cppObj != null)
                        {
                            // Try casting to IVehicleBehaviour first
                            var behaviour = il2cppObj.TryCast<IVehicleBehaviour>();
                            if (behaviour != null)
                            {
                                return behaviour;
                            }
                            
                            // If that doesn't work, try Vehicle then cast
                            var vehicleObj = il2cppObj.TryCast<Vehicle>();
                            if (vehicleObj != null)
                            {
                                // Vehicle should implement IVehicleBehaviour
                                return vehicleObj.Cast<IVehicleBehaviour>();
                            }
                        }
                    }
                }

                // Method 2: Try looking for a method that returns Vehicle or IVehicleBehaviour
                var methods = gatewayType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                foreach (var method in methods)
                {
                    if (method.GetParameters().Length == 0)
                    {
                        var returnType = method.ReturnType;
                        
                        // Check if it returns Vehicle or IVehicleBehaviour
                        if (returnType == typeof(Vehicle) || returnType == typeof(IVehicleBehaviour) ||
                            typeof(IVehicleBehaviour).IsAssignableFrom(returnType))
                        {
                            var result = method.Invoke(gateway, null);
                            if (result != null)
                            {
                                var il2cppObj = result as Il2CppSystem.Object;
                                if (il2cppObj != null)
                                {
                                    var behaviour = il2cppObj.TryCast<IVehicleBehaviour>();
                                    if (behaviour != null)
                                    {
                                        return behaviour;
                                    }
                                }
                            }
                        }
                    }
                }

                MelonLogger.Warning("[VehicleSpawner] Could not extract Vehicle from gateway");
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[VehicleSpawner] Error getting vehicle from gateway: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the GameObject from IVehicleEditGateway
        /// Public helper method for external use (e.g., MultiplayerManager)
        /// </summary>
        public static GameObject GetGameObjectFromGateway(IVehicleEditGateway gateway)
        {
            if (gateway == null) return null;

            try
            {
                var vehicle = GetVehicleFromGateway(gateway);
                if (vehicle != null)
                {
                    var monoBehaviour = vehicle.TryCast<MonoBehaviour>();
                    if (monoBehaviour != null)
                    {
                        return monoBehaviour.gameObject;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[VehicleSpawner] Error getting GameObject from gateway: {ex.Message}");
                return null;
            }
        }

        // ========== SYSTEM COMPONENT GETTERS ==========

        /// <summary>
        /// Get IVehicleFactory instance
        /// Per Hamish: Use ServiceLocator to get the factory
        /// </summary>
        private static IVehicleFactory GetVehicleFactory()
        {
            if (cachedFactory != null) return cachedFactory;

            MelonLogger.Msg("[VehicleSpawner] Searching for IVehicleFactory in scene...");

            try
            {
                // Search all MonoBehaviours in scene for IVehicleFactory
                var allObjects = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;

                    try
                    {
                        // Check type name contains "Factory" or "Vehicle"
                        string typeName = obj.GetType().FullName;
                        if (!typeName.Contains("Factory") && !typeName.Contains("Vehicle")) continue;

                        // Try to cast to IVehicleFactory
                        var il2cppObj = obj as Il2CppSystem.Object;
                        if (il2cppObj == null) continue;

                        var factory = il2cppObj.TryCast<IVehicleFactory>();
                        if (factory != null)
                        {
                            MelonLogger.Msg($"[VehicleSpawner] ✓ Found IVehicleFactory: {typeName}");
                            cachedFactory = factory;
                            return factory;
                        }
                    }
                    catch { }
                }

                // Try searching by interface type directly
                var assembly = typeof(IVehicleFactory).Assembly;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsInterface || type.IsAbstract) continue;
                    if (!typeof(IVehicleFactory).IsAssignableFrom(type)) continue;
                    
                    // Check for static Instance property
                    var instanceProp = type.GetProperty("Instance", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    
                    if (instanceProp != null)
                    {
                        var instance = instanceProp.GetValue(null);
                        if (instance != null)
                        {
                            var il2cppObj = instance as Il2CppSystem.Object;
                            if (il2cppObj != null)
                            {
                                var factory = il2cppObj.TryCast<IVehicleFactory>();
                                if (factory != null)
                                {
                                    MelonLogger.Msg($"[VehicleSpawner] ✓ Found IVehicleFactory via static Instance: {type.Name}");
                                    cachedFactory = factory;
                                    return factory;
                                }
                            }
                        }
                    }

                    // Try finding instances of the type via Unity's FindObjectsOfType using MonoBehaviour as fallback
                    try
                    {
                        var allMonos = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                        foreach (var obj in allMonos)
                        {
                            if (obj == null) continue;

                            var objType = obj.GetType();
                            if (!type.IsAssignableFrom(objType)) continue; // Only types that implement IVehicleFactory

                            var il2cppObj = obj as Il2CppSystem.Object;
                            if (il2cppObj == null) continue;

                            var factory = il2cppObj.TryCast<IVehicleFactory>();
                            if (factory != null)
                            {
                                MelonLogger.Msg($"[VehicleSpawner] Found IVehicleFactory via MonoBehaviour search: {objType.Name}");
                                cachedFactory = factory;
                                return factory;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Error($"[VehicleSpawner] Error during MonoBehaviour FindObjectsOfType search: {ex.Message}");
                    }
                }

                MelonLogger.Warning("[VehicleSpawner] Could not find IVehicleFactory in scene");
                MelonLogger.Warning("[VehicleSpawner] The factory is created when the game spawns its first vehicle");
                MelonLogger.Warning("[VehicleSpawner] Try spawning a vehicle through the game UI first, then try multiplayer spawning");
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[VehicleSpawner] Error searching for IVehicleFactory: {ex.Message}");
                MelonLogger.Error($"[VehicleSpawner] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Get ITechFrame instance
        /// </summary>
        private static ITechFrame GetTechFrame()
        {
            try
            {
                // Search scene for ITechFrame
                var allObjects = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    
                    try
                    {
                        var il2cppObj = obj as Il2CppSystem.Object;
                        if (il2cppObj == null) continue;

                        var techFrame = il2cppObj.TryCast<ITechFrame>();
                        if (techFrame != null)
                        {
                            MelonLogger.Msg($"[VehicleSpawner] Found ITechFrame: {obj.GetType().Name}");
                            return techFrame;
                        }
                    }
                    catch { }
                }

                // Try static instance pattern
                var assembly = typeof(ITechFrame).Assembly;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsInterface || type.IsAbstract) continue;
                    if (!typeof(ITechFrame).IsAssignableFrom(type)) continue;
                    
                    var instanceProp = type.GetProperty("Instance", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    
                    if (instanceProp != null)
                    {
                        var instance = instanceProp.GetValue(null);
                        if (instance != null)
                        {
                            var il2cppObj = instance as Il2CppSystem.Object;
                            if (il2cppObj != null)
                            {
                                var techFrame = il2cppObj.TryCast<ITechFrame>();
                                if (techFrame != null)
                                {
                                    MelonLogger.Msg($"[VehicleSpawner] Found ITechFrame via static: {type.Name}");
                                    return techFrame;
                                }
                            }
                        }
                    }
                }

                MelonLogger.Warning("[VehicleSpawner] Could not find ITechFrame");
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[VehicleSpawner] Error getting ITechFrame: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get VehicleRegister instance
        /// Per Hamish: "The dynamic register is just a wrapped list, a VehicleRegister instance"
        /// </summary>
        private static VehicleRegister GetVehicleRegister()
        {
            if (cachedRegister != null) return cachedRegister;

            try
            {
                // Search scene
                var allObjects = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    
                    if (obj.GetType().Name.Contains("VehicleRegister"))
                    {
                        var il2cppObj = obj as Il2CppSystem.Object;
                        if (il2cppObj != null)
                        {
                            var register = il2cppObj.TryCast<VehicleRegister>();
                            if (register != null)
                            {
                                MelonLogger.Msg($"[VehicleSpawner] Found VehicleRegister: {obj.GetType().Name}");
                                cachedRegister = register;
                                return register;
                            }
                        }
                    }
                }

                // Try static instance
                var registerType = typeof(VehicleRegister);
                var instanceProp = registerType.GetProperty("Instance",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                
                if (instanceProp != null)
                {
                    var instance = instanceProp.GetValue(null);
                    if (instance != null)
                    {
                        var il2cppObj = instance as Il2CppSystem.Object;
                        if (il2cppObj != null)
                        {
                            var register = il2cppObj.TryCast<VehicleRegister>();
                            if (register != null)
                            {
                                MelonLogger.Msg("[VehicleSpawner] Found VehicleRegister via static Instance");
                                cachedRegister = register;
                                return register;
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[VehicleSpawner] Error getting VehicleRegister: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the local player's VehicleController
        /// Per Hamish: "Vehicle control is assigned to VehicleController.ControlledVehicle
        /// in the player's VehicleController instance (in VehicleControlPlayerState)"
        /// </summary>
        private static VehicleController GetVehicleController()
        {
            if (cachedController != null) return cachedController;

            try
            {
                // Search for VehicleController
                var allObjects = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    
                    if (obj.GetType().Name.Contains("VehicleController"))
                    {
                        var il2cppObj = obj as Il2CppSystem.Object;
                        if (il2cppObj != null)
                        {
                            var controller = il2cppObj.TryCast<VehicleController>();
                            if (controller != null)
                            {
                                MelonLogger.Msg($"[VehicleSpawner] Found VehicleController: {obj.GetType().Name}");
                                cachedController = controller;
                                return controller;
                            }
                        }
                    }
                }

                MelonLogger.Warning("[VehicleSpawner] Could not find VehicleController");
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[VehicleSpawner] Error getting VehicleController: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clear all cached references (call when changing scenes)
        /// </summary>
        public static void ClearCaches()
        {
            cachedFactory = null;
            cachedRegister = null;
            cachedController = null;
            MelonLogger.Msg("[VehicleSpawner] Cleared cached references");
        }

        /// <summary>
        /// Try to initialize the factory by triggering the game's scene systems
        /// Per Hamish: "Scenes are loaded through the ISceneManager instance"
        /// This should initialize IVehicleFactory in the scene
        /// </summary>
        public static void TryInitializeFactory()
        {
            MelonLogger.Msg("[VehicleSpawner] Attempting to initialize factory via game systems...");
            
            try
            {
                // Look for ISceneManager
                var sceneManagerType = typeof(IVehicleFactory).Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "ISceneManager" || t.FullName?.Contains("ISceneManager") == true);
                
                if (sceneManagerType != null)
                {
                    MelonLogger.Msg($"[VehicleSpawner] Found ISceneManager type: {sceneManagerType.FullName}");
                    
                    // Try to find instance
                    var instanceProp = sceneManagerType.GetProperty("Instance", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    
                    if (instanceProp != null)
                    {
                        var sceneManager = instanceProp.GetValue(null);
                        if (sceneManager != null)
                        {
                            MelonLogger.Msg("[VehicleSpawner] ✓ Got ISceneManager instance");
                            // The factory should now be available in the scene
                            return;
                        }
                    }
                }
                
                // Alternative: Look for game state objects that might initialize the factory
                var allObjects = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    
                    string typeName = obj.GetType().FullName;
                    
                    // Look for game state or scenario managers
                    if (typeName.Contains("GameState") || typeName.Contains("Scenario") || typeName.Contains("Manager"))
                    {
                        MelonLogger.Msg($"[VehicleSpawner] Found potential game manager: {typeName}");
                        
                        // The factory should be initialized when these objects exist
                        // Try to get it now
                        var factory = GetVehicleFactory();
                        if (factory != null)
                        {
                            MelonLogger.Msg("[VehicleSpawner] ✓ Factory is now available!");
                            return;
                        }
                    }
                }
                
                MelonLogger.Warning("[VehicleSpawner] Could not find scene initialization systems");
                MelonLogger.Warning("[VehicleSpawner] Factory may need to be initialized by spawning through game UI first");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[VehicleSpawner] Error initializing factory: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if the factory is available
        /// </summary>
        public static bool IsFactoryAvailable()
        {
            if (cachedFactory != null) return true;
            
            var factory = GetVehicleFactory();
            return factory != null;
        }

        /// <summary>
        /// Wait for factory to become available (for use in coroutines)
        /// Returns true if factory became available, false if timeout
        /// </summary>
        public static IEnumerator WaitForFactory(int maxWaitSeconds = 10)
        {
            MelonLogger.Msg($"[VehicleSpawner] Waiting for factory (max {maxWaitSeconds}s)...");
            
            int waited = 0;
            while (waited < maxWaitSeconds)
            {
                if (IsFactoryAvailable())
                {
                    MelonLogger.Msg("[VehicleSpawner] ✓ Factory is available!");
                    yield return true;
                    yield break;
                }
                
                yield return new WaitForSeconds(1f);
                waited++;
                
                if (waited % 3 == 0)
                {
                    MelonLogger.Msg($"[VehicleSpawner] Still waiting for factory... ({waited}s/{maxWaitSeconds}s)");
                }
            }
            
            MelonLogger.Error("[VehicleSpawner] ✗ Factory did not become available!");
            MelonLogger.Error("[VehicleSpawner] The IVehicleFactory is only created when the game spawns its first vehicle.");
            MelonLogger.Error("[VehicleSpawner] WORKAROUND: Spawn a vehicle through the game UI (press ESC > Spawn Vehicle) first, then try multiplayer.");
            yield return false;
        }

        // ========== PUBLIC API ==========

        public static string GetDefaultTankId()
        {
            EnsureInitialized();
            return defaultTankId;
        }

        public static List<string> GetAvailableTankIds()
        {
            EnsureInitialized();
            return new List<string>(availableTankIds);
        }

        public static bool HasTank(string tankId)
        {
            EnsureInitialized();
            return blueprintPaths.ContainsKey(tankId);
        }

        public static int GetTankCount()
        {
            EnsureInitialized();
            return blueprintPaths.Count;
        }
    }
}