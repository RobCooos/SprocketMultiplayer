using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2CppTMPro;
using System.Linq;
using UnityEngine.Events;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using System.Collections;

namespace SprocketMultiplayer.Patches {
    [HarmonyPatch(typeof(Il2CppSprocket.MainMenu), "SceneStart")]
    public static class MenuPatch {
        private static bool buttonSpawned = false;

        [HarmonyPostfix]
        public static void Postfix(Il2CppSprocket.MainMenu __instance, Il2CppSystem.Threading.CancellationToken ct) {
            if (buttonSpawned) {
                MelonLogger.Msg("[MenuPatch] Multiplayer button already spawned, skipping.");
                return;
            }
            var menuButtonsParent = GameObject.Find("Main Menu/Menu Panel/Content/Menu Buttons")?.transform;
            if (menuButtonsParent == null) {
                MelonLogger.Error("[MenuPatch] Could not find 'Menu Buttons'.");
            }
            
            // Start coroutine to inject button (single-frame delay to ensure UI is ready)
            MelonCoroutines.Start(InjectButtonAfterDelay(__instance.gameObject));
        }

        private static IEnumerator InjectButtonAfterDelay(GameObject mainMenuObject) {
            // Wait one frame to ensure UI is fully initialized
            yield return new WaitForSeconds(0.05f); 
            yield return new WaitForSeconds(0.05f);


            // Find the correct parent for menu buttons
            var menuButtonsParent = GameObject.Find("Main Menu/Menu Panel/Content/Menu Buttons")?.transform;

            if (menuButtonsParent == null) {
                MelonLogger.Error("[MenuPatch] Could not find 'Menu Buttons' parent. Path: 'Main Menu/Menu Panel/Content/Menu Buttons'.");
                LogHierarchy(mainMenuObject); // Debug hierarchy
                yield break;
            }

            // Log all children for debugging
            MelonLogger.Msg($"[MenuPatch] Found parent 'Menu Buttons' with {menuButtonsParent.childCount} children:");
            for (int i = 0; i < menuButtonsParent.childCount; i++) {
                var child = menuButtonsParent.GetChild(i);
                var button = child.GetComponent<Button>();
                var text = child.GetComponentInChildren<TextMeshProUGUI>(true)?.text ?? child.GetComponentInChildren<Text>(true)?.text ?? "No text";
                MelonLogger.Msg($"[MenuPatch] Child {i}: {child.name}, Button: {(button != null ? "Yes" : "No")}, Text: {text}");
            }

            // Pick any menu item as template
            var template = menuButtonsParent.GetChild(0).gameObject; // just pick the first one
            var mpButton = Object.Instantiate(template, menuButtonsParent);
            mpButton.name = "MultiplayerButton";
            
            // Change the text
            var tmp = mpButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
                tmp.text = "Multiplayer";
            else {
                var legacyText = mpButton.GetComponentInChildren<Text>(true);
                if (legacyText != null)
                    legacyText.text = "Multiplayer";
                else
                    MelonLogger.Warning("[MenuPatch] No Text component found on template.");
            }


            // move it above quit or at the end
            int quitIndex = -1;
            for (int i = 0; i < menuButtonsParent.childCount; i++) {
                var child = menuButtonsParent.GetChild(i).gameObject;
                if (child.name.ToLower().Contains("quit")) {
                    quitIndex = i;
                    break;
                }
            }
            if (quitIndex >= 0) mpButton.transform.SetSiblingIndex(quitIndex);
        }

        private static void InjectMultiplayerButton(Button template) {
            if (template == null || template.transform.parent == null) {
                MelonLogger.Error("[MenuPatch] Invalid template or parent.");
                return;
            }

            var parent = template.transform.parent;

            // Avoid duplicates
            if (parent.Find("MultiplayerButton") != null) {
                MelonLogger.Msg("[MenuPatch] Multiplayer button already exists in hierarchy.");
                buttonSpawned = true;
                return;
            }

            // Clone the template button
            var mpButton = Object.Instantiate(template, parent);
            mpButton.name = "MultiplayerButton";

            // Set button text (support both TextMeshPro and legacy Text)
            var tmp = mpButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) {
                tmp.text = "Multiplayer";
            }
            else {
                var legacyText = mpButton.GetComponentInChildren<Text>(true);
                if (legacyText != null) {
                    legacyText.text = "Multiplayer";
                }
                else {
                    MelonLogger.Warning("[MenuPatch] No TextMeshProUGUI or Text component found on button.");
                }
            }

            // Position the button above the Quit button, if present
            var quitButton = parent.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(b => b.name.ToLower().Contains("quit") ||
                                    (b.GetComponentInChildren<TextMeshProUGUI>(true)?.text?.ToLower().Contains("quit") ?? false) ||
                                    (b.GetComponentInChildren<Text>(true)?.text?.ToLower().Contains("quit") ?? false));
            int siblingIndex = quitButton != null ? quitButton.transform.GetSiblingIndex() : template.transform.GetSiblingIndex() + 1;
            mpButton.transform.SetSiblingIndex(siblingIndex);

            // Set up the onClick listener
            mpButton.onClick = new Button.ButtonClickedEvent();
            if (!TryAddIL2CPPListener(mpButton, OnMPPressed)) {
                try {
                    mpButton.onClick.AddListener((UnityAction)OnMPPressed);
                }
                catch (System.Exception ex) {
                    MelonLogger.Error($"[MenuPatch] Failed to add onClick listener: {ex.Message}");
                }
            }

            buttonSpawned = true;

            // Force layout rebuild
            var parentRect = parent.GetComponent<RectTransform>();
            if (parentRect != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
                Canvas.ForceUpdateCanvases();
            }

            MelonLogger.Msg("[MenuPatch] ✅ Multiplayer button injected successfully.");
        }

        private static bool TryAddIL2CPPListener(Button button, System.Action handler) {
            try {
                var convertedDelegate = DelegateSupport.ConvertDelegate<UnityAction>(handler);
                if (convertedDelegate != null) {
                    button.onClick.AddListener(convertedDelegate);
                    return true;
                }
                return false;
            }
            catch {
                return false;
            }
        }

        private static void OnMPPressed() {
            MelonLogger.Msg("[MenuPatch] Multiplayer button pressed!");
            // TODO: Implement multiplayer lobby UI or networking flow
        }

        // Debug utility to log the scene hierarchy
        private static void LogHierarchy(GameObject root) {
            MelonLogger.Msg("[MenuPatch] Scene Hierarchy Debug:");
            if (root == null) {
                MelonLogger.Error("[MenuPatch] Root GameObject is null.");
                return;
            }
            LogGameObject(root, 0);
        }

        private static void LogGameObject(GameObject go, int depth) {
            string indent = new string(' ', depth * 2);
            MelonLogger.Msg($"{indent}- {go.name} (Active: {go.activeInHierarchy})");
            foreach (Transform child in go.transform) {
                LogGameObject(child.gameObject, depth + 1);
            }
        }
    }
}