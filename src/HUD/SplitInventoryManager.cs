using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Config;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.Input;

namespace ValheimSplitscreen.HUD
{
    /// <summary>
    /// Manages a cloned inventory display for Player 2 on a dedicated root canvas.
    /// P1 uses the original InventoryGui singleton; P2 gets a clone rendered on P2's screen.
    /// Both inventories can be open simultaneously.
    /// Includes gamepad navigation: D-pad to move, A to use/equip, X to drop, B to close.
    /// </summary>
    public class SplitInventoryManager : MonoBehaviour
    {
        public static SplitInventoryManager Instance { get; private set; }

        private GameObject _p2InvCanvasObj;
        private Canvas _p2InvCanvas;
        private GameObject _p2InvClone;
        private int _lastCloneChildCount;

        /// <summary>Whether P2's inventory clone is currently visible.</summary>
        public bool P2InventoryOpen => _p2InvClone != null && _p2InvClone.activeSelf;

        // ===== REFLECTION CACHE =====
        private static readonly FieldInfo _gridInventoryField = typeof(InventoryGrid).GetField("m_inventory",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly MethodInfo _gridUpdateInventory = FindMethod(typeof(InventoryGrid), "UpdateInventory");
        private static readonly FieldInfo _gridWidthField = typeof(InventoryGrid).GetField("m_width",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo _gridElementsField =
            typeof(InventoryGrid).GetField("m_elements", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? typeof(InventoryGrid).GetField("m_gridElements", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // ===== GAMEPAD NAVIGATION =====
        private InventoryGrid _playerGrid;
        private int _selectedCol;
        private int _selectedRow;
        private int _gridWidth = 8;
        private int _gridHeight = 4;

        // D-pad repeat
        private float _navHeldTime;
        private float _navRepeatTimer;
        private int _lastDpadX;
        private int _lastDpadY;
        private const float NavInitialDelay = 0.32f;
        private const float NavRepeatRate = 0.14f;

        // ===== SELECTION HIGHLIGHT =====
        private GameObject _highlightObj;
        private RectTransform _highlightRT;
        private Image _highlightImage;

        // ===== PERIODIC REFRESH =====
        private float _refreshTimer;
        private const float RefreshInterval = 0.5f;
        private float _lastLogTime;

        private static MethodInfo FindMethod(Type type, string name)
        {
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.Name == name) return m;
            return null;
        }

        private void Awake()
        {
            Instance = this;
            Debug.Log($"[Splitscreen][P2Inv] Awake — reflection: inv={_gridInventoryField != null}, update={_gridUpdateInventory != null}, width={_gridWidthField != null}, elements={_gridElementsField != null}");
        }

        public void OnSplitscreenActivated() { }

        public void OnSplitscreenDeactivated()
        {
            HideP2Inventory();
            DestroyP2Canvas();
        }

        // ===== CANVAS =====

        private void EnsureP2Canvas()
        {
            if (_p2InvCanvasObj != null) return;

            var p2Camera = SplitCameraManager.Instance?.Player2UiCamera;
            if (p2Camera == null) return;

            _p2InvCanvasObj = new GameObject("SplitscreenInventory_P2",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _p2InvCanvasObj.transform.SetParent(null, false);

            var rect = _p2InvCanvasObj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            _p2InvCanvas = _p2InvCanvasObj.GetComponent<Canvas>();
            _p2InvCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _p2InvCanvas.worldCamera = p2Camera;
            _p2InvCanvas.planeDistance = 1f;
            _p2InvCanvas.sortingOrder = 10;

            var scaler = _p2InvCanvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            SetLayerRecursively(_p2InvCanvasObj, SplitCameraManager.Player2HudLayer);
            Debug.Log($"[Splitscreen][P2Inv] Created canvas, RT={p2Camera.targetTexture?.width}x{p2Camera.targetTexture?.height}");
        }

        // ===== SHOW / HIDE =====

        public void ShowP2Inventory(global::Player p2)
        {
            if (P2InventoryOpen) return;
            if (p2 == null || InventoryGui.instance == null) return;

            EnsureP2Canvas();
            if (_p2InvCanvasObj == null) return;

            var sourceRoot = InventoryGui.instance.m_inventoryRoot;
            if (sourceRoot == null) return;

            // Clone the inventory root panel
            _p2InvClone = Instantiate(sourceRoot.gameObject, _p2InvCanvasObj.transform, false);
            _p2InvClone.name = "P2_InventoryRoot";
            _p2InvClone.SetActive(true);

            SetLayerRecursively(_p2InvClone, SplitCameraManager.Player2HudLayer);
            ConfigureNestedCanvases(_p2InvClone);

            BindGridsToP2(p2);
            CachePlayerGrid(p2);
            LayoutInventory();
            ForceRefreshGrids(p2);
            CreateSelectionHighlight();

            _selectedCol = 0;
            _selectedRow = 0;
            _lastCloneChildCount = CountDescendants(_p2InvClone);

            Debug.Log($"[Splitscreen][P2Inv] Opened for '{p2.GetPlayerName()}', grid={_gridWidth}x{_gridHeight}");
        }

        public void HideP2Inventory()
        {
            DestroyHighlight();
            if (_p2InvClone != null)
            {
                Destroy(_p2InvClone);
                _p2InvClone = null;
                _playerGrid = null;
            }
        }

        // ===== UPDATE =====

        private void Update()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            HandleP2InventoryToggle();

            if (!P2InventoryOpen) return;

            var p2 = SplitScreenManager.Instance?.PlayerManager?.Player2;
            if (p2 == null) { HideP2Inventory(); return; }

            // Gamepad navigation
            var p2Input = SplitInputManager.Instance?.GetInputState(1);
            if (p2Input != null)
                HandleGamepadNav(p2Input, p2);

            // Re-layer dynamically created item icons
            int currentCount = CountDescendants(_p2InvClone);
            if (currentCount != _lastCloneChildCount)
            {
                SetLayerRecursively(_p2InvClone, SplitCameraManager.Player2HudLayer);
                _lastCloneChildCount = currentCount;
                if (_highlightObj != null)
                    _highlightObj.layer = SplitCameraManager.Player2HudLayer;
            }

            // Periodic grid data refresh
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= RefreshInterval)
            {
                _refreshTimer = 0f;
                ForceRefreshGrids(p2);
            }

            UpdateHighlightPosition();

            // Periodic logging
            if (Time.time - _lastLogTime > 15f)
            {
                _lastLogTime = Time.time;
                Debug.Log($"[Splitscreen][P2Inv] Status: items={p2.GetInventory()?.GetAllItems().Count}, sel=({_selectedCol},{_selectedRow})");
            }
        }

        private void HandleP2InventoryToggle()
        {
            if (SplitInputManager.Instance == null) return;

            var p2 = SplitScreenManager.Instance?.PlayerManager?.Player2;
            if (p2 == null) return;

            var p2Input = SplitInputManager.Instance.GetInputState(1);
            if (p2Input == null) return;

            bool invPressed = p2Input.GetButtonDown("JoyButtonY") || p2Input.GetButtonDown("Inventory");

            if (P2InventoryOpen)
            {
                bool menuPressed = p2Input.GetButtonDown("JoyMenu") || p2Input.GetButtonDown("Menu");
                bool backPressed = p2Input.GetButtonDown("JoyButtonB");

                if (invPressed || menuPressed || backPressed)
                {
                    HideP2Inventory();
                    ConsumeP2InvButtons(p2Input);
                }
            }
            else
            {
                if (!invPressed) return;
                if (p2.IsDead() || p2.InCutscene() || p2.IsTeleporting()) return;
                if (global::Console.IsVisible() || Menu.IsVisible()) return;

                ShowP2Inventory(p2);
                ConsumeP2InvButtons(p2Input);
            }
        }

        private static void ConsumeP2InvButtons(PlayerInputState p2Input)
        {
            p2Input.ButtonNorthDown = false;
            p2Input.SelectButtonDown = false;
            p2Input.StartButtonDown = false;
            p2Input.ButtonEastDown = false;
            ZInput.ResetButtonStatus("Inventory");
            ZInput.ResetButtonStatus("JoyButtonY");
        }

        // ===== GAMEPAD NAVIGATION =====

        private void HandleGamepadNav(PlayerInputState input, global::Player p2)
        {
            int dpadX = 0, dpadY = 0;
            if (input.DpadRight) dpadX = 1;
            else if (input.DpadLeft) dpadX = -1;
            if (input.DpadDown) dpadY = 1;
            else if (input.DpadUp) dpadY = -1;

            bool dpadChanged = dpadX != _lastDpadX || dpadY != _lastDpadY;
            _lastDpadX = dpadX;
            _lastDpadY = dpadY;

            if (dpadX != 0 || dpadY != 0)
            {
                if (dpadChanged)
                {
                    MoveSelection(dpadX, dpadY);
                    _navHeldTime = 0f;
                    _navRepeatTimer = 0f;
                }
                else
                {
                    _navHeldTime += Time.unscaledDeltaTime;
                    if (_navHeldTime > NavInitialDelay)
                    {
                        _navRepeatTimer += Time.unscaledDeltaTime;
                        if (_navRepeatTimer >= NavRepeatRate)
                        {
                            _navRepeatTimer = 0f;
                            MoveSelection(dpadX, dpadY);
                        }
                    }
                }
                // Consume d-pad so it doesn't zoom camera
                input.DpadUpDown = false;
                input.DpadDownDown = false;
                input.DpadLeftDown = false;
                input.DpadRightDown = false;
            }
            else
            {
                _navHeldTime = 0f;
                _navRepeatTimer = 0f;
            }

            // A = use/equip selected item
            if (input.ButtonSouthDown)
            {
                UseSelectedItem(p2);
                input.ButtonSouthDown = false;
            }

            // X = drop selected item
            if (input.ButtonWestDown)
            {
                DropSelectedItem(p2);
                input.ButtonWestDown = false;
            }
        }

        private void MoveSelection(int dx, int dy)
        {
            _selectedCol = (_selectedCol + dx + _gridWidth) % _gridWidth;
            _selectedRow = (_selectedRow + dy + _gridHeight) % _gridHeight;
        }

        private void UseSelectedItem(global::Player p2)
        {
            var inv = p2.GetInventory();
            if (inv == null) return;

            var item = inv.GetItemAt(_selectedCol, _selectedRow);
            if (item == null) return;

            Debug.Log($"[Splitscreen][P2Inv] UseItem: '{item.m_shared.m_name}' at ({_selectedCol},{_selectedRow})");

            var savedLocal = global::Player.m_localPlayer;
            try
            {
                global::Player.m_localPlayer = p2;
                p2.UseItem(inv, item, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Splitscreen][P2Inv] UseItem failed: {ex.Message}");
            }
            finally
            {
                global::Player.m_localPlayer = savedLocal;
            }
        }

        private void DropSelectedItem(global::Player p2)
        {
            var inv = p2.GetInventory();
            if (inv == null) return;

            var item = inv.GetItemAt(_selectedCol, _selectedRow);
            if (item == null) return;

            Debug.Log($"[Splitscreen][P2Inv] DropItem: '{item.m_shared.m_name}'");

            var savedLocal = global::Player.m_localPlayer;
            try
            {
                global::Player.m_localPlayer = p2;
                p2.DropItem(inv, item, item.m_stack);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Splitscreen][P2Inv] DropItem failed: {ex.Message}");
            }
            finally
            {
                global::Player.m_localPlayer = savedLocal;
            }
        }

        // ===== SELECTION HIGHLIGHT =====

        private void CreateSelectionHighlight()
        {
            DestroyHighlight();
            if (_playerGrid == null) return;

            _highlightObj = new GameObject("P2_SlotHighlight");
            _highlightObj.transform.SetParent(_playerGrid.transform, false);
            _highlightObj.layer = SplitCameraManager.Player2HudLayer;

            _highlightImage = _highlightObj.AddComponent<Image>();
            _highlightImage.color = new Color(1f, 0.85f, 0.2f, 0.4f); // Gold
            _highlightImage.raycastTarget = false;

            _highlightRT = _highlightObj.GetComponent<RectTransform>();
        }

        private void UpdateHighlightPosition()
        {
            if (_highlightRT == null || _playerGrid == null) return;

            RectTransform slotRT = FindSlotRect(_selectedCol, _selectedRow);
            if (slotRT == null) return;

            // Match the slot's position and size
            _highlightRT.SetParent(slotRT.parent, false);
            _highlightRT.anchoredPosition = slotRT.anchoredPosition;
            _highlightRT.sizeDelta = slotRT.sizeDelta;
            _highlightRT.anchorMin = slotRT.anchorMin;
            _highlightRT.anchorMax = slotRT.anchorMax;
            _highlightRT.pivot = slotRT.pivot;
            _highlightObj.transform.SetAsLastSibling();
        }

        private RectTransform FindSlotRect(int col, int row)
        {
            if (_playerGrid == null) return null;

            // Try reflection: m_elements list
            if (_gridElementsField != null)
            {
                try
                {
                    var list = _gridElementsField.GetValue(_playerGrid) as IList;
                    if (list != null)
                    {
                        int index = row * _gridWidth + col;
                        if (index >= 0 && index < list.Count)
                        {
                            var element = list[index];
                            var goField = element?.GetType().GetField("m_go",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            var go = goField?.GetValue(element) as GameObject;
                            if (go != null) return go.GetComponent<RectTransform>();
                        }
                    }
                }
                catch { }
            }

            // Fallback: direct child indexing
            int idx = row * _gridWidth + col;
            if (idx >= 0 && idx < _playerGrid.transform.childCount)
                return _playerGrid.transform.GetChild(idx) as RectTransform;

            return null;
        }

        private void DestroyHighlight()
        {
            if (_highlightObj != null)
            {
                Destroy(_highlightObj);
                _highlightObj = null;
                _highlightRT = null;
                _highlightImage = null;
            }
        }

        // ===== GRID MANAGEMENT =====

        private void CachePlayerGrid(global::Player p2)
        {
            if (_p2InvClone == null) return;

            var grids = _p2InvClone.GetComponentsInChildren<InventoryGrid>(true);
            _playerGrid = null;

            foreach (var grid in grids)
            {
                string n = grid.gameObject.name.ToLowerInvariant();
                // Prefer the player grid over the container grid
                if (!n.Contains("container"))
                {
                    _playerGrid = grid;
                    break;
                }
            }
            if (_playerGrid == null && grids.Length > 0)
                _playerGrid = grids[0];

            // Read grid width
            if (_playerGrid != null && _gridWidthField != null)
            {
                try
                {
                    var w = _gridWidthField.GetValue(_playerGrid);
                    if (w is int width && width > 0) _gridWidth = width;
                }
                catch { }
            }

            // Read grid height from inventory
            var inv = p2.GetInventory();
            if (inv != null)
            {
                try { _gridHeight = inv.GetHeight(); }
                catch { _gridHeight = 4; }
                if (_gridHeight < 1) _gridHeight = 4;
            }

            Debug.Log($"[Splitscreen][P2Inv] CachePlayerGrid: '{_playerGrid?.gameObject.name}', {_gridWidth}x{_gridHeight}, grids found={grids.Length}");
        }

        private void BindGridsToP2(global::Player p2)
        {
            if (_p2InvClone == null || _gridInventoryField == null) return;

            var grids = _p2InvClone.GetComponentsInChildren<InventoryGrid>(true);
            var p2Inv = p2.GetInventory();

            foreach (var grid in grids)
            {
                try
                {
                    _gridInventoryField.SetValue(grid, p2Inv);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Splitscreen][P2Inv] BindGrid failed '{grid.gameObject.name}': {ex.Message}");
                }
            }
        }

        private void ForceRefreshGrids(global::Player p2)
        {
            if (_p2InvClone == null || _gridUpdateInventory == null) return;

            var grids = _p2InvClone.GetComponentsInChildren<InventoryGrid>(true);
            var p2Inv = p2.GetInventory();

            var savedLocal = global::Player.m_localPlayer;
            try
            {
                global::Player.m_localPlayer = p2;
                var paramCount = _gridUpdateInventory.GetParameters().Length;
                foreach (var grid in grids)
                {
                    try
                    {
                        object[] args;
                        if (paramCount >= 3)
                            args = new object[] { p2Inv, p2, null };
                        else if (paramCount == 2)
                            args = new object[] { p2Inv, p2 };
                        else if (paramCount == 1)
                            args = new object[] { p2Inv };
                        else
                            args = new object[0];

                        _gridUpdateInventory.Invoke(grid, args);
                    }
                    catch { }
                }
            }
            finally
            {
                global::Player.m_localPlayer = savedLocal;
            }
        }

        // ===== LAYOUT =====

        private void LayoutInventory()
        {
            var rootRect = _p2InvClone?.GetComponent<RectTransform>();
            if (rootRect == null) return;

            var p2Camera = SplitCameraManager.Instance?.Player2UiCamera;
            if (p2Camera == null) return;

            float vpW = p2Camera.targetTexture != null ? p2Camera.targetTexture.width : Screen.width;
            float vpH = p2Camera.targetTexture != null ? p2Camera.targetTexture.height : Screen.height;

            // Center the clone and scale to fill ~90% of the viewport
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;

            float rootW = Mathf.Max(1f, rootRect.rect.width);
            float rootH = Mathf.Max(1f, rootRect.rect.height);

            float scaleX = vpW / rootW;
            float scaleY = vpH / rootH;
            float scale = Mathf.Min(scaleX, scaleY) * 0.88f; // Fill 88% of viewport
            scale = Mathf.Max(scale, 0.25f); // Absolute minimum

            rootRect.localScale = new Vector3(scale, scale, 1f);
            Debug.Log($"[Splitscreen][P2Inv] Layout: vp={vpW}x{vpH}, root={rootW}x{rootH}, scale={scale:F3}");
        }

        // ===== CANVAS HELPERS =====

        private void ConfigureNestedCanvases(GameObject root)
        {
            var canvases = root.GetComponentsInChildren<Canvas>(true);
            var p2Camera = SplitCameraManager.Instance?.Player2UiCamera;
            if (p2Camera == null) return;

            foreach (var canvas in canvases)
            {
                if (canvas.overrideSorting)
                    canvas.worldCamera = p2Camera;
            }
        }

        private void DestroyP2Canvas()
        {
            HideP2Inventory();
            if (_p2InvCanvasObj != null)
            {
                Destroy(_p2InvCanvasObj);
                _p2InvCanvasObj = null;
                _p2InvCanvas = null;
            }
        }

        // ===== UTILITY =====

        private static int CountDescendants(GameObject go)
        {
            if (go == null) return 0;
            int count = go.transform.childCount;
            for (int i = 0; i < go.transform.childCount; i++)
                count += CountDescendants(go.transform.GetChild(i).gameObject);
            return count;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
        }

        private void OnDestroy()
        {
            Instance = null;
        }
    }
}
