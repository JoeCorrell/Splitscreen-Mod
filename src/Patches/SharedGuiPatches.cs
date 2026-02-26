using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Routes shared singleton UIs (StoreGui, TextViewer) to the correct player's context.
    /// Tracks which player opened the UI and swaps m_localPlayer during Update.
    /// Also routes the canvas to the correct player's UI camera.
    /// </summary>
    [HarmonyPatch]
    public static class SharedGuiPatches
    {
        // =====================================================================
        // StoreGui (Trader)
        // =====================================================================

        private static int _storeOwnerPlayer; // 0 = P1, 1 = P2
        private static bool _storeSwapped;
        private static global::Player _storeSavedLocal;

        [HarmonyPatch(typeof(StoreGui), "Show")]
        [HarmonyPrefix]
        public static void StoreGui_Show_Prefix()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            _storeOwnerPlayer = SplitscreenLog.CurrentPlayerIndex == 2 ? 1 : 0;
            SplitscreenLog.Log("StoreGui", $"Show: opened by P{_storeOwnerPlayer + 1}");
        }

        [HarmonyPatch(typeof(StoreGui), "Update")]
        [HarmonyPrefix]
        public static void StoreGui_Update_Prefix(StoreGui __instance)
        {
            _storeSwapped = false;
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (_storeOwnerPlayer != 1) return;
            if (!StoreGui.IsVisible()) return;

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null) return;

            _storeSavedLocal = global::Player.m_localPlayer;
            global::Player.m_localPlayer = p2;
            _storeSwapped = true;

            // Route canvas to P2's UI camera
            RouteCanvasToPlayer(__instance.gameObject, 1);
        }

        [HarmonyPatch(typeof(StoreGui), "Update")]
        [HarmonyPostfix]
        public static void StoreGui_Update_Postfix()
        {
            if (_storeSwapped)
            {
                global::Player.m_localPlayer = _storeSavedLocal;
                _storeSavedLocal = null;
                _storeSwapped = false;
            }
        }

        [HarmonyPatch(typeof(StoreGui), "Hide")]
        [HarmonyPostfix]
        public static void StoreGui_Hide_Postfix()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            if (_storeOwnerPlayer == 1)
            {
                // Restore canvas to default
                RouteCanvasToPlayer(StoreGui.instance?.gameObject, 0);
            }

            SplitscreenLog.Log("StoreGui", $"Hide: was owned by P{_storeOwnerPlayer + 1}");
            _storeOwnerPlayer = 0;
        }

        // =====================================================================
        // TextViewer (Runestones, Signs, Tutorial popups)
        // =====================================================================

        private static int _textViewerOwnerPlayer; // 0 = P1, 1 = P2

        [HarmonyPatch(typeof(TextViewer), "ShowText")]
        [HarmonyPrefix]
        public static void TextViewer_ShowText_Prefix()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            _textViewerOwnerPlayer = SplitscreenLog.CurrentPlayerIndex == 2 ? 1 : 0;
            SplitscreenLog.Log("TextViewer", $"ShowText: opened by P{_textViewerOwnerPlayer + 1}");

            // Route canvas to owner's UI camera
            if (TextViewer.instance != null)
                RouteCanvasToPlayer(TextViewer.instance.gameObject, _textViewerOwnerPlayer);
        }

        [HarmonyPatch(typeof(TextViewer), "Hide")]
        [HarmonyPostfix]
        public static void TextViewer_Hide_Postfix()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            if (_textViewerOwnerPlayer == 1 && TextViewer.instance != null)
                RouteCanvasToPlayer(TextViewer.instance.gameObject, 0);

            SplitscreenLog.Log("TextViewer", $"Hide: was owned by P{_textViewerOwnerPlayer + 1}");
            _textViewerOwnerPlayer = 0;
        }

        // =====================================================================
        // Canvas Routing Helper
        // =====================================================================

        /// <summary>
        /// Routes a UI element's parent canvas to the specified player's UI camera.
        /// playerIndex: 0 = P1, 1 = P2.
        /// </summary>
        private static void RouteCanvasToPlayer(GameObject uiElement, int playerIndex)
        {
            if (uiElement == null) return;
            if (SplitCameraManager.Instance == null) return;

            var canvas = uiElement.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var cam = playerIndex == 1
                ? SplitCameraManager.Instance.Player2UiCamera
                : SplitCameraManager.Instance.Player1UiCamera;
            if (cam == null) return;

            if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != cam)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1f;
                canvas.sortingOrder = 10;
                SplitscreenLog.Log("SharedGUI", $"Routed canvas '{canvas.gameObject.name}' to P{playerIndex + 1} UI camera");
            }
        }
    }
}
