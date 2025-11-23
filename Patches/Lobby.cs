using System.Collections;
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
using Exception = System.Exception;

namespace SprocketMultiplayer.Patches {
    public static class Lobby {
        public static GameObject CanvasGO;
        public static GameObject Panel;
        private static GameObject mainMenu; // reference to main menu
        private static GameObject centerPanel;
        
        private static TextMeshProUGUI headerTMP;
        public static List<TextMeshProUGUI> PlayerSlots = new List<TextMeshProUGUI>();
        
        private const int MAX_PLAYERS = 4;
        private const string EMPTY = "Empty Slot";
        
        private static string hostLobbyName = null;
        public static bool LobbyUIReady = false;
        
        private static List<string> pendingLobbyState = null;
        private static bool LobbyUICreated = false;
        private static bool IsReady() {
            return TMP_Settings.instance != null &&
                   TMP_Settings.defaultFontAsset != null;
        }

        
        // ================= ENTRY POINT =================
        private static IEnumerator WaitForMenuAndRetry() {
            MelonLogger.Msg("Waiting for Menu Panel...");
            GameObject menu;
            // Wait up to some frames for the Menu Panel
            int tries = 0;
            while ((menu = GameObject.Find("Menu Panel")) == null && tries++ < 600) // ~10s at 60fps
                yield return null;
            
            if (menu == null) {
                MelonLogger.Warning("Menu Panel not found after waiting.");
                yield break;
            }

            // Only instantiate if we don't already have Panel
            if (Panel != null) {
                MelonLogger.Msg("Panel already exists after wait; aborting retry.");
                yield break;
            }

            MelonLogger.Msg("Menu Panel found; calling Instantiate(menu).");
            Instantiate(menu);
        }
        
        public static IEnumerator WaitForLobbyCanvasThenCreateUI(List<string> namesFromServer) {
            
            if (LobbyUICreated) yield break;
            LobbyUICreated = true;
            
            // Wait for Menu Panel (client side panel)
            GameObject mainMenuGO = null;
            int tries = 0;

            while (mainMenuGO == null && tries++ < 400) {
                mainMenuGO = GameObject.Find("Menu Panel");
                yield return null;
            }

            if (mainMenuGO == null) {
                MelonLogger.Warning("[Lobby] Menu Panel not found after waiting; aborting.");
                yield break;
            }

            // Wait for TMP to be initialized (avoid native crash)
            int tmpTries = 0;
            while (!IsReady() && tmpTries++ < 600) {
                MelonLogger.Msg($"[Lobby] Waiting for TMP to initialize... ({tmpTries})");
                yield return null;
            }

            if (!IsReady()) {
                MelonLogger.Warning("[Lobby] TMP never became ready; creating UI.");
            }

            // Create the UI (this creates LobbyCanvas)
            Instantiate(mainMenuGO);

            // Apply lobby state
            HandleIncomingLobbyState(namesFromServer);
            MelonLogger.Msg("[Client] Applied lobby state from host (via coroutine).");
            
            LobbyUICreated = false;
        }
        
        public static void Instantiate(GameObject mainMenuGO) {
            if (Panel != null || LobbyUIReady) {
                MelonLogger.Msg("Instantiate: already instantiated -> abort");
                return;
            }

            if (!NetworkManager.Instance.IsHost && !NetworkManager.Instance.IsClient) {
                MelonLogger.Msg("Cannot create or join lobby: Not connected.");
                SceneManager.LoadScene("Main Menu");
                if (mainMenu != null) mainMenu.SetActive(true);
                return;
            }

            if (mainMenuGO == null) {
                MelonLogger.Warning("Menu Panel not found yet, delaying lobby creation...");
                MelonCoroutines.Start(WaitForMenuAndRetry());
                return;
            }

            // Save reference to the actual menu object we got
            mainMenu = mainMenuGO;
            if (mainMenu != null) mainMenu.SetActive(false);

            // create UI elements
            CreateLobbyUI();

            // Set header
            string localNickname = "Player";
            try {
                localNickname = MenuActions.GetSteamNickname();
            }
            catch { 
                MelonLogger.Warning("Could not fetch local Steam nickname for header.");
            }

            if (NetworkManager.Instance.IsHost) {
                headerTMP.text = $"{localNickname}'s Lobby";
                TryAddPlayer(localNickname); // host auto-adds self
            }
            else {
                MelonLogger.Msg("Client created lobby UI; waiting for server lobby state.");
            }
            
            if (pendingLobbyState != null) {
                ApplyLobbyState(pendingLobbyState);
                pendingLobbyState = null;
            }
        }
        
        
        // ================= UI =================
        private static void CreateLobbyUI() {
            try {
                MelonLogger.Msg("Creating lobby UI...");

                if (!IsReady()) {
                    MelonLogger.Warning("[Lobby] CreateLobbyUI: TMP not ready yet — we'll still create canvas but avoid adding TMP components until ready.");
                }

                // Canvas, Panel, Inject, FooterText
                CanvasGO = new GameObject("LobbyCanvas");
                var canvas = CanvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasGO.AddComponent<CanvasScaler>();
                CanvasGO.AddComponent<GraphicRaycaster>();
                CanvasGO.transform.SetAsLastSibling();

                AddFooterText(CanvasGO);
                Inject(CanvasGO);

                LobbyUIReady = true;
                MelonLogger.Msg("[Lobby] UI created and ready.");
            }
            catch(Exception ex) {
                MelonLogger.Error($"[Lobby] CreateLobbyUI CRASH: {ex}");
                // If this catch runs, we at least get a managed exception in log
            }

            if (PlayerSlots == null || PlayerSlots.Count == 0) {
                MelonLogger.Warning("CreateLobbyUI: PlayerSlots unexpectedly empty after creation.");
            }
        }
        
        private static void Inject(GameObject canvasGO) {
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

        private static GameObject CreatePanel(string name, Transform parent, Vector2 widthPercent, Vector2 anchorMin) {
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

        private static void SetupCenterPanel(Transform parent) {
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
            for (int i = 0; i < MAX_PLAYERS; i++) {
                var slot = CreatePlayerSlot($"Player{i + 1}: {EMPTY}");
                slot.transform.SetParent(slotsContainer.transform, false);

                // store TMP component (can be null if fallback occurred)
                var tmp = slot.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp == null) {
                    MelonLogger.Warning($"[Lobby] Slot {i+1}: TextMeshProUGUI is null (using fallback).");
                } else {
                    MelonLogger.Msg($"[Lobby] Slot {i+1}: TMP created OK.");
                }
                PlayerSlots.Add(tmp); // tmp can be null, code elsewhere must handle that
            }

        }
        
        private static GameObject CreatePlayerSlot(string name) {
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

        TextMeshProUGUI tmp = null;

        // Only attempt to add TMP if it's safe to,
        // otherwise it may softlock
        if (IsReady()) {
            try {
                MelonLogger.Msg("[Lobby] Creating TMP for player slot.");
                tmp = textGO.AddComponent<TextMeshProUGUI>();
            } catch (Exception ex) {
                MelonLogger.Error($"[Lobby] AddComponent<TextMeshProUGUI> threw: {ex.Message}");
                tmp = null;
            }
        }

        if (tmp == null) {
            // fallback to UnityEngine.UI.Text to avoid native crashes
            MelonLogger.Warning("[Lobby] TMP not available — using UnityEngine.UI.Text fallback for slot.");
            var fallback = textGO.AddComponent<UnityEngine.UI.Text>();
            fallback.text = name;
            fallback.fontSize = 20;
            fallback.alignment = TextAnchor.MiddleCenter;
            fallback.color = Color.white;
            // If TMP becomes ready later and you want to swap, implement a swap routine (optional).
            // Keep PlayerSlots typed as TMP: we'll keep null entries and guard usage elsewhere.
        } else {
            tmp.text = name; // initial: "Player1: Empty Slot"
            tmp.fontSize = 20;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var textRect = tmp.rectTransform;
            textRect.anchorMin = new Vector2(0, 0);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.offsetMin = new Vector2(8, 6);
            textRect.offsetMax = new Vector2(-8, -6);
        }

        // ensure text GO has RectTransform for layout
        var trect = textGO.GetComponent<RectTransform>();
        if (trect == null) trect = textGO.AddComponent<RectTransform>();
        trect.anchorMin = new Vector2(0, 0);
        trect.anchorMax = new Vector2(1, 1);

        return go;
    }
        
        private static void AddPlaceholderText(Transform parent, string text) {
            var textGO = new GameObject("PlaceholderText");
            textGO.transform.SetParent(parent, false);
            
            MelonLogger.Msg("[Lobby] About to AddComponent<TextMeshProUGUI> on SlotText");
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

        private static void AddFooterText(GameObject canvasGO) {
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
        

        // ================= PLAYER MANAGEMENT =================
        public static bool TryAddPlayer(string nickname) {
            if (string.IsNullOrEmpty(nickname)) return false;

            // Check if already present
            for (int i = 0; i < PlayerSlots.Count; i++) {
                var slot = PlayerSlots[i];
                string text = slot != null ? slot.text : null;
                if (!string.IsNullOrEmpty(text) && text.Contains($": {nickname}"))
                    return false;
            }

            // Count current players
            int filled = 0;
            for (int i = 0; i < PlayerSlots.Count; i++) {
                var slot = PlayerSlots[i];
                string text = slot != null ? slot.text : null;
                if (!string.IsNullOrEmpty(text) && text != $"Player{i + 1}: {EMPTY}")
                    filled++;
            }

            if (filled >= MAX_PLAYERS) return false;

            // Find first empty slot
            for (int i = 0; i < PlayerSlots.Count; i++) {
                var slot = PlayerSlots[i];
                string currentText = slot != null ? slot.text : null;
                if (string.IsNullOrEmpty(currentText) || currentText == $"Player{i + 1}: {EMPTY}") {
                    if (slot != null) slot.text = $"Player{i + 1}: {nickname}";
                    else {
                        // fallback: find the Text component under slot GameObject and set it
                        var slotGO = GameObject.Find($"PlayerSlot");
                    }
                    return true;
                }
            }

            return false;
        }
        
        public static void RemovePlayer(string nickname) {
            if (string.IsNullOrEmpty(nickname)) return;

            for (int i = 0; i < PlayerSlots.Count; i++) {
                var expected = $"Player{i + 1}: {nickname}";
                if (PlayerSlots[i].text == expected) {
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
        
        public static void UpdatePlayerPing(string nickname, int ping) {
            if (PlayerSlots == null) {
                MelonLogger.Msg("[Lobby] UpdatePlayerPing: PlayerSlots is null => abort");
                return;
            }

            for (int i = 0; i < PlayerSlots.Count; i++) {
                var slot = PlayerSlots[i];
                if (slot == null) {
                    // skip null TMPs
                    continue;
                }
                var text = slot.text ?? "";
                if (text.Contains($": {nickname}")) {
                    try {
                        slot.text = $"Player{i+1}: {nickname}, {ping} ms";
                    } catch (Exception ex) {
                        MelonLogger.Error($"[Lobby] Failed to set ping text for slot {i+1}: {ex.Message}");
                    }
                    break;
                }
            }
        }
        
        
        // ================= LOBBY STATE =================
        public static void HandleIncomingLobbyState(List<string> nicknames) {
            // caching if UI isn't ready
            if (!LobbyUIReady || Panel == null || PlayerSlots == null || PlayerSlots.Count == 0) {
                MelonLogger.Msg("[Lobby] UI not ready, caching incoming lobby state.");
                pendingLobbyState = new List<string>(nicknames);
                return;
            }

            // apply
            ApplyLobbyState(nicknames);
        }

        public static void ApplyLobbyState(List<string> nicknames) {
            if (!LobbyUIReady || Panel == null || PlayerSlots == null || PlayerSlots.Count == 0) {
                MelonLogger.Msg("[Lobby] ApplyLobbyState called while UI not ready; caching.");
                pendingLobbyState = new List<string>(nicknames);
                return;
            }

            // clamp nickname list to MAX_PLAYERS
            var safeList = new List<string>();
            for (int i = 0; i < Math.Min(nicknames.Count, PlayerSlots.Count); i++)
                safeList.Add(nicknames[i]);

            // determine host name (first entry) and update header
            if (safeList.Count > 0 && !string.IsNullOrEmpty(safeList[0])) {
                hostLobbyName = safeList[0];
                if (headerTMP != null && !NetworkManager.Instance.IsHost) {
                    headerTMP.text = $"{hostLobbyName}'s Lobby";
                }
            }

            // Fill slots (never exceed PlayerSlots)
            for (int i = 0; i < PlayerSlots.Count; i++) {
                string text;
                if (i < safeList.Count && !string.IsNullOrEmpty(safeList[i]))
                    text = $"Player{i + 1}: {safeList[i]}";
                else
                    text = $"Player{i + 1}: {EMPTY}";

                if (PlayerSlots[i] != null) {
                    try {
                        PlayerSlots[i].text = text;
                    } catch (Exception ex) {
                        MelonLogger.Error($"[Lobby] Failed to set text for slot {i+1}: {ex.Message}");
                    }
                } else {
                    MelonLogger.Warning($"[Lobby] Cannot set text for slot {i+1}: TMP is null.");
                }
            }
        }
        
    }
}
