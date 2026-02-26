using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimSplitscreen.UI
{
    /// <summary>
    /// Character selection UI for Player 2.
    /// Clones the real game character select panel (carousel style: Left/Right arrows to cycle).
    /// Renders on a ScreenSpaceOverlay canvas positioned in P2's half.
    /// </summary>
    public class CharacterSelectUI : MonoBehaviour
    {
        public static CharacterSelectUI Instance { get; private set; }

        public bool IsVisible { get; private set; }
        public bool IsMainMenuMode { get; set; }
        public bool IsMenuSplitMode { get; set; }

        private List<PlayerProfile> _profiles;
        private int _selectedIndex;
        private Action<PlayerProfile> _onSelected;
        private Action _onCancelled;

        // Cloned character select UI
        private GameObject _canvasRoot;
        private Canvas _canvas;
        private TMP_Text _characterNameText;
        private TMP_Text _sourceInfoText;
        private Button _leftButton;
        private Button _rightButton;

        // Cursor state to restore on close (only used in in-game mode)
        private CursorLockMode _prevCursorLock;
        private bool _prevCursorVisible;

        // Fallback IMGUI (used only when cloning fails)
        private bool _useFallbackIMGUI;
        private Vector2 _scrollPos;
        private GUIStyle _buttonStyle;
        private GUIStyle _headerStyle;
        private bool _stylesInit;

        private void Awake()
        {
            Instance = this;
        }

        public void Show(Action<PlayerProfile> onSelected, Action onCancelled)
        {
            _onSelected = onSelected;
            _onCancelled = onCancelled;

            Debug.Log("[Splitscreen][CharSelect] Loading player profiles...");
            _profiles = SaveSystem.GetAllPlayerProfiles();
            Debug.Log($"[Splitscreen][CharSelect] Found {_profiles.Count} profiles");

            _selectedIndex = 0;

            if (!IsMainMenuMode)
            {
                _prevCursorLock = Cursor.lockState;
                _prevCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            _useFallbackIMGUI = false;
            IsVisible = true;

            if (!CreateClonedPanel())
            {
                Debug.LogWarning("[Splitscreen][CharSelect] Failed to clone game panel, using fallback IMGUI");
                _useFallbackIMGUI = true;
            }
        }

        public void Hide()
        {
            IsVisible = false;
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
        }

        private void Update()
        {
            if (!IsVisible) return;

            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Hide();
                _onCancelled?.Invoke();
            }
        }

        // ===== CLONED PANEL CREATION =====

        private bool CreateClonedPanel()
        {
            var fejd = FejdStartup.instance;
            if (fejd == null)
            {
                Debug.Log("[Splitscreen][CharSelect] FejdStartup.instance is null (probably in-game)");
                return false;
            }

            GameObject charSelectPanel = FindCharacterSelectPanel(fejd);
            if (charSelectPanel == null)
            {
                Debug.LogWarning("[Splitscreen][CharSelect] Could not find character select panel");
                return false;
            }

            Debug.Log($"[Splitscreen][CharSelect] Cloning panel: '{charSelectPanel.name}'");

            // Create canvas for P2's character select
            _canvasRoot = new GameObject("P2_CharSelectCanvas");
            _canvasRoot.transform.SetParent(null, false);
            DontDestroyOnLoad(_canvasRoot);

            _canvas = _canvasRoot.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = MenuSplitController.CharSelectSortOrder;

            _canvasRoot.AddComponent<GraphicRaycaster>();

            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // Dark background behind the cloned panel (prevents purple/transparent artifacts)
            var bgObj = new GameObject("DarkBackground");
            bgObj.transform.SetParent(_canvasRoot.transform, false);
            var bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0.06f, 0.06f, 0.1f, 1f);
            bgImage.raycastTarget = false;
            var bgRT = bgObj.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            // Clone the panel
            var panelClone = Instantiate(charSelectPanel, _canvasRoot.transform, false);
            panelClone.name = "P2_CharSelectPanel";
            panelClone.SetActive(true);

            // Strip non-essential scripts
            StripScripts(panelClone);

            // Disable ButtonTip / KeyHint elements (they show broken references)
            DisableButtonTips(panelClone);

            // Position in P2's half
            var panelRT = panelClone.GetComponent<RectTransform>();
            if (panelRT != null)
                PositionInP2Half(panelRT);

            // Activate the SelectCharacter sub-panel (it may be inactive)
            ActivateSelectCharacterPanel(panelClone);

            // Wire up the carousel controls
            WireUpCarousel(panelClone);

            // Wire up bottom buttons
            WireUpBottomButtons(panelClone);

            // Update title for P2
            UpdateTitleForP2(panelClone);

            // Show initial character
            UpdateCharacterDisplay();

            Debug.Log($"[Splitscreen][CharSelect] Cloned carousel panel, {_profiles.Count} characters available");
            return true;
        }

        private GameObject FindCharacterSelectPanel(FejdStartup fejd)
        {
            string[] fieldNames = {
                "m_characterSelectScreen", "m_selectCharacterPanel",
                "m_characterList", "m_charSelectPanel"
            };

            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            foreach (var name in fieldNames)
            {
                var field = typeof(FejdStartup).GetField(name, flags);
                if (field != null)
                {
                    var value = field.GetValue(fejd);
                    if (value is GameObject go)
                    {
                        Debug.Log($"[Splitscreen][CharSelect] Found panel via field '{name}'");
                        return go;
                    }
                    if (value is Transform t)
                    {
                        Debug.Log($"[Splitscreen][CharSelect] Found panel via field '{name}' (Transform)");
                        return t.gameObject;
                    }
                }
            }

            // Fallback: search hierarchy by name
            var transforms = fejd.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                string goName = t.gameObject.name.ToLowerInvariant();
                if (goName.Contains("characterselect") || goName.Contains("charselect") ||
                    goName.Contains("selectcharacter"))
                {
                    Debug.Log($"[Splitscreen][CharSelect] Found panel by name: '{t.gameObject.name}'");
                    return t.gameObject;
                }
            }

            return null;
        }

        private void ActivateSelectCharacterPanel(GameObject panelClone)
        {
            // The SelectCharacter sub-panel might be inactive; activate it
            var transforms = panelClone.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t.gameObject.name == "SelectCharacter")
                {
                    t.gameObject.SetActive(true);
                    Debug.Log("[Splitscreen][CharSelect] Activated SelectCharacter sub-panel");
                    break;
                }
            }

            // Hide NewCharacterPanel and RemoveCharacterDialog
            foreach (var t in transforms)
            {
                string n = t.gameObject.name;
                if (n == "NewCharacterPanel" || n == "RemoveCharacterDialog")
                {
                    t.gameObject.SetActive(false);
                }
            }
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
            Debug.Log($"[Splitscreen][CharSelect] Stripped {stripped} scripts from panel clone");
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

        private void OnDestroy()
        {
            if (_canvasRoot != null) Destroy(_canvasRoot);
            Instance = null;
        }
    }
}
