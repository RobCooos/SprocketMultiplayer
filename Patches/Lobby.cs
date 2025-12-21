using System.Collections;
using System.Collections.Generic;
using System.IO;
using Il2CppSystem;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using SprocketMultiplayer.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Il2CppSystem;
using SprocketMultiplayer.Core;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Exception = System.Exception;

namespace SprocketMultiplayer.Patches {
    public static class Lobby {
        public static GameObject CanvasGO;
        public static GameObject Panel;
        private static GameObject mainMenu;
        private static GameObject centerPanel;
        private static Transform tankScrollContent;
        
        private static TextMeshProUGUI headerTMP;
        public static List<TextMeshProUGUI> PlayerSlots = new List<TextMeshProUGUI>();
        public static TMP_Dropdown mapDropdown;
        private static TextMeshProUGUI mapTextTMP;
        private static TextMeshProUGUI mapText;
        
        private const int MAX_PLAYERS = 4;
        private const string EMPTY = "Empty Slot";
        private static string currentMap = "Railway";
        public static string SelectedTank = null;
        private static string hostLobbyName = null;
        
        public static bool LobbyUIReady = false;
        private static List<string> pendingLobbyState = null;
        private static bool LobbyUICreated = false;
        private static Dictionary<GameObject, string> tankButtonMap = new Dictionary<GameObject, string>();
        private static Dictionary<GameObject, GameObject> tankLabelMap = new Dictionary<GameObject, GameObject>();
        public static Dictionary<string, PlayerInfo> Players = new Dictionary<string, PlayerInfo>();
        private static List<string> pendingPlayers = new List<string>();
        
        public class PlayerInfo {
            public string Tank;
            public int Ping;
        }
        
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
        
        private static void SendMapToClients(string mapName) {
            MelonLogger.Msg($"[Lobby] Broadcasting map: {mapName}");
            if (NetworkManager.Instance.IsHost) {
                NetworkManager.Instance.Send("MAP:" + mapName);
            }
        }
        
        public static void Instantiate(GameObject mainMenuGO)
        {
            if (Panel != null || LobbyUIReady)
            {
                MelonLogger.Msg("Instantiate: already instantiated -> abort");
                return;
            }

            if (!NetworkManager.Instance.IsHost && !NetworkManager.Instance.IsClient)
            {
                MelonLogger.Msg("Cannot create or join lobby: Not connected.");
                SceneManager.LoadScene("Main Menu");
                if (mainMenu != null) mainMenu.SetActive(true);
                return;
            }

            if (mainMenuGO == null)
            {
                MelonLogger.Warning("Menu Panel not found yet, delaying lobby creation...");
                MelonCoroutines.Start(WaitForMenuAndRetry());
                return;
            }
            
            mainMenu = mainMenuGO;
            if (mainMenu != null) mainMenu.SetActive(false);

            // create UI elements
            CreateLobbyUI();
            
            // Set header
            string localNickname = "Player";
            try
            {
                localNickname = MenuActions.GetSteamNickname();
            }
            catch
            {
                MelonLogger.Warning("Could not fetch local Steam nickname for header.");
            }

            if (NetworkManager.Instance.IsHost)
            {
                headerTMP.text = $"{localNickname}'s Lobby";
                TryAddPlayer(localNickname); // host auto-adds self
                // Initial map broadcast
                if (mapDropdown != null && mapDropdown.options.Count > 0)
                {
                    TMP_Dropdown.OptionData firstOption = null;
                    int firstIndex = 0;

                    foreach (var opt in mapDropdown.options)
                    {
                        firstOption = opt;
                        break; // first element
                    }

                    if (firstOption != null)
                    {
                        SendMapToClients(firstOption.text);
                    }
                    else
                    {
                        MelonLogger.Warning("[Lobby] No map option found to send to clients.");
                    }
                }


                if (pendingLobbyState != null)
                {
                    ApplyLobbyState(pendingLobbyState);
                    pendingLobbyState = null;
                }
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
            var leftPanel = CreatePanel("LeftPanel", Panel.transform, new Vector2(0.35f, 1f), new Vector2(0.00f, 0));
            try {
                SetupLeftPanel(leftPanel.transform);
            }
            catch (Exception ex) {
                MelonLogger.Error($"[Lobby] SetupLeftPanel CRASH: {ex}");
                AddPlaceholderText(leftPanel.transform, "Unable to create Tank List for some reason.\nCheck logs for ref.");
            }

            // CENTER PANEL: Connected Players
            centerPanel = CreatePanel("CenterPanel", Panel.transform, new Vector2(0.45f, 1f), new Vector2(0.35f, 0));
            SetupCenterPanel(centerPanel.transform);

            // RIGHT PANEL: Map / Settings
            var rightPanel = CreatePanel("RightPanel", Panel.transform, new Vector2(0.20f, 1f), new Vector2(0.80f, 0));
            SetupRightPanel(rightPanel.transform);
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
        
        private static void CreateTankEntry(TankInfo tank, Transform parent) {
        if (tank == null) {
            MelonLogger.Warning("[Lobby] Cannot create entry for null tank");
            return;
        }

        try {
            MelonLogger.Msg($"[Lobby] Creating entry for tank: {tank.Name}");

            // ROOT
            GameObject go = new GameObject(tank.Name);
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(120, 120);

            // Background
            Image bgImg = go.AddComponent<Image>();
            bgImg.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

            // Tank Image
            if (!string.IsNullOrEmpty(tank.ImagePath) && File.Exists(tank.ImagePath)) {
                try {
                    byte[] bytes = File.ReadAllBytes(tank.ImagePath);
                    Texture2D tex = new Texture2D(2, 2);
                    tex.LoadImage(bytes);

                    GameObject tankImgGO = new GameObject("TankImage");
                    tankImgGO.transform.SetParent(go.transform, false);

                    RectTransform imgRect = tankImgGO.AddComponent<RectTransform>();
                    imgRect.anchorMin = Vector2.zero;
                    imgRect.anchorMax = Vector2.one;
                    imgRect.offsetMin = new Vector2(5, 5);
                    imgRect.offsetMax = new Vector2(-5, -5);

                    Image tankImg = tankImgGO.AddComponent<Image>();
                    tankImg.sprite = Sprite.Create(
                        tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f)
                    );
                    tankImg.preserveAspect = true;
                    tankImg.color = Color.white;
                    tankImg.raycastTarget = false;

                } catch (Exception ex) {
                    MelonLogger.Warning($"[Lobby] Failed to load tank image for {tank.Name}: {ex.Message}");
                }
            }

            // Store mapping tank → button
            tankButtonMap[go] = tank.Name;

            // Button
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bgImg;

            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.normalColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
            colors.highlightedColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            colors.pressedColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            btn.colors = colors;

            // CLick → OnTankClicked(go)
            btn.onClick.AddListener((UnityAction)(() => OnTankClicked(go)));

            MelonLogger.Msg($"[Lobby] {tank.Name}: Entry created successfully!");

        } catch (Exception ex) {
            MelonLogger.Error($"[Lobby] Exception in CreateTankEntry for {tank?.Name}: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
            }
        
        private static void OnTankClicked(GameObject tankButton) {
            if (tankButtonMap.ContainsKey(tankButton)) {
                string tankName = tankButtonMap[tankButton];
                MelonLogger.Msg($"[Lobby] Tank selected: {tankName}");
        
                SelectedTank = tankName;
        
                // Send tank selection to host/server
                if (NetworkManager.Instance != null) {
                    string nickname = NetworkManager.Instance.LocalNickname;
            
                    if (NetworkManager.Instance.IsHost) {
                        // Host: directly register their own tank
                        MultiplayerManager.Instance.SetPlayerTank(nickname, tankName);
                        MelonLogger.Msg($"[Lobby] Host registered tank: {tankName}");
                    } else {
                        // Client: send tank choice to host
                        NetworkManager.Instance.Send($"TANK_SELECT:{nickname}:{tankName}");
                        MelonLogger.Msg($"[Lobby] Client sent tank selection to host: {tankName}");
                    }
                }
            }
        }
        
        private class TankEntryHandler : MonoBehaviour {
            public string tankName;
            public GameObject label;

            public void OnClick() {
                SelectedTank = tankName;
                MelonLogger.Msg("[Lobby] Player selected tank: " + tankName);
            }

            public void OnPointerEnter(BaseEventData data) {
                if (label != null)
                    label.SetActive(true);
            }

            public void OnPointerExit(BaseEventData data) {
                if (label != null)
                    label.SetActive(false);
            }
        }

        private static void SetupLeftPanel(Transform parent) {
        // DEBUG START
        MelonLogger.Msg($"[UI DEBUG] parent = {parent}");
        MelonLogger.Msg($"[UI DEBUG] PlayerSlots exists? {PlayerSlots != null}");
        MelonLogger.Msg($"[UI DEBUG] PlayerSlots count = {PlayerSlots?.Count}");
        // DEBUG END
        
        // ===== Check faction =====
        string faction = "Unknown";
        try {
            faction = Main.GetPlayerFaction();
        } catch {
            MelonLogger.Warning("[Lobby] Could not fetch player faction, using placeholder.");
        }
        
        if (!Main.VehicleManager.CheckFaction(faction)) {
            AddPlaceholderText(parent, "Restricted faction selected.\nSelect AllowedVehicles.");
            return;
        }
        
        // ===== Scroll View =====
        var scrollGO = new GameObject("TankScrollView");
        scrollGO.transform.SetParent(parent, false);
        
        var scrollRect = scrollGO.AddComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0, 0);
        scrollRect.anchorMax = new Vector2(1, 1);
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = Vector2.zero;
        
        var scroll = scrollGO.AddComponent<ScrollRect>();
        
        // Viewport
        var viewGO = new GameObject("Viewport");
        viewGO.transform.SetParent(scrollGO.transform, false);
        var viewMask = viewGO.AddComponent<Mask>();
        viewMask.showMaskGraphic = false;
        var viewImg = viewGO.AddComponent<Image>();
        viewImg.color = new Color(0, 0, 0, 0.3f);
        var viewRect = viewGO.GetComponent<RectTransform>();
        viewRect.anchorMin = Vector2.zero;
        viewRect.anchorMax = Vector2.one;
        viewRect.offsetMin = Vector2.zero;
        viewRect.offsetMax = Vector2.zero;
        
        // Content
        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(viewGO.transform, false);
        tankScrollContent = contentGO.transform;
        var contentRect = contentGO.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0, 0);
        
        var gridLayout = contentGO.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(120, 120); // Size of each tank card
        gridLayout.spacing = new Vector2(10, 10); // Gap between cards
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 2; // 2 columns
        gridLayout.childAlignment = TextAnchor.UpperCenter;
        gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        
        var fitter = contentGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        scroll.content = contentRect;
        scroll.viewport = viewRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        
        // ===== Load tanks =====
        MelonLogger.Msg("[Lobby] About to call TankDatabase.LoadTanks()");
        
        string tanksPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "Sprocket", "Factions", "AllowedVehicles", "Blueprints", "Vehicles"
        );
        
        MelonLogger.Msg($"[Lobby] Tank path: {tanksPath}");
        
        var tanks = TankDatabase.LoadTanks();
        MelonLogger.Msg($"[Lobby] LoadTanks returned: {(tanks == null ? "NULL" : $"List with {tanks.Count} items")}");
        
        if (tanks == null || tanks.Count == 0) {
            AddPlaceholderText(parent, "No available tanks found.");
            return;
        }
        
        MelonLogger.Msg("[Lobby] Starting tank entry creation loop");
        foreach (var tank in tanks) {
            try {
                MelonLogger.Msg($"[Lobby] Creating entry for tank: {tank?.Name ?? "NULL"}");
                CreateTankEntry(tank, contentGO.transform);
            } catch (Exception ex) {
                MelonLogger.Error($"[Lobby] Failed to create tank entry: {ex}");
            }
        }
        MelonLogger.Msg("[Lobby] Tank entry creation complete");
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

        private static void SetupRightPanel(Transform parent) {
            // ===== Title =====
            var titleGO = new GameObject("MapTitle");
            titleGO.transform.SetParent(parent, false);

            var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
            titleTMP.text = "Selected Map";
            titleTMP.fontSize = 26;
            titleTMP.alignment = TextAlignmentOptions.Center;
            titleTMP.color = Color.white;

            var titleRect = titleTMP.rectTransform;
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.sizeDelta = new Vector2(0, 40);
            titleRect.anchoredPosition = new Vector2(0, -10);


            // ===== Map Name (hardcoded Railway) =====
            var mapGO = new GameObject("SelectedMapText");
            mapGO.transform.SetParent(parent, false);

            mapTextTMP = mapGO.AddComponent<TextMeshProUGUI>();
            mapTextTMP.text = "Railway";
            mapTextTMP.fontSize = 22;
            mapTextTMP.alignment = TextAlignmentOptions.Center;
            mapTextTMP.color = Color.white;

            var mapRect = mapTextTMP.rectTransform;
            mapRect.anchorMin = new Vector2(0, 1);
            mapRect.anchorMax = new Vector2(1, 1);
            mapRect.pivot = new Vector2(0.5f, 1);
            mapRect.sizeDelta = new Vector2(0, 30);
            mapRect.anchoredPosition = new Vector2(0, -60);


            // ===== Start Button (host only) =====
            if (NetworkManager.Instance.IsHost)
            {
                var btnGO = new GameObject("StartButton");
                btnGO.transform.SetParent(parent, false);

                var btn = btnGO.AddComponent<Button>();
                var img = btnGO.AddComponent<Image>();
                img.color = new Color(0.3f, 0.3f, 0.3f, 0.9f);

                var btnRect = btnGO.GetComponent<RectTransform>();
                btnRect.anchorMin = new Vector2(0.2f, 0);
                btnRect.anchorMax = new Vector2(0.8f, 0);
                btnRect.pivot = new Vector2(0.5f, 0);
                btnRect.sizeDelta = new Vector2(0, 40);
                btnRect.anchoredPosition = new Vector2(0, 20);

                // button label
                var labelGO = new GameObject("StartButtonLabel");
                labelGO.transform.SetParent(btnGO.transform, false);

                var label = labelGO.AddComponent<TextMeshProUGUI>();
                label.text = "Start";
                label.fontSize = 22;
                label.color = Color.white;
                label.alignment = TextAlignmentOptions.Center;

                var labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                var action = (UnityAction) delegate {
                    OnStartButtonPressed();
                };
                btn.onClick.AddListener(action);

            }
        }
        
        private static void OnStartButtonPressed()
        {
            MelonLogger.Msg("[Lobby] ========== HOST PRESSED START ==========");
    
            try {
                // Ensure everyone selected a tank
                if (string.IsNullOrEmpty(SelectedTank)) {
                    MelonLogger.Warning("[Lobby] Somebody hasn't selected a tank! Using default.");
                    VehicleSpawner.EnsureInitialized();
                    SelectedTank = VehicleSpawner.GetDefaultTankId();
            
                    if (!string.IsNullOrEmpty(SelectedTank)) {
                        MelonLogger.Msg($"[Lobby] Using default tank: {SelectedTank}");
                        if (MultiplayerManager.Instance != null && NetworkManager.Instance != null) {
                            MultiplayerManager.Instance.SetPlayerTank(
                                NetworkManager.Instance.LocalNickname, 
                                SelectedTank
                            );
                        }
                    } else {
                        MelonLogger.Error("[Lobby] No tanks available!");
                    }
                } else {
                    MelonLogger.Msg($"[Lobby] Host tank: {SelectedTank}");
                }
                
                // Send map load command to all clients
                MelonLogger.Msg("[Lobby] Broadcasting map to clients...");
                SendMapToClients("Railway");
        
                // Load scene using Sprocket's proper scene manager
                MelonLogger.Msg("[Lobby] Starting scene load...");
                MelonCoroutines.Start(LoadSprocketScene("Railway"));
            } catch (Exception ex) {
                MelonLogger.Error($"[Lobby] Error in OnStartButtonPressed: {ex.Message}");
                MelonLogger.Error($"[Lobby] Stack: {ex.StackTrace}");
            }
        }

        private static IEnumerator LoadSceneAfterDelay(string sceneName) {
            yield return new WaitForSeconds(0.1f);
            SceneManager.LoadScene(sceneName);
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
        
        // Below are functions to load scene properly.
        
        private static IEnumerator LoadSprocketScene(string sceneName) {
        MelonLogger.Msg($"[Lobby] Loading scene '{sceneName}'...");
        
        // Small delay to ensure network messages are sent
        yield return new WaitForSeconds(0.2f);
        
        try
        {
            // Find ISceneManager instance
            var sceneManager = GetSprocketSceneManager();
            
            if (sceneManager == null)
            {
                MelonLogger.Error("[Lobby] Could not find ISceneManager! Falling back to Unity SceneManager.");
                SceneManager.LoadScene(sceneName);
                yield break;
            }
            
            MelonLogger.Msg($"[Lobby] Found ISceneManager, loading scene '{sceneName}'...");
            
            // Create cancellation token
            var cts = new Il2CppSystem.Threading.CancellationTokenSource();
            var ct = cts.Token;
            
            // Load scene using Sprocket's scene manager
            // Using the proper Load<T> method with ScenarioGameState
            var loadTask = sceneManager.Load<Il2CppSprocket.ScenarioGameState>(
                sceneName,
                null, // no arguments
                Il2CppSprocket.SceneManagement.SceneLoadOptions.None,
                ct
            );
            
            MelonLogger.Msg("[Lobby] Scene load task started, waiting for completion...");
            
            // Wait for task to complete
            int timeout = 0;
            while (!loadTask.IsCompleted && !loadTask.IsFaulted && !loadTask.IsCanceled && timeout < 100) {
                timeout++;
            }
            
            if (timeout >= 100) {
                MelonLogger.Error("[Lobby] Scene load timeout!");
                cts.Cancel();
                yield break;
            }
            
            if (loadTask.IsFaulted)
            {
                MelonLogger.Error("[Lobby] Scene load faulted!");
                if (loadTask.Exception != null)
                {
                    MelonLogger.Error($"[Lobby] Exception: {loadTask.Exception.Message}");
                }
                yield break;
            }
            
            if (loadTask.IsCanceled)
            {
                MelonLogger.Error("[Lobby] Scene load was canceled!");
                yield break;
            }
            
            MelonLogger.Msg($"[Lobby] ✓ Scene '{sceneName}' loaded successfully!");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[Lobby] Exception loading scene: {ex.Message}");
            MelonLogger.Error($"[Lobby] Stack: {ex.StackTrace}");
            
            // Fallback to Unity scene manager
            MelonLogger.Warning("[Lobby] Falling back to Unity SceneManager...");
            SceneManager.LoadScene(sceneName);
        }
    }

        private static Il2CppSprocket.SceneManagement.ISceneManager GetSprocketSceneManager() {
            try
            {
                // Search for ISceneManager in scene
                var allObjects = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    
                    // Look for objects with "SceneManager" in their type name
                    if (obj.GetType().FullName.Contains("SceneManager"))
                    {
                        var il2cppObj = obj as Il2CppSystem.Object;
                        if (il2cppObj != null)
                        {
                            // Try to cast to ISceneManager
                            var sceneManager = il2cppObj.TryCast<Il2CppSprocket.SceneManagement.ISceneManager>();
                            if (sceneManager != null)
                            {
                                MelonLogger.Msg($"[Lobby] Found ISceneManager: {obj.GetType().FullName}");
                                return sceneManager;
                            }
                        }
                    }
                }
                
                // Try static instance pattern
                var sceneManagerType = typeof(Il2CppSprocket.SceneManagement.ISceneManager);
                var assembly = sceneManagerType.Assembly;
                
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsInterface || type.IsAbstract) continue;
                    if (!typeof(Il2CppSprocket.SceneManagement.ISceneManager).IsAssignableFrom(type)) continue;
                    
                    var instanceProp = type.GetProperty("Instance",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    
                    if (instanceProp != null)
                    {
                        var instance = instanceProp.GetValue(null);
                        if (instance != null)
                        {
                            var il2cppObj = instance as Il2CppSystem.Object;
                            if (il2cppObj != null)
                            {
                                var sceneManager = il2cppObj.TryCast<Il2CppSprocket.SceneManagement.ISceneManager>();
                                if (sceneManager != null)
                                {
                                    MelonLogger.Msg($"[Lobby] Found ISceneManager via static: {type.FullName}");
                                    return sceneManager;
                                }
                            }
                        }
                    }
                }
                
                MelonLogger.Warning("[Lobby] Could not find ISceneManager");
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Lobby] Error getting ISceneManager: {ex.Message}");
                return null;
            }
        }
        
        // ================= PLAYER MANAGEMENT =================
        public static void SetPlayerTank(string nickname, string tankName) {
            if (!Players.TryGetValue(nickname, out var info))
                return;

            info.Tank = tankName;
            RefreshPlayerSlot(nickname);
        }
        private static void RefreshPlayerSlot(string nickname) {
            
            for (int i = 0; i < PlayerSlots.Count; i++) {
                var slot = PlayerSlots[i];
                if (slot == null) continue;

                if (slot.text.Contains($": {nickname}")) {
                    var info = Players[nickname];
                    string tankStr = string.IsNullOrEmpty(info.Tank) ? "" : $" — {info.Tank}";
                    string pingStr = info.Ping > 0 ? $", {info.Ping} ms" : "";

                    slot.text = $"Player{i+1}: {nickname}{tankStr}{pingStr}";
                    MelonLogger.Msg($"[Lobby] Refreshed slot for {nickname}: {slot.text}");
                    return;
                }
            }
        }

        public static bool TryAddPlayer(string nickname) {
            if (string.IsNullOrEmpty(nickname)) return false;

            // Check if already present
            if (Players.ContainsKey(nickname))
                return false;

            // Count current players
            int filled = 0;
            for (int i = 0; i < PlayerSlots.Count; i++) {
                var slot = PlayerSlots[i];
                string text = slot?.text;
                if (!string.IsNullOrEmpty(text) && text != $"Player{i + 1}: {EMPTY}")
                    filled++;
            }
            if (filled >= MAX_PLAYERS) return false;

            // find empty slot
            for (int i = 0; i < PlayerSlots.Count; i++) {
                var slot = PlayerSlots[i];
                string currentText = slot?.text;
                if (string.IsNullOrEmpty(currentText) || currentText == $"Player{i+1}: {EMPTY}") {
                    // Assign the slot directly
                    slot.text = $"Player{i+1}: {nickname}";
                    
                    Players[nickname] = new PlayerInfo {
                        Tank = "",
                        Ping = 0
                    };
                    
                    RefreshPlayerSlot(nickname);
                    MelonLogger.Msg($"[Lobby] Added player {nickname} to slot {i+1}");

                    return true;
                }
            }

            return false;
        }
        
        public static void RemovePlayer(string nickname) {
            if (string.IsNullOrEmpty(nickname)) return;

            for (int i = 0; i < PlayerSlots.Count; i++) {
                var slot = PlayerSlots[i];
                if (slot.text.Contains($": {nickname}")) {
                    slot.text = $"Player{i + 1}: {EMPTY}";
                    MelonLogger.Msg($"[Lobby] Removed player {nickname} from slot {i+1}");
                    return;
                }
            }
        }
        
        public static void OnPlayerConnected(string nickname) {
            if (!TryAddPlayer(nickname)) {
                MelonLogger.Msg($"Cannot add {nickname}: Lobby full or already present.");
                return;
            }
            
            if (!Players.ContainsKey(nickname)) {
                Players[nickname] = new PlayerInfo {
                    Tank = "",
                    Ping = 0
                };
            }
            
            RefreshPlayerSlot(nickname);
        }
        
        public static void OnPlayerDisconnected(string nickname) {
            Players.Remove(nickname);
            RemovePlayer(nickname);
        }
        
        public static void UpdatePlayerPing(string nickname, int ping) {
            if (!Players.TryGetValue(nickname, out var info))
                return;

            info.Ping = ping;
            RefreshPlayerSlot(nickname);
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
                pendingLobbyState = new List<string>(nicknames);
                return;
            }
            if (PlayerSlots == null || PlayerSlots.Count == 0) {
                pendingPlayers = new List<string>(nicknames);
                return;
            }

            var safeList = new List<string>();
            for (int i = 0; i < Math.Min(nicknames.Count, PlayerSlots.Count); i++)
                safeList.Add(nicknames[i]);

            if (safeList.Count > 0 && !string.IsNullOrEmpty(safeList[0])) {
                hostLobbyName = safeList[0];
                if (headerTMP != null && !NetworkManager.Instance.IsHost) {
                    headerTMP.text = $"{hostLobbyName}'s Lobby";
                }
            }

            for (int i = 0; i < PlayerSlots.Count; i++) {
                string nickname = (i < safeList.Count) ? safeList[i] : null;

                if (!string.IsNullOrEmpty(nickname)) {
                    if (!Players.ContainsKey(nickname)) {
                        Players[nickname] = new PlayerInfo {
                            Tank = "",
                            Ping = 0
                        };
                    }

                    PlayerSlots[i].text = $"Player{i + 1}: {nickname}";
                } else {
                    PlayerSlots[i].text = $"Player{i + 1}: {EMPTY}";
                }
            }
        }
    }
}