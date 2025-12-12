using System;
using MelonLoader;
using UnityEngine;

namespace SprocketMultiplayer.Patches {
    public class VehicleControl {
        public static void AssignVehicleToPlayer(GameObject vehicleGameObject) {
            try {
                MelonLogger.Msg($"[AssignVehicle] Attempting to assign vehicle: {vehicleGameObject.name}");
                
                // 1. Find the Player GameObject
                var playerGO = GameObject.Find("Player");
                if (playerGO == null) {
                    MelonLogger.Error("Player GameObject not found!");
                    return;
                }
                
                // 2. Get the Player component
                var playerComp = playerGO.GetComponent("Sprocket.PlayerControl.Player");
                if (playerComp == null) {
                    MelonLogger.Error("Player component not found!");
                    return;
                }
                
                // 3. Get currentState field (VehicleControlPlayerState)
                var il2cppType = playerComp.GetIl2CppType();
                var bindingFlags = Il2CppSystem.Reflection.BindingFlags.Public | 
                                  Il2CppSystem.Reflection.BindingFlags.NonPublic | 
                                  Il2CppSystem.Reflection.BindingFlags.Instance;
                
                var currentStateField = il2cppType.GetField("currentState", bindingFlags);
                if (currentStateField == null) {
                    MelonLogger.Error("currentState field not found!");
                    return;
                }
                
                var currentState = currentStateField.GetValue(playerComp);
                if (currentState == null) {
                    MelonLogger.Error("currentState is null!");
                    return;
                }
                
                // 4. Cast to MonoBehaviour to access as component
                var stateComp = currentState.TryCast<MonoBehaviour>();
                if (stateComp == null) {
                    MelonLogger.Error("Couldn't cast state to MonoBehaviour!");
                    return;
                }
                
                // 5. Get vehicleController field from VehicleControlPlayerState
                var stateType = stateComp.GetIl2CppType();
                var vehicleControllerField = stateType.GetField("vehicleController", bindingFlags);
                if (vehicleControllerField == null) {
                    MelonLogger.Error("vehicleController field not found!");
                    return;
                }
                
                var vehicleController = vehicleControllerField.GetValue(stateComp);
                if (vehicleController == null) {
                    MelonLogger.Error("vehicleController is null!");
                    return;
                }
                
                // 6. Cast vehicleController to MonoBehaviour
                var vcComp = vehicleController.TryCast<MonoBehaviour>();
                if (vcComp == null) {
                    MelonLogger.Error("Couldn't cast vehicleController to MonoBehaviour!");
                    return;
                }
                
                // 7. Get the vehicle's IVehicleBehaviour component
                var vehicleBehaviour = vehicleGameObject.GetComponent("Sprocket.Vehicles.IVehicleBehaviour");
                if (vehicleBehaviour == null) {
                    MelonLogger.Error($"Vehicle {vehicleGameObject.name} doesn't have IVehicleBehaviour!");
                    return;
                }
                
                // 8. Get set_ControlledVehicle method
                var vcType = vcComp.GetIl2CppType();
                var setMethod = vcType.GetMethod("set_ControlledVehicle", bindingFlags);
                if (setMethod == null) {
                    MelonLogger.Error("set_ControlledVehicle method not found!");
                    return;
                }
                
                // 9. Assign the vehicle
                MelonLogger.Msg($"[AssignVehicle] Calling set_ControlledVehicle with {vehicleGameObject.name}");
                var args = new[] { vehicleBehaviour.Cast<Il2CppSystem.Object>() };
                setMethod.Invoke(vcComp, args);
                
                MelonLogger.Msg($"[AssignVehicle] Successfully assigned vehicle: {vehicleGameObject.name}");
            }
            catch (Exception ex) {
                MelonLogger.Error($"[AssignVehicle] Failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}