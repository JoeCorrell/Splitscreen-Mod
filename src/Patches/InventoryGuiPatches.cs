using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Config;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.HUD;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Splitscreen patches for the Inventory UI.
    /// The original InventoryGui singleton is used exclusively for Player 1.
    /// Player 2 gets a dedicated clone managed by SplitInventoryManager.
    /// This patch handles P1's inventory layout scaling for half-screen viewports.
    /// </summary>
    [HarmonyPatch]
    public static class InventoryGuiPatches
    {
        private static bool _hasSavedLayoutState;
        private static Vector3 _savedInventoryScale;
        private static Vector2 _savedInventoryAnchoredPosition;
        private static Vector2 _savedInventoryAnchorMin;
        private static Vector2 _savedInventoryAnchorMax;
        private static Vector2 _savedInventoryPivot;

        /// <summary>
        /// Always 0 — the original InventoryGui only serves P1.
        /// P2 uses SplitInventoryManager with a separate clone.
        /// Kept for backward compatibility with CraftingPatches.
        /// </summary>
        public static int ActiveOwnerPlayerIndex => 0;

        /// <summary>
        /// Whether P2's dedicated inventory clone is currently visible.
        /// </summary>
        public static bool P2InventoryOpen =>
            SplitInventoryManager.Instance?.P2InventoryOpen ?? false;

        private static readonly AccessTools.FieldRef<InventoryGui, Animator> AnimatorFieldRef =
            AccessTools.FieldRefAccess<InventoryGui, Animator>("m_animator");

        /// <summary>
        /// After InventoryGui.Update runs for P1, scale the inventory layout
        /// to fit the half-screen viewport. The root canvas is already pointed
        /// at P1's UI camera by HudPatches.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(InventoryGui __instance)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (SplitCameraManager.Instance == null) return;
            if (!IsInventoryVisible(__instance)) return;

            var p1Camera = SplitCameraManager.Instance.Player1UiCamera;
            if (p1Camera != null)
            {
                ApplyScaledInventoryLayout(__instance, p1Camera);
            }
        }

        /// <summary>
        /// Restores P1's inventory layout when splitscreen deactivates.
        /// </summary>
        public static void RestoreInventoryCanvas()
        {
            var inv = InventoryGui.instance;
            if (inv != null)
            {
                RestoreInventoryLayout(inv);
            }
            _hasSavedLayoutState = false;
        }

        private static bool IsInventoryVisible(InventoryGui inv)
        {
            if (inv == null) return false;
            try
            {
                var animator = AnimatorFieldRef(inv);
                if (animator != null) return animator.GetBool("visible");
            }
            catch { }
            return InventoryGui.IsVisible();
        }

        private static void ApplyScaledInventoryLayout(InventoryGui inv, UnityEngine.Camera uiCamera)
        {
            var rootRect = inv.m_inventoryRoot as RectTransform;
            if (rootRect == null) return;

            if (!_hasSavedLayoutState)
            {
                _savedInventoryScale = rootRect.localScale;
                _savedInventoryAnchoredPosition = rootRect.anchoredPosition;
                _savedInventoryAnchorMin = rootRect.anchorMin;
                _savedInventoryAnchorMax = rootRect.anchorMax;
                _savedInventoryPivot = rootRect.pivot;
                _hasSavedLayoutState = true;
            }

            float viewportWidth = uiCamera.targetTexture != null ? uiCamera.targetTexture.width : Screen.width;
            float viewportHeight = uiCamera.targetTexture != null ? uiCamera.targetTexture.height : Screen.height;
            bool horizontalSplit = SplitscreenPlugin.Instance?.SplitConfig?.Orientation?.Value == SplitOrientation.Horizontal;

            float rootWidth = Mathf.Max(1f, rootRect.rect.width);
            float rootHeight = Mathf.Max(1f, rootRect.rect.height);

            float paddingX = viewportWidth * (horizontalSplit ? 0.02f : 0.03f);
            float paddingTop = viewportHeight * (horizontalSplit ? 0.02f : 0.04f);
            float paddingBottom = viewportHeight * (horizontalSplit ? 0.05f : 0.08f);

            float fitX = (viewportWidth - (paddingX * 2f)) / rootWidth;
            float fitY = (viewportHeight - paddingTop - paddingBottom) / rootHeight;

            float fitScale = Mathf.Min(1f, fitX, fitY);
            fitScale = Mathf.Clamp(fitScale, horizontalSplit ? 0.20f : 0.55f, horizontalSplit ? 0.55f : 1f);

            if (horizontalSplit)
            {
                rootRect.anchorMin = new Vector2(0.5f, 1f);
                rootRect.anchorMax = new Vector2(0.5f, 1f);
                rootRect.pivot = new Vector2(0.5f, 1f);
                rootRect.anchoredPosition = new Vector2(0f, 0f - paddingTop);
            }
            else
            {
                rootRect.anchorMin = new Vector2(0.5f, 0.5f);
                rootRect.anchorMax = new Vector2(0.5f, 0.5f);
                rootRect.pivot = new Vector2(0.5f, 0.5f);
                rootRect.anchoredPosition = Vector2.zero;
            }

            rootRect.localScale = new Vector3(
                _savedInventoryScale.x * fitScale,
                _savedInventoryScale.y * fitScale,
                _savedInventoryScale.z);
        }

        private static void RestoreInventoryLayout(InventoryGui inv)
        {
            if (!_hasSavedLayoutState || inv == null)
            {
                _hasSavedLayoutState = false;
                return;
            }

            var rootRect = inv.m_inventoryRoot as RectTransform;
            if (rootRect == null)
            {
                _hasSavedLayoutState = false;
                return;
            }

            rootRect.localScale = _savedInventoryScale;
            rootRect.anchoredPosition = _savedInventoryAnchoredPosition;
            rootRect.anchorMin = _savedInventoryAnchorMin;
            rootRect.anchorMax = _savedInventoryAnchorMax;
            rootRect.pivot = _savedInventoryPivot;
            _hasSavedLayoutState = false;
        }
    }

    /// <summary>
    /// Remaps mouse coordinates for GraphicRaycaster when a canvas targets one of our
    /// splitscreen UI cameras (which render to RenderTextures). Without this, mouse
    /// position in screen coordinates doesn't match the RT coordinate space, so clicks
    /// land in the wrong place.
    /// </summary>
    [HarmonyPatch]
    public static class SplitscreenPointerRemapPatch
    {
        [ThreadStatic] private static bool _didRemap;
        [ThreadStatic] private static Vector2 _savedPosition;
        [ThreadStatic] private static Vector2 _savedPressPosition;

        [HarmonyPatch(typeof(GraphicRaycaster), "Raycast",
            new[] { typeof(PointerEventData), typeof(List<RaycastResult>) })]
        [HarmonyPrefix]
        public static void Raycast_Prefix(GraphicRaycaster __instance, PointerEventData eventData)
        {
            _didRemap = false;
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (SplitCameraManager.Instance == null) return;

            Canvas canvas = __instance.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceCamera) return;

            var cam = canvas.worldCamera;
            if (cam == null || cam.targetTexture == null) return;

            var camMgr = SplitCameraManager.Instance;
            bool isP1 = cam == camMgr.Player1UiCamera;
            bool isP2 = cam == camMgr.Player2UiCamera;
            if (!isP1 && !isP2) return;

            bool horizontal = SplitscreenPlugin.Instance?.SplitConfig?.Orientation?.Value == SplitOrientation.Horizontal;
            _savedPosition = eventData.position;
            _savedPressPosition = eventData.pressPosition;
            Vector2 pos = eventData.position;

            if (horizontal)
            {
                float halfH = Screen.height / 2f;
                // Horizontal split is fixed as P2=top, P1=bottom.
                // Only the top half needs Y remapping into RT-local coordinates.
                if (isP2 && pos.y >= halfH)
                {
                    pos.y -= halfH;
                    eventData.position = pos;
                    var pp = eventData.pressPosition;
                    if (pp.y >= halfH) { pp.y -= halfH; eventData.pressPosition = pp; }
                    _didRemap = true;
                }
            }
            else
            {
                float halfW = Screen.width / 2f;
                if (isP2 && pos.x >= halfW)
                {
                    pos.x -= halfW;
                    eventData.position = pos;
                    var pp = eventData.pressPosition;
                    if (pp.x >= halfW) { pp.x -= halfW; eventData.pressPosition = pp; }
                    _didRemap = true;
                }
            }
        }

        [HarmonyPatch(typeof(GraphicRaycaster), "Raycast",
            new[] { typeof(PointerEventData), typeof(List<RaycastResult>) })]
        [HarmonyPostfix]
        public static void Raycast_Postfix(GraphicRaycaster __instance, PointerEventData eventData)
        {
            if (_didRemap)
            {
                eventData.position = _savedPosition;
                eventData.pressPosition = _savedPressPosition;
                _didRemap = false;
            }
        }
    }
}
