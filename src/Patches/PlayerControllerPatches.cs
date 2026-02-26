using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.Input;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Patches for PlayerController to route input correctly in splitscreen.
    ///
    /// In normal Valheim, PlayerController reads from ZInput (global) and applies to its Player.
    /// In splitscreen, we need:
    /// - Player 1's controller reads from Gamepad 0
    /// - Player 2's controller reads from Gamepad 1
    ///
    /// Since Player 2's controls are driven by SplitPlayerManager.UpdatePlayer2Controls(),
    /// we need to prevent the default PlayerController on Player 2 from reading global input.
    /// </summary>
    [HarmonyPatch]
    public static class PlayerControllerPatches
    {
        private static bool _loggedP2Skip;

        /// <summary>
        /// Skip FixedUpdate for Player 2's PlayerController.
        /// We handle Player 2's input ourselves in SplitPlayerManager.
        /// </summary>
        [HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
        [HarmonyPrefix]
        public static bool FixedUpdate_Prefix(PlayerController __instance)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return true;

            // Get the Player component on this same object
            var player = __instance.GetComponent<global::Player>();
            if (player == null) return true;

            var playerMgr = SplitScreenManager.Instance.PlayerManager;
            if (playerMgr != null && playerMgr.IsPlayer2(player))
            {
                if (!_loggedP2Skip)
                {
                    Debug.Log("[Splitscreen][Patch] Skipping PlayerController.FixedUpdate for Player 2 (we drive P2 controls ourselves)");
                    _loggedP2Skip = true;
                }
                return false;
            }

            // Player 1 proceeds with normal FixedUpdate
            return true;
        }

        /// <summary>
        /// Skip LateUpdate for Player 2's PlayerController (camera look handling).
        /// We handle this in SplitPlayerManager.
        /// </summary>
        [HarmonyPatch(typeof(PlayerController), "LateUpdate")]
        [HarmonyPrefix]
        public static bool LateUpdate_Prefix(PlayerController __instance)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return true;

            var player = __instance.GetComponent<global::Player>();
            if (player == null) return true;

            var playerMgr = SplitScreenManager.Instance.PlayerManager;
            if (playerMgr != null && playerMgr.IsPlayer2(player))
            {
                return false; // Skip - we handle Player 2's look in our update
            }

            return true;
        }
    }
}
