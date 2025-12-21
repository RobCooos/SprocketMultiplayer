using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MelonLoader;
using SprocketMultiplayer.Core;
using SprocketMultiplayer.Patches;
using UnityEngine;


namespace SprocketMultiplayer
{
    public class NetworkManager {
        private TcpListener server;
        private TcpClient client;
        private NetworkStream stream;
        private bool isHost; //for this code
        private const int BufferSize = 1024;
        private string hostNickname = "Host";
        public string HostNickname => hostNickname;
        public bool IsActiveMultiplayer => IsHost || IsClient;
        
        public bool IsHost { get; private set; } //for getting Host in other values
        public bool IsClient { get; private set; }
        public int CurrentPort { get; private set; }
        public string CurrentIP { get; private set; } = "127.0.0.1";
        public string LocalNickname { get; set; }

        public int ClientCount => connectedClients.Count;
        private readonly List<string> connectedClients = new List<string>();


        private List<TcpClient> clients = new List<TcpClient>();
        private Dictionary<TcpClient, DateTime> pingSentTime = new Dictionary<TcpClient, DateTime>();
        private readonly Dictionary<TcpClient, string> clientNicknames = new Dictionary<TcpClient, string>();
        private readonly HashSet<TcpClient> clientsReadyForPing = new HashSet<TcpClient>(); // NEW
        
        private DateTime lastPingTime = DateTime.MinValue;
        public static NetworkManager Instance { get; private set; }

        public NetworkManager() {
            Instance = this;
        }
        
        // ================= HOST =================
        public void StartHost(int port) {

            try {
                if (server != null) {
                    MelonLogger.Msg("Host already running.");
                    return;
                }
                
                isHost = true;
                IsHost = true;
                // ^ This is like so dumb but whatever
                try {
                    hostNickname = MenuActions.GetSteamNickname();
                } catch {
                    MelonLogger.Warning("Could not fetch host Steam nickname; using fallback 'Host'.");
                }

                LocalNickname = hostNickname;

                IsClient = false;

                server = new TcpListener(IPAddress.Any, port);
                server.Start();
                MelonLogger.Msg($"Host started on port {port}.");
                ListenForClients();
            }   
            catch (Exception ex) {
                MelonLogger.Error($"Failed to start host on port {port}: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
        }
        private async void ListenForClients() {
        MelonLogger.Msg("[Network] Listening for incoming clients...");

        while (isHost && server != null) {
            TcpClient newClient = null;
            try {
                newClient = await server.AcceptTcpClientAsync();
            } catch (Exception ex) {
                MelonLogger.Error($"[Network] Client accept failed: {ex.Message}");
                break;
            }

            string endpoint = newClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            MelonLogger.Msg($"[Network] Client connected (endpoint: {endpoint}). Waiting for JOIN...");

            // Read initial message (JOIN:[nickname]>) with a short timeout
            try {
                var ns = newClient.GetStream();
                ns.ReadTimeout = 5000; // 5s handshake
                byte[] buffer = new byte[BufferSize];
                int bytesRead = await ns.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead <= 0) throw new Exception("Handshake read returned 0 bytes.");

                string initial = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                string nickname;
                if (initial.StartsWith("JOIN:")) {
                    nickname = initial.Substring("JOIN:".Length).Trim();
                    if (string.IsNullOrEmpty(nickname)) nickname = "Player";
                } else {
                    nickname = $"Player_{endpoint}";
                    MelonLogger.Warning($"[Network] Unexpected initial client message: {initial}. Assigned nickname {nickname}");
                }

                // registration only after successful handshake:
                clients.Add(newClient);
                connectedClients.Add(endpoint);
                clientNicknames[newClient] = nickname;
                MelonLogger.Msg($"[Network] Registered client nickname: {nickname} ({endpoint})");

                // Notify lobby
                try {
                    Lobby.OnPlayerConnected(nickname);
                } catch (Exception ex) {
                    MelonLogger.Error($"[Network] Lobby.OnPlayerConnected threw: {ex.Message}");
                }

                // Broadcast state
                BroadcastLobbyState();
            }
            catch (Exception ex) {
                MelonLogger.Error($"[Network] Error during client handshake from {endpoint}: {ex.Message}");
                try { newClient.Close(); } catch {}
                // do not add the client to clients list if handshake failed
            }
        }
        }

        private void BroadcastLobbyState() {
            // Build list: host first, then clients in arbitrary order
            var names = new List<string>();
            names.Add(hostNickname);

            foreach (var kv in clientNicknames) {
                var nick = kv.Value;
                if (!string.IsNullOrEmpty(nick) && nick != hostNickname)
                    names.Add(nick);
            }

            // Limit to MAX_PLAYERS (match Lobby.MAX_PLAYERS)
            int max = 4; // keep in sync with Lobby.MAX_PLAYERS
            if (names.Count > max) names = names.GetRange(0, max);

            // Compose CSV
            string csv = string.Join(",", names);
            string msg = "LOBBY_STATE:" + csv;

            // Send to all clients (copy list to avoid modification during loop)
            foreach (var c in clients.ToArray()) {
                try {
                    SendToClient(c, msg);
                    clientsReadyForPing.Add(c);
                }
                catch (Exception ex) {
                    try {
                        var ep = c.Client.RemoteEndPoint;
                        MelonLogger.Warning($"Failed to send lobby state to {ep}: {ex.Message}");
                    } catch {
                        MelonLogger.Warning($"Failed to send lobby state to a client: {ex.Message}");
                    }
                }
            }

            MelonLogger.Msg($"[Network] Broadcasted lobby state: {csv}");
        }
        
        private void OnClientConnected(IAsyncResult ar) {
            try {
                var newClient = server.EndAcceptTcpClient(ar);
                clients.Add(newClient);
                MelonLogger.Msg($"Client connected from {newClient.Client.RemoteEndPoint}.");
                server.BeginAcceptTcpClient(OnClientConnected, null); // Keep accepting new clients
            }
            catch (Exception ex) {
                MelonLogger.Error($"Client connection error: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
        }

        // ================= CLIENT =================
        public void ConnectToHost(string ip, int port) {
        try {
            if (client?.Connected == true) {
                MelonLogger.Msg("Already connected to a host.");
            return;
            }

            // Reset host state
            isHost = false;
            IsHost = false;
            IsClient = false;

            MelonLogger.Msg($"Attempting to connect to {ip}:{port}...");

            client = new TcpClient(); 
            client.Connect(IPAddress.Parse(ip), port);
            stream = client.GetStream();

            if (client.Connected) {
                IsClient = true;
                CurrentIP = ip;
                CurrentPort = port;
                MelonLogger.Msg($"Connected to host at {ip}:{port}!");

                // Send nickname to host
                try {
                    string nickname = "Player";
                    try
                    {
                        nickname = MenuActions.GetSteamNickname();
                    }
                    catch { }
                    
                    LocalNickname = nickname;
                    
                    byte[] joinData = Encoding.UTF8.GetBytes("JOIN:" + nickname);
                    stream.Write(joinData, 0, joinData.Length);
                    MelonLogger.Msg($"Sent JOIN with nickname '{nickname}' to host.");
                }
                catch (Exception ex) {
                    MelonLogger.Warning($"Failed to send JOIN to host: {ex.Message}");
                }

                // start listening for messages from host in background
                _ = ReceiveFromHostAsync();

            }
            else {
                MelonLogger.Msg($"Failed to connect to {ip}:{port}. Connection not established.");
            }
        }
        catch (Exception ex) {
            MelonLogger.Error($"Failed to connect to {ip}:{port}. Reason: {ex.Message}\n" +
                          "This usually happens if the host isn't running, the IP/port is wrong, or your network is blocking the connection.");
        CleanupClient();
        }
        }

        private async System.Threading.Tasks.Task ReceiveFromHostAsync() {
        try {
            byte[] buffer = new byte[BufferSize];
            while (IsClient && client?.Connected == true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    CleanupClient();
                    break;
                }
                
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                MelonLogger.Msg($"[Client] Received from host: {message}");

                // === LOBBY STATE ===
                if (message.StartsWith("LOBBY_STATE:")) {
                    string csv = message.Substring("LOBBY_STATE:".Length);
                    var names = new List<string>();
                    if (!string.IsNullOrEmpty(csv)) {
                        foreach (var s in csv.Split(',')) {
                            var t = s.Trim();
                            if (!string.IsNullOrEmpty(t)) names.Add(t);
                        }
                    }

                    if (Lobby.Panel == null) {
                        MelonLogger.Msg("[Client] Lobby UI not present yet. Waiting...");
                        MelonCoroutines.Start(Lobby.WaitForLobbyCanvasThenCreateUI(names));
                        continue;
                    }

                    Lobby.HandleIncomingLobbyState(names);
                    MelonLogger.Msg("[Client] Applied lobby state from host.");
                    continue;
                }

                // === MAP LOAD ===
                if (message.StartsWith("MAP:"))
                {
                    string map = message.Substring(4).Trim();
                    MelonLogger.Msg("[Client] Host loading map: " + map);
                    UnityEngine.SceneManagement.SceneManager.LoadScene(map);
                    continue;
                }

                // === SPAWN COMMAND ===
                if (message.StartsWith("SPAWN:"))
                {
                    // Format: SPAWN:{nickname}:{tankName}
                    string[] parts = message.Split(':');
                    if (parts.Length >= 3)
                    {
                        string nickname = parts[1];
                        string tankName = parts[2];
                        
                        MelonLogger.Msg($"[Client] Processing spawn: {nickname} -> {tankName}");
                        MultiplayerManager.Instance.OnClientSpawnMessage(nickname, tankName);
                    }
                    else
                    {
                        MelonLogger.Warning($"[Client] Invalid SPAWN message format: {message}");
                    }
                    continue;
                }

                // === PING ===
                if (message == "Ping!")
                {
                    Send("Pong!");
                    continue;
                }

                // Unknown message
                MelonLogger.Msg($"[Client] Unhandled message: {message}");
            }
        }
        catch (Exception ex) {
            MelonLogger.Error($"ReceiveFromHostAsync error: {ex.Message}");
            CleanupClient();
        }
    }
        

        // ================= POLLING =================
        public void PollEvents() {
            if (isHost)
                PollHostClients();
        }

        private void PollHostClients() {
        // send pings every 2 seconds
        if ((DateTime.UtcNow - lastPingTime).TotalSeconds >= 2) {
            foreach (var c in clients)
            {
                if (c?.Connected != true) continue;
                if (!clientsReadyForPing.Contains(c)) continue;

                SendToClient(c, "Ping!");
                pingSentTime[c] = DateTime.UtcNow;
            }
            lastPingTime = DateTime.UtcNow;
        }

        for (int i = clients.Count - 1; i >= 0; i--)
        {
            var c = clients[i];
            try
            {
                if (!c.Connected || (c.Client.Poll(0, SelectMode.SelectRead) && c.Client.Available == 0))
                {
                    MelonLogger.Msg($"Client {c.Client.RemoteEndPoint} disconnected.");
                    RemoveClient(c);
                    
                    // remove from maps
                    if (clientNicknames.ContainsKey(c)) clientNicknames.Remove(c);
                    BroadcastLobbyState();
                    continue;
                }

                if (c.GetStream().DataAvailable)
                {
                    byte[] buffer = new byte[BufferSize];
                    int bytesRead = c.GetStream().Read(buffer, 0, buffer.Length);
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    MelonLogger.Msg($"Received from {c.Client.RemoteEndPoint}: {message}");

                    // === TANK SELECTION (NEW) ===
                    if (message.StartsWith("TANK_SELECT:")) {
                        // Format: TANK_SELECT:{nickname}:{tankName}
                        string[] parts = message.Split(':');
                        if (parts.Length >= 3) {
                            string nickname = parts[1];
                            string tankName = parts[2];
                            
                            MelonLogger.Msg($"[Host] Player {nickname} selected tank: {tankName}");
                            MultiplayerManager.Instance.SetPlayerTank(nickname, tankName);
                        }
                        continue;
                    }

                    // === PONG ===
                    if (message == "Pong!") {
                        if (pingSentTime.ContainsKey(c)) {
                            int ping = (int)((DateTime.UtcNow - pingSentTime[c]).TotalMilliseconds);
                            if (clientNicknames.TryGetValue(c, out string nickname)) {
                                Lobby.UpdatePlayerPing(nickname, ping);
                                MelonLogger.Msg($"Updated ping for {nickname}: {ping} ms");
                            }
                            pingSentTime[c] = DateTime.UtcNow;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error with client {c.Client.RemoteEndPoint}: {ex.Message}");
                RemoveClient(c);
                c.Close();
            }
        }
    }
        
        // ================= SENDING =================
        public void Send(string msg) {
            if (isHost) {
                foreach (var c in clients)
                    SendToClient(c, msg);

                MelonLogger.Msg($"Sent to all clients: {msg}");
            }
            else if (client?.Connected == true) {
                byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
                stream.Write(data, 0, data.Length);
                MelonLogger.Msg($"Sent to host: {msg}");
            }
        }

        private void SendToClient(TcpClient c, string msg) {
            if (c?.Connected != true) return;
            byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
            c.GetStream().Write(data, 0, data.Length);
            MelonLogger.Msg($"Sent to {c.Client.RemoteEndPoint}: {msg}");
        }

        // ================= CLEANUP =================
        public void Shutdown() {
            try {
                stream?.Close();
                client?.Close();
                if (isHost) {
                    server?.Stop();
                }
                foreach (var c in clients) {
                    c.Close();
                }

                stream = null;
                client = null;
                server = null;
                clients.Clear();
                isHost = false;

                MelonLogger.Msg("NetworkManager shut down.");
            }
            catch (Exception ex) {
                MelonLogger.Error($"Shutdown error: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
        }

        private void CleanupClient() {
            try {
                stream?.Close();
                client?.Close();
                stream = null;
                client = null;
                MelonLogger.Msg("Client connection cleaned up.");
            }
            catch (Exception ex) {
                MelonLogger.Error($"Cleanup error: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
        }
        
        private void RemoveClient(TcpClient c) {
            clients.Remove(c);
            connectedClients.Remove(c.Client.RemoteEndPoint.ToString());
            clientNicknames.Remove(c);
            clientsReadyForPing.Remove(c);
            pingSentTime.Remove(c);
        }

    }
}
