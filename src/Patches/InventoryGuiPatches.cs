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

        // Saved CanvasScaler state
        private static bool _hasSavedInvScalerState;
        private static CanvasScaler.ScaleMode _savedInvScaleMode;
        private static float _savedInvScaleFactor;
        private static Vector2 _savedInvRefResolution;

        private static bool _hasSavedLayoutState;
        private static Vector3 _savedInventoryScale;
        private static Vector2 _savedInventoryAnchoredPosition;
        private static Vector2 _savedInventoryAnchorMin;
        private static Vector2 _savedInventoryAnchorMax;
        private static Vector2 _savedInventoryPivot;

        // 0 = Player 1, 1 = Player 2
        private static int _activeInventoryPlayerIndex;

        /// <summary>
        /// Exposes which player currently owns the inventory panel.
        /// 0 = Player 1, 1 = Player 2. Used by CraftingPatches and SharedGuiPatches.
        /// </summary>
        public static int ActiveOwnerPlayerIndex => _activeInventoryPlayerIndex;

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
            var localName = global::Player.m_localPlayer?.GetPlayerName();
            SplitscreenLog.Log("Inventory", $"Show: owner=P{_activeInventoryPlayerIndex + 1}, m_localPlayer='{localName}'");
        }

        [HarmonyPatch(typeof(InventoryGui), "Hide")]
        [HarmonyPostfix]
        public static void Hide_Postfix()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            SplitscreenLog.Log("Inventory", $"Hide: was owner=P{_activeInventoryPlayerIndex + 1}");
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

            // Switch CanvasScaler to ConstantPixelSize to match RT dimensions
            var invScaler = canvas.GetComponent<CanvasScaler>();
            if (invScaler != null)
            {
                if (!_hasSavedInvScalerState)
                {
                    _savedInvScaleMode = invScaler.uiScaleMode;
                    _savedInvScaleFactor = invScaler.scaleFactor;
                    _savedInvRefResolution = invScaler.referenceResolution;
                    _hasSavedInvScalerState = true;
                }
                if (invScaler.uiScaleMode != CanvasScaler.ScaleMode.ConstantPixelSize)
                {
                    invScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    invScaler.scaleFactor = 1f;
                }
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

            if (inv != null)
            {
                var canvas = inv.m_inventoryRoot != null
                    ? inv.m_inventoryRoot.GetComponentInParent<Canvas>()
                    : inv.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    if (_hasSavedCanvasState)
                    {
                        canvas.renderMode = _savedRenderMode;
                        canvas.worldCamera = _savedWorldCamera;
                        canvas.planeDistance = _savedPlaneDistance;
                        canvas.sortingOrder = _savedSortingOrder;
                    }
                    if (_hasSavedInvScalerState)
                    {
                        var scaler = canvas.GetComponent<CanvasScaler>();
                        if (scaler != null)
                        {
                            scaler.uiScaleMode = _savedInvScaleMode;
                            scaler.scaleFactor = _savedInvScaleFactor;
                            scaler.referenceResolution = _savedInvRefResolution;
                        }
                    }
                }
            }

            _hasSavedCanvasState = false;
            _hasSavedInvScalerState = false;
            _activeInventoryPlayerIndex = 0;
            _savedLocalBeforeUpdate = null;
            _swappedLocalForUpdate = false;
        }

        private static float _lastInvButtonLogTime;
        private static float _lastInvBlockLogTime;

        private static void TryOpenInventoryForPlayer2(InventoryGui inventoryGui)
        {
            if (SplitInputManager.Instance == null) return;

            var p2 = SplitScreenManager.Instance?.PlayerManager?.Player2;
            if (p2 == null) return;
            if (p2.IsDead() || p2.InCutscene() || p2.IsTeleporting())
            {
                if (SplitscreenLog.ShouldLog("Inv.p2state", 5f))
                    SplitscreenLog.Log("Inventory", $"P2 blocked: dead={p2.IsDead()}, cutscene={p2.InCutscene()}, teleporting={p2.IsTeleporting()}");
                return;
            }

            var p2Input = SplitInputManager.Instance.GetInputState(1);
            if (p2Input == null) return;

            bool invPressed = p2Input.GetButtonDown("Inventory");
            bool joyYPressed = p2Input.GetButtonDown("JoyButtonY");
            bool openPressed = invPressed || joyYPressed;

            // Log button state periodically even when not pressed (to verify input is working)
            if (Time.time - _lastInvButtonLogTime > 5f)
            {
                _lastInvButtonLogTime = Time.time;
                bool hasInv = p2.GetInventory() != null;
                int itemCount = hasInv ? p2.GetInventory().GetAllItems().Count : -1;
                SplitscreenLog.Log("Inventory", $"P2 input check: Inventory={p2Input.GetButton("Inventory")}, JoyButtonY={p2Input.GetButton("JoyButtonY")}, hasInventory={hasInv}, items={itemCount}");
            }

            if (!openPressed) return;

            SplitscreenLog.Log("Inventory", $"P2 pressed inventory! invPressed={invPressed}, joyYPressed={joyYPressed}");

            // If inventory is already visible...
            if (IsInventoryVisible(inventoryGui))
            {
                // Consume button-down state to prevent original Update from re-toggling
                p2Input.SelectButtonDown = false;
                p2Input.ButtonNorthDown = false;

                if (_activeInventoryPlayerIndex == 1)
                {
                    SplitscreenLog.Log("Inventory", "P2 closing own inventory");
                    inventoryGui.Hide();
                    _activeInventoryPlayerIndex = 0;
                }
                else
                {
                    SplitscreenLog.Log("Inventory", "Transferring inventory from P1 to P2");
                    inventoryGui.Hide();
                    _activeInventoryPlayerIndex = 1;
                    ExecuteAsPlayer(p2, () =>
                    {
                        inventoryGui.Show(null, 1);
                    });
                }
                ZInput.ResetButtonStatus("Inventory");
                ZInput.ResetButtonStatus("JoyButtonY");
                return;
            }

            // Inventory not visible — check blocking conditions with logging
            bool consoleVisible = global::Console.IsVisible();
            bool menuVisible = Menu.IsVisible();
            bool minimapOpen = Minimap.IsOpen();
            bool inRadial = Hud.InRadial();
            bool pieceSelection = Hud.IsPieceSelectionVisible();
            bool chatFocused = Chat.instance != null && Chat.instance.HasFocus();
            bool textViewerVisible = TextViewer.instance != null && TextViewer.instance.IsVisible();
            bool freeFly = GameCamera.InFreeFly();

            bool blocked = consoleVisible || menuVisible || minimapOpen || inRadial || pieceSelection || chatFocused || textViewerVisible || freeFly;
            if (blocked)
            {
                if (Time.time - _lastInvBlockLogTime > 2f)
                {
                    _lastInvBlockLogTime = Time.time;
                    SplitscreenLog.Log("Inventory", $"P2 inv BLOCKED: console={consoleVisible}, menu={menuVisible}, minimap={minimapOpen}, radial={inRadial}, pieceSelect={pieceSelection}, chat={chatFocused}, textViewer={textViewerVisible}, freeFly={freeFly}");
                }
                return;
            }

            // Open fresh for P2
            string p1Name = global::Player.m_localPlayer?.GetPlayerName() ?? "null";
            SplitscreenLog.Log("Inventory", $"P2 opening inventory — no blockers, m_localPlayer={p1Name}, P2={p2.GetPlayerName()}");
            _activeInventoryPlayerIndex = 1;
            ExecuteAsPlayer(p2, () =>
            {
                try
                {
                    // Check P2 has a valid inventory before trying to show
                    var inv = p2.GetInventory();
                    if (inv == null)
                    {
                        SplitscreenLog.Err("Inventory", "P2 inventory is NULL! Cannot show.");
                        _activeInventoryPlayerIndex = 0;
                        return;
                    }

                    inventoryGui.Show(null, 1);
                    ZInput.ResetButtonStatus("Inventory");
                    ZInput.ResetButtonStatus("JoyButtonY");

                    // Consume button-down state so our ZInput postfix patches
                    // don't re-report the press to the original InventoryGui.Update
                    p2Input.SelectButtonDown = false;
                    p2Input.ButtonNorthDown = false;

                    bool visible = IsInventoryVisible(inventoryGui);
                    SplitscreenLog.Log("Inventory", $"P2 inventory Show() called, visible={visible}, m_inventoryRoot.active={inventoryGui.m_inventoryRoot?.gameObject.activeSelf}");
                    if (!visible)
                    {
                        SplitscreenLog.Warn("Inventory", "P2 inventory NOT visible after Show()! Checking animator...");
                        var anim = AnimatorFieldRef(inventoryGui);
                        SplitscreenLog.Log("Inventory", $"  Animator: {(anim != null ? $"enabled={anim.enabled}, visible_param={anim.GetBool("visible")}" : "NULL")}");
                    }
                }
                catch (System.Exception ex)
                {
                    SplitscreenLog.Err("Inventory", $"P2 inventory Show() EXCEPTION: {ex}");
                    _activeInventoryPlayerIndex = 0;
                }
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
                if (isP1 && pos.y >= halfH)
                {
                    pos.y -= halfH;
                    eventData.position = pos;
                    // Also remap pressPosition so drag-and-drop detection works correctly
                    var pp = eventData.pressPosition;
                    if (pp.y >= halfH) { pp.y -= halfH; eventData.pressPosition = pp; }
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
