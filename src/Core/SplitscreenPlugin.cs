using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Config;
using ValheimSplitscreen.HUD;
using ValheimSplitscreen.Input;
using ValheimSplitscreen.Player;

namespace ValheimSplitscreen.Core
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class SplitscreenPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.splitscreen.valheim";
        public const string PluginName = "Valheim Splitscreen";
        public const string PluginVersion = "1.0.0";

        public static SplitscreenPlugin Instance { get; private set; }
        public static Harmony HarmonyInstance { get; private set; }

        public SplitscreenConfig SplitConfig { get; private set; }
        public SplitScreenManager Manager { get; private set; }

        private void Awake()
        {
            Instance = this;
            Game.isModded = true;

            SplitConfig = new SplitscreenConfig(Config);
            Manager = gameObject.AddComponent<SplitScreenManager>();

            Logger.LogInfo($"{PluginName} v{PluginVersion} - Applying Harmony patches...");

            HarmonyInstance = new Harmony(PluginGUID);
            try
            {
                HarmonyInstance.PatchAll(typeof(SplitscreenPlugin).Assembly);

                // Log all successfully applied patches
                int patchCount = 0;
                foreach (var method in HarmonyInstance.GetPatchedMethods())
                {
                    var info = Harmony.GetPatchInfo(method);
                    int count = (info.Prefixes?.Count ?? 0) + (info.Postfixes?.Count ?? 0) + (info.Transpilers?.Count ?? 0);
                    Logger.LogInfo($"  Patched: {method.DeclaringType?.Name}.{method.Name} ({count} patches)");
                    patchCount++;
                }
                Logger.LogInfo($"{PluginName} v{PluginVersion} loaded! ({patchCount} methods patched)");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"CRITICAL: Harmony PatchAll FAILED! No patches applied!");
                Logger.LogError($"Exception: {ex.GetType().Name}: {ex.Message}");
                Logger.LogError($"Stack: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Logger.LogError($"Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    Logger.LogError($"Inner Stack: {ex.InnerException.StackTrace}");
                }
            }
        }

        private void OnDestroy()
        {
            HarmonyInstance?.UnpatchSelf();
            Instance = null;
        }
    }
}
