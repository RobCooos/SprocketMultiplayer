using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SprocketMultiplayer.UI
{
    public class Console : MonoBehaviour
    {
        public static Console Instance { get; private set; }
        public static bool IsOpen { get; private set; } = false;

        private GameObject canvasGO;
        private GameObject panelGO;
        private Text logText;

        private string inputText = "";
        private readonly List<string> outputLog = new List<string>();
        private bool initialized = false;
        private const int MaxLogLines = 25;
        
        

        private void Awake() {
            MelonLogger.Msg("[Console] Awake");
            Instance = this;
            CreateConsoleUI();
        }
        private void AddLog(string message) {
            outputLog.Add(message);
            if (outputLog.Count > MaxLogLines)
                outputLog.RemoveRange(0, outputLog.Count - MaxLogLines);

            logText.text = string.Join("\n", outputLog) + $"\n> {inputText}_";
        }
        
        private void CreateConsoleUI() {
            try {
                // Canvas
                canvasGO = new GameObject("ConsoleCanvas");
                var canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
                DontDestroyOnLoad(canvasGO);

                // Panel
                panelGO = new GameObject("ConsolePanel");
                panelGO.transform.SetParent(canvasGO.transform, false);
                var panelImage = panelGO.AddComponent<Image>();
                panelImage.color = new Color(0f, 0f, 0f, 0.7f);

                var panelRect = panelGO.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0, 0);
                panelRect.anchorMax = new Vector2(1, 0.5f);
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;

                // Text display
                var textGO = new GameObject("LogText");
                textGO.transform.SetParent(panelGO.transform, false);
                logText = textGO.AddComponent<Text>();
                logText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                logText.color = Color.white;
                logText.alignment = TextAnchor.UpperLeft;
                logText.horizontalOverflow = HorizontalWrapMode.Wrap;
                logText.verticalOverflow = VerticalWrapMode.Overflow;

                var textRect = textGO.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0, 0);
                textRect.anchorMax = new Vector2(1, 1);
                textRect.offsetMin = new Vector2(10, 10);
                textRect.offsetMax = new Vector2(-10, -10);

                HideConsole();
                initialized = true;

                MelonLogger.Msg("[Console] Initialized successfully.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Console] Failed to create UI: {ex}");
            }
        }

        private void ShowConsole() {
            if (canvasGO == null) return;
            canvasGO.SetActive(true);
            MelonLogger.Msg("[Sprocket Multiplayer] [Console] Shown");
        }

        private void HideConsole() {
            if (canvasGO == null) return;
            canvasGO.SetActive(false);
            MelonLogger.Msg("[Sprocket Multiplayer] [Console] Hidden");
        }

        private void Update()
{
    if (!initialized || Keyboard.current == null)
        return;

    // Toggle console
    if (Keyboard.current.backquoteKey.wasPressedThisFrame) {
        IsOpen = !IsOpen;
        if (IsOpen) {
            ShowConsole();
            AddLog("------------------------------------");
            AddLog($"[{System.DateTime.Now:HH:mm:ss}] Sprocket Multiplayer Console initiated!");
            AddLog("Authored by RobCos.");
            AddLog("Type 'help' to view available commands.");
            AddLog("------------------------------------");
        }
        else HideConsole();
    }

    if (!IsOpen)
        return;

    var keyboard = Keyboard.current;
    bool shift = keyboard.shiftKey.isPressed;

    // Letters
    for (Key key = Key.A; key <= Key.Z; key++) {
        if (keyboard[key].wasPressedThisFrame) {
            char c = (char)(key - Key.A + (shift ? 'A' : 'a'));
            inputText += c;
        }
    }

    // Numbers
    for (Key key = Key.Digit0; key <= Key.Digit9; key++) {
        if (keyboard[key].wasPressedThisFrame) {
            inputText += (char)('0' + (key - Key.Digit0));
        }
    }

    // Numpad
    for (Key key = Key.Numpad0; key <= Key.Numpad9; key++) {
        if (keyboard[key].wasPressedThisFrame) {
            inputText += (char)('0' + (key - Key.Numpad0));
        }
    }

    // Basic editing
    if (keyboard.backspaceKey.wasPressedThisFrame && inputText.Length > 0)
        inputText = inputText.Substring(0, inputText.Length - 1);

    // ─────────────────────────────────────────────
    // SYMBOLS
    // ─────────────────────────────────────────────
    if (keyboard.spaceKey.wasPressedThisFrame) inputText += ' ';
    if (keyboard.periodKey.wasPressedThisFrame) inputText += shift ? '>' : '.';
    if (keyboard.commaKey.wasPressedThisFrame) inputText += shift ? '<' : ',';
    if (keyboard.slashKey.wasPressedThisFrame) inputText += shift ? '?' : '/';
    if (keyboard.minusKey.wasPressedThisFrame) inputText += shift ? '_' : '-';
    if (keyboard.equalsKey.wasPressedThisFrame) inputText += shift ? '+' : '=';
    if (keyboard.semicolonKey.wasPressedThisFrame) inputText += shift ? ':' : ';';
    if (keyboard.leftBracketKey.wasPressedThisFrame) inputText += shift ? '{' : '[';
    if (keyboard.rightBracketKey.wasPressedThisFrame) inputText += shift ? '}' : ']';


    // ─────────────────────────────────────────────
    // Enter / Submit
    // ─────────────────────────────────────────────
    if (keyboard.enterKey.wasPressedThisFrame) {
        ProcessCommand(inputText);
        inputText = "";
    }

    // Update text UI
    logText.text = string.Join("\n", outputLog) + $"\n> {inputText}_";
}

       

        public bool IsFocused() {
            // Always true while open
            return IsOpen;
        }

        private void ProcessCommand(string cmd) {

            if (string.IsNullOrWhiteSpace(cmd))
                return;

            AddLog($"> {cmd}");

            string[] parts = cmd.Split(' ');
            switch (parts[0].ToLower()) {
                        
                case "ping":
                    AddLog("Pong!");
                    break;

                case "host":
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int port))
                    {
                        NetworkManager.Instance?.StartHost(port);
                        AddLog($"Started host on port {port}");
                    }
                    else AddLog("Usage: host <port>");
                    break;

                case "connect":
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int connectPort)) {
                        NetworkManager.Instance?.ConnectToHost(parts[1], connectPort);
                        AddLog($"Connecting to {parts[1]}:{connectPort}");
                    }
                    else AddLog("Usage: connect <ip> <port>");
                    break;

                case "help":
                    AddLog("Available commands:");
                    AddLog("─────────────────────────────");
                    AddLog("ping                - Check connection responsiveness");
                    AddLog("host <port>         - Start hosting a multiplayer session");
                    AddLog("connect <ip> <port> - Connect to an existing host");
                    AddLog("status              - Show current network status");
                    AddLog("clients             - Show connected clients (Host only)");
                    AddLog("inspect <target>    - Inspect game assemblies (debug)");
                    AddLog("clear               - Clear the console log");
                    AddLog("time                - Display system time");
                    AddLog("info                - Show mod information");
                    AddLog("help                - Display this help list");
                    AddLog("─────────────────────────────");
                    break;
                
                case "time":
                    AddLog($"System time: {DateTime.Now:HH:mm:ss}");
                    break;

                case "info":
                    AddLog("Currently running Sprocket Multiplayer v0.8.");
                    AddLog("Created with love by RobCos!");
                    AddLog("Don't be afraid to DM me in Discord if you find a bug.");
                    break;
                
                case "clear":
                    outputLog.Clear();
                    AddLog("Console cleared.");
                    break;
                
                case "status":
                    if (NetworkManager.Instance == null)
                    {
                        AddLog("NetworkManager not initialized.");
                        break;
                    }

                    AddLog("--- Network Status ---");

                    if (NetworkManager.Instance.IsHost)
                    {
                        AddLog("Mode: Host");
                        AddLog($"Listening on port: {NetworkManager.Instance.CurrentPort}");
                        AddLog($"Connected clients: {NetworkManager.Instance.ClientCount}");
                    }
                    else if (NetworkManager.Instance.IsClient)
                    {
                        AddLog("Mode: Client");
                        AddLog($"Connected to: {NetworkManager.Instance.CurrentIP}:{NetworkManager.Instance.CurrentPort}");
                    }
                    else
                    {
                        AddLog("Mode: Disconnected");
                    }

                    AddLog("-----------------------");
                    break;
                
                case "clients": 
                {
                    var nm = NetworkManager.Instance;
                    if (nm == null)
                    {
                        AddLog("No NetworkManager instance found.");
                        break;
                    }

                    var nmType = nm.GetType();
                    
                    // Try common property names that might exist
                    var prop = nmType.GetProperty("ClientCount")
                               ?? nmType.GetProperty("Clients")
                               ?? nmType.GetProperty("ConnectedClients")
                               ?? nmType.GetProperty("clientCount")
                               ?? nmType.GetProperty("clients");

                    if (prop != null)
                    {
                        var val = prop.GetValue(nm);
                        if (val is int)
                        {
                            AddLog("Connected clients: " + val);
                        }
                        else if (val is System.Collections.IEnumerable)
                        {
                            int cnt = 0;
                            foreach (var _ in (System.Collections.IEnumerable)val) cnt++;
                            AddLog("Connected clients: " + cnt);
                        }
                        else
                        {
                            AddLog("Clients property present but of unknown type: " + (val?.GetType().Name ?? "null"));
                        }
                        break;
                    }

                    // Try methods that might return a count
                    var method = nmType.GetMethod("GetClientCount")
                                 ?? nmType.GetMethod("ClientCount")
                                 ?? nmType.GetMethod("GetClientsCount");

                    if (method != null)
                    {
                        try
                        {
                            var res = method.Invoke(nm, null);
                            AddLog("Connected clients: " + (res ?? "null"));
                        }
                        catch (Exception ex)
                        {
                            AddLog("Error invoking client-count method: " + ex.Message);
                        }
                        break;
                    }

                    // Nothing found — tell what to do
                    AddLog("NetworkManager does not expose clients or client count.");
                    AddLog("Send this to dev with full Console Log, this is most likely his mistake.");
                    break;
                }
                
                case "inspect":
                    if (parts.Length < 2)
                    {
                        AddLog("Usage: inspect <spawning|vehicles|search>");
                        break;
                    }
                    
                    switch (parts[1].ToLower())
                    {
                        case "spawning":
                            AddLog("Inspecting Vehicle.Spawning assembly...");
                            Patches.AssemblyInspector.InspectVehicleSpawning();
                            AddLog("Check MelonLoader console for results.");
                            break;
                        case "vehicles":
                            AddLog("Inspecting Vehicles assembly...");
                            Patches.AssemblyInspector.InspectVehiclesAssembly();
                            AddLog("Check MelonLoader console for results.");
                            break;
                        case "search":
                            AddLog("Searching for spawn methods...");
                            Patches.AssemblyInspector.SearchForSpawningMethods();
                            AddLog("Check MelonLoader console for results.");
                            break;
                        default:
                            AddLog("Unknown inspect target. Use: spawning, vehicles, or search");
                            break;
                    }
                    break;
                
                default:
                    AddLog($"Unknown command: {cmd}");
                    MelonLogger.Msg($"Attempted to utilize: {cmd}. Reason of unsuccessful response: Unknown Command.");
                    break;
            }
        }
    }
}