using System.Collections.Generic;

namespace SprocketMultiplayer.Core {
    public static class Sync {
        private static readonly List<ISyncModule> Modules = new List<ISyncModule>();

        /// <summary>
        /// Registers a sync module into the system
        /// </summary>
        public static void Register(ISyncModule module)
        {
            // Prevent duplicates
            if (!Modules.Contains(module))
                Modules.Add(module);
        }

        /// <summary>
        /// Called every network tick (server and client)
        /// </summary>
        public static void Tick(float deltaTime)
        {
            // Loop through all Modules and give them update time
            foreach (var module in Modules)
                module.Tick(deltaTime);
        }

        /// <summary>
        /// Routes incoming network data to all Modules.
        /// </summary>
        public static void HandlePacket(byte[] data)
        {
            // parse headers (packet type, module id, etc.) later
            // For now, just broadcast to all Modules
            foreach (var module in Modules)
                module.OnPacket(data);
        }
    }

    /// <summary>
    /// Base interface for all sync Modules.
    /// Position sync, turret sync, etc. is to be implemented later
    /// </summary>
    public interface ISyncModule {
        /// <summary>
        /// Called every network tick.
        /// </summary>
        void Tick(float deltaTime);

        /// <summary>
        /// Called when this module receives raw network data.
        /// </summary>
        void OnPacket(byte[] data);
    }
}