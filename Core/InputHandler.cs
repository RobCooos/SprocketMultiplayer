using UnityEngine;
using UnityEngine.InputSystem;
using MelonLoader;
using SprocketMultiplayer.Core;
using SprocketMultiplayer.UI;

namespace SprocketMultiplayer.Core
{
    /// <summary>
    /// Handles debug input for testing spawning
    /// F7 - Initialize BlueprintSpawner
    /// F8 - Spawn tank next to player
    /// </summary>
    public class InputHandler : MonoBehaviour
    {
        private void Update()
        {
            if (Keyboard.current == null) return;

            // Ignore input if console is open
            var console = Console.Instance;
            if (console != null && Console.IsOpen && console.IsFocused()) return;

            // F7: Initialize blueprint spawner
            if (Keyboard.current.f7Key.wasPressedThisFrame)
            {
                MelonLogger.Msg("=== F7: INITIALIZING BLUEPRINT SPAWNER ===");
                BlueprintSpawner.Initialize();
                
                int count = BlueprintSpawner.GetTankCount();
                MelonLogger.Msg($"✓ Blueprint spawner ready! {count} tanks loaded.");
                
                if (count > 0) {
                    var tanks = BlueprintSpawner.GetAvailableTankIds();
                    MelonLogger.Msg("Available tanks:");
                    foreach (var tank in tanks) {
                        MelonLogger.Msg($"  - {tank}");
                    }
                }
            }

            // F8: Spawn default tank next to player
            if (Keyboard.current.f8Key.wasPressedThisFrame)
            {
                SpawnTankNextToPlayer();
            }
        }

        private void SpawnTankNextToPlayer()
        {
            var player = GameObject.Find("Player");
            if (player == null)
            {
                MelonLogger.Warning("Player object not found!");
                return;
            }

            string tankId = BlueprintSpawner.GetDefaultTankId();
            if (string.IsNullOrEmpty(tankId))
            {
                MelonLogger.Warning("No tanks available! Press F7 first.");
                return;
            }

            // Spawn position: 5 units to the left of player, 1 unit up
            Vector3 spawnPos = player.transform.position - player.transform.right * 5f + Vector3.up * 1f;
            Quaternion spawnRot = Quaternion.LookRotation(player.transform.forward, Vector3.up);

            GameObject tank = BlueprintSpawner.SpawnTank(tankId, spawnPos, spawnRot);
            
            if (tank != null)
            {
                MelonLogger.Msg($"✓ Tank '{tankId}' spawned at {spawnPos}");
            }
            else
            {
                MelonLogger.Error("✗ Tank spawn failed!");
            }
        }
    }
}