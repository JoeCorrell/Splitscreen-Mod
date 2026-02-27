using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// When splitscreen is active (or armed), force the hosted world to accept
    /// LAN connections so other players on the same network can join.
    /// Uses reflection since m_openServer/m_publicServer are private.
    /// </summary>
    [HarmonyPatch]
    public static class LanHostingPatches
    {
        private static FieldInfo _openServerField;
        private static FieldInfo _publicServerField;
        private static bool _fieldsCached;

        private static void CacheFields()
        {
            if (_fieldsCached) return;
            _fieldsCached = true;

            var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            _openServerField = typeof(ZNet).GetField("m_openServer", flags);
            _publicServerField = typeof(ZNet).GetField("m_publicServer", flags);

            Debug.Log($"[Splitscreen][LAN] Field cache: m_openServer={_openServerField != null}, m_publicServer={_publicServerField != null}");
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        [HarmonyPostfix]
        public static void ZNet_Start_Postfix(ZNet __instance)
        {
            var mgr = SplitScreenManager.Instance;
            if (mgr == null) return;

            // Only apply when splitscreen is armed, in menu split, or active
            if (mgr.State != SplitscreenState.MenuSplit &&
                mgr.State != SplitscreenState.AwaitingP2Character &&
                mgr.State != SplitscreenState.Armed &&
                mgr.State != SplitscreenState.Active) return;

            // Check config
            var config = SplitscreenPlugin.Instance?.SplitConfig;
            if (config != null && !config.ForceLanHosting.Value) return;

            // Only on the server (host) side
            if (!__instance.IsServer()) return;

            CacheFields();

            if (_openServerField == null || _publicServerField == null)
            {
                Debug.LogWarning("[Splitscreen][LAN] Cannot force LAN hosting: fields not found via reflection");
                return;
            }

            bool wasOpen = (bool)_openServerField.GetValue(__instance);
            bool wasPublic = (bool)_publicServerField.GetValue(__instance);

            _openServerField.SetValue(__instance, true);
            _publicServerField.SetValue(__instance, false); // LAN only, not global server list

            Debug.Log($"[Splitscreen][LAN] Forced LAN hosting: openServer={wasOpen}->true, publicServer={wasPublic}->false");
        }
    }
}
