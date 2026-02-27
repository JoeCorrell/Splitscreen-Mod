using HarmonyLib;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Runs vanilla PlayerController for P2 by swapping m_localPlayer context
    /// during P2 controller updates. This preserves native bindings/layout logic.
    /// </summary>
    [HarmonyPatch]
    public static class PlayerControllerPatches
    {
        [HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
        [HarmonyPrefix]
        public static void FixedUpdate_Prefix(PlayerController __instance, out global::Player __state)
        {
            __state = null;
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var mgr = SplitScreenManager.Instance.PlayerManager;
            if (mgr == null) return;

            var player = __instance.GetComponent<global::Player>();
            if (player == null || !mgr.IsPlayer2(player)) return;

            __state = global::Player.m_localPlayer;
            global::Player.m_localPlayer = player;
            mgr.IsUpdatingPlayer2 = true;
        }

        [HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
        [HarmonyPostfix]
        public static void FixedUpdate_Postfix(global::Player __state)
        {
            if (__state == null) return;

            global::Player.m_localPlayer = __state;
            var mgr = SplitScreenManager.Instance?.PlayerManager;
            if (mgr != null) mgr.IsUpdatingPlayer2 = false;
        }

        [HarmonyPatch(typeof(PlayerController), "LateUpdate")]
        [HarmonyPrefix]
        public static void LateUpdate_Prefix(PlayerController __instance, out global::Player __state)
        {
            __state = null;
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var mgr = SplitScreenManager.Instance.PlayerManager;
            if (mgr == null) return;

            var player = __instance.GetComponent<global::Player>();
            if (player == null || !mgr.IsPlayer2(player)) return;

            __state = global::Player.m_localPlayer;
            global::Player.m_localPlayer = player;
            mgr.IsUpdatingPlayer2 = true;
        }

        [HarmonyPatch(typeof(PlayerController), "LateUpdate")]
        [HarmonyPostfix]
        public static void LateUpdate_Postfix(global::Player __state)
        {
            if (__state == null) return;

            global::Player.m_localPlayer = __state;
            var mgr = SplitScreenManager.Instance?.PlayerManager;
            if (mgr != null) mgr.IsUpdatingPlayer2 = false;
        }
    }
}
