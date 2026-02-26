using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.Player;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Patches for the Hud class in splitscreen.
    /// Switches the vanilla HUD canvas from ScreenSpaceOverlay to ScreenSpaceCamera
    /// targeting P1's UI camera so it renders into P1's RenderTexture.
    /// Also switches CanvasScaler to ConstantPixelSize so the coordinate space
    /// matches the RT dimensions (not the full screen), preventing clipping.
    /// </summary>
    [HarmonyPatch]
    public static class HudPatches
    {
        // Saved original canvas state to restore on deactivation
        private static RenderMode _savedRenderMode;
        private static UnityEngine.Camera _savedWorldCamera;
        private static bool _hasSavedState;
        private static float _savedPlaneDistance;
        private static int _savedSortingOrder;

        // Saved original CanvasScaler state
        private static CanvasScaler.ScaleMode _savedScaleMode;
        private static float _savedScaleFactor;
        private static Vector2 _savedReferenceResolution;
        private static float _savedMatchWidthOrHeight;
        private static bool _hasSavedScalerState;

        private static float _lastHudLogTime;
        private static Canvas _cachedHudCanvas;

        /// <summary>
        /// After HUD updates, enforce ScreenSpaceCamera mode pointing at P1's camera
        /// and ConstantPixelSize CanvasScaler so the HUD fits the RT dimensions.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(Hud __instance)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (SplitCameraManager.Instance == null) return;

            if (_cachedHudCanvas == null)
                _cachedHudCanvas = __instance.m_rootObject?.GetComponentInParent<Canvas>();
            var canvas = _cachedHudCanvas;
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
                Debug.Log($"[Splitscreen][HUD]   renderMode={_savedRenderMode}, worldCamera={_savedWorldCamera?.name ?? "null"}");
                Debug.Log($"[Splitscreen][HUD]   planeDistance={_savedPlaneDistance}, sortingOrder={_savedSortingOrder}");
                Debug.Log($"[Splitscreen][HUD]   canvas GO='{canvas.gameObject.name}'");
            }

            // Save CanvasScaler state
            if (!_hasSavedScalerState)
            {
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    _savedScaleMode = scaler.uiScaleMode;
                    _savedScaleFactor = scaler.scaleFactor;
                    _savedReferenceResolution = scaler.referenceResolution;
                    _savedMatchWidthOrHeight = scaler.matchWidthOrHeight;
                    _hasSavedScalerState = true;
                    Debug.Log($"[Splitscreen][HUD] Saved CanvasScaler: mode={_savedScaleMode}, scaleFactor={_savedScaleFactor}, refRes={_savedReferenceResolution}, match={_savedMatchWidthOrHeight}");
                }
            }

            // Switch to ScreenSpaceCamera so it renders into P1's RT
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != p1Camera)
            {
                Debug.Log($"[Splitscreen][HUD] Switching canvas: renderMode={canvas.renderMode}->{RenderMode.ScreenSpaceCamera}, worldCam={canvas.worldCamera?.name ?? "null"}->{p1Camera.name}");
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = p1Camera;
                canvas.planeDistance = 1f;
                canvas.sortingOrder = 0;
            }

            // Switch CanvasScaler to ConstantPixelSize so it uses RT dimensions.
            // Unity's ScaleWithScreenSize uses Screen.width/height, which is the full
            // monitor resolution, NOT the RT dimensions. This causes elements on the far
            // side of the HUD to be clipped when rendering to a half-screen RT.
            // ConstantPixelSize with scaleFactor=1 makes 1 canvas unit = 1 RT pixel,
            // so the canvas coordinate space exactly matches the RT (e.g., 960x1080).
            // Anchored elements reposition naturally to the RT edges.
            var hudScaler = canvas.GetComponent<CanvasScaler>();
            if (hudScaler != null && hudScaler.uiScaleMode != CanvasScaler.ScaleMode.ConstantPixelSize)
            {
                hudScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                hudScaler.scaleFactor = 1f;
                var rt = p1Camera.targetTexture;
                Debug.Log($"[Splitscreen][HUD] Switched CanvasScaler to ConstantPixelSize, RT={rt?.width}x{rt?.height}");
            }

            // Periodic state logging
            if (Time.time - _lastHudLogTime > 15f)
            {
                _lastHudLogTime = Time.time;
                var rt = p1Camera.targetTexture;
                Debug.Log($"[Splitscreen][HUD] Periodic: renderMode={canvas.renderMode}, worldCam={canvas.worldCamera?.name}, RT={rt?.width}x{rt?.height}, scaleFactor={canvas.scaleFactor}");
            }
        }

        /// <summary>
        /// Restore HUD canvas and CanvasScaler when splitscreen deactivates.
        /// </summary>
        public static void RestoreHudAnchors()
        {
            Debug.Log($"[Splitscreen][HUD] === RestoreHudAnchors ===");

            if (Hud.instance == null)
            {
                Debug.LogWarning("[Splitscreen][HUD] Hud.instance is null, can't restore");
                _hasSavedState = false;
                _hasSavedScalerState = false;
                return;
            }

            var canvas = Hud.instance.m_rootObject?.GetComponentInParent<Canvas>();
            if (canvas != null && _hasSavedState)
            {
                canvas.renderMode = _savedRenderMode;
                canvas.worldCamera = _savedWorldCamera;
                canvas.planeDistance = _savedPlaneDistance;
                canvas.sortingOrder = _savedSortingOrder;
                Debug.Log($"[Splitscreen][HUD] Restored canvas: renderMode={canvas.renderMode}, worldCam={canvas.worldCamera?.name ?? "null"}");
            }

            if (canvas != null && _hasSavedScalerState)
            {
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = _savedScaleMode;
                    scaler.scaleFactor = _savedScaleFactor;
                    scaler.referenceResolution = _savedReferenceResolution;
                    scaler.matchWidthOrHeight = _savedMatchWidthOrHeight;
                    Debug.Log($"[Splitscreen][HUD] Restored CanvasScaler: mode={_savedScaleMode}, refRes={_savedReferenceResolution}");
                }
            }

            _hasSavedState = false;
            _hasSavedScalerState = false;
            _cachedHudCanvas = null;
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
        private static readonly Dictionary<int, int> _cachedChildCountByBar = new Dictionary<int, int>();

        [HarmonyPatch(typeof(HotkeyBar), "Update")]
        [HarmonyPrefix]
        public static void Update_Prefix(HotkeyBar __instance, out global::Player __state)
        {
            __state = null;
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (__instance.gameObject.layer != SplitCameraManager.Player2HudLayer) return;

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null) return;

            __state = global::Player.m_localPlayer;
            global::Player.m_localPlayer = p2;

            if (SplitscreenLog.ShouldLog("HotkeyBar.swap", 10f))
                SplitscreenLog.Log("HotkeyBar", $"Swapped m_localPlayer to P2 for HotkeyBar on layer {__instance.gameObject.layer}");
        }

        [HarmonyPatch(typeof(HotkeyBar), "Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(HotkeyBar __instance, global::Player __state)
        {
            if (__state != null)
            {
                global::Player.m_localPlayer = __state;
            }

            // Re-layer children to P2's HUD layer only when child count changes
            if (__instance.gameObject.layer == SplitCameraManager.Player2HudLayer)
            {
                int id = __instance.GetInstanceID();
                int childCount = __instance.transform.childCount;
                bool missingCachedCount = !_cachedChildCountByBar.TryGetValue(id, out int cachedCount);
                if (missingCachedCount || childCount != cachedCount)
                {
                    _cachedChildCountByBar[id] = childCount;
                    var transforms = __instance.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < transforms.Length; i++)
                        transforms[i].gameObject.layer = SplitCameraManager.Player2HudLayer;
                }
            }
        }
    }
}
