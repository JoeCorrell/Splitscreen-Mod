using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ValheimSplitscreen.UI
{
    /// <summary>
    /// Character selection UI for Player 2.
    /// Clones the full game menu canvas (like the P2 pause menu approach) and
    /// activates the character select sub-panel in P2's half. This gives P2
    /// the native game look instead of a custom dark background overlay.
    /// </summary>
    public class CharacterSelectUI : MonoBehaviour
    {
        public static CharacterSelectUI Instance { get; private set; }

        /// <summary>
        /// Set to true around Instantiate calls so Harmony prefixes can skip
        /// FejdStartup.Awake on cloned instances (prevents singleton clobbering).
        /// </summary>
        public static bool IsCloning { get; set; }

        public bool IsVisible { get; private set; }
        public bool IsMainMenuMode { get; set; }
        public bool IsMenuSplitMode { get; set; }

        private List<PlayerProfile> _profiles;
        private int _selectedIndex;
        private Action<PlayerProfile> _onSelected;
        private Action _onCancelled;

        // Cloned canvas (entire game UI)
        private GameObject _canvasRoot;
        private Canvas _canvas;
        private TMP_Text _characterNameText;
        private TMP_Text _sourceInfoText;
        private Button _leftButton;
        private Button _rightButton;

        // P2 UI camera — viewport rect confines clone to P2's screen half
        private GameObject _p2UICamObj;
        private UnityEngine.Camera _p2UICam;

        // Cursor state to restore on close (only used in in-game mode)
        private CursorLockMode _prevCursorLock;
        private bool _prevCursorVisible;

        // 3D character preview: override FejdStartup's camera and character model
        private bool _overridingCamera;

        // P2 world camera — renders the 3D scene from the character select angle
        // in P2's viewport, so P2 sees campfire + character while P1 sees the menu
        private GameObject _p2WorldCamObj;
        private UnityEngine.Camera _p2WorldCam;

        // Gamepad input for P2 character select (menu context — SplitInputManager isn't active yet)
        private ValheimSplitscreen.Input.PlayerInputState _p2MenuInput = new ValheimSplitscreen.Input.PlayerInputState();
        private float _stickCycleCooldown;
        private const float StickCycleInterval = 0.3f;
        private int _inputGraceFrames; // ignore input for N frames after Show() to avoid capturing the menu button press

        // Reflection cache for FejdStartup 3D preview
        private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo _fejdMainCamera = typeof(FejdStartup).GetField("m_mainCamera", BF);
        private static readonly FieldInfo _fejdCameraMarkerChar = typeof(FejdStartup).GetField("m_cameraMarkerCharacter", BF);
        private static readonly FieldInfo _fejdCameraMarkerMain = typeof(FejdStartup).GetField("m_cameraMarkerMain", BF);
        private static readonly FieldInfo _fejdPlayerInstance = typeof(FejdStartup).GetField("m_playerInstance", BF);
        private static readonly MethodInfo _fejdSetupPreview = FindFejdMethod("SetupCharacterPreview");
        private static readonly MethodInfo _fejdClearPreview = FindFejdMethod("ClearCharacterPreview");

        // EventSystem input module — disabled while P2 char select is open
        // to prevent the gamepad from also navigating P1's menu
        private BaseInputModule _disabledInputModule;

        // Fallback IMGUI (used only when cloning fails)
        private bool _useFallbackIMGUI;
        private Vector2 _scrollPos;
        private GUIStyle _buttonStyle;
        private GUIStyle _headerStyle;
        private bool _stylesInit;

        private static MethodInfo FindFejdMethod(string name)
        {
            foreach (var m in typeof(FejdStartup).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.Name == name) return m;
            return null;
        }

        private void Awake()
        {
            Instance = this;
            Debug.Log("[Splitscreen][CharSelect] Awake");
        }

        public void Show(Action<PlayerProfile> onSelected, Action onCancelled)
        {
            Debug.Log($"[Splitscreen][CharSelect] Show() called — IsMainMenuMode={IsMainMenuMode}, IsMenuSplitMode={IsMenuSplitMode}");

            Debug.Log($"[Splitscreen][CharSelect] Show() environment: screen={Screen.width}x{Screen.height}, " +
                $"fejd={(FejdStartup.instance != null ? "present" : "null")}, eventSystem={(EventSystem.current != null ? EventSystem.current.name : "null")}");
            // Clean up previous state if Show() is called while already visible
            if (IsVisible)
            {
                Debug.LogWarning("[Splitscreen][CharSelect] Show() called while already visible — cleaning up first");
                Hide();
            }

            _onSelected = onSelected;
            _onCancelled = onCancelled;

            Debug.Log("[Splitscreen][CharSelect] Loading player profiles...");
            _profiles = SaveSystem.GetAllPlayerProfiles();
            Debug.Log($"[Splitscreen][CharSelect] Found {_profiles.Count} profiles");
            for (int i = 0; i < _profiles.Count; i++)
                Debug.Log($"[Splitscreen][CharSelect]   Profile[{i}]: {_profiles[i].GetName()}");

            _selectedIndex = 0;
            _stickCycleCooldown = 0f;
            _inputGraceFrames = 3; // ignore gamepad for 3 frames so the menu A-press doesn't auto-select

            if (!IsMainMenuMode)
            {
                _prevCursorLock = Cursor.lockState;
                _prevCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            _useFallbackIMGUI = false;
            IsVisible = true;

            // Log available gamepads for P2 input
            Debug.Log($"[Splitscreen][CharSelect] Available gamepads: {Gamepad.all.Count}");
            for (int i = 0; i < Gamepad.all.Count; i++)
                Debug.Log($"[Splitscreen][CharSelect]   Gamepad[{i}]: {Gamepad.all[i].displayName}");

            if (!CreateClonedPanel())
            {
                Debug.LogWarning("[Splitscreen][CharSelect] Failed to clone game canvas, using fallback IMGUI");
                _useFallbackIMGUI = true;
            }

            // In menu split mode, do NOT disable EventSystem — P1 still needs
            // mouse input for their menu. P2 input is handled via gamepad polling
            // in Update(). In non-menu-split mode, disable to isolate P2.
            if (!IsMenuSplitMode)
                DisableEventSystemInput();
        }

        public void Hide()
        {
            Debug.Log($"[Splitscreen][CharSelect] Hide() called — IsVisible={IsVisible}, IsMenuSplitMode={IsMenuSplitMode}");
            IsVisible = false;
            StopCharacterPreview();
            RestoreEventSystemInput();

            if (!IsMainMenuMode && !IsMenuSplitMode)
            {
                Cursor.lockState = _prevCursorLock;
                Cursor.visible = _prevCursorVisible;
            }
            IsMenuSplitMode = false;

            if (_canvasRoot != null)
            {
                Destroy(_canvasRoot);
                _canvasRoot = null;
                _canvas = null;
                _characterNameText = null;
                _sourceInfoText = null;
                _leftButton = null;
                _rightButton = null;
            }
            if (_p2UICamObj != null)
            {
                Destroy(_p2UICamObj);
                _p2UICamObj = null;
                _p2UICam = null;
            }
            Debug.Log("[Splitscreen][CharSelect][Diag] Cleared cloned canvas and P2 UI camera");
            Debug.Log("[Splitscreen][CharSelect] Hidden and cleaned up");
        }

        private void Update()
        {
            if (!IsVisible) return;

            // Keyboard: ESC to cancel
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Debug.Log("[Splitscreen][CharSelect] ESC pressed, cancelling");
                Hide();
                _onCancelled?.Invoke();
                return;
            }

            // P2 gamepad input for character select navigation
            HandleP2GamepadInput();
        }

        /// <summary>
        /// Read P2's gamepad (or keyboard fallback) and handle character select navigation.
        /// D-pad Left/Right or Left stick = cycle characters.
        /// A (ButtonSouth) = select. B (ButtonEast) = back/cancel.
        /// </summary>
        private void HandleP2GamepadInput()
        {
            // Grace period: ignore input for a few frames after Show() to prevent
            // the A-button press that opened the menu from immediately selecting a character
            if (_inputGraceFrames > 0)
            {
                _inputGraceFrames--;
                return;
            }

            // Find P2's gamepad: use SplitInputManager if available, otherwise use second gamepad
            Gamepad p2Gamepad = null;
            var inputMgr = ValheimSplitscreen.Input.SplitInputManager.Instance;
            if (inputMgr != null)
            {
                p2Gamepad = inputMgr.GetGamepad(1);
            }
            else
            {
                // Fallback: if P1 uses keyboard, P2 gets first gamepad; otherwise P2 gets second
                int gpCount = Gamepad.all.Count;
                if (gpCount >= 2)
                    p2Gamepad = Gamepad.all[1];
                else if (gpCount == 1)
                    p2Gamepad = Gamepad.all[0]; // assume P1 is on keyboard
            }

            // Read input state with edge detection
            if (p2Gamepad != null)
                _p2MenuInput.ReadFromGamepad(p2Gamepad);
            else
                _p2MenuInput.ReadFromKeyboardFallback();

            // D-pad Left/Right: cycle characters (edge-triggered)
            if (_p2MenuInput.DpadLeftDown)
            {
                Debug.Log("[Splitscreen][CharSelect] P2 gamepad: D-pad Left");
                OnLeftClicked();
            }
            else if (_p2MenuInput.DpadRightDown)
            {
                Debug.Log("[Splitscreen][CharSelect] P2 gamepad: D-pad Right");
                OnRightClicked();
            }

            // Left stick horizontal: cycle with cooldown
            float stickX = _p2MenuInput.MoveAxis.x;
            if (_stickCycleCooldown > 0f)
            {
                _stickCycleCooldown -= Time.unscaledDeltaTime;
            }
            else if (Mathf.Abs(stickX) > 0.5f)
            {
                if (stickX < -0.5f)
                {
                    Debug.Log("[Splitscreen][CharSelect] P2 gamepad: Stick Left");
                    OnLeftClicked();
                }
                else
                {
                    Debug.Log("[Splitscreen][CharSelect] P2 gamepad: Stick Right");
                    OnRightClicked();
                }
                _stickCycleCooldown = StickCycleInterval;
            }

            // A button (ButtonSouth): select character
            if (_p2MenuInput.ButtonSouthDown)
            {
                Debug.Log("[Splitscreen][CharSelect] P2 gamepad: A pressed (select)");
                OnStartClicked();
            }

            // B button (ButtonEast): back/cancel
            if (_p2MenuInput.ButtonEastDown)
            {
                Debug.Log("[Splitscreen][CharSelect] P2 gamepad: B pressed (back)");
                OnBackClicked();
            }
        }

        /// <summary>
        /// Called from the Harmony postfix on FejdStartup.UpdateCamera.
        /// Overrides camera position to the character select campfire scene
        /// immediately after the game positions the camera each frame.
        /// </summary>
        public void OverrideCameraFromPatch(FejdStartup fejd)
        {
            if (fejd == null) return;

            // If we have a P2 world camera, keep it at the character marker
            if (_p2WorldCam != null)
            {
                var marker = _fejdCameraMarkerChar?.GetValue(fejd) as Transform;
                if (marker != null)
                {
                    _p2WorldCamObj.transform.position = marker.position;
                    _p2WorldCamObj.transform.rotation = marker.rotation;
                }
                return; // don't override Main Camera — P1 keeps their view
            }

            // Legacy: override Main Camera directly (non-menu-split mode)
            if (!_overridingCamera) return;

            var camObj = _fejdMainCamera?.GetValue(fejd) as GameObject;
            var markerC = _fejdCameraMarkerChar?.GetValue(fejd) as Transform;

            if (camObj != null && markerC != null)
            {
                camObj.transform.position = markerC.position;
                camObj.transform.rotation = markerC.rotation;
            }
        }

        // ===== FULL CANVAS CLONE (like P2 pause menu approach) =====

        private bool CreateClonedPanel()
        {
            Debug.Log("[Splitscreen][CharSelect] === CreateClonedPanel START ===");

            var fejd = FejdStartup.instance;
            if (fejd == null)
            {
                Debug.LogError("[Splitscreen][CharSelect] FAIL: FejdStartup.instance is null");
                return false;
            }

            Canvas sourceCanvas = FindRootMenuCanvas(fejd);
            if (sourceCanvas == null)
            {
                Debug.LogError("[Splitscreen][CharSelect] FAIL: Could not find root menu canvas");
                return false;
            }

            Debug.Log($"[Splitscreen][CharSelect] Source canvas: '{sourceCanvas.gameObject.name}', renderMode={sourceCanvas.renderMode}");
            LogCanvasDetails(sourceCanvas, "source_before_clone");

            // Deactivate source before cloning to prevent Awake/OnEnable from
            // firing on clone components (avoids ObjectDB NullRef, UnifiedPopup
            // duplicate errors, etc.)
            bool wasActive = sourceCanvas.gameObject.activeSelf;
            sourceCanvas.gameObject.SetActive(false);
            IsCloning = true;
            _canvasRoot = Instantiate(sourceCanvas.gameObject);
            IsCloning = false;
            sourceCanvas.gameObject.SetActive(wasActive);

            _canvasRoot.name = "P2_CharSelectCanvas";
            _canvasRoot.transform.SetParent(null, false);
            DontDestroyOnLoad(_canvasRoot);
            Debug.Log($"[Splitscreen][CharSelect] Cloned canvas (inactive), FejdStartup.instance valid: {FejdStartup.instance != null}");
            LogCloneTreeStats("clone_inactive");

            // Strip scripts while clone is still inactive — prevents any
            // remaining MonoBehaviours from running when we activate
            StripScripts(_canvasRoot);

            // Create P2 UI camera with viewport rect for P2's half.
            // This is the key change from the old RectMask2D approach:
            // a camera viewport reliably clips ALL rendering (including
            // nested sub-canvases) to the specified screen region.
            _p2UICamObj = new GameObject("P2_UICamera");
            DontDestroyOnLoad(_p2UICamObj);
            _p2UICam = _p2UICamObj.AddComponent<UnityEngine.Camera>();
            _p2UICam.clearFlags = CameraClearFlags.Depth;
            _p2UICam.cullingMask = BuildLayerMask(_canvasRoot);
            _p2UICam.depth = 51; // above P1's UI camera (depth 50)
            _p2UICam.orthographic = true;
            _p2UICam.nearClipPlane = 0.1f;
            _p2UICam.farClipPlane = 1000f;

            bool horizontal = MenuSplitController.Instance?.IsHorizontal ?? true;
            if (horizontal)
                _p2UICam.rect = new Rect(0, 0.5f, 1, 0.5f); // top half (P2)
            else
                _p2UICam.rect = new Rect(0.5f, 0, 0.5f, 1); // right half

            Debug.Log($"[Splitscreen][CharSelect] Created P2_UICamera: viewport={_p2UICam.rect}");

            // Set clone canvas to ScreenSpaceCamera so it renders only in P2's viewport
            _canvas = _canvasRoot.GetComponent<Canvas>();
            if (_canvas == null)
            {
                Debug.LogError("[Splitscreen][CharSelect] FAIL: Clone has no Canvas component!");
                return false;
            }
            LogCanvasDetails(_canvas, "clone_before_assign_camera");
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _p2UICam;
            _canvas.planeDistance = 1f;
            ConfigureCloneCanvasScalers();
            Debug.Log($"[Splitscreen][CharSelect] Clone canvas set to ScreenSpaceCamera with P2_UICamera " +
                $"(planeDistance={_canvas.planeDistance}, camNear={_p2UICam.nearClipPlane}, camFar={_p2UICam.farClipPlane})");
            LogCanvasDetails(_canvas, "clone_after_assign_camera");

            // Now activate the clone — scripts are already stripped so nothing bad fires
            _canvasRoot.SetActive(true);
            LogCloneTreeStats("clone_active_immediate");
            LogCameraDetails(_p2UICam, "p2_ui_cam_immediate");

            // Activate only the character select panel, hide everything else
            Transform charSelectPanel = ActivateCharSelectOnly(_canvasRoot);
            if (charSelectPanel == null)
            {
                Debug.LogError("[Splitscreen][CharSelect] FAIL: Character select panel not found in clone");
                return false;
            }

            WireUpCarousel(charSelectPanel.gameObject);
            WireUpBottomButtons(charSelectPanel.gameObject);
            DisableButtonTips(_canvasRoot);
            HideChangelogInClone(_canvasRoot);
            FixCanvasGroupsForInteraction(charSelectPanel.gameObject);
            EnsureGraphicRaycaster();
            EnsureButtonInteractability(charSelectPanel.gameObject);
            UpdateCharacterDisplay();
            if (IsMenuSplitMode)
                StartCharacterPreviewWithP2Camera();
            else
                StartCharacterPreview();
            StartCoroutine(LogCloneStateNextFrames(charSelectPanel));

            Debug.Log($"[Splitscreen][CharSelect] === CreateClonedPanel END — {_profiles.Count} characters available ===");
            return true;
        }

        /// <summary>Log hierarchy to Debug.Log for diagnostics.</summary>
        private static void LogHierarchy(Transform root, int maxDepth, int maxChildrenPerLevel)
        {
            LogHierarchyRecursive(root, 0, maxDepth, maxChildrenPerLevel);
        }

        private static void LogHierarchyRecursive(Transform t, int depth, int maxDepth, int maxChildren)
        {
            if (depth > maxDepth) return;
            string indent = new string(' ', depth * 2);
            var rt = t.GetComponent<RectTransform>();
            string rtInfo = rt != null
                ? $" anchors=({rt.anchorMin.x:F2},{rt.anchorMin.y:F2})-({rt.anchorMax.x:F2},{rt.anchorMax.y:F2})"
                : "";
            string maskInfo = t.GetComponent<RectMask2D>() != null ? " [MASK]" : "";
            Debug.Log($"[Splitscreen][Hierarchy] {indent}{t.name} active={t.gameObject.activeSelf}{rtInfo}{maskInfo} children={t.childCount}");
            int limit = Mathf.Min(t.childCount, maxChildren);
            for (int i = 0; i < limit; i++)
                LogHierarchyRecursive(t.GetChild(i), depth + 1, maxDepth, maxChildren);
            if (t.childCount > limit)
                Debug.Log($"[Splitscreen][Hierarchy] {indent}  ... and {t.childCount - limit} more");
        }
        private static int BuildLayerMask(GameObject root)
        {
            if (root == null) return 1 << 5;

            int mask = 0;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                mask |= 1 << transforms[i].gameObject.layer;
            }

            mask |= 1 << 5; // UI layer fallback
            return mask;
        }

        private void ConfigureCloneCanvasScalers()
        {
            if (_canvasRoot == null) return;

            bool horizontal = MenuSplitController.Instance?.IsHorizontal ?? true;

            var scalers = _canvasRoot.GetComponentsInChildren<CanvasScaler>(true);
            if (scalers == null || scalers.Length == 0)
            {
                Debug.Log("[Splitscreen][CharSelect] Clone has no CanvasScaler components");
                return;
            }

            for (int i = 0; i < scalers.Length; i++)
            {
                var scaler = scalers[i];
                if (scaler == null) continue;

                var oldMode = scaler.uiScaleMode;
                var oldRef = scaler.referenceResolution;

                if (oldMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                {
                    // Already configured for split by MenuSplitController (inherited
                    // from the P1 canvas we cloned). Just ensure matchWidthOrHeight
                    // is correct — do NOT double the reference again.
                    if (horizontal)
                        scaler.matchWidthOrHeight = 1f;
                    else
                        scaler.matchWidthOrHeight = 0f;

                    Debug.Log(
                        $"[Splitscreen][CharSelect] Clone scaler '{scaler.name}' already ScaleWithScreenSize — " +
                        $"kept ref={oldRef.x:F0}x{oldRef.y:F0}, set match={scaler.matchWidthOrHeight:F2}");
                }
                else
                {
                    // Original scaler (e.g. ConstantPixelSize) — switch to
                    // ScaleWithScreenSize and double the split dimension.
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    var newRef = oldRef;
                    if (horizontal)
                    {
                        newRef.y *= 2f;
                        scaler.matchWidthOrHeight = 1f;
                    }
                    else
                    {
                        newRef.x *= 2f;
                        scaler.matchWidthOrHeight = 0f;
                    }
                    scaler.referenceResolution = newRef;

                    Debug.Log(
                        $"[Splitscreen][CharSelect] Adjusted clone CanvasScaler '{scaler.name}': " +
                        $"mode={oldMode}->{scaler.uiScaleMode}, ref={oldRef.x:F0}x{oldRef.y:F0}->{newRef.x:F0}x{newRef.y:F0}, " +
                        $"matchMode={scaler.screenMatchMode}, match={scaler.matchWidthOrHeight:F2}");
                }
            }
        }

        private Canvas FindRootMenuCanvas(FejdStartup fejd)
        {
            // Try FejdStartup's own parent canvas
            var canvas = fejd.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.isRootCanvas)
            {
                Debug.Log($"[Splitscreen][CharSelect] Found root canvas via FejdStartup parent: '{canvas.gameObject.name}'");
                return canvas;
            }

            // Fallback: find root ScreenSpaceOverlay canvas that isn't ours
            var allCanvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var c in allCanvases)
            {
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay &&
                    c.gameObject.name != "SplitscreenMenuOverlay" &&
                    c.gameObject.name != "P2_CharSelectCanvas")
                {
                    Debug.Log($"[Splitscreen][CharSelect] Found root canvas by search: '{c.gameObject.name}'");
                    return c;
                }
            }

            return null;
        }

        // NOTE: UndoP1ConfinementInClone, UnwrapCloneWrapperToCanvasRoot, and
        // ClipToP2Half have been removed. The old approach used RectMask2D containers
        // (P1_MenuClip/P1_InnerFull/P2_HalfClip) which failed to clip across nested
        // Canvas boundaries. The new approach uses ScreenSpaceCamera with viewport rects
        // which reliably confines all rendering to the target screen region.

        /// <summary>
        /// In the cloned hierarchy, hide everything except the path from
        /// CharacterSelection to root. At every ancestor level, hide all
        /// siblings that aren't in the path — this catches MainMenu, Logo,
        /// StartGame, etc. at any nesting depth.
        /// </summary>
        private Transform ActivateCharSelectOnly(GameObject root)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);

            // First: find the CharacterSelection panel
            Transform charSelectPanel = null;
            foreach (var t in transforms)
            {
                string name = t.gameObject.name;
                if (name == "CharacterSelection" || name == "characterSelection" ||
                    name == "SelectCharacter" || name == "selectCharacter" ||
                    name == "CharacterSelect" || name == "characterSelect" ||
                    name.ToLowerInvariant().Contains("characterselect") ||
                    name.ToLowerInvariant().Contains("selectcharacter"))
                {
                    charSelectPanel = t;
                    Debug.Log($"[Splitscreen][CharSelect] Found character select panel: '{name}'");
                    break;
                }
            }

            if (charSelectPanel == null)
            {
                Debug.LogWarning("[Splitscreen][CharSelect] Could not find character select panel by name, trying field search");
                charSelectPanel = FindCharSelectTransform(root, transforms);
            }

            if (charSelectPanel == null)
            {
                Debug.LogError("[Splitscreen][CharSelect] Could not find character select panel at all!");
                return null;
            }

            // Build set of transforms in the path from CharacterSelection to root
            var enablePath = new HashSet<Transform>();
            var walk = charSelectPanel;
            while (walk != null && walk != root.transform)
            {
                enablePath.Add(walk);
                walk = walk.parent;
            }

            // At every level of the path, hide siblings that aren't in the path.
            // This catches MainMenu, Logo, StartGame, etc. at any nesting depth.
            foreach (var pathNode in new List<Transform>(enablePath))
            {
                var parent = pathNode.parent;
                if (parent == null) continue;

                for (int i = 0; i < parent.childCount; i++)
                {
                    var sibling = parent.GetChild(i);
                    if (enablePath.Contains(sibling)) continue;
                    if (sibling.name == "DimBackground") continue;

                    if (sibling.gameObject.activeSelf)
                    {
                        Debug.Log($"[Splitscreen][CharSelect] Hiding sibling: '{sibling.name}' (parent='{parent.name}')");
                        sibling.gameObject.SetActive(false);
                    }
                }
            }

            // Enable the character select panel and all ancestors in the path
            charSelectPanel.gameObject.SetActive(true);
            var ancestor = charSelectPanel.parent;
            while (ancestor != null && ancestor != root.transform)
            {
                ancestor.gameObject.SetActive(true);
                ancestor = ancestor.parent;
            }
            Debug.Log($"[Splitscreen][CharSelect] Activated character select panel: '{charSelectPanel.name}'");
            LogPanelVisibility(charSelectPanel, "activate_char_select_only");

            // Hide sub-panels that should stay hidden even inside char select
            foreach (var t in transforms)
            {
                string n = t.gameObject.name;
                if (n == "NewCharacterPanel" || n == "RemoveCharacterDialog")
                {
                    t.gameObject.SetActive(false);
                }
            }

            return charSelectPanel;
        }

        private Transform FindCharSelectTransform(GameObject root, Transform[] transforms)
        {
            foreach (var t in transforms)
            {
                string goName = t.gameObject.name.ToLowerInvariant();
                if (goName.Contains("characterselect") || goName.Contains("charselect") ||
                    goName.Contains("selectcharacter"))
                {
                    Debug.Log($"[Splitscreen][CharSelect] Found panel by pattern: '{t.gameObject.name}'");
                    return t;
                }
            }
            return null;
        }

        private void WireUpCarousel(GameObject panelClone)
        {
            var buttons = panelClone.GetComponentsInChildren<Button>(true);
            bool wiredLeft = false;
            bool wiredRight = false;
            foreach (var btn in buttons)
            {
                if (!btn.gameObject.activeInHierarchy) continue;
                string name = btn.gameObject.name;

                if (!wiredLeft && name == "Left")
                {
                    _leftButton = btn;
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(OnLeftClicked);
                    Debug.Log("[Splitscreen][CharSelect] Wired Left arrow button");
                    wiredLeft = true;
                }
                else if (!wiredRight && name == "Right")
                {
                    _rightButton = btn;
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(OnRightClicked);
                    Debug.Log("[Splitscreen][CharSelect] Wired Right arrow button");
                    wiredRight = true;
                }
            }

            // Find the CharacterName text
            var texts = panelClone.GetComponentsInChildren<TMP_Text>(true);
            foreach (var txt in texts)
            {
                if (txt.gameObject.name == "CharacterName")
                {
                    _characterNameText = txt;
                    Debug.Log("[Splitscreen][CharSelect] Found CharacterName text");
                }
                else if (txt.gameObject.name == "SourceInfoChar")
                {
                    _sourceInfoText = txt;
                }
            }
        }

        private void WireUpBottomButtons(GameObject panelClone)
        {
            var buttons = panelClone.GetComponentsInChildren<Button>(true);
            bool wiredStart = false;
            bool wiredBack = false;
            bool wiredNew = false;
            bool wiredNewBig = false;
            foreach (var btn in buttons)
            {
                if (!btn.gameObject.activeInHierarchy) continue;
                string name = btn.gameObject.name;

                if (!wiredStart && name == "Start")
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(OnStartClicked);
                    Debug.Log("[Splitscreen][CharSelect] Wired Start button");
                    wiredStart = true;
                }
                else if (!wiredBack && name == "Back")
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(OnBackClicked);
                    Debug.Log("[Splitscreen][CharSelect] Wired Back button");
                    wiredBack = true;
                }
                else if ((!wiredNew && name == "New") || (!wiredNewBig && name == "New_big"))
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(OnNewClicked);
                    Debug.Log($"[Splitscreen][CharSelect] Wired {name} button");
                    if (name == "New") wiredNew = true;
                    if (name == "New_big") wiredNewBig = true;
                }
            }

            Debug.Log($"[Splitscreen][CharSelect][Diag] Button wiring summary: start={wiredStart}, back={wiredBack}, new={wiredNew}, newBig={wiredNewBig}");
        }

        private void UpdateTitleForP2(GameObject panelClone)
        {
            var texts = panelClone.GetComponentsInChildren<TMP_Text>(true);
            foreach (var txt in texts)
            {
                if (txt.gameObject.name == "Topic")
                {
                    txt.text = "Player 2 - Select Character";
                    break;
                }
            }
        }

        /// <summary>
        /// Move the active character select panel lower in P2's half so it's not
        /// floating in the center. Adjusts the anchoredPosition downward.
        /// </summary>
        private void PositionContentLower(Transform charSelectPanel)
        {
            if (charSelectPanel == null) return;
            var rt = charSelectPanel.GetComponent<RectTransform>();
            if (rt == null) return;

            // Anchor panel into the lower area so it fits better in a half-screen viewport.
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0.85f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Debug.Log($"[Splitscreen][CharSelect] Repositioned '{charSelectPanel.name}' to lower area");
        }

        // Deprecated root-scan helper kept for compatibility with existing call sites, if any.
        private void PositionContentLower(GameObject root)
        {
            if (root == null) return;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (!t.gameObject.activeSelf) continue;
                string name = t.name;
                if (name == "CharacterSelection" || name == "characterSelection" ||
                    name == "SelectCharacter" || name == "selectCharacter" ||
                    name == "CharacterSelect" || name == "characterSelect" ||
                    name.ToLowerInvariant().Contains("characterselect") ||
                    name.ToLowerInvariant().Contains("selectcharacter"))
                {
                    PositionContentLower(t);
                    break;
                }
            }
        }

        // ===== 3D CHARACTER PREVIEW =====

        /// <summary>
        /// Start overriding the camera and spawn the character preview model.
        /// Called when the cloned character select UI opens.
        /// </summary>
        private void StartCharacterPreview()
        {
            var fejd = FejdStartup.instance;
            if (fejd == null)
            {
                Debug.LogWarning("[Splitscreen][CharSelect] StartCharacterPreview: FejdStartup.instance is null, skipping");
                return;
            }

            // Log reflection state for debugging
            // NOTE: m_mainCamera is a GameObject, not a Camera component
            var camObj = _fejdMainCamera?.GetValue(fejd) as GameObject;
            var cam = camObj?.GetComponent<UnityEngine.Camera>();
            var marker = _fejdCameraMarkerChar?.GetValue(fejd) as Transform;
            var markerMain = _fejdCameraMarkerMain?.GetValue(fejd) as Transform;
            Debug.Log($"[Splitscreen][CharSelect] Starting 3D preview - reflection fields: " +
                $"camField={_fejdMainCamera != null}, markerField={_fejdCameraMarkerChar != null}, " +
                $"setupMethod={_fejdSetupPreview != null}, clearMethod={_fejdClearPreview != null}");
            Debug.Log($"[Splitscreen][CharSelect] Reflection values: " +
                $"camObj={(camObj != null ? camObj.name : "NULL")}, " +
                $"camComponent={(cam != null ? cam.name : "NULL")}, " +
                $"charMarker={(marker != null ? marker.name + " pos=" + marker.position : "NULL")}, " +
                $"mainMarker={(markerMain != null ? markerMain.name + " pos=" + markerMain.position : "NULL")}");

            if (camObj == null)
                Debug.LogError("[Splitscreen][CharSelect] m_mainCamera GameObject is NULL!");
            else if (cam == null)
                Debug.LogError("[Splitscreen][CharSelect] m_mainCamera has no Camera component!");
            if (marker == null)
                Debug.LogError("[Splitscreen][CharSelect] m_cameraMarkerCharacter is NULL — camera override will not work!");

            _overridingCamera = true;
            Debug.Log("[Splitscreen][CharSelect] Camera override enabled (via Harmony postfix on FejdStartup.UpdateCamera)");

            // Set up initial character model
            if (_profiles != null && _profiles.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _profiles.Count)
            {
                InvokeSetupPreview(_profiles[_selectedIndex]);
            }
        }

        /// <summary>
        /// Create a P2-specific world camera that renders the character select 3D scene
        /// (campfire + character model) in P2's viewport. This avoids overriding Main Camera,
        /// so P1 keeps the normal menu background while P2 sees the character select scene.
        /// </summary>
        private void StartCharacterPreviewWithP2Camera()
        {
            var fejd = FejdStartup.instance;
            if (fejd == null)
            {
                Debug.LogWarning("[Splitscreen][CharSelect] StartCharacterPreviewWithP2Camera: FejdStartup null, skipping");
                return;
            }

            var mainCamObj = _fejdMainCamera?.GetValue(fejd) as GameObject;
            var mainCam = mainCamObj?.GetComponent<UnityEngine.Camera>();
            var marker = _fejdCameraMarkerChar?.GetValue(fejd) as Transform;

            if (mainCam == null || marker == null)
            {
                Debug.LogWarning($"[Splitscreen][CharSelect] P2 world camera: mainCam={(mainCam != null ? "OK" : "NULL")}, marker={(marker != null ? "OK" : "NULL")}");
                // Fall back to camera override approach
                StartCharacterPreview();
                return;
            }

            // Create P2's world camera — DON'T use CopyFrom because it copies
            // depthTextureMode and rendering settings that corrupt Valheim's shared
            // depth buffers. Only set the essentials manually.
            _p2WorldCamObj = new GameObject("P2_WorldCamera");
            DontDestroyOnLoad(_p2WorldCamObj);
            _p2WorldCam = _p2WorldCamObj.AddComponent<UnityEngine.Camera>();

            _p2WorldCam.fieldOfView = mainCam.fieldOfView;
            _p2WorldCam.nearClipPlane = mainCam.nearClipPlane;
            _p2WorldCam.farClipPlane = mainCam.farClipPlane;
            _p2WorldCam.cullingMask = mainCam.cullingMask;
            _p2WorldCam.clearFlags = CameraClearFlags.Skybox;
            _p2WorldCam.backgroundColor = Color.black;
            _p2WorldCam.depthTextureMode = DepthTextureMode.None;
            _p2WorldCam.allowHDR = false; // avoid shared HDR buffer conflicts
            _p2WorldCam.allowMSAA = false;

            // Viewport = P2's half, depth between Main Camera (0) and P1 UI camera (50)
            bool horizontal = MenuSplitController.Instance?.IsHorizontal ?? true;
            _p2WorldCam.rect = horizontal
                ? new Rect(0, 0.5f, 1, 0.5f)  // top half
                : new Rect(0.5f, 0, 0.5f, 1);  // right half
            _p2WorldCam.depth = 1; // above Main Camera (0), below UI cameras (50+)

            // Position at character select marker
            _p2WorldCamObj.transform.position = marker.position;
            _p2WorldCamObj.transform.rotation = marker.rotation;

            Debug.Log($"[Splitscreen][CharSelect] Created P2_WorldCamera: viewport={_p2WorldCam.rect}, " +
                $"pos={marker.position}, rot={marker.rotation.eulerAngles}, depth={_p2WorldCam.depth}");

            // Spawn the character model (this uses FejdStartup's internal system)
            if (_profiles != null && _profiles.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _profiles.Count)
            {
                InvokeSetupPreview(_profiles[_selectedIndex]);
            }

            // Don't override Main Camera — P1 keeps their normal menu view
            _overridingCamera = false;
        }

        /// <summary>
        /// Stop overriding the camera and clear the character preview.
        /// </summary>
        private void StopCharacterPreview()
        {
            Debug.Log("[Splitscreen][CharSelect] Stopping character preview, disabling camera override");
            _overridingCamera = false;

            // Destroy P2 world camera if we created one
            if (_p2WorldCamObj != null)
            {
                Destroy(_p2WorldCamObj);
                _p2WorldCamObj = null;
                _p2WorldCam = null;
                Debug.Log("[Splitscreen][CharSelect] Destroyed P2_WorldCamera");
            }

            var fejd = FejdStartup.instance;
            if (fejd == null) return;

            // Clear the character model
            if (_fejdClearPreview != null)
            {
                try
                {
                    _fejdClearPreview.Invoke(fejd, null);
                    Debug.Log("[Splitscreen][CharSelect] Cleared character preview");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Splitscreen][CharSelect] ClearCharacterPreview failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Call FejdStartup.SetupCharacterPreview to spawn/update the 3D character model.
        /// </summary>
        private void InvokeSetupPreview(PlayerProfile profile)
        {
            var fejd = FejdStartup.instance;
            if (fejd == null || _fejdSetupPreview == null) return;

            try
            {
                var paramCount = _fejdSetupPreview.GetParameters().Length;
                if (paramCount >= 1)
                    _fejdSetupPreview.Invoke(fejd, new object[] { profile });
                else
                    _fejdSetupPreview.Invoke(fejd, null);

                Debug.Log($"[Splitscreen][CharSelect] SetupCharacterPreview called for '{profile?.GetName()}'");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Splitscreen][CharSelect] SetupCharacterPreview failed: {ex.Message}");
            }
        }

        // ===== CAROUSEL NAVIGATION =====

        private void OnLeftClicked()
        {
            if (_profiles == null || _profiles.Count == 0) return;
            _selectedIndex--;
            if (_selectedIndex < 0) _selectedIndex = _profiles.Count - 1;
            UpdateCharacterDisplay();
        }

        private void OnRightClicked()
        {
            if (_profiles == null || _profiles.Count == 0) return;
            _selectedIndex++;
            if (_selectedIndex >= _profiles.Count) _selectedIndex = 0;
            UpdateCharacterDisplay();
        }

        private void OnStartClicked()
        {
            if (_profiles == null || _profiles.Count == 0) return;
            if (_selectedIndex >= 0 && _selectedIndex < _profiles.Count)
            {
                OnCharacterSelected(_profiles[_selectedIndex]);
            }
        }

        private void OnBackClicked()
        {
            Debug.Log("[Splitscreen][CharSelect] Back clicked");
            Hide();
            _onCancelled?.Invoke();
        }

        private void OnNewClicked()
        {
            Debug.Log("[Splitscreen][CharSelect] New character selected");
            OnCharacterSelected(null);
        }

        private void UpdateCharacterDisplay()
        {
            if (_profiles == null || _profiles.Count == 0)
            {
                if (_characterNameText != null)
                    _characterNameText.text = "No characters found";
                if (_sourceInfoText != null)
                    _sourceInfoText.text = "Press 'New' to create one";
                return;
            }

            var profile = _profiles[_selectedIndex];
            if (_characterNameText != null)
                _characterNameText.text = profile.GetName();
            if (_sourceInfoText != null)
                _sourceInfoText.text = $"Character {_selectedIndex + 1} of {_profiles.Count}";

            // Update the 3D character model to match the selected profile
            if (_overridingCamera || _p2WorldCam != null)
                InvokeSetupPreview(profile);
        }

        private void OnCharacterSelected(PlayerProfile profile)
        {
            Debug.Log($"[Splitscreen][CharSelect] Selected: {(profile != null ? profile.GetName() : "CREATE NEW")}");
            Hide();
            _onSelected?.Invoke(profile);
        }

        private IEnumerator LogCloneStateNextFrames(Transform activePanel)
        {
            yield return null;
            LogCloneTreeStats("clone_frame1");
            LogCanvasDetails(_canvas, "clone_canvas_frame1");
            LogCameraDetails(_p2UICam, "p2_ui_cam_frame1");
            LogPanelVisibility(activePanel, "panel_frame1");
            LogP2ScalingDiagnostics("clone_frame1");

            yield return null;
            LogCloneTreeStats("clone_frame2");
            LogCanvasDetails(_canvas, "clone_canvas_frame2");
            LogCameraDetails(_p2UICam, "p2_ui_cam_frame2");
            LogPanelVisibility(activePanel, "panel_frame2");
            LogP2ScalingDiagnostics("clone_frame2");
        }

        // ===== UTILITY =====

        /// <summary>
        /// Strip all non-essential scripts from the clone.
        /// Uses DestroyImmediate because the clone is inactive at this point —
        /// deferred Destroy would leave components alive when we SetActive(true),
        /// causing their Awake/OnEnable to fire (e.g. FejdStartup.Awake clobbers
        /// the singleton, ObjectDB.Awake triggers other mods' Harmony patches).
        /// </summary>
        private void StripScripts(GameObject root)
        {
            int stripped = 0;
            var allComponents = root.GetComponentsInChildren<Component>(true);
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                if (comp is RectTransform) continue;
                if (comp is CanvasRenderer) continue;
                if (comp is Canvas) continue;
                if (comp is CanvasScaler) continue;
                if (comp is GraphicRaycaster) continue;
                if (comp is Button) continue;
                if (comp is Image) continue;
                if (comp is RawImage) continue;
                if (comp is Text) continue;
                if (comp is LayoutGroup) continue;
                if (comp is LayoutElement) continue;
                if (comp is ContentSizeFitter) continue;
                if (comp is Selectable) continue;
                if (comp is Mask) continue;
                if (comp is RectMask2D) continue;
                if (comp is ScrollRect) continue;
                if (comp is Scrollbar) continue;
                if (comp is Toggle) continue;
                if (comp is Slider) continue;

                string typeName = comp.GetType().Name;
                if (typeName.Contains("TMP_") || typeName.Contains("TextMeshPro")) continue;

                if (comp is MonoBehaviour mb)
                {
                    DestroyImmediate(mb);
                    stripped++;
                }
                else if (comp is Animator anim)
                {
                    DestroyImmediate(anim);
                    stripped++;
                }
            }
            Debug.Log($"[Splitscreen][CharSelect] Stripped {stripped} scripts from canvas clone");
        }

        private static void DisableButtonTips(GameObject root)
        {
            int disabled = 0;
            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                string name = t.gameObject.name.ToLowerInvariant();
                if (name.Contains("buttontip") || name.Contains("button_tip") ||
                    name.Contains("keyhint") || name.Contains("key_hint") ||
                    name.Contains("gamepadtip") || name.Contains("gamepad_tip") ||
                    name.Contains("keyboardhint"))
                {
                    t.gameObject.SetActive(false);
                    disabled++;
                }
            }
            if (disabled > 0)
                Debug.Log($"[Splitscreen][CharSelect] Disabled {disabled} button tip elements");

            // Also clear any TMP_Text that shows "MISSING BUTTON DEF" placeholders
            int cleared = 0;
            var texts = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var txt in texts)
            {
                if (txt.text != null && txt.text.Contains("MISSING BUTTON DEF"))
                {
                    txt.text = "";
                    cleared++;
                }
            }
            if (cleared > 0)
                Debug.Log($"[Splitscreen][CharSelect] Cleared {cleared} 'MISSING BUTTON DEF' text elements");
        }

        private static void HideChangelogInClone(GameObject root)
        {
            int hidden = 0;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                string name = t.gameObject.name;
                if (name == "Canvas Changelog" || name == "Changelog" ||
                    name.ToLowerInvariant().Contains("changelog"))
                {
                    t.gameObject.SetActive(false);
                    hidden++;
                    Debug.Log($"[Splitscreen][CharSelect] Hidden changelog element: '{name}'");
                }
            }
            if (hidden == 0)
                Debug.Log("[Splitscreen][CharSelect] No changelog elements found to hide");
        }

        /// <summary>
        /// Fix CanvasGroup components so P2's clone buttons receive raycasts and interaction.
        /// Valheim uses CanvasGroups to control panel visibility — hidden panels have
        /// interactable=false and blocksRaycasts=false. The clone inherits these disabled
        /// states since the character select panel was inactive in the source canvas.
        /// </summary>
        private void FixCanvasGroupsForInteraction(GameObject charSelectPanel)
        {
            int fixedCount = 0;

            // Fix all CanvasGroups on the panel and its children
            var panelGroups = charSelectPanel.GetComponentsInChildren<CanvasGroup>(true);
            foreach (var cg in panelGroups)
            {
                bool needsFix = !cg.interactable || !cg.blocksRaycasts || cg.alpha < 0.01f;
                if (needsFix)
                {
                    Debug.Log($"[Splitscreen][CharSelect] Fixing CanvasGroup on '{cg.gameObject.name}': " +
                        $"interactable={cg.interactable}->true, blocksRaycasts={cg.blocksRaycasts}->true, alpha={cg.alpha:F2}->{(cg.alpha < 0.01f ? "1" : "kept")}");
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                    if (cg.alpha < 0.01f) cg.alpha = 1f;
                    fixedCount++;
                }
            }

            // Fix CanvasGroups on ancestors between panel and canvas root
            var walk = charSelectPanel.transform.parent;
            while (walk != null && _canvasRoot != null && walk != _canvasRoot.transform)
            {
                var cg = walk.GetComponent<CanvasGroup>();
                if (cg != null && (!cg.interactable || !cg.blocksRaycasts))
                {
                    Debug.Log($"[Splitscreen][CharSelect] Fixing ancestor CanvasGroup on '{cg.gameObject.name}': " +
                        $"interactable={cg.interactable}->true, blocksRaycasts={cg.blocksRaycasts}->true");
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                    fixedCount++;
                }
                walk = walk.parent;
            }

            // Fix on canvas root itself
            if (_canvasRoot != null)
            {
                var rootCG = _canvasRoot.GetComponent<CanvasGroup>();
                if (rootCG != null && (!rootCG.interactable || !rootCG.blocksRaycasts))
                {
                    Debug.Log($"[Splitscreen][CharSelect] Fixing root CanvasGroup: " +
                        $"interactable={rootCG.interactable}->true, blocksRaycasts={rootCG.blocksRaycasts}->true");
                    rootCG.interactable = true;
                    rootCG.blocksRaycasts = true;
                    fixedCount++;
                }
            }

            // Log all CanvasGroups in the entire clone for diagnostics
            if (_canvasRoot != null)
            {
                var allGroups = _canvasRoot.GetComponentsInChildren<CanvasGroup>(true);
                Debug.Log($"[Splitscreen][CharSelect] CanvasGroup summary: {allGroups.Length} total, {fixedCount} fixed");
                foreach (var cg in allGroups)
                {
                    Debug.Log($"[Splitscreen][CharSelect]   CG '{cg.gameObject.name}': interactable={cg.interactable}, " +
                        $"blocksRaycasts={cg.blocksRaycasts}, alpha={cg.alpha:F2}, activeInHierarchy={cg.gameObject.activeInHierarchy}");
                }
            }
        }

        /// <summary>
        /// Ensure the clone canvas has an active GraphicRaycaster so pointer events work.
        /// </summary>
        private void EnsureGraphicRaycaster()
        {
            if (_canvasRoot == null) return;

            var raycaster = _canvasRoot.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                raycaster = _canvasRoot.AddComponent<GraphicRaycaster>();
                Debug.Log("[Splitscreen][CharSelect] Added missing GraphicRaycaster to clone canvas");
            }
            else if (!raycaster.enabled)
            {
                raycaster.enabled = true;
                Debug.Log("[Splitscreen][CharSelect] Re-enabled GraphicRaycaster on clone canvas");
            }
            else
            {
                Debug.Log($"[Splitscreen][CharSelect] GraphicRaycaster OK: enabled={raycaster.enabled}");
            }

            // Log EventSystem state
            var es = EventSystem.current;
            if (es != null)
            {
                var module = es.currentInputModule;
                Debug.Log($"[Splitscreen][CharSelect] EventSystem: '{es.name}', enabled={es.enabled}, " +
                    $"module={(module != null ? module.GetType().Name + " enabled=" + module.enabled : "null")}");
            }
            else
            {
                Debug.LogWarning("[Splitscreen][CharSelect] No EventSystem.current found!");
            }
        }

        /// <summary>
        /// Ensure all buttons in the character select panel are interactable and
        /// their target graphics have raycastTarget enabled.
        /// </summary>
        private static void EnsureButtonInteractability(GameObject charSelectPanel)
        {
            var buttons = charSelectPanel.GetComponentsInChildren<Button>(true);
            int fixedInteractable = 0;
            int fixedRaycast = 0;

            foreach (var btn in buttons)
            {
                if (!btn.interactable)
                {
                    btn.interactable = true;
                    fixedInteractable++;
                    Debug.Log($"[Splitscreen][CharSelect] Fixed non-interactable button: '{btn.gameObject.name}'");
                }

                // Ensure the button's target graphic accepts raycasts
                var graphic = btn.targetGraphic;
                if (graphic != null && !graphic.raycastTarget)
                {
                    graphic.raycastTarget = true;
                    fixedRaycast++;
                    Debug.Log($"[Splitscreen][CharSelect] Fixed raycastTarget on '{btn.gameObject.name}' graphic");
                }

                // Also check the button's own Image if targetGraphic is null
                if (graphic == null)
                {
                    var img = btn.GetComponent<Image>();
                    if (img != null)
                    {
                        img.raycastTarget = true;
                        btn.targetGraphic = img;
                        fixedRaycast++;
                        Debug.Log($"[Splitscreen][CharSelect] Assigned Image as targetGraphic on '{btn.gameObject.name}'");
                    }
                }
            }

            Debug.Log($"[Splitscreen][CharSelect] Button interactability check: {buttons.Length} buttons, " +
                $"{fixedInteractable} made interactable, {fixedRaycast} raycast targets fixed");
        }

        private static void SetButtonText(Button btn, string text)
        {
            var tmp = btn.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) { tmp.text = text; return; }
            var uiText = btn.GetComponentInChildren<Text>(true);
            if (uiText != null) uiText.text = text;
        }

        // ===== FALLBACK IMGUI =====

        private void OnGUI()
        {
            if (!IsVisible || !_useFallbackIMGUI) return;

            if (!_stylesInit)
            {
                _buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 18,
                    fixedHeight = 40,
                    margin = new RectOffset(5, 5, 3, 3)
                };
                _headerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _stylesInit = true;
            }

            if (IsMenuSplitMode && MenuSplitController.Instance != null)
                DrawFallbackInRect(MenuSplitController.Instance.GetP2ScreenRect());
            else
                DrawFallbackFullScreen();
        }

        private void DrawFallbackInRect(Rect area)
        {
            float panelW = Mathf.Min(area.width - 60f, 500f);
            float panelH = Mathf.Min(area.height - 60f, 600f);
            float panelX = area.x + (area.width - panelW) / 2f;
            float panelY = area.y + (area.height - panelH) / 2f;

            GUILayout.BeginArea(new Rect(panelX, panelY, panelW, panelH));
            GUILayout.Label("Player 2 - Select Character", _headerStyle);
            GUILayout.Space(10);
            DrawFallbackProfileList(panelH - 120);
            GUILayout.EndArea();
        }

        private void DrawFallbackFullScreen()
        {
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float panelW = 450f;
            float panelH = Mathf.Min(Screen.height * 0.7f, 600f);
            float panelX = (Screen.width - panelW) / 2f;
            float panelY = (Screen.height - panelH) / 2f;

            GUILayout.BeginArea(new Rect(panelX, panelY, panelW, panelH));
            GUILayout.Label("Select Character for Player 2", _headerStyle);
            GUILayout.Space(10);
            DrawFallbackProfileList(panelH - 120);
            GUILayout.EndArea();
        }

        private void DrawFallbackProfileList(float listHeight)
        {
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(listHeight));

            if (_profiles != null && _profiles.Count > 0)
            {
                foreach (var profile in _profiles)
                {
                    if (GUILayout.Button(profile.GetName(), _buttonStyle))
                        OnCharacterSelected(profile);
                }
            }
            else
            {
                GUILayout.Label("No saved characters found.");
            }

            GUILayout.EndScrollView();
            GUILayout.Space(8);

            if (GUILayout.Button("+ Create New Character", _buttonStyle))
                OnCharacterSelected(null);

            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            if (GUILayout.Button("Cancel (Esc)", _buttonStyle))
            {
                Hide();
                _onCancelled?.Invoke();
            }
            GUI.color = Color.white;
        }

        private void DisableEventSystemInput()
        {
            var es = EventSystem.current;
            if (es != null)
            {
                _disabledInputModule = es.currentInputModule;
                if (_disabledInputModule != null)
                {
                    _disabledInputModule.enabled = false;
                    Debug.Log($"[Splitscreen][CharSelect] Disabled EventSystem input module ({_disabledInputModule.GetType().Name}) to isolate P2 gamepad");
                }
            }
        }

        private void RestoreEventSystemInput()
        {
            if (_disabledInputModule != null)
            {
                _disabledInputModule.enabled = true;
                Debug.Log("[Splitscreen][CharSelect] Restored EventSystem input module");
                _disabledInputModule = null;
            }
        }

        private static void LogCanvasDetails(Canvas canvas, string tag)
        {
            if (canvas == null)
            {
                Debug.Log($"[Splitscreen][CharSelect][Diag] Canvas '{tag}' is null");
                return;
            }

            var rt = canvas.GetComponent<RectTransform>();
            string anchors = rt != null
                ? $"anchors=({rt.anchorMin.x:F2},{rt.anchorMin.y:F2})-({rt.anchorMax.x:F2},{rt.anchorMax.y:F2}), size=({rt.rect.width:F1}x{rt.rect.height:F1})"
                : "anchors=n/a";

            int childCanvasCount = canvas.GetComponentsInChildren<Canvas>(true).Length;
            int graphicsCount = canvas.GetComponentsInChildren<Graphic>(true).Length;

            Debug.Log($"[Splitscreen][CharSelect][Diag] Canvas '{tag}': name='{canvas.name}', active={canvas.gameObject.activeInHierarchy}, enabled={canvas.enabled}, " +
                $"renderMode={canvas.renderMode}, worldCamera={(canvas.worldCamera != null ? canvas.worldCamera.name : "null")}, planeDistance={canvas.planeDistance}, " +
                $"sortOrder={canvas.sortingOrder}, {anchors}, childCanvases={childCanvasCount}, graphics={graphicsCount}");
        }

        private static void LogCameraDetails(UnityEngine.Camera camera, string tag)
        {
            if (camera == null)
            {
                Debug.Log($"[Splitscreen][CharSelect][Diag] Camera '{tag}' is null");
                return;
            }

            Debug.Log($"[Splitscreen][CharSelect][Diag] Camera '{tag}': name='{camera.name}', active={camera.gameObject.activeInHierarchy}, enabled={camera.enabled}, " +
                $"clear={camera.clearFlags}, rect={camera.rect}, depth={camera.depth}, near={camera.nearClipPlane}, far={camera.farClipPlane}, " +
                $"cull={camera.cullingMask}, target={(camera.targetTexture != null ? camera.targetTexture.name : "SCREEN")}");
        }

        private void LogCloneTreeStats(string tag)
        {
            if (_canvasRoot == null)
            {
                Debug.Log($"[Splitscreen][CharSelect][Diag] Clone stats '{tag}': canvasRoot is null");
                return;
            }

            var transforms = _canvasRoot.GetComponentsInChildren<Transform>(true);
            var graphics = _canvasRoot.GetComponentsInChildren<Graphic>(true);
            var canvases = _canvasRoot.GetComponentsInChildren<Canvas>(true);

            int activeObjects = 0;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].gameObject.activeInHierarchy)
                {
                    activeObjects++;
                }
            }

            int activeGraphics = 0;
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null && graphics[i].gameObject.activeInHierarchy)
                {
                    activeGraphics++;
                }
            }

            Debug.Log($"[Splitscreen][CharSelect][Diag] Clone stats '{tag}': name='{_canvasRoot.name}', active={_canvasRoot.activeInHierarchy}, " +
                $"objects={transforms.Length}, activeObjects={activeObjects}, canvases={canvases.Length}, graphics={graphics.Length}, activeGraphics={activeGraphics}");
        }

        private static void LogPanelVisibility(Transform panel, string tag)
        {
            if (panel == null)
            {
                Debug.Log($"[Splitscreen][CharSelect][Diag] Panel visibility '{tag}': panel is null");
                return;
            }

            int childActive = 0;
            for (int i = 0; i < panel.childCount; i++)
            {
                if (panel.GetChild(i).gameObject.activeSelf) childActive++;
            }

            Debug.Log($"[Splitscreen][CharSelect][Diag] Panel visibility '{tag}': panel='{panel.name}', activeSelf={panel.gameObject.activeSelf}, " +
                $"activeInHierarchy={panel.gameObject.activeInHierarchy}, children={panel.childCount}, activeChildren={childActive}");
        }

        /// <summary>
        /// Log comprehensive P2 scaling diagnostics: camera pixel rect, canvas rect
        /// transform size, local scale (the effective scale factor from CanvasScaler),
        /// and the CanvasScaler configuration.
        /// </summary>
        private void LogP2ScalingDiagnostics(string tag)
        {
            Debug.Log($"[Splitscreen][Scale][P2] === P2 scaling diagnostics ({tag}) ===");
            Debug.Log($"[Splitscreen][Scale][P2] Screen: {Screen.width}x{Screen.height}");

            if (_p2UICam != null)
            {
                Debug.Log($"[Splitscreen][Scale][P2] P2_UICamera: pixelRect={_p2UICam.pixelRect}, " +
                    $"pixelW={_p2UICam.pixelWidth}, pixelH={_p2UICam.pixelHeight}, " +
                    $"viewport={_p2UICam.rect}, enabled={_p2UICam.enabled}");
            }
            else
            {
                Debug.Log("[Splitscreen][Scale][P2] P2_UICamera: NULL");
            }

            if (_canvas != null)
            {
                var rt = _canvas.GetComponent<RectTransform>();
                Debug.Log($"[Splitscreen][Scale][P2] P2 Canvas '{_canvas.name}': " +
                    $"renderMode={_canvas.renderMode}, " +
                    $"rtSize={rt?.rect.width:F1}x{rt?.rect.height:F1}, " +
                    $"localScale=({rt?.localScale.x:F4},{rt?.localScale.y:F4}), " +
                    $"scaleFactor={_canvas.scaleFactor:F4}, " +
                    $"worldCam={(_canvas.worldCamera != null ? _canvas.worldCamera.name : "null")}");

                var scaler = _canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    Debug.Log($"[Splitscreen][Scale][P2] P2 CanvasScaler: mode={scaler.uiScaleMode}, " +
                        $"scaleFactor={scaler.scaleFactor:F4}, " +
                        $"ref={scaler.referenceResolution.x:F0}x{scaler.referenceResolution.y:F0}, " +
                        $"matchMode={scaler.screenMatchMode}, match={scaler.matchWidthOrHeight:F2}");
                }
            }
            else
            {
                Debug.Log("[Splitscreen][Scale][P2] P2 Canvas: NULL");
            }

            Debug.Log($"[Splitscreen][Scale][P2] === End P2 scaling diagnostics ({tag}) ===");
        }

        private void OnDestroy()
        {
            RestoreEventSystemInput();
            _overridingCamera = false;
            if (_canvasRoot != null) Destroy(_canvasRoot);
            if (_p2UICamObj != null) Destroy(_p2UICamObj);
            if (_p2WorldCamObj != null) Destroy(_p2WorldCamObj);
            Instance = null;
        }
    }
}

