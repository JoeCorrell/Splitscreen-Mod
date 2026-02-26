using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.Player;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Patches for the Hud class in splitscreen.
    /// Switches the vanilla HUD canvas from ScreenSpaceOverlay to ScreenSpaceCamera
    /// targeting P1's main camera so it renders into P1's RenderTexture and auto-scales
    /// to the viewport dimensions.
    /// </summary>
    [HarmonyPatch]
    public static class HudPatches
    {
        // Saved original state to restore on deactivation
        private static RenderMode _savedRenderMode;
        private static UnityEngine.Camera _savedWorldCamera;
        private static bool _hasSavedState;
        private static float _savedPlaneDistance;
        private static int _savedSortingOrder;
        private static float _lastHudLogTime;

        /// <summary>
        /// After HUD updates, enforce ScreenSpaceCamera mode pointing at P1's camera.
        /// Some game systems may reset the canvas render mode.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(Hud __instance)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (SplitCameraManager.Instance == null) return;

            var canvas = __instance.m_rootObject?.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                if (Time.time - _lastHudLogTime > 5f)
                {
                    _lastHudLogTime = Time.time;
                    Debug.LogWarning("[Splitscreen][HUD] Update_Postfix: canvas is null!");
                }
                return;
            }

            var p1Camera = SplitCameraManager.Instance.Player1UiCamera;
            if (p1Camera == null)
            {
                if (Time.time - _lastHudLogTime > 5f)
                {
                    _lastHudLogTime = Time.time;
                    Debug.LogWarning("[Splitscreen][HUD] Update_Postfix: p1Camera is null!");
                }
                return;
            }

            // Save original state on first run
            if (!_hasSavedState)
            {
                _savedRenderMode = canvas.renderMode;
                _savedWorldCamera = canvas.worldCamera;
                _savedPlaneDistance = canvas.planeDistance;
                _savedSortingOrder = canvas.sortingOrder;
                _hasSavedState = true;
                Debug.Log($"[Splitscreen][HUD] === SAVING CANVAS STATE ===");
                Debug.Log($"[Splitscreen][HUD]   renderMode={_savedRenderMode}");
                Debug.Log($"[Splitscreen][HUD]   worldCamera={_savedWorldCamera?.name ?? "null"}");
                Debug.Log($"[Splitscreen][HUD]   planeDistance={_savedPlaneDistance}");
                Debug.Log($"[Splitscreen][HUD]   sortingOrder={_savedSortingOrder}");
                Debug.Log($"[Splitscreen][HUD]   canvas GO='{canvas.gameObject.name}'");
            }

            // Switch to ScreenSpaceCamera so it renders into P1's RT
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != p1Camera)
            {
                Debug.Log($"[Splitscreen][HUD] Switching canvas to ScreenSpaceCamera: was renderMode={canvas.renderMode}, worldCam={canvas.worldCamera?.name}");
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = p1Camera;
                canvas.planeDistance = 1f;
                canvas.sortingOrder = 0;
                Debug.Log($"[Splitscreen][HUD] Canvas NOW: renderMode={canvas.renderMode}, worldCam={canvas.worldCamera?.name}, targetRT={p1Camera.targetTexture?.name}");
            }

            // Periodic state logging
            if (Time.time - _lastHudLogTime > 15f)
            {
                _lastHudLogTime = Time.time;
                Debug.Log($"[Splitscreen][HUD] Periodic: canvas renderMode={canvas.renderMode}, worldCam={canvas.worldCamera?.name}, planeDistance={canvas.planeDistance}");
            }
        }

        /// <summary>
        /// Restore HUD canvas when splitscreen deactivates.
        /// </summary>
        public static void RestoreHudAnchors()
        {
            Debug.Log($"[Splitscreen][HUD] === RestoreHudAnchors ===");
            Debug.Log($"[Splitscreen][HUD]   hasSavedState={_hasSavedState}");
            Debug.Log($"[Splitscreen][HUD]   savedRenderMode={_savedRenderMode}");
            Debug.Log($"[Splitscreen][HUD]   savedWorldCamera={_savedWorldCamera?.name ?? "null"}");

            if (Hud.instance == null)
            {
                Debug.LogWarning("[Splitscreen][HUD] Hud.instance is null, can't restore");
                _hasSavedState = false;
                return;
            }

            var canvas = Hud.instance.m_rootObject?.GetComponentInParent<Canvas>();
            if (canvas != null && _hasSavedState)
            {
                Debug.Log($"[Splitscreen][HUD] Restoring canvas BEFORE: renderMode={canvas.renderMode}, worldCam={canvas.worldCamera?.name}");
                canvas.renderMode = _savedRenderMode;
                canvas.worldCamera = _savedWorldCamera;
                canvas.planeDistance = _savedPlaneDistance;
                canvas.sortingOrder = _savedSortingOrder;
                Debug.Log($"[Splitscreen][HUD] Restored canvas AFTER: renderMode={canvas.renderMode}, worldCam={canvas.worldCamera?.name}");
            }
            else
            {
                Debug.LogWarning($"[Splitscreen][HUD] Cannot restore: canvas={canvas != null}, hasSavedState={_hasSavedState}");
            }
            _hasSavedState = false;
        }
    }

    /// <summary>
    /// Swaps Player.m_localPlayer to P2 when the P2 clone's HotkeyBar runs its Update().
    /// This lets the full Update chain (data refresh + UpdateIcons) operate on P2's inventory.
    /// Also re-layers newly created icon GameObjects to Player2HudLayer.
    /// </summary>
    [HarmonyPatch]
    public static class HotkeyBarPatches
    {
        private static bool _swapped;
        private static global::Player _savedLocal;

        [HarmonyPatch(typeof(HotkeyBar), "Update")]
        [HarmonyPrefix]
        public static void Update_Prefix(HotkeyBar __instance)
        {
            _swapped = false;
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (__instance.gameObject.layer != SplitCameraManager.Player2HudLayer) return;

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null) return;

            _savedLocal = global::Player.m_localPlayer;
            global::Player.m_localPlayer = p2;
            _swapped = true;
        }

        [HarmonyPatch(typeof(HotkeyBar), "Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(HotkeyBar __instance)
        {
            if (_swapped)
            {
                global::Player.m_localPlayer = _savedLocal;
                _savedLocal = null;
                _swapped = false;
            }

            // Re-layer any newly created icon children to P2's HUD layer
            if (__instance.gameObject.layer == SplitCameraManager.Player2HudLayer)
            {
                var transforms = __instance.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                    transforms[i].gameObject.layer = SplitCameraManager.Player2HudLayer;
            }
        }
    }
}
