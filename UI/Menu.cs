using System;
using HarmonyLib;
using MelonLoader;
using System.Collections;
using Il2CppSprocket;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;                 
using Steamworks;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Il2CppSystem;
using SprocketMultiplayer;
using SprocketMultiplayer.Patches;
using Exception = System.Exception;

namespace SprocketMultiplayer.UI {
    [HarmonyPatch(typeof(MainMenu), "SceneStart")]
    public static class Menu {
        [HarmonyPostfix, HarmonyPatch(typeof(MainMenu), "SceneStart")]
        public static void Postfix() {
            MelonLogger.Msg("[Sprocket Multiplayer] Main Menu detected — waiting for UI...");
            MelonCoroutines.Start(WaitForUI());
        }
        [HarmonyPostfix, HarmonyPatch(typeof(SceneManager), "Internal_SceneLoaded")]
        public static void SceneChanged(Scene scene, LoadSceneMode mode) {
            if (scene.name != "MainMenu")
                uiInitialized = false;
        }
        
        private static IEnumerator WaitForUI() {
            yield return new WaitForSeconds(1f);
            if (uiInitialized) yield break;
            uiInitialized = true;

            // wait for the menu panel
            GameObject menuRoot = null;
            while (menuRoot == null) {
                menuRoot = GameObject.Find("Menu Panel");
                yield return null;
            }

            // find the button
            var menuButtons = menuRoot.transform.Find("Content/Menu Buttons");
            if (menuButtons == null) {
                MelonLogger.Warning("Menu Buttons parent not found!");
                yield break;
            }

            GameObject scenarioButton = null;
            foreach (var child in menuButtons.GetComponentsInChildren<UnityEngine.Transform>(true)) {
                var tmp = child.Find("Text (TMP)")?.GetComponent<TMP_Text>();
                if (tmp != null && tmp.text.IndexOf("scenarios", System.StringComparison.OrdinalIgnoreCase) >= 0) {
                    scenarioButton = child.gameObject;
                    tmp.text = "Multiplayer";
                    break;
                }
            }

            if (scenarioButton == null) {
                MelonLogger.Warning("Scenarios button not found, aborting UI modifications...");
                yield break;
            }

            // Log components
            MelonLogger.Msg("=== Components on Multiplayer button ===");
            foreach (var c in scenarioButton.GetComponents<Component>())
                MelonLogger.Msg($" - {c.GetType().FullName}");
            MelonLogger.Msg("=======================================================");

            // ================ CASE 1: standard UnityEngine.UI.Button ================
            var button = scenarioButton.GetComponent<Button>();
            if (button != null) {

                // Il2Cpp-compatible listener
                button.onClick.AddListener(
                    (UnityAction)delegate { MenuActions.OnMultiplayerClick(); }
                );

                button.interactable = true;
                MelonLogger.Msg("Overridden UnityEngine.UI.Button.onClick for Multiplayer.");
            }
            else {
                // ================ CASE 2: custom click handling (EventTrigger) ================
                if (scenarioButton.GetComponent<HandleClicks>() == null)
                    scenarioButton.AddComponent<HandleClicks>();
                else {
                    if (scenarioButton.GetComponent<HandleClicks>() == null)
                        scenarioButton.AddComponent<HandleClicks>();

                    // FORCE RAYCAST + SIZE
                    var img = scenarioButton.GetComponent<Image>() ?? scenarioButton.AddComponent<Image>();
                    img.color = Color.clear;
                    img.raycastTarget = true;

                    var rect = scenarioButton.GetComponent<RectTransform>();
                    if (rect != null && rect.sizeDelta == Vector2.zero)
                        rect.sizeDelta = new Vector2(300, 60);

                    var cg = scenarioButton.GetComponent<CanvasGroup>();
                    if (cg != null) cg.blocksRaycasts = true;

                    MelonLogger.Msg("HandleClicks + raycast + size + CanvasGroup forced.");
                }

                MelonLogger.Msg("Added HandleClicks component for Multiplayer interception.");
            }

            MelonLogger.Msg("Multiplayer button modified successfully.");
        }

        private static bool uiInitialized;
        
        
        //  Custom click handler
        public class HandleClicks : MonoBehaviour {
            private bool clicked = false;

            private void Awake() {
                // guarantee raycast target
                var img = GetComponent<Image>();
                if (img == null) {
                    img = gameObject.AddComponent<Image>();
                    img.color = Color.clear;
                }
                img.raycastTarget = true;

                // wipe any old EventTriggers (prevents original click)
                foreach (var et in GetComponents<EventTrigger>()) {
                    et.triggers.Clear();
                    
                }
                foreach (var mb in GetComponents<MonoBehaviour>()) {
                    var handler = mb.TryCast<IPointerClickHandler>();
                    if (handler != null && mb != this) {
                        MelonLogger.Msg($"Destroying original IPointerClickHandler: {mb.GetType().FullName}");
                        Destroy(mb);
                    }
                }


                // wipe any other IPointerClickHandler implementations
                foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true)) {
                    var handler = mb.TryCast<IPointerClickHandler>();
                    if (handler != null && !(mb is EventTrigger) && mb != this) {
                        MelonLogger.Msg($"Destroying original IPointerClickHandler: {mb.GetType().FullName}");
                        Destroy(mb);
                    }
                }
                // set up EventTrigger (PointerClick) that MP uses
                var trigger = GetComponent<EventTrigger>() ?? gameObject.AddComponent<EventTrigger>();
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown }; 
                
                entry.callback.AddListener(
                    (UnityAction<BaseEventData>)delegate { OnClicked(); }
                );

                trigger.triggers.Add(entry);
                MelonLogger.Msg("EventTrigger set up for PointerClick.");
            }

            private void OnClicked() {
                if (clicked) return;
                clicked = true;
                MelonLogger.Msg("Multiplayer button clicked – launching lobby.");
                MenuActions.OnMultiplayerClick();
            }
            
        }
    }
}
    
    public static class MenuActions {
        public static void OnMultiplayerClick() {
            MelonLogger.Msg("Getting Steam Nickname...");
            string nickname = GetSteamNickname();
            MelonLogger.Msg($"Nickname set: {nickname}, proceeding.");
            LaunchLobby();
        }

    public static string GetSteamNickname() {
        try {
            if (!SteamAPI.Init()) {
                MelonLogger.Warning("Steam API not initialized. Using fallback nickname.");
                return "Player" + UnityEngine.Random.Range(1000, 9999);
            }

            string nickname = SteamFriends.GetPersonaName();

            if (string.IsNullOrEmpty(nickname))
                nickname = "Player" + UnityEngine.Random.Range(1000, 9999);

            return nickname;
        }
        catch {
            // Fallback if anything goes wrong
            return "Player" + UnityEngine.Random.Range(1000, 9999);
        }
    }
    
    private static void LaunchLobby() {
        MelonLogger.Msg("Launching Multiplayer Lobby...");

        if (NetworkManager.Instance.IsClient) {
            MelonLogger.Msg("Client detected — waiting for host lobby state.");
            return;
        }

        try {
            GameObject mainMenuGO = GameObject.Find("Menu Panel");
            Lobby.Instantiate(mainMenuGO);
            MelonLogger.Msg("Loading...");
        }
        catch (Exception ex) {
            MelonLogger.Error($"Failed to load in: {ex}");
        }
    }
}
