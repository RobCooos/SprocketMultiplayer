using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using MelonLoader;
using SprocketMultiplayer.Unused;
using Steamworks;

namespace SprocketMultiplayer.Core {
    [Disabled]
    public class SteamNetworkManager {
        
        // Handles / types from Steamworks.NET
        private HSteamListenSocket listenSocket = HSteamListenSocket.Invalid;
        private HSteamNetConnection[] connectionHandles = new HSteamNetConnection[0];
        private readonly List<HSteamNetConnection> clients = new List<HSteamNetConnection>();

        private HSteamNetConnection clientConnection = HSteamNetConnection.Invalid; // if acting as client
        private HSteamNetPollGroup pollGroup = HSteamNetPollGroup.Invalid;

        private bool isHost;
        private const int MaxMessages = 32;

        public void Init() {
            if (!SteamAPI.Init()) {
                MelonLogger.Error("[SteamNet] SteamAPI.Init failed!");
                return;
            }
            MelonLogger.Msg("[SteamNet] SteamAPI initialized.");
        }

        public void StartHost() {
            try {
                isHost = true;
                // Create a poll group (optional but useful for grouping connections)
                pollGroup = SteamNetworkingSockets.CreatePollGroup();

                // Create listen socket for P2P (virtual port 0)
                SteamNetworkingConfigValue_t[] cfg = Array.Empty<SteamNetworkingConfigValue_t>();
                listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, cfg.Length, cfg);
                MelonLogger.Msg("[SteamNet] Host listen socket created.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SteamNet] StartHost error: {ex.Message}");
            }
        }

        public void StopHost() {
            try {
                if (listenSocket != HSteamListenSocket.Invalid) {
                    SteamNetworkingSockets.CloseListenSocket(listenSocket);
                    listenSocket = HSteamListenSocket.Invalid;
                }

                foreach (var h in clients) {
                    if (h != HSteamNetConnection.Invalid)
                        SteamNetworkingSockets.CloseConnection(h, 0, "Server shutting down", false);
                }
                clients.Clear();

                if (pollGroup != HSteamNetPollGroup.Invalid) {
                    SteamNetworkingSockets.DestroyPollGroup(pollGroup);
                    pollGroup = HSteamNetPollGroup.Invalid;
                }

                isHost = false;
                MelonLogger.Msg("[SteamNet] Host stopped.");
            }
            catch (Exception ex) {
                MelonLogger.Error($"[SteamNet] StopHost error: {ex.Message}");
            }
        }

        // Connect to a host given their CSteamID
        public void ConnectToHost(CSteamID hostSteamId)
        {
            try
            {
                // Build identity for the remote peer
                SteamNetworkingIdentity identity = new SteamNetworkingIdentity();
                identity.SetSteamID(hostSteamId);

                SteamNetworkingConfigValue_t[] cfg = Array.Empty<SteamNetworkingConfigValue_t>();
                clientConnection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, cfg.Length, cfg);

                MelonLogger.Msg($"[SteamNet] Initiated P2P connect to {hostSteamId} (handle {clientConnection}).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SteamNet] ConnectToHost error: {ex.Message}");
            }
        }

        // Call this from your update loop
        public void Poll()
        {
            if (!SteamAPI.IsSteamRunning()) return;

            // Pump Steam callbacks (important)
            SteamAPI.RunCallbacks();

            // Accept incoming connection attempts (server)
            if (isHost && listenSocket != HSteamListenSocket.Invalid) {
                // Check for any new connection attempts and accept them by checking connections list.
                // SteamNetworkingSockets will call connection state changes that you can observe by querying connections.
            }

            // Receive messages for server clients
            for (int idx = clients.Count - 1; idx >= 0; idx--) {
                var hConn = clients[idx];
                if (hConn == HSteamNetConnection.Invalid) continue;

                IntPtr[] msgPtrs = new IntPtr[MaxMessages];
                int received = SteamNetworkingSockets.ReceiveMessagesOnConnection(hConn, msgPtrs, msgPtrs.Length);
                if (received > 0) {
                    for (int i = 0; i < received; i++) {
                        // Marshal SteamNetworkingMessage_t from pointer
                        SteamNetworkingMessage_t msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(msgPtrs[i]);

                        // Copy unmanaged data into managed byte[]
                        var bytes = new byte[msg.m_cbSize];
                        Marshal.Copy(msg.m_pData, bytes, 0, (int)msg.m_cbSize);

                        string text = Encoding.UTF8.GetString(bytes);
                        MelonLogger.Msg($"[SteamNet][Server] Received from {hConn}: {text}");

                        // Release message memory (SteamNetworkingMessage_t has Release() instance method)
                        msg.Release();
                    }
                }

                // Check connection status
                SteamNetConnectionInfo_t info;
                if (SteamNetworkingSockets.GetConnectionInfo(hConn, out info)) {
                    // handle connected/disconnected states
                    if (info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected) {
                        // it's connected
                    }
                    else if (info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                          || info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally) {
                        MelonLogger.Msg($"[SteamNet] Connection {hConn} closed or problem: {info.m_szEndDebug}");
                        SteamNetworkingSockets.CloseConnection(hConn, 0, "closing", false);
                        clients.RemoveAt(idx);
                    }
                }
            }

            // Receive messages for client (if acting as client)
            if (clientConnection != HSteamNetConnection.Invalid) {
                IntPtr[] clientMsgs = new IntPtr[MaxMessages];
                int rec = SteamNetworkingSockets.ReceiveMessagesOnConnection(clientConnection, clientMsgs, clientMsgs.Length);
                if (rec > 0) {
                    for (int i = 0; i < rec; i++) {
                        SteamNetworkingMessage_t msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(clientMsgs[i]);
                        var bytes = new byte[msg.m_cbSize];
                        Marshal.Copy(msg.m_pData, bytes, 0, (int)msg.m_cbSize);
                        string text = Encoding.UTF8.GetString(bytes);
                        MelonLogger.Msg($"[SteamNet][Client] Received: {text}");
                        msg.Release();
                    }
                }
            }

            // Accept new incoming connections: enumerate connections and add newly connected
            // (Simplest approach: enumerate all connections via SteamNetworkingSockets or use CreateConnectionSignaling/Callbacks)
            // One practical pattern: track known connections, and if SteamNetworkingSockets.GetConnectionInfo reports new connections where state == Connected, add to clients.
        }

        // Send to one connection
        public void SendToConnection(HSteamNetConnection hConn, string text) {
            if (hConn == HSteamNetConnection.Invalid) return;
            byte[] data = Encoding.UTF8.GetBytes(text);
            IntPtr ptr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);

            var result = SteamNetworkingSockets.SendMessageToConnection(hConn, ptr, (uint)data.Length, 0, out long msgNum);
            Marshal.FreeHGlobal(ptr);

            if (result != EResult.k_EResultOK)
                MelonLogger.Error($"[SteamNet] Send failed: {result}");
        }


        // Broadcast to all server clients
        public void Broadcast(string text) {
            byte[] b = Encoding.UTF8.GetBytes(text);
            IntPtr ptr = Marshal.AllocHGlobal(b.Length);
            Marshal.Copy(b, 0, ptr, b.Length);

            foreach (var c in clients)
                if (c != HSteamNetConnection.Invalid)
                    SteamNetworkingSockets.SendMessageToConnection(c, ptr, (uint)b.Length, 0, out _);

            Marshal.FreeHGlobal(ptr);
        }


        // Utility that the server should call after SteamNetworkingSockets created: map incoming listens to connection list
        // Example helper: iterate all connections and add new ones whose GetConnectionInfo shows listen socket handle matches
        public void ScanForNewConnections()
        {
            // Not all Steamworks.NET versions provide a direct enumerate-all-connections API.
            // In theory, when an incoming connection transitions to Connected state, you can observe it through GetConnectionInfo or connection status callback.
            // Implement a callback handler (SteamNetworkingSockets has connection status callbacks in native Steam API) or periodically call GetConnectionInfo for known handles.
        }
    }
}
