using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Patches to enable Player 2 to interact with world objects.
    /// In vanilla Valheim, interactions check against Player.m_localPlayer.
    /// We need certain interactions to also work for Player 2.
    /// </summary>
    [HarmonyPatch]
    public static class InteractionPatches
    {
        /// <summary>
        /// Bed.IsCurrent() calls private IsMine() which compares GetOwner() against
        /// Game.instance.GetPlayerProfile().GetPlayerID() (which is Player 1's profile).
        /// Player 2 has a separate profile, so we need to also check Player 2's ID.
        /// Bed.GetOwner() is private, so we read the ZDO data directly using
        /// the same key: ZDOVars.s_owner.
        /// </summary>
        [HarmonyPatch(typeof(Bed), "IsCurrent")]
        [HarmonyPostfix]
        public static void Bed_IsCurrent_Postfix(Bed __instance, ref bool __result)
        {
            if (__result) return;
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null) return;

            // Bed.GetOwner() is private - read the ZDO directly (same as GetOwner does)
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || nview.GetZDO() == null) return;

            long bedOwner = nview.GetZDO().GetLong(ZDOVars.s_owner, 0L);
            if (bedOwner != 0L && bedOwner == p2.GetPlayerID())
            {
                __result = true;
            }
        }

        /// <summary>
        /// PrivateArea.IsPermitted() is PRIVATE. Patching it with a postfix that calls
        /// IsPermitted() again would cause infinite recursion.
        ///
        /// Instead, we patch IsPermitted directly and in the postfix we read the permitted
        /// player list using Traverse (reflection) to avoid recursion.
        ///
        /// Logic: If the check was for Player 2's ID and failed, check if Player 1's ID
        /// is in the permitted list. This lets Player 2 access Player 1's wards.
        /// </summary>
        [HarmonyPatch(typeof(PrivateArea), "IsPermitted")]
        [HarmonyPostfix]
        public static void PrivateArea_IsPermitted_Postfix(PrivateArea __instance, long playerID, ref bool __result)
        {
            if (__result) return;
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null) return;

            var p1 = global::Player.m_localPlayer;
            if (p1 == null) return;

            long p1ID = p1.GetPlayerID();
            long p2ID = p2.GetPlayerID();

            // Only act if the failed check was for Player 2
            if (playerID != p2ID) return;

            // Read the permitted list via Traverse to avoid recursion
            // GetPermittedPlayers() is a separate method that just reads data, won't recurse
            var permitted = Traverse.Create(__instance).Method("GetPermittedPlayers").GetValue<List<KeyValuePair<long, string>>>();
            if (permitted == null) return;

            // Check if Player 1 is in the permitted list
            foreach (var kvp in permitted)
            {
                if (kvp.Key == p1ID)
                {
                    __result = true;
                    return;
                }
            }

            // Also check if Player 1 is the creator/owner of this ward
            var piece = __instance.GetComponent<Piece>();
            if (piece != null && piece.GetCreator() == p1ID)
            {
                __result = true;
            }
        }
    }
}
