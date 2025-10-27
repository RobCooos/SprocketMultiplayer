using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using SprocketMultiplayer.Core;

namespace SprocketMultiplayer.Patches {
    [HarmonyPatch]
    public static class PhotoModeBlockPatch {
        static MethodBase TargetMethod() {
            string[] candidateTypeNames = {
                "PhotoModeManager",
                "PhotoMode",
                "PhotoModeController",
                "PhotoModeBehaviour"
            };

            string[] candidateMethodNames = {
                "TogglePhotoMode",
                "Toggle",
                "EnterPhotoMode",
                "SetPhotoMode"
            };

            foreach (string typeName in candidateTypeNames) {
                var t = AccessTools.TypeByName(typeName);
                if (t == null) continue;

                foreach (string methodName in candidateMethodNames) {
                    var m = AccessTools.Method(t, methodName);
                    if (m != null) {
                        MelonLogger.Msg($"[PhotoModeBlock] Found {t.FullName}.{methodName}");
                        return m;
                    }
                }
            }

            MelonLogger.Warning("PhotoModeBlock skipped — No PhotoMode class found.");
            return null;

        }

        static bool Prefix(){
            if (PhotomodeBlock.IsMultiplayer) {
                MelonLogger.Msg("[PhotoModeBlock] Prevented PhotoMode use in MP");
                return false;
            }

            return true;
        }
        
        static bool Prepare() {
            // only apply this patch if multiplayer is enabled
            return SprocketMultiplayer.Core.PhotomodeBlock.IsMultiplayer;
        }

    }
}