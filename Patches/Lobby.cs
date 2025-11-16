using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using SprocketMultiplayer.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Il2CppSystem;
using UnityEngine.SceneManagement;

namespace SprocketMultiplayer.Patches {
    public static class Lobby {
        public static GameObject CanvasGO;
        public static GameObject Panel;
        public static List<TextMeshProUGUI> PlayerSlots = new List<TextMeshProUGUI>();

        private static GameObject mainMenu; // reference to main menu
        private static GameObject centerPanel;
        private static TextMeshProUGUI headerTMP;

        private const int MAX_PLAYERS = 4;
        private const string EMPTY = "Empty Slot";

        public static void Instantiate(GameObject mainMenuGO) {
            if (!NetworkManager.Instance.IsHost && !NetworkManager.Instance.IsClient) {
                MelonLogger.Msg("Cannot create or join lobby: Not connected.");
                SceneManager.LoadScene("Main Menu");
                if (mainMenu != null) mainMenu.SetActive(true);
                return;
            }

            if (Panel != null) {
                MelonLogger.Msg("Lobby already instantiated.");
                return;
            }

            mainMenu = mainMenuGO;
            if (mainMenu != null)
                mainMenu.SetActive(false); // hide main menu

            // Try to disable scenario select if present
            GameObject scenarioRoot = GameObject.Find("ScenarioSelectScreen");
            if (scenarioRoot) scenarioRoot.SetActive(false);

            // Create Canvas
            CanvasGO = new GameObject("LobbyCanvas");
            var canvas = CanvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasGO.AddComponent<CanvasScaler>();
            CanvasGO.AddComponent<GraphicRaycaster>();
            CanvasGO.transform.SetAsLastSibling();
            AddFooterText(CanvasGO);

            // Inject UI panels
            Inject(CanvasGO);

            // Set header text (host or local nickname)
            string nickname = "Host";
            try {
                nickname = MenuActions.GetSteamNickname();
            } 
            catch { 
                MelonLogger.Warning("Could not fetch Steam nickname; using fallback."); 
            }

            if (headerTMP != null)
                headerTMP.text = $"{nickname}'s Lobby";

            // Host: auto-add self to slot 0
            if (NetworkManager.Instance.IsHost) {
                TryAddPlayer(nickname);
            } 
            else 
            {
                // Client: wait for server lobby state
                MelonLogger.Msg("Client created lobby UI; waiting for server lobby state.");
            }
        }


        private static void Inject(GameObject canvasGO)
        {
            // ROOT PANEL (central)
            Panel = new GameObject("LobbyRootPanel");
            Panel.transform.SetParent(canvasGO.transform, false);
            var rect = Panel.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(900, 500); // smaller central panel
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            var bg = Panel.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.7f);

            // LEFT PANEL: Tank Selection
            var leftPanel = CreatePanel("LeftPanel", Panel.transform, new Vector2(0.25f, 1f), new Vector2(0, 0));
            AddPlaceholderText(leftPanel.transform, "Tank Selection Placeholder");

            // CENTER PANEL: Connected Players
            centerPanel = CreatePanel("CenterPanel", Panel.transform, new Vector2(0.5f, 1f), new Vector2(0.25f, 0));
            SetupCenterPanel(centerPanel.transform);

            // RIGHT PANEL: Map / Settings
            var rightPanel = CreatePanel("RightPanel", Panel.transform, new Vector2(0.25f, 1f), new Vector2(0.75f, 0));
            AddPlaceholderText(rightPanel.transform, "Map / Settings Placeholder");
        }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 widthPercent, Vector2 anchorMin)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();

            rect.anchorMin = anchorMin;
            rect.anchorMax = new Vector2(anchorMin.x + widthPercent.x, 1f);

            // Separation between panels
            rect.offsetMin = new Vector2(10, 10);
            rect.offsetMax = new Vector2(-10, -10);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 0.6f);

            return go;
        }

        private static void SetupCenterPanel(Transform parent)
        {
            // Header
            var headerGO = new GameObject("LobbyHeader");
            headerGO.transform.SetParent(parent, false);
            headerTMP = headerGO.AddComponent<TextMeshProUGUI>();
            headerTMP.fontSize = 28;
            headerTMP.alignment = TextAlignmentOptions.Center;
            headerTMP.color = Color.white;
            var headerRect = headerTMP.rectTransform;
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0, 40);
            headerRect.anchoredPosition = new Vector2(0, -10);

            // Container for slots
            var slotsContainer = new GameObject("SlotsContainer");
            slotsContainer.transform.SetParent(parent, false);
            var scRect = slotsContainer.AddComponent<RectTransform>();
            scRect.anchorMin = new Vector2(0, 0);
            scRect.anchorMax = new Vector2(1, 1);
            scRect.offsetMin = new Vector2(10, 10);
            scRect.offsetMax = new Vector2(-10, -60); // keep room for the header
            scRect.pivot = new Vector2(0.5f, 0.5f);

            // Add VerticalLayoutGroup so slots stack nicely
            var vlg = slotsContainer.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 8;
            vlg.padding = new RectOffset(6, 6, 6, 6);

            // Add ContentSizeFitter so the layout sizes properly
            var csf = slotsContainer.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Create MAX_PLAYERS slots
            PlayerSlots.Clear();
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                var slot = CreatePlayerSlot($"Player{i + 1}: {EMPTY}");
                slot.transform.SetParent(slotsContainer.transform, false);

                // store TMP component
                var tmp = slot.GetComponentInChildren<TextMeshProUGUI>();
                PlayerSlots.Add(tmp);
            }
        }

        private static GameObject CreatePlayerSlot(string name)
        {
            var go = new GameObject("PlayerSlot");
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, 48);

            // Background
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);

            // Layout element
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = 48;
            layout.flexibleWidth = 1;

            // Text child (nickname + ping)
            var textGO = new GameObject("SlotText");
            textGO.transform.SetParent(go.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = name; // initial: "Player1: Empty Slot"
            tmp.fontSize = 20;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var textRect = tmp.rectTransform;
            textRect.anchorMin = new Vector2(0, 0);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.offsetMin = new Vector2(8, 6);
            textRect.offsetMax = new Vector2(-8, -6);

            return go;
        }

        /// <summary>
        /// Tries to add a player to the first empty slot.
        /// Returns true if succeeded, false if lobby full or name already present.
        /// </summary>
        public static bool TryAddPlayer(string nickname)
        {
            if (string.IsNullOrEmpty(nickname)) return false;

            // Check if already present
            for (int i = 0; i < PlayerSlots.Count; i++)
            {
                var text = PlayerSlots[i].text;
                if (text.Contains($": {nickname}")) // simple check
                    return false;
            }

            // Count current players
            int filled = 0;
            for (int i = 0; i < PlayerSlots.Count; i++)
            {
                if (PlayerSlots[i].text != $"Player{i + 1}: {EMPTY}")
                    filled++;
            }

            if (filled >= MAX_PLAYERS) return false;

            // Find first empty slot
            for (int i = 0; i < PlayerSlots.Count; i++)
            {
                if (PlayerSlots[i].text == $"Player{i + 1}: {EMPTY}")
                {
                    PlayerSlots[i].text = $"Player{i + 1}: {nickname}";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Removes a player by nickname, turning the slot back into an empty slot.
        /// </summary>
        public static void RemovePlayer(string nickname)
        {
            if (string.IsNullOrEmpty(nickname)) return;

            for (int i = 0; i < PlayerSlots.Count; i++)
            {
                var expected = $"Player{i + 1}: {nickname}";
                if (PlayerSlots[i].text == expected)
                {
                    PlayerSlots[i].text = $"Player{i + 1}: {EMPTY}";
                    return;
                }
            }
        }
        
        public static void OnPlayerConnected(string nickname) {
            if (!TryAddPlayer(nickname)) {
                MelonLogger.Msg($"Cannot add {nickname}: Lobby full or already present.");
            }
        }

        public static void OnPlayerDisconnected(string nickname) {
            RemovePlayer(nickname);
        }
        
        public static void UpdatePlayerPing(string nickname, int ping)
        {
            for (int i = 0; i < PlayerSlots.Count; i++) {
                var text = PlayerSlots[i].text;
                if (text.Contains($": {nickname}")) {
                    PlayerSlots[i].text = $"Player{i+1}: {nickname}, {ping} ms";
                    break;
                }
            }
        }

        private static void AddPlaceholderText(Transform parent, string text)
        {
            var textGO = new GameObject("PlaceholderText");
            textGO.transform.SetParent(parent, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 28;
            tmp.alignment = TextAlignmentOptions.Center;

            var rect = tmp.rectTransform;
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void AddFooterText(GameObject canvasGO)
        {
            // Parent object for text + background
            var footerGO = new GameObject("FooterText");
            footerGO.transform.SetParent(canvasGO.transform, false);
            var rect = footerGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, 50); // height above bottom
            rect.sizeDelta = new Vector2(0, 40);

            // Background image
            var bg = footerGO.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.5f); // black with 50% transparency

            // Text
            var textGO = new GameObject("FooterTextTMP");
            textGO.transform.SetParent(footerGO.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = "-> Mod is still in development. Things may break! <-";
            tmp.fontSize = 20;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var textRect = tmp.rectTransform;
            textRect.anchorMin = new Vector2(0, 0);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }
        
        /// <summary>
        /// Replace current slot contents with provided list of nicknames.
        /// The list should represent slots in order; use null or empty for empty slots.
        /// </summary>
        public static void ApplyLobbyState(List<string> nicknames) {
            if (PlayerSlots == null || PlayerSlots.Count == 0) return;
            for (int i = 0; i < PlayerSlots.Count; i++) {
                string text;
                if (i < nicknames.Count && !string.IsNullOrEmpty(nicknames[i]))
                    text = $"Player{i + 1}: {nicknames[i]}";
                else
                    text = $"Player{i + 1}: {EMPTY}";

                PlayerSlots[i].text = text;
            }
        }
        
    }
}
