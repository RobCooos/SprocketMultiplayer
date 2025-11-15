using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MelonLoader;
using SprocketMultiplayer.Core;
using SprocketMultiplayer.Patches;

namespace SprocketMultiplayer
{
    public class NetworkManager {
        private TcpListener server;
        private TcpClient client;
        private NetworkStream stream;
        private bool isHost; //for this code
        private const int BufferSize = 1024;
        public bool IsActiveMultiplayer => IsHost || IsClient;
        
        public bool IsHost { get; private set; } //for getting Host in other values
        public bool IsClient { get; private set; }

        public int CurrentPort { get; private set; }
        public string CurrentIP { get; private set; } = "127.0.0.1";

        public int ClientCount => connectedClients.Count;
        private readonly List<string> connectedClients = new List<string>();


        private List<TcpClient> clients = new List<TcpClient>();
        private Dictionary<TcpClient, DateTime> pingSentTime = new Dictionary<TcpClient, DateTime>();
        private readonly Dictionary<TcpClient, string> clientNicknames = new Dictionary<TcpClient, string>();
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
                try {
                    var newClient = await server.AcceptTcpClientAsync();
                    clients.Add(newClient);
                    
                    string nickname = MenuActions.GetSteamNickname();
                    clientNicknames[newClient] = nickname;
                    
                    Lobby.OnPlayerConnected(nickname);
                    
                    string endpoint = newClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                    connectedClients.Add(endpoint);
                    
                    MelonLogger.Msg($"[Network] Client connected: {nickname} ({endpoint})");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Network] Client accept failed: {ex.Message}");
                    break;
                }
            }
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
            while (IsClient && client?.Connected == true) {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) {
                    MelonLogger.Msg("Disconnected from host.");
                    CleanupClient();
                    break;
                }

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                MelonLogger.Msg($"[Client] Received from host: {message}");
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
            else
                PollClient();
        }

        private void PollHostClients() {
        // send pings every 2 seconds
        if ((DateTime.UtcNow - lastPingTime).TotalSeconds >= 2) {
            foreach (var c in clients)
            {
                if (c?.Connected != true) continue;
                
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
                    clients.RemoveAt(i);
                    c.Close();
                    continue;
                }

                if (c.GetStream().DataAvailable)
                {
                    byte[] buffer = new byte[BufferSize];
                    int bytesRead = c.GetStream().Read(buffer, 0, buffer.Length);
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    MelonLogger.Msg($"Received from {c.Client.RemoteEndPoint}: {message}");

                    // handle pong
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
                clients.RemoveAt(i);
                c.Close();
            }
        }
    }
        
        private void PollClient() {
            if (stream == null || !client?.Connected == true || !stream.DataAvailable) return;

            try {
                byte[] buffer = new byte[BufferSize];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) {
                    MelonLogger.Msg("Disconnected from host.");
                    CleanupClient();
                    return;
                }
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                // respond to ping
                if (message == "Ping!") {
                    Send("Pong!");
                }
            }
            catch (Exception ex) {
                MelonLogger.Error($"PollEvents error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                CleanupClient();
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
                byte[] data = Encoding.UTF8.GetBytes(msg);
                stream.Write(data, 0, data.Length);
                MelonLogger.Msg($"Sent to host: {msg}");
            }
        }

        private void SendToClient(TcpClient c, string msg) {
            if (c?.Connected != true) return;
            byte[] data = Encoding.UTF8.GetBytes(msg);
            c.GetStream().Write(data, 0, data.Length);
            MelonLogger.Msg($"Sent to {c.Client.RemoteEndPoint}: {msg}");
        }

        // ================= CLEANUP =================
        public void Shutdown() {
            try {
                stream?.Close();
                client?.Close();
                foreach (var c in clients) c.Close();
                if (isHost) server?.Stop();

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
    }
    
    
}
