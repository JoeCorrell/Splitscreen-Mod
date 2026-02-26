using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Patches for the Game class to support splitscreen.
    ///
    /// Key changes:
    /// - Prevent the game from pausing when splitscreen is active (2 players = "multiplayer").
    /// - Adjust difficulty scaling to account for Player 2.
    /// - Handle the save system for both players.
    /// </summary>
    [HarmonyPatch]
    public static class GamePatches
    {
        /// <summary>
        /// In splitscreen, don't allow pausing since there are effectively 2 players.
        /// This mirrors the behavior when other players are connected in multiplayer.
        /// </summary>
        [HarmonyPatch(typeof(Game), "IsPaused")]
        [HarmonyPostfix]
        public static void IsPaused_Postfix(ref bool __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            // Don't pause in splitscreen - treat it like multiplayer
            __result = false;
        }

        /// <summary>
        /// Adjust the player difficulty count to include Player 2.
        /// </summary>
        [HarmonyPatch(typeof(Game), "GetPlayerDifficulty")]
        [HarmonyPostfix]
        public static void GetPlayerDifficulty_Postfix(ref int __result, Vector3 pos)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null) return;

            // Ensure Player 2 is counted for difficulty
            float dist = Vector3.Distance(
                new Vector3(pos.x, 0, pos.z),
                new Vector3(p2.transform.position.x, 0, p2.transform.position.z)
            );

            if (dist < Game.instance.m_difficultyScaleRange && __result < 2)
            {
                __result = Mathf.Max(__result, 2);
            }
        }

        /// <summary>
        /// When CanPause is checked, return false in splitscreen.
        /// This prevents the time scale from being set to 0.
        /// </summary>
        [HarmonyPatch(typeof(Game), "CanPause")]
        [HarmonyPostfix]
        public static void CanPause_Postfix(ref bool __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            __result = false;
        }

        /// <summary>
        /// When EverybodyIsTryingToSleep is checked, include Player 2.
        /// </summary>
        [HarmonyPatch(typeof(Game), "EverybodyIsTryingToSleep")]
        [HarmonyPostfix]
        public static void EverybodyIsTryingToSleep_Postfix(ref bool __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (!__result) return; // If already false, stay false

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null) return;

            // Player 2 must also be in bed
            var zdo = p2.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null && !zdo.GetBool(ZDOVars.s_inBed))
            {
                __result = false;
            }
        }
    }
}
