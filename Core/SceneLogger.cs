using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

namespace SprocketMultiplayer.Core
{
    public class SceneLogger {
        private static HashSet<GameObject> trackedVehicles = new HashSet<GameObject>();

        // Temporary: store the spawner currently creating a vehicle (if you hook it)
        private static string currentSpawner = "Unknown";
        
        public static void LogSceneDetails() {
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

            foreach (var root in roots) {
                var comps = root.GetComponentsInChildren<Component>(true);
                var typeCounts = new Dictionary<string, int>();

                foreach (var c in comps) {
                    if (c == null) continue;
                    var t = c.GetType().FullName ?? "Unknown";
                    if (!typeCounts.ContainsKey(t))
                        typeCounts[t] = 0;
                    typeCounts[t]++;
                }

                var sb = new StringBuilder();
                sb.AppendLine($"== {root.name} ==");
                foreach (var kvp in typeCounts)
                    sb.AppendLine($"  {kvp.Key} ×{kvp.Value}");

                MelonLogger.Msg(sb.ToString());
            }
        }

        public static void LogVehiclesAdvanced() {
            var all = GameObject.FindObjectsOfType<GameObject>(true);
            int count = 0;

            foreach (var go in all) {
                // Heuristic: vehicles have Rigidbody + multiple colliders + many children
                var rb = go.GetComponent<Rigidbody>();
                if (rb && go.GetComponentsInChildren<Collider>().Length > 5)
                {
                    count++;
                    MelonLogger.Msg(
                        $"[VehicleGuess] {go.name} | Rigidbodies: {go.GetComponentsInChildren<Rigidbody>().Length} | Colliders: {go.GetComponentsInChildren<Collider>().Length}");
                }
            }

            MelonLogger.Msg($"[VehicleGuess] Total possible vehicles: {count}");
        }

        public static void LogControlAssignments() {
            var controllers = GameObject.FindObjectsOfType<MonoBehaviour>()
                .Where(x => x.GetType().Name.Contains("Controller") ||
                            x.GetType().Name.Contains("Input"))
                .ToList();

            foreach (var c in controllers)
            {
                var fields = c.GetType().GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                foreach (var f in fields)
                {
                    if (f.FieldType.Name.Contains("Vehicle") || f.FieldType.Name.Contains("Tank"))
                    {
                        var value = f.GetValue(c) as UnityEngine.Object;
                        MelonLogger.Msg($"[Control] {c.GetType().Name}.{f.Name} = {value?.name ?? "null"}");
                    }
                }
            }
            var player = GameObject.Find("Player")?.GetComponent("Sprocket.PlayerControl.Player");
            if (player != null)
            {
                foreach (var field in player.GetType().GetFields(
                             System.Reflection.BindingFlags.Public |
                             System.Reflection.BindingFlags.NonPublic |
                             System.Reflection.BindingFlags.Instance))
                {
                    if (field.FieldType.Name.Contains("Vehicle"))
                    {
                        var vehicle = field.GetValue(player) as UnityEngine.Object;
                        MelonLogger.Msg($"[Assignment Check] Player field {field.Name} = {vehicle?.name ?? "null"}");
                    }
                }
            }

        }

        public static void LogSpawners()
        {
            var spawners = GameObject.FindObjectsOfType<MonoBehaviour>(true)
                .Where(x => x.GetType().Name.Contains("Spawn") ||
                            x.GetType().Name.Contains("Factory") ||
                            x.GetType().Name.Contains("Manager"));

            foreach (var s in spawners)
                MelonLogger.Msg($"[Spawner] {s.name} ({s.GetType().FullName})");


            foreach (var s in spawners)
            {
                var methods = s.GetType().GetMethods(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance
                ).Where(m => m.Name.Contains("Spawn") || m.Name.Contains("Create") || m.Name.Contains("Instantiate"));

                foreach (var m in methods)
                    MelonLogger.Msg($"[Spawner Method] {s.name}.{m.Name}");
            }

            var allMono = GameObject.FindObjectsOfType<MonoBehaviour>(true);
            foreach (var mb in allMono)
            {
                var typeName = mb.GetType().Name;
                if (typeName.Contains("Spawn") || typeName.Contains("Factory") || typeName.Contains("Manager"))
                {
                    foreach (var method in mb.GetType().GetMethods(
                                 System.Reflection.BindingFlags.Public |
                                 System.Reflection.BindingFlags.NonPublic |
                                 System.Reflection.BindingFlags.Instance))
                    {
                        if (method.Name.Contains("Spawn"))
                        {
                            MelonLogger.Msg($"[Spawner Method] {mb.name}.{method.Name}");
                        }
                    }
                }
            }
        }

        public static void LogPlayerLinks()
        {
            var player = GameObject.Find("Player");
            if (player == null)
            {
                MelonLogger.Warning("No Player object found.");
                return;
            }

            MelonLogger.Msg($"[PlayerLinks Deep] Components on {player.name}:");

            var comps = player.GetComponents<MonoBehaviour>();
            foreach (var mb in comps)
            {
                if (mb == null) continue;

                try
                {
                    // Safe IL2CPP name fetch
                    var type = mb.GetIl2CppType();
                    var fullName = type?.FullName ?? mb.GetType().FullName;
                    MelonLogger.Msg($"  {fullName}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"  (Failed to get type name: {ex.Message})");
                }
            }
        }
        
        public static void LogPlayerFields() {
            var player = GameObject.Find("Player");
            if (player == null)
            {
                MelonLogger.Warning("No Player object found.");
                return;
            }

            var comp = player.GetComponent("Sprocket.PlayerControl.Player");
            if (comp == null)
            {
                MelonLogger.Warning("Player component not found!");
                return;
            }

            MelonLogger.Msg("[PlayerFieldsSafe] Checking visible fields/properties:");

            var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance;

            // Fields
            foreach (var f in comp.GetType().GetFields(flags))
            {
                object val = null;
                try { val = f.GetValue(comp); } catch { }
                if (val is UnityEngine.Object uo)
                    MelonLogger.Msg($"  Field {f.Name} = {uo.name}");
                else
                    MelonLogger.Msg($"  Field {f.Name} = {val}");
            }

            // Properties
            foreach (var p in comp.GetType().GetProperties(flags))
            {
                object val = null;
                try { val = p.GetValue(comp); } catch { }
                if (val is UnityEngine.Object uo)
                    MelonLogger.Msg($"  Prop {p.Name} = {uo.name}");
                else
                    MelonLogger.Msg($"  Prop {p.Name} = {val}");
            }
        }
        
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
    }
}