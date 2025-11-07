using System;
using HarmonyLib;
using Il2CppSprocket.CustomBattles;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

namespace SprocketMultiplayer.Patches {
    [HarmonyPatch(typeof(Il2CppSprocket.CustomBattles.CustomBattlesMain), "Initiate")]
    public static class CustomBattle {
        [HarmonyPostfix]
        static void Postfix(Il2CppSprocket.CustomBattles.CustomBattlesMain __instance) {
            MelonLogger.Msg("CustomBattleCreation loaded — applying multiplayer modifications...");

            try {
                var canvas = GameObject.Find("Canvas");
                if (canvas == null) {
                    MelonLogger.Warning("Canvas not found in CustomBattleCreation scene!");
                    return;
                }

                // find start button
                var startButton = GameObject.Find("StartBattleButton");
                if (startButton != null) {
                    var text = startButton.GetComponentInChildren<TMP_Text>();
                    if (text != null)
                        text.text = "Host Multiplayer Lobby";
                }

                // disable local-only settings
                var localSettings = GameObject.Find("LocalOnlyLabel");
                if (localSettings != null)
                    localSettings.SetActive(false);

                // can now inject multiplayer setup UI here
                MultiplayerUI.Inject(canvas);
            }
            catch (Exception ex) {
                MelonLogger.Error($"Error patching CustomBattleCreation: {ex}");
            }
        }
    }
    
    public static class MultiplayerUI {
        public static void Inject(GameObject canvas) {
            var multiplayerText = new GameObject("MultiplayerText");
            multiplayerText.transform.SetParent(canvas.transform, false);

            var rect = multiplayerText.AddComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0, 200);

            var tmp = multiplayerText.AddComponent<TextMeshProUGUI>();
            tmp.text = "Multiplayer Mode Active";
            tmp.fontSize = 36;
            tmp.alignment = TextAlignmentOptions.Center;

            MelonLogger.Msg("Injected Multiplayer UI into CustomBattleCreation scene.");
        }
    }
}

// This will be used later.
// or not, I may as well delete it