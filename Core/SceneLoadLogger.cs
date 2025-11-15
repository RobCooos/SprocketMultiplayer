using System;
using HarmonyLib;
using MelonLoader;

[HarmonyPatch(typeof(UnityEngine.SceneManagement.SceneManager), "LoadScene", new Type[] { typeof(string) })]
public class SceneLoadLogger {
    [HarmonyPrefix]
    public static void OnLoadScene(string sceneName) {
        MelonLogger.Msg($"[SceneLoad] Requested scene: {sceneName}");

        // Print stack trace so we know who requested the scene
        MelonLogger.Msg(Environment.StackTrace);
        
    }
}