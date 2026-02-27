using System;
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

        // Cursor state to restore on close (only used in in-game mode)
        private CursorLockMode _prevCursorLock;
        private bool _prevCursorVisible;

        // 3D character preview: override FejdStartup's camera and character model
        private bool _overridingCamera;

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
        }

        public void Show(Action<PlayerProfile> onSelected, Action onCancelled)
        {
            Debug.Log($"[Splitscreen][CharSelect] Show() called — IsMainMenuMode={IsMainMenuMode}, IsMenuSplitMode={IsMenuSplitMode}");

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

            // Disable EventSystem input module to prevent P2's gamepad from
            // also navigating P1's menu buttons via Unity's EventSystem
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
            if (!_overridingCamera || fejd == null) return;

            var camObj = _fejdMainCamera?.GetValue(fejd) as GameObject;
            var marker = _fejdCameraMarkerChar?.GetValue(fejd) as Transform;

            if (camObj != null && marker != null)
            {
                camObj.transform.position = marker.position;
                camObj.transform.rotation = marker.rotation;
            }
        }

        // ===== FULL CANVAS CLONE (like P2 pause menu approach) =====

        private bool CreateClonedPanel()
        {
            var fejd = FejdStartup.instance;
            if (fejd == null)
            {
                Debug.Log("[Splitscreen][CharSelect] FejdStartup.instance is null (probably in-game)");
                return false;
            }

            Canvas sourceCanvas = FindRootMenuCanvas(fejd);
            if (sourceCanvas == null)
            {
                Debug.LogWarning("[Splitscreen][CharSelect] Could not find root menu canvas");
                return false;
            }

            Debug.Log($"[Splitscreen][CharSelect] Cloning full canvas: '{sourceCanvas.gameObject.name}', children={sourceCanvas.transform.childCount}");

            // Clone the entire canvas hierarchy to get the native game UI look.
            // Set IsCloning flag so Harmony prefix skips FejdStartup.Awake on the clone
            // (prevents the clone from clobbering FejdStartup.instance).
            IsCloning = true;
            _canvasRoot = Instantiate(sourceCanvas.gameObject);
            IsCloning = false;
            _canvasRoot.name = "P2_CharSelectCanvas";
            _canvasRoot.transform.SetParent(null, false);
            DontDestroyOnLoad(_canvasRoot);

            // Strip game scripts to avoid singleton conflicts
            StripScripts(_canvasRoot);
            DisableButtonTips(_canvasRoot);

            // Undo P1 menu confinement structure if it was included in the clone
            UndoP1ConfinementInClone();

            // Configure canvas as ScreenSpaceOverlay above the game menu
            _canvas = _canvasRoot.GetComponent<Canvas>();
            if (_canvas != null)
            {
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = MenuSplitController.CharSelectSortOrder;
            }

            // Keep the source CanvasScaler (ScaleWithScreenSize matching the game)

            // Clip the clone to P2's half using a RectMask2D container
            ClipToP2Half();

            // Activate only the character select panel, hide everything else
            ActivateCharSelectOnly(_canvasRoot);

            // Wire up carousel and bottom buttons for P2
            WireUpCarousel(_canvasRoot);
            WireUpBottomButtons(_canvasRoot);

            // Update title for P2
            UpdateTitleForP2(_canvasRoot);

            // Position the character select content lower in P2's half
            PositionContentLower(_canvasRoot);

            // Show initial character
            UpdateCharacterDisplay();

            // Start 3D character preview (camera override + character model)
            StartCharacterPreview();

            Debug.Log($"[Splitscreen][CharSelect] Full canvas clone created, {_profiles.Count} characters available");
            return true;
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

        /// <summary>
        /// If MenuSplitController confined the game menu before we cloned it,
        /// the clone contains a P1_MenuClip wrapper. Undo that so the clone
        /// has a clean hierarchy for our own P2 clipping.
        /// </summary>
        private void UndoP1ConfinementInClone()
        {
            if (_canvasRoot == null) return;
            var p1Clip = _canvasRoot.transform.Find("P1_MenuClip");
            if (p1Clip == null) return;

            Debug.Log($"[Splitscreen][CharSelect] Undoing P1_MenuClip in clone ({p1Clip.childCount} children)");
            var children = new List<Transform>();
            for (int i = 0; i < p1Clip.childCount; i++)
                children.Add(p1Clip.GetChild(i));
            foreach (var c in children)
                c.SetParent(_canvasRoot.transform, false);
            Destroy(p1Clip.gameObject);
        }

        /// <summary>
        /// Creates a RectMask2D clip container anchored to P2's half,
        /// then reparents all existing canvas children under it.
        /// This confines the cloned UI to P2's screen area.
        /// </summary>
        private void ClipToP2Half()
        {
            // Create clip container
            var clipObj = new GameObject("P2_HalfClip");
            clipObj.transform.SetParent(_canvasRoot.transform, false);
            var clipRT = clipObj.AddComponent<RectTransform>();
            PositionInP2Half(clipRT);
            clipObj.AddComponent<RectMask2D>();

            // Semi-transparent background so 3D scene peeks through behind the UI
            var bgObj = new GameObject("DimBackground");
            bgObj.transform.SetParent(clipObj.transform, false);
            var bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0.02f, 0.45f);
            bgImage.raycastTarget = false;
            var bgRT = bgObj.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            // Collect existing children (skip our clip container)
            var children = new List<Transform>();
            for (int i = 0; i < _canvasRoot.transform.childCount; i++)
            {
                var child = _canvasRoot.transform.GetChild(i);
                if (child.gameObject != clipObj)
                    children.Add(child);
            }

            // Reparent all children under the clip container
            foreach (var child in children)
                child.SetParent(clipObj.transform, false);

            Debug.Log($"[Splitscreen][CharSelect] Clipped {children.Count} children to P2 half");
        }

        /// <summary>
        /// In the cloned hierarchy, hide everything except the path from
        /// CharacterSelection to root. At every ancestor level, hide all
        /// siblings that aren't in the path — this catches MainMenu, Logo,
        /// StartGame, etc. at any nesting depth.
        /// </summary>
        private void ActivateCharSelectOnly(GameObject root)
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
                return;
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
                    if (sibling.name == "DimBackground" || sibling.name == "P2_HalfClip") continue;

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

            // Hide sub-panels that should stay hidden even inside char select
            foreach (var t in transforms)
            {
                string n = t.gameObject.name;
                if (n == "NewCharacterPanel" || n == "RemoveCharacterDialog")
                {
                    t.gameObject.SetActive(false);
                }
            }
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
            foreach (var btn in buttons)
            {
                string name = btn.gameObject.name;

                if (name == "Left")
                {
                    _leftButton = btn;
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(OnLeftClicked);
                    Debug.Log("[Splitscreen][CharSelect] Wired Left arrow button");
                }
                else if (name == "Right")
                {
                    _rightButton = btn;
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(OnRightClicked);
                    Debug.Log("[Splitscreen][CharSelect] Wired Right arrow button");
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
            foreach (var btn in buttons)
            {
                string name = btn.gameObject.name;

                if (name == "Start")
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(OnStartClicked);
                    SetButtonText(btn, "Select");
                    Debug.Log("[Splitscreen][CharSelect] Wired Start -> Select button");
                }
                else if (name == "Back")
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(OnBackClicked);
                    Debug.Log("[Splitscreen][CharSelect] Wired Back button");
                }
                else if (name == "New" || name == "New_big")
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(OnNewClicked);
                    Debug.Log($"[Splitscreen][CharSelect] Wired {name} button");
                }
                else if (name == "Remove" || name == "ManageSaves")
                {
                    // Disable these for P2 (too complex)
                    btn.gameObject.SetActive(false);
                    Debug.Log($"[Splitscreen][CharSelect] Hidden {name} button");
                }
            }
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

        private void PositionInP2Half(RectTransform rt)
        {
            bool horizontal = MenuSplitController.Instance?.IsHorizontal ?? true;

            if (horizontal)
            {
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(1, 0.5f);
            }
            else
            {
                rt.anchorMin = new Vector2(0.5f, 0);
                rt.anchorMax = new Vector2(1, 1);
            }
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>
        /// Move the active character select panel lower in P2's half so it's not
        /// floating in the center. Adjusts the anchoredPosition downward.
        /// </summary>
        private void PositionContentLower(GameObject root)
        {
            // Find the active character select panel
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (!t.gameObject.activeSelf) continue;
                string name = t.gameObject.name;
                if (name == "CharacterSelection" || name == "characterSelection" ||
                    name == "SelectCharacter" || name == "selectCharacter" ||
                    name == "CharacterSelect" || name == "characterSelect" ||
                    name.ToLowerInvariant().Contains("characterselect") ||
                    name.ToLowerInvariant().Contains("selectcharacter"))
                {
                    var rt = t.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        // Anchor to bottom-center of the container and offset up slightly
                        rt.anchorMin = new Vector2(0f, 0f);
                        rt.anchorMax = new Vector2(1f, 0.85f);
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                        Debug.Log($"[Splitscreen][CharSelect] Repositioned '{name}' to lower area");
                    }
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
        /// Stop overriding the camera and clear the character preview.
        /// </summary>
        private void StopCharacterPreview()
        {
            Debug.Log("[Splitscreen][CharSelect] Stopping character preview, disabling camera override");
            _overridingCamera = false;

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
            if (_overridingCamera)
                InvokeSetupPreview(profile);
        }

        private void OnCharacterSelected(PlayerProfile profile)
        {
            Debug.Log($"[Splitscreen][CharSelect] Selected: {(profile != null ? profile.GetName() : "CREATE NEW")}");
            Hide();
            _onSelected?.Invoke(profile);
        }

        // ===== UTILITY =====

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
                    Destroy(mb);
                    stripped++;
                }
                else if (comp is Animator anim)
                {
                    Destroy(anim);
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

        private void OnDestroy()
        {
            RestoreEventSystemInput();
            _overridingCamera = false;
            if (_canvasRoot != null) Destroy(_canvasRoot);
            Instance = null;
        }
    }
}
