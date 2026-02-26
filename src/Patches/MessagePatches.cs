using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.HUD;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Routes Player.Message and MessageHud.ShowMessage to the correct player's HUD.
    /// Without this, all messages ("Item picked up", "Boss defeated", etc.) only show on P1's HUD.
    /// </summary>
    [HarmonyPatch]
    public static class MessagePatches
    {
        /// <summary>
        /// When Player.Message is called on P2, redirect to P2's HUD message display.
        /// </summary>
        [HarmonyPatch(typeof(global::Player), "Message")]
        [HarmonyPrefix]
        public static bool Player_Message_Prefix(global::Player __instance, MessageHud.MessageType type, string msg, int amount, Sprite icon)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return true;

            var mgr = SplitScreenManager.Instance.PlayerManager;
            if (mgr == null || !mgr.IsPlayer2(__instance)) return true;

            // Route to P2's message display
            var p2Hud = Player2HudUpdater.Instance;
            if (p2Hud != null)
            {
                p2Hud.ShowMessage(type, msg, amount, icon);
                SplitscreenLog.Log("Message", $"P2 message: type={type}, msg='{msg}', amount={amount}");
                return false; // Skip original (which would show on P1's HUD)
            }

            // Fallback: let the original run (will show on P1's HUD)
            SplitscreenLog.Warn("Message", $"P2 message fallback (no P2 HUD): type={type}, msg='{msg}'");
            return true;
        }

        /// <summary>
        /// Log MessageHud.ShowMessage calls with player context.
        /// </summary>
        [HarmonyPatch(typeof(MessageHud), "ShowMessage")]
        [HarmonyPrefix]
        public static void MessageHud_ShowMessage_Prefix(MessageHud.MessageType type, string text)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            if (SplitscreenLog.ShouldLog("MessageHud.show", 1f))
                SplitscreenLog.Log("MessageHud", $"ShowMessage: type={type}, text='{text}', context=P{SplitscreenLog.CurrentPlayerIndex}");
        }
    }
}
