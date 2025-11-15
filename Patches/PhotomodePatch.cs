using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppSprocket;
using Il2CppSprocket.Gameplay;
using Il2CppSprocket.VehicleControl;
using MelonLoader;
using SprocketMultiplayer.Unused;
using UnityEngine;
using UnityEngine.InputSystem;


namespace SprocketMultiplayer.Patches {
    [HarmonyPatch]
    [Disabled]
    public static class PhotomodePatch {
        static IEnumerable<MethodBase> TargetMethods() {
            var targets = new[] {
                AccessTools.Method(typeof(ScenarioGameState), "Update"),
                AccessTools.Method(typeof(VehicleEditorScenarioGameState), "Update"),
                AccessTools.Method(typeof(MissionScenarioGameState), "Update"),
            };

            foreach (var m in targets) {
                if (m == null) {
                    MelonLogger.Warning("[PhotomodePatch] Failed to locate one of the target methods!");
                    continue;
                }

                MelonLogger.Msg($"[PhotomodePatch] Targeting {m.DeclaringType.FullName}.{m.Name}");
                yield return m;
            }
        }

        static bool Prefix(object __instance) {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsActiveMultiplayer)
                return true;

            MelonLogger.Msg($"[PHOTOMODE PATCH] Hit on {__instance.GetType().Name}.Update() - MP ACTIVE");

            var field = AccessTools.Field(__instance.GetType(), "photomodeRequested");
            if (field == null)
                return true;

            bool requested = (bool)field.GetValue(__instance);
            if (!requested)
                return true;

            MelonLogger.Warning("PHOTOMODE BLOCKED - Multiplayer session active!");
            field.SetValue(__instance, false);
            return false;
        }
    }
    
    [HarmonyPatch]
    [Disabled]
    public static class PhotomodeInputPatch {
        static IEnumerable<MethodBase> TargetMethods() {
            var methods = new[] {
                AccessTools.Method("PlayerInput:Update"),
                AccessTools.Method("VehicleControlPlayerState:Update"),
                AccessTools.Method("VehicleDesignerPlayerState:Update")
            };

            foreach (var m in methods) {
                if (m != null) {
                    MelonLogger.Msg($"Targeting {m.DeclaringType?.FullName}.{m.Name}");
                    yield return m;
                } else {
                    MelonLogger.Warning("Skipped null method target");
                }
            }
        }

        [HarmonyPrefix]
        static bool Prefix() {
            if (!NetworkManager.Instance?.IsActiveMultiplayer ?? true) return true;

            if (Keyboard.current?.pKey?.wasPressedThisFrame == true) {
                MelonLogger.Warning("Photomode disabled: User is in Host or Client state.");
                return false; // Block entire Update
            }
            return true;
        }
    }
}
