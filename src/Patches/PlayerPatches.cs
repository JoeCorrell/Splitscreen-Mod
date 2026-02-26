using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.Player;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Patches for the Player class to support multiple local players.
    ///
    /// Key issues solved:
    /// - Player.m_localPlayer is a static singleton. We need Player 2 to also be recognized
    ///   as "local" for physics/animation but NOT override m_localPlayer.
    /// - Player.SetLocalPlayer() is called on spawn - we prevent Player 2 from stealing it.
    /// - Various methods that reference m_localPlayer need to be context-aware.
    /// </summary>
    [HarmonyPatch]
    public static class PlayerPatches
    {
        // Rate-limit logging for per-frame patches
        private static float _lastSwapLogTime;

        /// <summary>
        /// Prevent Player 2 from overwriting m_localPlayer when spawned.
        /// Player 1 should always remain the "main" local player for singleton systems.
        /// </summary>
        [HarmonyPatch(typeof(global::Player), "SetLocalPlayer")]
        [HarmonyPrefix]
        public static bool SetLocalPlayer_Prefix(global::Player __instance)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return true;

            var playerMgr = SplitScreenManager.Instance.PlayerManager;
            if (playerMgr == null) return true;

            // Block during Player 2 instantiation (Player2 field not set yet)
            if (playerMgr.IsSpawningPlayer2)
            {
                Debug.Log($"[Splitscreen][Patch] BLOCKED SetLocalPlayer during P2 spawn (instance={__instance.GetHashCode()})");
                return false;
            }

            // If this is Player 2, don't let it steal m_localPlayer
            if (playerMgr.IsPlayer2(__instance))
            {
                Debug.Log($"[Splitscreen][Patch] BLOCKED SetLocalPlayer for Player 2 (instance={__instance.GetHashCode()})");
                return false;
            }

            Debug.Log($"[Splitscreen][Patch] ALLOWED SetLocalPlayer for Player 1 (instance={__instance.GetHashCode()}, name={__instance.GetPlayerName()})");
            return true; // Player 1 proceeds normally
        }

        /// <summary>
        /// During Player 2's FixedUpdate, temporarily set m_localPlayer to Player 2.
        /// This prevents the self-destruction check (Player.cs line 740-744) which destroys
        /// any owned Player that isn't m_localPlayer. It also makes all the Player logic
        /// (combat, dodge, crouch, etc.) work correctly for Player 2.
        /// </summary>
        [HarmonyPatch(typeof(global::Player), "FixedUpdate")]
        [HarmonyPrefix]
        public static void FixedUpdate_Prefix(global::Player __instance, ref global::Player __state)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var mgr = SplitScreenManager.Instance.PlayerManager;
            if (mgr != null && mgr.IsPlayer2(__instance))
            {
                // Save Player 1 ref, temporarily make Player 2 the "local player"
                __state = global::Player.m_localPlayer;
                global::Player.m_localPlayer = __instance;

                if (Time.time - _lastSwapLogTime > 10f)
                {
                    _lastSwapLogTime = Time.time;
                    Debug.Log($"[Splitscreen][Patch] FixedUpdate: swapped m_localPlayer to P2 (P1={__state?.GetPlayerName()}, P2={__instance.GetPlayerName()})");
                }
            }
        }

        [HarmonyPatch(typeof(global::Player), "FixedUpdate")]
        [HarmonyPostfix]
        public static void FixedUpdate_Postfix(global::Player __instance, global::Player __state)
        {
            // Restore Player 1 as m_localPlayer after Player 2's FixedUpdate
            if (__state != null)
            {
                global::Player.m_localPlayer = __state;
            }
        }

        /// <summary>
        /// Same pattern for Update - prevent self-destruction and make game logic work.
        /// </summary>
        [HarmonyPatch(typeof(global::Player), "Update")]
        [HarmonyPrefix]
        public static void Update_Prefix(global::Player __instance, ref global::Player __state)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var mgr = SplitScreenManager.Instance.PlayerManager;
            if (mgr != null && mgr.IsPlayer2(__instance))
            {
                __state = global::Player.m_localPlayer;
                global::Player.m_localPlayer = __instance;
            }
        }

        [HarmonyPatch(typeof(global::Player), "Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(global::Player __instance, global::Player __state)
        {
            if (__state != null)
            {
                global::Player.m_localPlayer = __state;
            }
        }

        /// <summary>
        /// When the game checks IsOwner on a ZNetView, Player 2's objects should also be "owned".
        /// This ensures Player 2's character processes correctly on the local machine.
        /// </summary>
        [HarmonyPatch(typeof(ZNetView), "IsOwner")]
        [HarmonyPostfix]
        public static void ZNetView_IsOwner_Postfix(ZNetView __instance, ref bool __result)
        {
            if (__result) return;
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null) return;

            // If this ZNetView belongs to Player 2, treat it as owned
            if (__instance.gameObject == p2.gameObject)
            {
                __result = true;
            }
        }

        /// <summary>
        /// When the game saves the player profile, also save Player 2's data.
        /// </summary>
        [HarmonyPatch(typeof(Game), "SavePlayerProfile")]
        [HarmonyPostfix]
        public static void SavePlayerProfile_Postfix()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            Debug.Log("[Splitscreen][Patch] Game.SavePlayerProfile -> also saving P2 profile");
            SplitScreenManager.Instance.PlayerManager?.SavePlayer2Profile();
        }

        /// <summary>
        /// When the game shuts down, ensure Player 2 is cleaned up.
        /// </summary>
        [HarmonyPatch(typeof(Game), "Shutdown")]
        [HarmonyPrefix]
        public static void Shutdown_Prefix()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            Debug.Log("[Splitscreen][Patch] Game.Shutdown -> saving P2 profile");
            SplitScreenManager.Instance.PlayerManager?.SavePlayer2Profile();
        }

        /// <summary>
        /// When Player 2 dies, handle respawn separately.
        /// </summary>
        [HarmonyPatch(typeof(global::Player), "OnDeath")]
        [HarmonyPostfix]
        public static void OnDeath_Postfix(global::Player __instance)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var playerMgr = SplitScreenManager.Instance.PlayerManager;
            if (playerMgr == null) return;

            if (playerMgr.IsPlayer2(__instance))
            {
                Debug.Log("[Splitscreen][Patch] Player 2 died! Starting respawn coroutine (10s delay)");
                SplitScreenManager.Instance.StartCoroutine(RespawnPlayer2Coroutine());
            }
        }

        private static System.Collections.IEnumerator RespawnPlayer2Coroutine()
        {
            yield return new WaitForSeconds(10f);

            var mgr = SplitScreenManager.Instance;
            if (mgr == null || !mgr.SplitscreenActive)
            {
                Debug.Log("[Splitscreen][Patch] Respawn cancelled: splitscreen no longer active");
                yield break;
            }

            // Save reference to the profile before despawning
            var savedProfile = mgr.PlayerManager.Player2Profile;
            Debug.Log($"[Splitscreen][Patch] Respawning P2 with profile '{savedProfile?.GetName()}'");
            mgr.PlayerManager.DespawnSecondPlayer();
            yield return new WaitForSeconds(1f);
            mgr.PlayerManager.SpawnSecondPlayer(savedProfile);
        }
    }
}
