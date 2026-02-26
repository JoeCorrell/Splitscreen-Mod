using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Ensures Inventory/Crafting canvas renders into Player 1's splitscreen RT.
    /// Without this, ScreenSpaceOverlay inventory can end up hidden behind compositor OnGUI.
    /// </summary>
    [HarmonyPatch]
    public static class InventoryGuiPatches
    {
        private static bool _hasSavedState;
        private static RenderMode _savedRenderMode;
        private static UnityEngine.Camera _savedWorldCamera;
        private static float _savedPlaneDistance;
        private static int _savedSortingOrder;

        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(InventoryGui __instance)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (SplitCameraManager.Instance == null) return;

            var p1UiCamera = SplitCameraManager.Instance.Player1UiCamera;
            if (p1UiCamera == null) return;

            var canvas = __instance.m_inventoryRoot != null
                ? __instance.m_inventoryRoot.GetComponentInParent<Canvas>()
                : __instance.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            if (!_hasSavedState)
            {
                _savedRenderMode = canvas.renderMode;
                _savedWorldCamera = canvas.worldCamera;
                _savedPlaneDistance = canvas.planeDistance;
                _savedSortingOrder = canvas.sortingOrder;
                _hasSavedState = true;
            }

            if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != p1UiCamera)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = p1UiCamera;
                canvas.planeDistance = 1f;
                canvas.sortingOrder = 10;
            }
        }

        public static void RestoreInventoryCanvas()
        {
            if (!_hasSavedState) return;

            var inv = InventoryGui.instance;
            if (inv == null)
            {
                _hasSavedState = false;
                return;
            }

            var canvas = inv.m_inventoryRoot != null
                ? inv.m_inventoryRoot.GetComponentInParent<Canvas>()
                : inv.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                _hasSavedState = false;
                return;
            }

            canvas.renderMode = _savedRenderMode;
            canvas.worldCamera = _savedWorldCamera;
            canvas.planeDistance = _savedPlaneDistance;
            canvas.sortingOrder = _savedSortingOrder;
            _hasSavedState = false;
        }
    }
}

