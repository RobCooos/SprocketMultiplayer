using System;
using System.Reflection;
using System.Linq;
using MelonLoader;

namespace SprocketMultiplayer.Patches
{
    public static class AssemblyInspector
    {
        public static void InspectVehicleSpawning()
        {
            MelonLogger.Msg("===== INSPECTING Il2CppSprocket.Vehicle.Spawning =====");
            
            try
            {
                // Try to find the assembly
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Il2CppSprocket.Vehicle.Spawning");
                
                if (assembly == null)
                {
                    MelonLogger.Warning("Assembly not found! Searching all assemblies...");
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name.Contains("Sprocket") && 
                            (asm.GetName().Name.Contains("Vehicle") || asm.GetName().Name.Contains("Spawn")))
                        {
                            MelonLogger.Msg($"  Found: {asm.GetName().Name}");
                        }
                    }
                    return;
                }
                
                MelonLogger.Msg($"✓ Found assembly: {assembly.FullName}");
                MelonLogger.Msg("");
                
                // List all types
                MelonLogger.Msg("=== PUBLIC TYPES ===");
                var types = assembly.GetTypes().Where(t => t.IsPublic).ToArray();
                foreach (var type in types)
                {
                    MelonLogger.Msg($"\nClass: {type.Name}");
                    MelonLogger.Msg($"  Namespace: {type.Namespace}");
                    
                    // List public methods
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                        .Where(m => m.DeclaringType == type)
                        .ToArray();
                    
                    if (methods.Length > 0)
                    {
                        MelonLogger.Msg("  Methods:");
                        foreach (var method in methods)
                        {
                            var parameters = string.Join(", ", method.GetParameters()
                                .Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            MelonLogger.Msg($"    {(method.IsStatic ? "static " : "")}{method.ReturnType.Name} {method.Name}({parameters})");
                        }
                    }
                    
                    // List public fields
                    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                    if (fields.Length > 0)
                    {
                        MelonLogger.Msg("  Fields:");
                        foreach (var field in fields)
                        {
                            MelonLogger.Msg($"    {(field.IsStatic ? "static " : "")}{field.FieldType.Name} {field.Name}");
                        }
                    }
                    
                    // List public properties
                    var props = type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                    if (props.Length > 0)
                    {
                        MelonLogger.Msg("  Properties:");
                        foreach (var prop in props)
                        {
                            MelonLogger.Msg($"    {prop.PropertyType.Name} {prop.Name}");
                        }
                    }
                }
                
                MelonLogger.Msg("\n===== INSPECTION COMPLETE =====");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to inspect assembly: {ex}");
            }
        }
        
        public static void InspectVehiclesAssembly()
        {
            MelonLogger.Msg("===== INSPECTING Il2CppSprocket.Vehicles =====");
            
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Il2CppSprocket.Vehicles");
                
                if (assembly == null)
                {
                    MelonLogger.Warning("Il2CppSprocket.Vehicles not found!");
                    return;
                }
                
                MelonLogger.Msg($"✓ Found assembly: {assembly.FullName}");
                
                var types = assembly.GetTypes().Where(t => t.IsPublic).Take(20).ToArray();
                MelonLogger.Msg($"\nFirst 20 public types:");
                foreach (var type in types)
                {
                    MelonLogger.Msg($"  - {type.Name}");
                }
                
                MelonLogger.Msg("\n===== INSPECTION COMPLETE =====");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to inspect Vehicles assembly: {ex}");
            }
        }
        
        public static void SearchForSpawningMethods()
        {
            MelonLogger.Msg("===== SEARCHING FOR SPAWN METHODS =====");
            
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.Contains("Sprocket")) continue;
                
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                            .Where(m => m.Name.ToLower().Contains("spawn") || 
                                       m.Name.ToLower().Contains("instantiate") ||
                                       m.Name.ToLower().Contains("create") && m.Name.ToLower().Contains("vehicle"));
                        
                        foreach (var method in methods)
                        {
                            var parameters = string.Join(", ", method.GetParameters()
                                .Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            MelonLogger.Msg($"{assembly.GetName().Name}.{type.Name}.{method.Name}({parameters})");
                        }
                    }
                }
                catch { }
            }
            
            MelonLogger.Msg("===== SEARCH COMPLETE =====");
        }
    }
}