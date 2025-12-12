using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

namespace SprocketMultiplayer.Core {
    public class SceneLogger {
        private static HashSet<GameObject> trackedVehicles = new HashSet<GameObject>();
        
        private static string currentSpawner = "Unknown";
        
        
        public static void LogVehicleComponents(GameObject vehicle)
        {
            if (vehicle == null) {
                MelonLogger.Warning("[LogVehicleComponents] Vehicle is null, skipping.");
                return;
            }

            foreach (var c in vehicle.GetComponents<MonoBehaviour>()) {
                if (c == null) continue;
                var t = c.GetType().FullName;
                if (t.Contains("Control") || t.Contains("Driver") || t.Contains("Input"))
                    MelonLogger.Msg($"  {vehicle.name} has {t}");
            }

            foreach (var go in GameObject.FindObjectsOfType<GameObject>(true)) {
                if (go.GetComponents<MonoBehaviour>().Any(c => c.GetType().Name.Contains("Control") || c.GetType().Name.Contains("Driver")))
                    MelonLogger.Msg($"[Controllable Vehicle] {go.name}");
            }
        }
        public static void TrackNewVehicles()
        {
            foreach (var go in GameObject.FindObjectsOfType<GameObject>(true))
            {
                if (!trackedVehicles.Contains(go) &&
                    go.GetComponentsInChildren<Rigidbody>().Length > 0 &&
                    go.GetComponentsInChildren<Collider>().Length > 5)
                {
                    trackedVehicles.Add(go);
                    MelonLogger.Msg($"[Vehicle Spawned] {go.name} at {Time.time} by {currentSpawner}");
                }
            }
        }
        public static void ScanPlayerStates() {
        MelonLogger.Msg("===== PLAYER STATE SCAN =====");
        
        var playerGO = GameObject.Find("Player");
        if (playerGO == null) return;
        
        var allComps = playerGO.GetComponents<MonoBehaviour>();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        
        foreach (var comp in allComps)
        {
            if (comp == null) continue;
            
            string typename = comp.GetIl2CppType().FullName;
            MelonLogger.Msg($"\n=== {typename} ===");
            
            // Log ALL fields
            var fields = comp.GetType().GetFields(flags);
            MelonLogger.Msg($"Fields ({fields.Length}):");
            foreach (var f in fields)
            {
                try
                {
                    var val = f.GetValue(comp);
                    var valStr = val?.ToString() ?? "null";
                    
                    // Show GameObject names if applicable
                    if (val is GameObject go)
                        valStr = $"GameObject '{go.name}'";
                    else if (val is Component c)
                        valStr = $"{c.GetType().Name} on '{c.gameObject.name}'";
                    
                    MelonLogger.Msg($"  {f.Name} ({f.FieldType.Name}) = {valStr}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"  {f.Name} (FAILED: {ex.Message})");
                }
            }
            
            // Log ALL methods
            var methods = comp.GetType().GetMethods(flags)
                .Where(m => !m.Name.StartsWith("get_") && 
                           !m.Name.StartsWith("set_") &&
                           !m.Name.StartsWith("add_") &&
                           !m.Name.StartsWith("remove_"))
                .ToList();
            
            MelonLogger.Msg($"\nMethods ({methods.Count}):");
            foreach (var m in methods.Take(30)) // Limit output
            {
                var paramStr = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name));
                MelonLogger.Msg($"  {m.Name}({paramStr})");
            }
        }
        
        MelonLogger.Msg("\n===== END PLAYER STATE SCAN =====");
    }
        public static void ScanVehiclePlayerLinks() {
            MelonLogger.Msg("===== VEHICLE → PLAYER LINKS =====");
    
            var vehicles = GameObject.FindObjectsOfType<GameObject>()
                .Where(go => go.GetComponent<Rigidbody>() != null && 
                             go.GetComponentsInChildren<Collider>().Length > 10)
                .ToList();
    
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    
            foreach (var vehicle in vehicles)
            {
                MelonLogger.Msg($"\n=== Vehicle: {vehicle.name} ===");
        
                var comps = vehicle.GetComponentsInChildren<MonoBehaviour>()
                    .Where(c => c != null && 
                                (c.GetType().Name.Contains("Control") ||
                                 c.GetType().Name.Contains("Driver") ||
                                 c.GetType().Name.Contains("Player")))
                    .ToList();
        
                foreach (var comp in comps)
                {
                    MelonLogger.Msg($"\n  Component: {comp.GetType().FullName}");
            
                    foreach (var f in comp.GetType().GetFields(flags))
                    {
                        var val = f.GetValue(comp);
                        if (val != null && (f.Name.ToLower().Contains("player") || 
                                            f.FieldType.Name.Contains("Player")))
                        {
                            MelonLogger.Msg($"    ⭐ {f.Name} = {val}");
                        }
                    }
                }
            }
    
            MelonLogger.Msg("\n===== END VEHICLE → PLAYER LINKS =====");
        }
        public static void ScanPlayerStatesIL2CPP() {
        MelonLogger.Msg("===== IL2CPP PLAYER STATE SCAN =====");
        
        var playerGO = GameObject.Find("Player");
        if (playerGO == null) return;
        
        var allComps = playerGO.GetComponents<MonoBehaviour>();
        
        foreach (var comp in allComps)
        {
            if (comp == null) continue;
            
            try
            {
                // Get the IL2CPP type instead of the C# wrapper type
                var il2cppType = comp.GetIl2CppType();
                var typeName = il2cppType.FullName;
                
                MelonLogger.Msg($"\n=== {typeName} ===");
                
                // Use IL2CPP reflection to get fields
                var bindingFlags = Il2CppSystem.Reflection.BindingFlags.Public | 
                                  Il2CppSystem.Reflection.BindingFlags.NonPublic | 
                                  Il2CppSystem.Reflection.BindingFlags.Instance;
                
                var fields = il2cppType.GetFields(bindingFlags);
                MelonLogger.Msg($"Fields ({fields.Length}):");
                
                foreach (var field in fields)
                {
                    try
                    {
                        var fieldName = field.Name;
                        var fieldType = field.FieldType.Name;
                        
                        // Try to get value
                        var val = field.GetValue(comp);
                        var valStr = val?.ToString() ?? "null";
                        
                        // Highlight vehicle-related fields
                        if (fieldName.ToLower().Contains("vehicle") ||
                            fieldName.ToLower().Contains("control") ||
                            fieldName.ToLower().Contains("current") ||
                            fieldType.ToLower().Contains("vehicle"))
                        {
                            MelonLogger.Msg($"  ⭐ {fieldName} ({fieldType}) = {valStr}");
                        }
                        else
                        {
                            MelonLogger.Msg($"  {fieldName} ({fieldType}) = {valStr}");
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"  {field.Name} (ERROR: {ex.Message})");
                    }
                }
                
                // Get methods
                var methods = il2cppType.GetMethods(bindingFlags);
                MelonLogger.Msg($"\nMethods (first 30):");
                
                int count = 0;
                foreach (var method in methods)
                {
                    if (count++ >= 30) break;
                    
                    try
                    {
                        var methodName = method.Name;
                        
                        // Highlight vehicle-related methods
                        if (methodName.ToLower().Contains("vehicle") ||
                            methodName.ToLower().Contains("enter") ||
                            methodName.ToLower().Contains("possess") ||
                            methodName.ToLower().Contains("assign") ||
                            methodName.ToLower().Contains("control"))
                        {
                            var paramCount = method.GetParameters().Length;
                            MelonLogger.Msg($"  ⭐ {methodName}(params: {paramCount})");
                        }
                        else
                        {
                            MelonLogger.Msg($"  {methodName}");
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"Failed to scan component: {ex.Message}");
            }
        }
        
        MelonLogger.Msg("\n===== END IL2CPP SCAN =====");
    }

        static MonoBehaviour FindPlayerControlPlayer(GameObject playerGO)
        {
            foreach (var mb in playerGO.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;

                string fullname = null;
                try
                {
                    fullname = mb.GetIl2CppType().FullName;
                }
                catch
                {
                    fullname = mb.GetType().FullName;
                }

                if (fullname != null && fullname.Contains("Sprocket.PlayerControl.Player"))
                {
                    MelonLogger.Msg("[VCI] FOUND PlayerControl.Player → " + fullname);
                    return mb;
                }
            }

            MelonLogger.Warning("[VCI] PlayerControl.Player NOT FOUND on Player object!");
            return null;
        }
        public static class VehicleControlInspector {
            private static Dictionary<string, object> lastSnapshot = new Dictionary<string, object>();
            public static void LogVehicleControlMap() {
            MelonLogger.Msg("===== VEHICLE CONTROL INSPECTION =====");

            // 1) Find PlayerControl.Player
            var playerGO = GameObject.Find("Player");
            if (!playerGO) {
                MelonLogger.Warning("[VCI] Player not found.");
                return;
            }

            var player = FindPlayerControlPlayer(playerGO);
            if (player == null) {
                MelonLogger.Warning("[VCI] PlayerControl.Player not found on Player object.");
                return;
            }

            MelonLogger.Msg($"[VCI] Player component: {player.GetType().FullName}");

            // 2) Log all fields referencing vehicles
            MelonLogger.Msg("[VCI] Player vehicle-related fields:");

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            GameObject controlledVehicleGO = null;

            foreach (var f in player.GetType().GetFields(flags)) {
                object value = null;
                try { value = f.GetValue(player); } catch { }

                if (value is UnityEngine.Object uo) {
                    var name = uo ? uo.name : "null";
                    MelonLogger.Msg($"   FIELD {f.Name} → {uo.GetType().Name} '{name}'");

                    if (uo is GameObject go)
                        controlledVehicleGO = go;
                    else if (uo is Component comp)
                        controlledVehicleGO = comp.gameObject;
                }
            }

            foreach (var p in player.GetType().GetProperties(flags)) {
                object value = null;
                try {
                    value = p.GetValue(player);
                }
                catch {
                    continue;
                }

                if (value is UnityEngine.Object uo) {
                    var name = uo ? uo.name : "null";
                    MelonLogger.Msg($"   PROP  {p.Name} → {uo.GetType().Name} '{name}'");

                    if (uo is GameObject go)
                        controlledVehicleGO = go;
                    else if (uo is Component comp)
                        controlledVehicleGO = comp.gameObject;
                }
            }

            // 3) Show the vehicle this player is currently bound to
            if (controlledVehicleGO != null) {
                MelonLogger.Msg($"[VCI] Player is currently bound to vehicle: {controlledVehicleGO.name}");
            }
            else {
                MelonLogger.Msg("[VCI] Player is NOT assigned to any vehicle.");
            }
            var comps = playerGO.GetComponents<MonoBehaviour>();
            foreach (var c in comps) {
                MelonLogger.Msg("[FoundPlayerComp] " + c.GetType().FullName);
            }

            // 4) Scan the vehicle for control modules
            if (controlledVehicleGO != null)
            {
                MelonLogger.Msg($"[VCI] Scanning vehicle control modules of {controlledVehicleGO.name}...");

                var controls = controlledVehicleGO.GetComponentsInChildren<MonoBehaviour>(true)
                    .Where(c => c != null)
                    .Where(c => {
                        var n = c.GetType().Name;
                        return n.Contains("Control") || n.Contains("Driver") || n.Contains("Input");
                    });

                foreach (var ctrl in controls)
                {
                    MelonLogger.Msg($"   MODULE {ctrl.GetType().FullName}");

                    // Scan its fields for player or input links
                    foreach (var f in ctrl.GetType().GetFields(flags))
                    {
                        try
                        {
                            var val = f.GetValue(ctrl);
                            if (val is UnityEngine.Object uo)
                                MelonLogger.Msg($"      FIELD {f.Name} → {uo.name}");
                        } catch {}
                    }

                    // Scan methods that look like control handlers
                    foreach (var m in ctrl.GetType().GetMethods(flags))
                    {
                        var name = m.Name;

                        if (name.Contains("Input") ||
                            name.Contains("Drive") ||
                            name.Contains("Steer") ||
                            name.Contains("Aim") ||
                            name.Contains("Update") ||
                            name.Contains("Assign"))
                        {
                            MelonLogger.Msg($"      METHOD {m.Name}()");
                        }
                    }
                }
            }

            // 5) Detect changes since last call (useful for debugging)
            var snapshot = new Dictionary<string, object>();
            snapshot["player_vehicle"] = controlledVehicleGO;

            if (lastSnapshot.Count > 0)
            {
                if (!EqualsSafe(lastSnapshot["player_vehicle"], controlledVehicleGO))
                {
                    MelonLogger.Msg($"[VCI] CHANGE DETECTED: PlayerVehicle → {(controlledVehicleGO ? controlledVehicleGO.name : "null")}");
                }
            }

            lastSnapshot = snapshot;

            MelonLogger.Msg("===== END VEHICLE CONTROL INSPECTION =====");
        }

        private static bool EqualsSafe(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.Equals(b);
        }
    }
        public static void DeepScanVehicle(GameObject vehicle)
        {
            MelonLogger.Msg("===== VEHICLE DEEP SCAN =====");

            if (vehicle == null)
            {
                MelonLogger.Warning("[VDS] Vehicle is NULL");
                return;
            }

            var comps = vehicle.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (var comp in comps)
            {
                if (comp == null) continue;

                string fullname = "Unknown";
                try { fullname = comp.GetIl2CppType().FullName; }
                catch { fullname = comp.GetType().FullName; }

                // ищем интересующие названия
                if (!(fullname.Contains("Control") ||
                      fullname.Contains("Driver") ||
                      fullname.Contains("Controller") ||
                      fullname.Contains("Input") ||
                      fullname.Contains("Engine") ||
                      fullname.Contains("Motor") ||
                      fullname.Contains("Transmission") ||
                      fullname.Contains("Steer") ||
                      fullname.Contains("Drive") ||
                      fullname.Contains("Turret") ||
                      fullname.Contains("Gun")))
                    continue;

                MelonLogger.Msg($"[VDS] Component: {fullname}");
                
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                foreach (var field in comp.GetType().GetFields(flags))
                {
                    object val = null;
                    try { val = field.GetValue(comp); } catch { }

                    if (val is UnityEngine.Object uo)
                        MelonLogger.Msg($"    FIELD {field.Name} = {uo.name}");
                    else
                        MelonLogger.Msg($"    FIELD {field.Name} = {val}");
                }
            }

            MelonLogger.Msg("===== END VEHICLE DEEP SCAN =====");
        }
        
    }
}