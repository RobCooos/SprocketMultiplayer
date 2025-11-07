using HarmonyLib;
using Il2CppSprocket;
using Il2CppSprocket.Gameplay;
using MelonLoader;
using SprocketMultiplayer.Core;

namespace SprocketMultiplayer.Patches
{
    [HarmonyPatch(typeof(ScenarioGameState), nameof(ScenarioGameState.Update))]
    public static class PhotomodePatch
    {
        static bool Prefix(ScenarioGameState __instance)
        {
            // Find the private field "photomodeRequested" on the actual instance type
            var field = AccessTools.Field(__instance.GetType(), "photomodeRequested");
            if (field == null) return true;

            bool requested = (bool)field.GetValue(__instance);
            if (!requested) return true;

            if (!PhotomodeBlock.CanUsePhotomode)
            {
                MelonLogger.Warning("[Sprocket Multiplayer] Photomode is disabled in Multiplayer mode.");
                field.SetValue(__instance, false);
                return false; // Skip original Update to prevent transition
            }

            return true;
        }
    }
}