using HarmonyLib;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.Patches
{
    [HarmonyPatch]
    public static class FejdStartupPatches
    {
        [HarmonyPatch(typeof(FejdStartup), "OnWorldStart")]
        [HarmonyPrefix]
        public static bool OnWorldStart_Prefix(FejdStartup __instance)
        {
            var mgr = SplitScreenManager.Instance;
            if (mgr == null)
            {
                return true;
            }

            if (mgr.TryGateWorldStart(__instance))
            {
                MainMenuPatches.RefreshButtonState();
                return false;
            }

            MainMenuPatches.RefreshButtonState();
            return true;
        }
    }
}
