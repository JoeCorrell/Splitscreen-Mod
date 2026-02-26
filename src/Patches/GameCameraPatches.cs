using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.Input;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Patches for GameCamera to support RenderTexture-based splitscreen.
    /// Enforces that the main camera keeps rendering to P1's RT with full rect.
    /// </summary>
    [HarmonyPatch]
    public static class GameCameraPatches
    {
        private static float _lastViewportFixLogTime;
        private static int _viewportFixCount;
        private static float _lastPeriodicLogTime;

        /// <summary>
        /// After the main camera sets up each frame, ensure our RT targeting stays correct.
        /// </summary>
        [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
        [HarmonyPostfix]
        public static void LateUpdate_Postfix(GameCamera __instance)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (SplitCameraManager.Instance == null) return;

            var cam = __instance.GetComponent<UnityEngine.Camera>();
            if (cam == null) return;

            var p1RT = SplitCameraManager.Instance.P1RenderTexture;
            if (p1RT == null) return;

            bool needsFix = false;
            string fixDetails = "";

            // Enforce targetTexture = P1's RT
            if (cam.targetTexture != p1RT)
            {
                fixDetails += $"targetTexture was {(cam.targetTexture != null ? cam.targetTexture.name : "null")}; ";
                cam.targetTexture = p1RT;
                needsFix = true;
            }

            // Enforce full rect
            Rect fullRect = new Rect(0f, 0f, 1f, 1f);
            if (cam.rect != fullRect)
            {
                fixDetails += $"rect was {cam.rect}; ";
                cam.rect = fullRect;
                needsFix = true;
            }

            // Also enforce sky camera
            if (__instance.m_skyCamera != null)
            {
                if (__instance.m_skyCamera.targetTexture != p1RT)
                {
                    fixDetails += $"skyRT was {(__instance.m_skyCamera.targetTexture != null ? __instance.m_skyCamera.targetTexture.name : "null")}; ";
                    __instance.m_skyCamera.targetTexture = p1RT;
                    needsFix = true;
                }
                if (__instance.m_skyCamera.rect != fullRect)
                {
                    fixDetails += $"skyRect was {__instance.m_skyCamera.rect}; ";
                    __instance.m_skyCamera.rect = fullRect;
                    needsFix = true;
                }
            }

            if (needsFix)
            {
                _viewportFixCount++;
                if (Time.time - _lastViewportFixLogTime > 5f)
                {
                    Debug.Log($"[Splitscreen][GameCam] Fixed P1 camera RT/rect {_viewportFixCount} times in last 5s. Last fix: {fixDetails}");
                    _lastViewportFixLogTime = Time.time;
                    _viewportFixCount = 0;
                }
            }

            // Periodic status
            if (Time.time - _lastPeriodicLogTime > 15f)
            {
                _lastPeriodicLogTime = Time.time;
                Debug.Log($"[Splitscreen][GameCam] Status: cam.targetTexture={cam.targetTexture?.name}, cam.rect={cam.rect}, cam.enabled={cam.enabled}");
                if (__instance.m_skyCamera != null)
                    Debug.Log($"[Splitscreen][GameCam] SkyStatus: targetTexture={__instance.m_skyCamera.targetTexture?.name}, rect={__instance.m_skyCamera.rect}, enabled={__instance.m_skyCamera.enabled}");
            }
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
        [HarmonyPostfix]
        public static void UpdateMouseCapture_Postfix()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            if (SplitInputManager.Instance != null && SplitInputManager.Instance.Player1UsesKeyboard)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
