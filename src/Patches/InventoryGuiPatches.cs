using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Config;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.Input;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Splitscreen handling for Inventory/Crafting UI:
    /// - routes inventory ownership/input between P1 and P2
    /// - renders inventory into the correct player's UI camera
    /// - scales the inventory root to fit half-resolution viewports
    /// </summary>
    [HarmonyPatch]
    public static class InventoryGuiPatches
    {
        private static bool _hasSavedCanvasState;
        private static RenderMode _savedRenderMode;
        private static UnityEngine.Camera _savedWorldCamera;
        private static float _savedPlaneDistance;
        private static int _savedSortingOrder;

        private static bool _hasSavedLayoutState;
        private static Vector3 _savedInventoryScale;
        private static Vector2 _savedInventoryAnchoredPosition;
        private static Vector2 _savedInventoryAnchorMin;
        private static Vector2 _savedInventoryAnchorMax;
        private static Vector2 _savedInventoryPivot;

        // 0 = Player 1, 1 = Player 2
        private static int _activeInventoryPlayerIndex;

        // Temporary local-player swap for InventoryGui.Update when P2 owns the panel.
        private static bool _swappedLocalForUpdate;
        private static global::Player _savedLocalBeforeUpdate;

        private static readonly AccessTools.FieldRef<InventoryGui, Animator> AnimatorFieldRef =
            AccessTools.FieldRefAccess<InventoryGui, Animator>("m_animator");

        [HarmonyPatch(typeof(InventoryGui), "Show", new[] { typeof(Container), typeof(int) })]
        [HarmonyPrefix]
        public static void Show_Prefix()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            _activeInventoryPlayerIndex = GetContextPlayerIndex();
        }

        [HarmonyPatch(typeof(InventoryGui), "Hide")]
        [HarmonyPostfix]
        public static void Hide_Postfix()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            _activeInventoryPlayerIndex = 0;
        }

        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPrefix]
        public static void Update_Prefix(InventoryGui __instance)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            TryOpenInventoryForPlayer2(__instance);

            if (_activeInventoryPlayerIndex != 1 || !IsInventoryVisible(__instance))
            {
                return;
            }

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null || global::Player.m_localPlayer == p2)
            {
                return;
            }

            _savedLocalBeforeUpdate = global::Player.m_localPlayer;
            global::Player.m_localPlayer = p2;
            _swappedLocalForUpdate = true;
        }

        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(InventoryGui __instance)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (SplitCameraManager.Instance == null) return;

            if (_swappedLocalForUpdate)
            {
                global::Player.m_localPlayer = _savedLocalBeforeUpdate;
                _savedLocalBeforeUpdate = null;
                _swappedLocalForUpdate = false;
            }

            if (!IsInventoryVisible(__instance))
            {
                _activeInventoryPlayerIndex = 0;
            }

            var canvas = __instance.m_inventoryRoot != null
                ? __instance.m_inventoryRoot.GetComponentInParent<Canvas>()
                : __instance.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            if (!_hasSavedCanvasState)
            {
                _savedRenderMode = canvas.renderMode;
                _savedWorldCamera = canvas.worldCamera;
                _savedPlaneDistance = canvas.planeDistance;
                _savedSortingOrder = canvas.sortingOrder;
                _hasSavedCanvasState = true;
            }

            var uiCamera = _activeInventoryPlayerIndex == 1
                ? SplitCameraManager.Instance.Player2UiCamera
                : SplitCameraManager.Instance.Player1UiCamera;
            if (uiCamera == null) return;

            if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != uiCamera)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCamera;
                canvas.planeDistance = 1f;
                canvas.sortingOrder = 10;
            }

            ApplyScaledInventoryLayout(__instance, uiCamera);
        }

        public static void RestoreInventoryCanvas()
        {
            var inv = InventoryGui.instance;
            if (inv != null)
            {
                RestoreInventoryLayout(inv);
            }

            if (_hasSavedCanvasState && inv != null)
            {
                var canvas = inv.m_inventoryRoot != null
                    ? inv.m_inventoryRoot.GetComponentInParent<Canvas>()
                    : inv.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    canvas.renderMode = _savedRenderMode;
                    canvas.worldCamera = _savedWorldCamera;
                    canvas.planeDistance = _savedPlaneDistance;
                    canvas.sortingOrder = _savedSortingOrder;
                }
            }

            _hasSavedCanvasState = false;
            _activeInventoryPlayerIndex = 0;
            _savedLocalBeforeUpdate = null;
            _swappedLocalForUpdate = false;
        }

        private static void TryOpenInventoryForPlayer2(InventoryGui inventoryGui)
        {
            if (IsInventoryVisible(inventoryGui))
            {
                return;
            }

            if (SplitInputManager.Instance == null)
            {
                return;
            }

            var p2 = SplitScreenManager.Instance?.PlayerManager?.Player2;
            if (p2 == null || p2.IsDead() || p2.InCutscene() || p2.IsTeleporting())
            {
                return;
            }

            if (global::Console.IsVisible() || Menu.IsVisible() || Minimap.IsOpen() || Hud.InRadial())
            {
                return;
            }
            if (Chat.instance != null && Chat.instance.HasFocus())
            {
                return;
            }
            if (TextViewer.instance != null && TextViewer.instance.IsVisible())
            {
                return;
            }
            if (GameCamera.InFreeFly())
            {
                return;
            }

            var p2Input = SplitInputManager.Instance.GetInputState(1);
            if (p2Input == null)
            {
                return;
            }

            bool openPressed = p2Input.GetButtonDown("Inventory") || p2Input.GetButtonDown("JoyButtonY");
            if (!openPressed)
            {
                return;
            }

            _activeInventoryPlayerIndex = 1;
            ExecuteAsPlayer(p2, () =>
            {
                p2.ShowTutorial("inventory", force: true);
                inventoryGui.Show(null);
                ZInput.ResetButtonStatus("Inventory");
                ZInput.ResetButtonStatus("JoyButtonY");
            });
        }

        private static void ExecuteAsPlayer(global::Player player, Action action)
        {
            if (player == null)
            {
                action?.Invoke();
                return;
            }

            var prev = global::Player.m_localPlayer;
            try
            {
                global::Player.m_localPlayer = player;
                action?.Invoke();
            }
            finally
            {
                global::Player.m_localPlayer = prev;
            }
        }

        private static int GetContextPlayerIndex()
        {
            var mgr = SplitScreenManager.Instance?.PlayerManager;
            if (mgr?.Player2 != null && global::Player.m_localPlayer == mgr.Player2)
            {
                return 1;
            }
            return 0;
        }

        private static bool IsInventoryVisible(InventoryGui inv)
        {
            if (inv == null) return false;
            var animator = AnimatorFieldRef(inv);
            if (animator == null) return InventoryGui.IsVisible();
            return animator.GetBool("visible");
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
            bool horizontalSplit = SplitscreenPlugin.Instance?.SplitConfig?.Orientation?.Value == ValheimSplitscreen.Config.SplitOrientation.Horizontal;

            float rootWidth = Mathf.Max(1f, rootRect.rect.width);
            float rootHeight = Mathf.Max(1f, rootRect.rect.height);

            float paddingX = viewportWidth * (horizontalSplit ? 0.02f : 0.03f);
            float paddingTop = viewportHeight * (horizontalSplit ? 0.02f : 0.04f);
            float paddingBottom = viewportHeight * (horizontalSplit ? 0.05f : 0.08f);

            float fitX = (viewportWidth - (paddingX * 2f)) / rootWidth;
            float fitY = (viewportHeight - paddingTop - paddingBottom) / rootHeight;

            float fitScale = Mathf.Min(1f, fitX, fitY);
            // For horizontal split the viewport is very short, allow aggressive downscaling
            fitScale = Mathf.Clamp(fitScale, horizontalSplit ? 0.20f : 0.55f, horizontalSplit ? 0.55f : 1f);

            if (horizontalSplit)
            {
                // For top/bottom split, lock to top-center and keep a large bottom safe area
                // so the panel never crosses the center divider into the other player's pane.
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
            Vector2 pos = eventData.position;

            if (horizontal)
            {
                float halfH = Screen.height / 2f;
                if (isP1 && pos.y >= halfH)
                {
                    pos.y -= halfH;
                    eventData.position = pos;
                    _didRemap = true;
                }
                else if (isP2 && pos.y < halfH)
                {
                    // P2's bottom half — coordinates already match RT space
                }
            }
            else
            {
                float halfW = Screen.width / 2f;
                if (isP1 && pos.x < halfW)
                {
                    // P1's left half — coordinates already match RT space
                }
                else if (isP2 && pos.x >= halfW)
                {
                    pos.x -= halfW;
                    eventData.position = pos;
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
                _didRemap = false;
            }
        }
    }
}
