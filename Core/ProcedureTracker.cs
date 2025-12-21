using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using MelonLoader;
using Il2CppSprocket.Vehicles;

namespace SprocketMultiplayer.Core
{
    // Usage:
    // 1. Load a scenario and spawn a tank normally
    // 2. Press F9 to START tracking
    // 3. Spawn another tank (or wait for one to spawn)
    // 4. Press F10 to STOP tracking and dump results
    // 5. Send the log output for analysis
    public class ProcedureTracker : MonoBehaviour
    {
        private static bool isTracking = false;
        private static List<string> trackedEvents = new List<string>();
        private static Dictionary<string, Component> trackedComponents = new Dictionary<string, Component>();
        private static List<GameObject> vehiclesBeforeTracking = new List<GameObject>();
        
        private void Update() {
            if (Keyboard.current == null) return;

            // F9: Start tracking
            if (Keyboard.current.f9Key.wasPressedThisFrame)
            {
                StartTracking();
            }

            // F10: Stop tracking and dump results
            if (Keyboard.current.f10Key.wasPressedThisFrame)
            {
                StopTrackingAndDump();
            }

            // If tracking, monitor for new vehicles
            if (isTracking)
            {
                MonitorForNewVehicles();
            }
        }

        private void StartTracking()
        {
            MelonLogger.Msg("========================================");
            MelonLogger.Msg("=== PROCEDURE TRACKING STARTED ===");
            MelonLogger.Msg("========================================");
            
            isTracking = true;
            trackedEvents.Clear();
            trackedComponents.Clear();
            
            // Record all existing vehicles
            vehiclesBeforeTracking.Clear();
            var existingVehicles = FindObjectsOfType<Vehicle>();
            foreach (var vehicle in existingVehicles)
            {
                vehiclesBeforeTracking.Add(vehicle.gameObject);
                MelonLogger.Msg($"[Baseline] Existing vehicle: {vehicle.gameObject.name}");
            }
            
            LogEvent("=== TRACKING BASELINE ===");
            LogEvent($"Scene: {SceneManager.GetActiveScene().name}");
            LogEvent($"Existing vehicles: {vehiclesBeforeTracking.Count}");
            LogEvent("Now spawn a tank...");
            
            MelonLogger.Msg("✓ Tracking active. Spawn a tank now, then press F10 to analyze.");
        }

        private void StopTrackingAndDump()
        {
            if (!isTracking)
            {
                MelonLogger.Warning("Tracking is not active! Press F9 first.");
                return;
            }

            isTracking = false;
            
            MelonLogger.Msg("========================================");
            MelonLogger.Msg("=== PROCEDURE TRACKING STOPPED ===");
            MelonLogger.Msg("========================================");
            
            // Find newly spawned vehicles
            var allVehicles = FindObjectsOfType<Vehicle>();
            var newVehicles = new List<Vehicle>();
            
            foreach (var vehicle in allVehicles)
            {
                if (!vehiclesBeforeTracking.Contains(vehicle.gameObject))
                {
                    newVehicles.Add(vehicle);
                }
            }
            
            MelonLogger.Msg($"\n[RESULT] Found {newVehicles.Count} newly spawned vehicle(s)");
            
            if (newVehicles.Count > 0)
            {
                foreach (var vehicle in newVehicles)
                {
                    AnalyzeVehicle(vehicle);
                }
            }
            else
            {
                MelonLogger.Warning("No new vehicles detected! Did you spawn one?");
            }
            
            // Dump all tracked events
            MelonLogger.Msg("\n========================================");
            MelonLogger.Msg("=== TRACKED EVENTS LOG ===");
            MelonLogger.Msg("========================================");
            foreach (var evt in trackedEvents)
            {
                MelonLogger.Msg(evt);
            }
            
            MelonLogger.Msg("\n========================================");
            MelonLogger.Msg("=== ANALYSIS COMPLETE ===");
            MelonLogger.Msg("========================================");
        }

        private void MonitorForNewVehicles()
        {
            // Continuously check for new vehicles during tracking
            var allVehicles = FindObjectsOfType<Vehicle>();
            foreach (var vehicle in allVehicles)
            {
                if (!vehiclesBeforeTracking.Contains(vehicle.gameObject))
                {
                    if (!trackedComponents.ContainsKey(vehicle.gameObject.name))
                    {
                        trackedComponents[vehicle.gameObject.name] = vehicle;
                        LogEvent($"[NEW VEHICLE DETECTED] {vehicle.gameObject.name} at position {vehicle.transform.position}");
                    }
                }
            }
        }

        private void AnalyzeVehicle(Vehicle vehicle)
        {
            MelonLogger.Msg($"\n╔══════════════════════════════════════════════════════════╗");
            MelonLogger.Msg($"║ ANALYZING VEHICLE: {vehicle.gameObject.name}");
            MelonLogger.Msg($"╚══════════════════════════════════════════════════════════╝");
            
            // Basic info
            MelonLogger.Msg($"\n[BASIC INFO]");
            MelonLogger.Msg($"  Name: {vehicle.gameObject.name}");
            MelonLogger.Msg($"  Position: {vehicle.transform.position}");
            MelonLogger.Msg($"  Rotation: {vehicle.transform.rotation.eulerAngles}");
            MelonLogger.Msg($"  Active: {vehicle.gameObject.activeSelf}");
            MelonLogger.Msg($"  Layer: {LayerMask.LayerToName(vehicle.gameObject.layer)}");
            MelonLogger.Msg($"  Tag: {vehicle.gameObject.tag}");
            
            // All components
            MelonLogger.Msg($"\n[ALL COMPONENTS]");
            var allComponents = vehicle.GetComponents<Component>();
            foreach (var comp in allComponents)
            {
                if (comp != null)
                {
                    MelonLogger.Msg($"  • {comp.GetType().FullName}");
                }
            }
            
            // Vehicle-specific analysis
            MelonLogger.Msg($"\n[VEHICLE COMPONENT ANALYSIS]");
            AnalyzeVehicleComponent(vehicle);
            
            // ===== Check for spawning components =====
            MelonLogger.Msg($"\n[SPAWNING COMPONENTS - Il2CppSprocket.Vehicle.Spawning]");
            FindComponentsInNamespace(vehicle.gameObject, "Il2CppSprocket.Vehicle.Spawning");
            
            // ===== Check for VehicleControl components =====
            MelonLogger.Msg($"\n[VEHICLE CONTROL - Il2CppSprocket.VehicleControl]");
            FindComponentsInNamespace(vehicle.gameObject, "Il2CppSprocket.VehicleControl");
            
            // ===== Check for VehicleController components =====
            MelonLogger.Msg($"\n[VEHICLE CONTROLLER - Il2CppSprocket.VehicleController]");
            FindComponentsInNamespace(vehicle.gameObject, "Il2CppSprocket.VehicleController");
            
            // ===== Check for VehicleControlUI components =====
            MelonLogger.Msg($"\n[VEHICLE CONTROL UI - Il2CppSprocket.VehicleControlUI]");
            FindComponentsInNamespace(vehicle.gameObject, "Il2CppSprocket.VehicleControlUI");
            
            // Check for AI components
            MelonLogger.Msg($"\n[AI COMPONENTS - Il2CppSprocket.Vehicles.AI]");
            FindAIComponents(vehicle.gameObject);
            
            // Check for control-related components (generic)
            MelonLogger.Msg($"\n[CONTROL COMPONENTS (Generic)]");
            FindControlComponents(vehicle.gameObject);
            
            // Analyze hierarchy
            MelonLogger.Msg($"\n[HIERARCHY]");
            LogHierarchy(vehicle.transform, 0);
            
            // Check parent relationships
            MelonLogger.Msg($"\n[PARENT STRUCTURE]");
            Transform current = vehicle.transform;
            int depth = 0;
            while (current != null && depth < 10)
            {
                MelonLogger.Msg($"  Level {depth}: {current.gameObject.name}");
                current = current.parent;
                depth++;
            }
            
            // ===== NEW: Try to find spawner references =====
            MelonLogger.Msg($"\n[SPAWNER DETECTION]");
            DetectSpawnerSystems();
        }

        private void AnalyzeVehicleComponent(Vehicle vehicle)
        {
            try
            {
                var vehicleType = vehicle.GetType();
                MelonLogger.Msg($"  Type: {vehicleType.FullName}");
                
                // Get all fields
                var fields = vehicleType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MelonLogger.Msg($"\n  [KEY FIELDS]");
                
                foreach (var field in fields)
                {
                    try
                    {
                        var value = field.GetValue(vehicle);
                        string valueStr = value?.ToString() ?? "null";
                        
                        // Log potentially interesting fields
                        if (field.Name.ToLower().Contains("control") ||
                            field.Name.ToLower().Contains("player") ||
                            field.Name.ToLower().Contains("ai") ||
                            field.Name.ToLower().Contains("team") ||
                            field.Name.ToLower().Contains("input") ||
                            field.Name.ToLower().Contains("owner"))
                        {
                            MelonLogger.Msg($"    {field.Name} ({field.FieldType.Name}): {valueStr}");
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"    {field.Name}: [Error reading: {ex.Message}]");
                    }
                }
                
                // Get all properties
                var properties = vehicleType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                MelonLogger.Msg($"\n  [KEY PROPERTIES]");
                
                foreach (var prop in properties)
                {
                    try
                    {
                        if (!prop.CanRead) continue;
                        
                        var value = prop.GetValue(vehicle);
                        string valueStr = value?.ToString() ?? "null";
                        
                        if (prop.Name.ToLower().Contains("control") ||
                            prop.Name.ToLower().Contains("player") ||
                            prop.Name.ToLower().Contains("ai") ||
                            prop.Name.ToLower().Contains("team") ||
                            prop.Name.ToLower().Contains("input") ||
                            prop.Name.ToLower().Contains("owner"))
                        {
                            MelonLogger.Msg($"    {prop.Name} ({prop.PropertyType.Name}): {valueStr}");
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"    {prop.Name}: [Error reading: {ex.Message}]");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"  Error analyzing Vehicle component: {ex.Message}");
            }
        }

        private void FindControlComponents(GameObject obj)
        {
            var allComponents = obj.GetComponentsInChildren<Component>(true);
            
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                
                string typeName = comp.GetType().Name.ToLower();
                
                if (typeName.Contains("control") || 
                    typeName.Contains("input") || 
                    typeName.Contains("player") ||
                    typeName.Contains("driver") ||
                    typeName.Contains("commander"))
                {
                    MelonLogger.Msg($"  • {comp.GetType().FullName} on {comp.gameObject.name}");
                    InspectComponent(comp, "    ");
                }
            }
        }

        private void FindAIComponents(GameObject obj)
        {
            var allComponents = obj.GetComponentsInChildren<Component>(true);
            
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                
                string fullTypeName = comp.GetType().FullName;
                string typeName = comp.GetType().Name.ToLower();
                
                if (fullTypeName.Contains("Il2CppSprocket.Vehicles.AI") ||
                    typeName.Contains("ai") || 
                    typeName.Contains("bot") || 
                    typeName.Contains("agent") ||
                    typeName.Contains("behavior"))
                {
                    MelonLogger.Msg($"  • {comp.GetType().FullName} on {comp.gameObject.name}");
                    InspectComponent(comp, "    ");
                }
            }
        }

        private void FindComponentsInNamespace(GameObject obj, string namespaceFilter)
        {
            var allComponents = obj.GetComponentsInChildren<Component>(true);
            bool foundAny = false;
            
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                
                string fullTypeName = comp.GetType().FullName;
                
                if (fullTypeName.Contains(namespaceFilter))
                {
                    foundAny = true;
                    MelonLogger.Msg($"  • {fullTypeName} on {comp.gameObject.name}");
                    InspectComponent(comp, "    ");
                }
            }
            
            if (!foundAny)
            {
                MelonLogger.Msg($"  (No components found in namespace: {namespaceFilter})");
            }
        }

        private void DetectSpawnerSystems()
        {
            try
            {
                // Look for singleton spawners or managers
                var spawnerTypes = new[]
                {
                    "VehicleSpawner",
                    "SpawnManager",
                    "VehicleManager",
                    "ScenarioManager"
                };

                foreach (var typeName in spawnerTypes)
                {
                    try
                    {
                        // Search all loaded assemblies
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            var type = assembly.GetTypes().FirstOrDefault(t => t.Name.Contains(typeName));
                            if (type != null)
                            {
                                MelonLogger.Msg($"  Found type: {type.FullName}");
                                
                                // Check for singleton instance
                                var instanceField = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                                var instanceProp = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                                
                                if (instanceField != null)
                                {
                                    var instance = instanceField.GetValue(null);
                                    MelonLogger.Msg($"    Instance field found: {instance != null}");
                                    if (instance != null)
                                    {
                                        InspectSpawnerInstance(instance, "      ");
                                    }
                                }
                                
                                if (instanceProp != null)
                                {
                                    var instance = instanceProp.GetValue(null);
                                    MelonLogger.Msg($"    Instance property found: {instance != null}");
                                    if (instance != null)
                                    {
                                        InspectSpawnerInstance(instance, "      ");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"  Error searching for {typeName}: {ex.Message}");
                    }
                }
                
                // Also try FindObjectOfType for common spawner types
                MelonLogger.Msg($"\n  [Active Spawner Objects in Scene]");
                var allObjects = FindObjectsOfType<MonoBehaviour>();
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    string typeName = obj.GetType().Name.ToLower();
                    if (typeName.Contains("spawn") || typeName.Contains("manager"))
                    {
                        MelonLogger.Msg($"    • {obj.GetType().FullName} on {obj.gameObject.name}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"  Error detecting spawner systems: {ex.Message}");
            }
        }

        private void InspectSpawnerInstance(object instance, string indent)
        {
            try
            {
                var type = instance.GetType();
                
                // Look for spawn methods
                MelonLogger.Msg($"{indent}[Spawn Methods]");
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                foreach (var method in methods)
                {
                    if (method.Name.ToLower().Contains("spawn"))
                    {
                        var parameters = method.GetParameters();
                        string paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        MelonLogger.Msg($"{indent}  • {method.Name}({paramStr}) -> {method.ReturnType.Name}");
                    }
                }
                
                // Look for relevant fields
                MelonLogger.Msg($"{indent}[Relevant Fields]");
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    try
                    {
                        var value = field.GetValue(instance);
                        MelonLogger.Msg($"{indent}  • {field.Name} ({field.FieldType.Name}): {value?.ToString() ?? "null"}");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"{indent}Error inspecting spawner: {ex.Message}");
            }
        }

        private void InspectComponent(Component comp, string indent)
        {
            try
            {
                var type = comp.GetType();
                
                // Fields
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    try
                    {
                        var value = field.GetValue(comp);
                        MelonLogger.Msg($"{indent}[Field] {field.Name}: {value?.ToString() ?? "null"}");
                    }
                    catch { }
                }
                
                // Properties
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var prop in properties)
                {
                    try
                    {
                        if (!prop.CanRead) continue;
                        var value = prop.GetValue(comp);
                        MelonLogger.Msg($"{indent}[Property] {prop.Name}: {value?.ToString() ?? "null"}");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"{indent}Error inspecting component: {ex.Message}");
            }
        }

        private void LogHierarchy(Transform root, int depth)
        {
            if (depth > 5) return; // Limit depth to avoid spam
            
            string indent = new string(' ', depth * 2);
            MelonLogger.Msg($"{indent}└─ {root.gameObject.name}");
            
            var components = root.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp != null && !(comp is Transform))
                {
                    MelonLogger.Msg($"{indent}   • {comp.GetType().Name}");
                }
            }
            
            foreach (Transform child in root)
            {
                LogHierarchy(child, depth + 1);
            }
        }

        private void LogEvent(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logEntry = $"[{timestamp}] {message}";
            trackedEvents.Add(logEntry);
        }

        private void OnDestroy()
        {
            if (isTracking)
            {
                MelonLogger.Warning("ProcedureTracker destroyed while still tracking!");
            }
        }
    }
}