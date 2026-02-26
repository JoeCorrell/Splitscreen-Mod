using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Diagnostic logging for all world interactions.
    /// Logs player context (P1/P2) when interacting with chests, crafting stations, doors, etc.
    /// </summary>
    [HarmonyPatch]
    public static class InteractionLoggingPatches
    {
        private static string LocalName() => global::Player.m_localPlayer?.GetPlayerName() ?? "null";

        private static string PlayerTag(Humanoid h)
        {
            if (h == null) return "null";
            string name = h.GetHoverName() ?? "?";
            bool isP2 = SplitScreenManager.Instance?.PlayerManager?.IsPlayer2(h as global::Player) == true;
            return $"'{name}' (P{(isP2 ? 2 : 1)})";
        }

        // --- Container (Chests) ---

        [HarmonyPatch(typeof(Container), "Interact")]
        [HarmonyPrefix]
        public static void Container_Interact_Prefix(Container __instance, Humanoid character)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            SplitscreenLog.Log("Interact", $"Container.Interact: player={PlayerTag(character)}, container='{__instance.m_name}', m_localPlayer='{LocalName()}'");
        }

        // --- CraftingStation ---

        [HarmonyPatch(typeof(CraftingStation), "Interact")]
        [HarmonyPrefix]
        public static void CraftingStation_Interact_Prefix(CraftingStation __instance, Humanoid user)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            SplitscreenLog.Log("Interact", $"CraftingStation.Interact: player={PlayerTag(user)}, station='{__instance.m_name}', m_localPlayer='{LocalName()}'");
        }

        // --- Door ---

        [HarmonyPatch(typeof(Door), "Interact")]
        [HarmonyPrefix]
        public static void Door_Interact_Prefix(Door __instance, Humanoid character)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            SplitscreenLog.Log("Interact", $"Door.Interact: player={PlayerTag(character)}, door='{__instance.gameObject.name}', m_localPlayer='{LocalName()}'");
        }

        // --- Fireplace ---

        [HarmonyPatch(typeof(Fireplace), "Interact")]
        [HarmonyPrefix]
        public static void Fireplace_Interact_Prefix(Fireplace __instance, Humanoid user)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            SplitscreenLog.Log("Interact", $"Fireplace.Interact: player={PlayerTag(user)}, fireplace='{__instance.m_name}', m_localPlayer='{LocalName()}'");
        }

        // --- Fermenter ---

        [HarmonyPatch(typeof(Fermenter), "Interact")]
        [HarmonyPrefix]
        public static void Fermenter_Interact_Prefix(Fermenter __instance, Humanoid user)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            SplitscreenLog.Log("Interact", $"Fermenter.Interact: player={PlayerTag(user)}, m_localPlayer='{LocalName()}'");
        }

        // --- Sign ---

        [HarmonyPatch(typeof(Sign), "Interact")]
        [HarmonyPrefix]
        public static void Sign_Interact_Prefix(Sign __instance, Humanoid character)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            SplitscreenLog.Log("Interact", $"Sign.Interact: player={PlayerTag(character)}, m_localPlayer='{LocalName()}'");
        }

        // --- ItemStand ---

        [HarmonyPatch(typeof(ItemStand), "Interact")]
        [HarmonyPrefix]
        public static void ItemStand_Interact_Prefix(ItemStand __instance, Humanoid user)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            SplitscreenLog.Log("Interact", $"ItemStand.Interact: player={PlayerTag(user)}, m_localPlayer='{LocalName()}'");
        }

        // --- Beehive ---

        [HarmonyPatch(typeof(Beehive), "Interact")]
        [HarmonyPrefix]
        public static void Beehive_Interact_Prefix(Beehive __instance, Humanoid character)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            SplitscreenLog.Log("Interact", $"Beehive.Interact: player={PlayerTag(character)}, m_localPlayer='{LocalName()}'");
        }

        // --- Piece.CanBeRemoved: Allow P2 to remove pieces placed by P1 ---

        [HarmonyPatch(typeof(Piece), "CanBeRemoved")]
        [HarmonyPostfix]
        public static void Piece_CanBeRemoved_Postfix(Piece __instance, ref bool __result)
        {
            if (__result) return;
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null) return;

            // If the piece was placed by P1, allow P2 to remove it (they're on the same team)
            var p1 = global::Player.m_localPlayer;
            if (p1 != null && __instance.GetCreator() == p1.GetPlayerID())
            {
                __result = true;
                if (SplitscreenLog.ShouldLog("Piece.remove", 2f))
                    SplitscreenLog.Log("Interact", $"Piece.CanBeRemoved: allowed P2 to remove P1's piece '{__instance.m_name}'");
            }
        }
    }
}
