using System;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace SprocketMultiplayer.Core.AI {
    public static class Spawner {
        public static void SpawnVehicle(string blueprintName, int team, bool attachAI, Vector3? position = null) {
            // Default spawn position
            Vector3 spawnPosition = position ?? Vector3.zero;

            // Step 1: Find blueprint path in user directory
            string userDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string blueprintPath = Path.Combine(userDir, "My Games", "Sprocket", "Factions", "test", "Blueprints", "Vehicles", blueprintName + ".blueprint");

            if (!File.Exists(blueprintPath)) {
                MelonLogger.Error($"Blueprint file not found: {blueprintPath}");
                return;
            }

            MelonLogger.Msg($"Blueprint file found: {blueprintPath}");
            FileInfo info = new FileInfo(blueprintPath);
            MelonLogger.Msg($"File size: {info.Length} bytes, last modified: {info.LastWriteTime}");

            // Step 2: "Load" blueprint placeholder
            // Since actual blueprint loading API is unknown, we just log
            MelonLogger.Msg($"[DEBUG] Would load blueprint here: {blueprintName}");

            // Step 3: Spawn a placeholder GameObject
            GameObject tankGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tankGO.transform.position = spawnPosition;
            tankGO.name = $"Vehicle_{blueprintName}";

            // Step 4: Team and AI placeholders
            // Since the actual Vehicle component is unknown, we attach placeholder scripts/components
            tankGO.AddComponent<VehiclePlaceholder>().TeamID = team;

            if (attachAI) {
                tankGO.AddComponent<VehicleAIPlaceholder>();
            }

            MelonLogger.Msg($"[DEBUG] Spawned placeholder vehicle for team {team} with AI={attachAI}");
        }
    }

    // Placeholder component to store team info
    public class VehiclePlaceholder : MonoBehaviour {
        public int TeamID;
    }

    // Placeholder AI component
    public class VehicleAIPlaceholder : MonoBehaviour {
        private void Start() {
            MelonLogger.Msg($"[DEBUG] AI component attached to {gameObject.name}");
        }
    }
}
