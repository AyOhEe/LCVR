﻿using GameNetcodeStuff;
using HarmonyLib;
using LCVR.Assets;
using LCVR.Input;
using LCVR.Networking;
using LCVR.Player;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace LCVR.Patches
{
    [LCVRPatch]
    [HarmonyPatch(typeof(PlayerControllerB), "Update")]
    public static class PlayerControllerB_Update_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            // Remove HUD rotating
            for (int i = 111; i <= 123; i++)
            {
                codes[i].opcode = OpCodes.Nop;
                codes[i].operand = null;
            }

            // Remove FOV updating
            for (int i = 305; i <= 316; i++)
            {
                codes[i].opcode = OpCodes.Nop;
                codes[i].operand = null;
            }

            // Remove Player Rig Updating
            //for (int i = 1965; i <= 1990; i++)
            //{
            //    codes[i].opcode = OpCodes.Nop;
            //    codes[i].operand = null;
            //}

            return codes.AsEnumerable();
        }
    }

    [LCVRPatch]
    [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate")]
    internal static class PlayerControllerB_LateUpdate_Patches
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            // Remove Player Rig Updating
            //for (int i = 497; i <= 516; i++)
            //{
            //    codes[i].opcode = OpCodes.Nop;
            //    codes[i].operand = null;
            //}

            // Make it so player sends position updates more frequently (Multiplayer 6 DOF looks better with this)
            codes[138].operand = 0.025f;
            codes[141].operand = 0.025f;

            return codes.AsEnumerable();
        }
    }

    [LCVRPatch]
    [HarmonyPatch]
    internal static class PlayerControllerPatches
    {
        private static bool isDead = false;

        private static readonly FieldInfo cameraUpField = typeof(PlayerControllerB).GetField("cameraUp", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void SetCameraUp(this PlayerControllerB player, float value)
        {
            cameraUpField.SetValue(player, value);
        }

        private static float GetCameraUp(this PlayerControllerB player)
        {
            return (float)cameraUpField.GetValue(player);
        }

        [HarmonyPatch(typeof(PlayerControllerB), "ScrollMouse_performed")]
        [HarmonyPrefix]
        private static bool OnScroll(PlayerControllerB __instance, ref InputAction.CallbackContext context)
        {
            if (__instance.inTerminalMenu)
                return true;

            if (Mathf.Abs(context.ReadValue<float>()) < 0.75f)
                return false;

            return true;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        [HarmonyPostfix]
        private static void UpdatePrefix(PlayerControllerB __instance)
        {
            if (__instance.IsInactivePlayer() || !__instance.IsOwner)
                return;

            __instance.localArmsMatchCamera = false;

            if (__instance.isPlayerControlled)
            {
                __instance.playerBodyAnimator.runtimeAnimatorController = AssetManager.localVrMetarig;
            }
        }

        [HarmonyPatch(typeof(PlayerControllerB), "DamagePlayer")]
        [HarmonyPostfix]
        public static void AfterDamagePlayer()
        {
            VRPlayer.VibrateController(XRNode.LeftHand, 0.1f, 0.5f);
            VRPlayer.VibrateController(XRNode.RightHand, 0.1f, 0.5f);
        }

        [HarmonyPatch(typeof(PlayerControllerB), "PlayerLookInput")]
        [HarmonyPostfix]
        private static void AfterPlayerLookInput(PlayerControllerB __instance)
        {
            // Handle spectator camera pivoting
            if (isDead)
            {
                var movement = Actions.XR_RightHand_Thumbstick.ReadValue<Vector2>() * Plugin.Config.SpectateCameraSpeedModifier.Value;

                __instance.spectateCameraPivot.Rotate(new Vector3(0, movement.x, 0));
                __instance.SetCameraUp(__instance.GetCameraUp() - movement.y);
                __instance.SetCameraUp(Mathf.Clamp(__instance.GetCameraUp(), -80, 80));
                __instance.spectateCameraPivot.transform.localEulerAngles = new Vector3(__instance.GetCameraUp(), __instance.spectateCameraPivot.transform.localEulerAngles.y, 0);

                return;
            }

            var rot = Actions.XR_HeadRotation.ReadValue<Quaternion>().eulerAngles.x;

            if (rot > 180)
            {
                rot -= 360;
            }

            __instance.SetCameraUp(rot);
        }

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.SpawnPlayerAnimation))]
        [HarmonyPrefix]
        private static bool OnPlayerSpawnAnimation()
        {
            return false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "SetHoverTipAndCurrentInteractTrigger")]
        [HarmonyPrefix]
        private static bool SetHoverTipAndCurrentInteractTriggerPrefix(PlayerControllerB __instance)
        {
            if (__instance.isGrabbingObjectAnimation)
                return false;

            var ray = new Ray(__instance.gameplayCamera.transform.position, __instance.gameplayCamera.transform.forward);
            if (!__instance.isFreeCamera && Physics.SphereCast(ray, 0.5f, out var hit, 5, 8))
                hit.collider.gameObject.GetComponent<PlayerControllerB>()?.ShowNameBillboard();

            return false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "ClickHoldInteraction")]
        [HarmonyPrefix]
        private static bool ClickHoldInteractionPrefix()
        {
            return false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "KillPlayer")]
        [HarmonyPostfix]
        private static void OnPlayerDeath(PlayerControllerB __instance)
        {
            if (!__instance.IsOwner)
                return;

            isDead = true;

            Logger.Log("VR Player died");

            __instance.GetComponent<VRPlayer>().OnDeath();
        }

        // Shush I'm counting this as a player controller patch
        [HarmonyPatch(typeof(StartOfRound), "ReviveDeadPlayers")]
        [HarmonyPostfix]
        private static void OnPlayerRevived(StartOfRound __instance)
        {
            if (!isDead)
                return;

            isDead = false;

            Logger.Log("VR Player revived");

            __instance.localPlayerController.GetComponent<VRPlayer>().OnRevive();
        }

        [HarmonyPatch(typeof(PlayerControllerB), "SwitchToItemSlot")]
        [HarmonyPostfix]
        private static void SwitchedToItemSlot(PlayerControllerB __instance)
        {
            // Ignore if it's someone else, that is handled by the universal patch
            if (!__instance.IsOwner)
                return;

            // Find held item
            var item = __instance.currentlyHeldObjectServer;
            if (item == null)
                return;

            // Add or enable VR item script on item if there is one for this item
            if (Player.Items.items.TryGetValue(item.itemProperties.itemName, out var type))
            {
                var component = (MonoBehaviour)item.GetComponent(type);
                if (component == null)
                    item.gameObject.AddComponent(type);
                else
                    component.enabled = true;
            }
        }

        private static bool IsInactivePlayer(this PlayerControllerB player)
        {
            if (player == StartOfRound.Instance.localPlayerController)
                return false;

            return !player.transform.Find("ScavengerModel/metarig/CameraContainer/MainCamera").GetComponent<Camera>().enabled;
        }
    }

    [LCVRPatch(LCVRPatchTarget.Universal)]
    [HarmonyPatch]
    internal static class UniversalPlayerControllerPatches
    {
        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        [HarmonyPostfix]
        private static void UpdatePrefix(PlayerControllerB __instance)
        {
            if (!__instance.IsOwner)
            {
                var networkPlayer = __instance.GetComponent<VRNetPlayer>();
                if (networkPlayer != null)
                {
                    __instance.playerBodyAnimator.runtimeAnimatorController = AssetManager.remoteVrMetarig;
                }
                // Used to restore the original metarig if a VR player leaves and a non-vr players join in their place
                else
                {
                    __instance.playerBodyAnimator.runtimeAnimatorController = __instance.playersManager.otherClientsAnimatorController;
                }
            }
        }

        [HarmonyPatch(typeof(PlayerControllerB), "SwitchToItemSlot")]
        [HarmonyPostfix]
        private static void SwitchedToItemSlot(PlayerControllerB __instance)
        {
            // Ignore if it's us, we have the VR patch for that if we're in VR
            if (__instance.IsOwner)
                return;

            // Find held item
            var item = __instance.currentlyHeldObjectServer;
            if (item == null)
                return;

            // Find remote VR player, if they're not VR then we don't have to set up special VR items
            var remotePlayer = __instance.GetComponent<VRNetPlayer>();
            if (remotePlayer == null)
                return;

            Logger.LogDebug(item.itemProperties.itemName);

            // Add or enable VR item script on item if there is one for this item
            if (Player.Items.items.TryGetValue(item.itemProperties.itemName, out var type))
            {
                var component = (MonoBehaviour)item.GetComponent(type);
                if (component == null)
                    item.gameObject.AddComponent(type);
                else
                    component.enabled = true;
            }
        }
    }
}
