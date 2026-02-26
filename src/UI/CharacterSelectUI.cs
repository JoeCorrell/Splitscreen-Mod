using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimSplitscreen.UI
{
    /// <summary>
    /// IMGUI overlay that lets the user pick a character profile for Player 2.
    /// Supports two display modes:
    /// - In-game mode: centered full-screen overlay with dark background
    /// - Main menu mode: right-aligned side panel that doesn't block the main menu
    /// </summary>
    public class CharacterSelectUI : MonoBehaviour
    {
        public static CharacterSelectUI Instance { get; private set; }

        public bool IsVisible { get; private set; }

        /// <summary>
        /// When true, draws as a right-side panel on the main menu.
        /// When false, draws as a centered overlay (in-game mode).
        /// </summary>
        public bool IsMainMenuMode { get; set; }

        private List<PlayerProfile> _profiles;
        private Vector2 _scrollPos;
        private Action<PlayerProfile> _onSelected;
        private Action _onCancelled;

        // Cached styles
        private GUIStyle _windowStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private bool _stylesInitialized;

        // Cursor state to restore on close (only used in in-game mode)
        private CursorLockMode _prevCursorLock;
        private bool _prevCursorVisible;

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Show the character selection overlay.
        /// Set IsMainMenuMode before calling this.
        /// </summary>
        public void Show(Action<PlayerProfile> onSelected, Action onCancelled)
        {
            _onSelected = onSelected;
            _onCancelled = onCancelled;

            // Load all available character profiles
            Debug.Log("[Splitscreen][CharSelect] Loading player profiles...");
            _profiles = SaveSystem.GetAllPlayerProfiles();
            _scrollPos = Vector2.zero;

            Debug.Log($"[Splitscreen][CharSelect] Found {_profiles.Count} profiles:");
            for (int i = 0; i < _profiles.Count; i++)
            {
                Debug.Log($"[Splitscreen][CharSelect]   [{i}] name='{_profiles[i].GetName()}' file='{_profiles[i].GetFilename()}'");
            }

            // Save and unlock cursor (only needed in-game where cursor is locked)
            if (!IsMainMenuMode)
            {
                _prevCursorLock = Cursor.lockState;
                _prevCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            IsVisible = true;
            Debug.Log($"[Splitscreen][CharSelect] UI opened, menuMode={IsMainMenuMode}");
        }

        /// <summary>
        /// Hide the overlay and restore cursor.
        /// </summary>
        public void Hide()
        {
            IsVisible = false;
            if (!IsMainMenuMode)
            {
                Cursor.lockState = _prevCursorLock;
                Cursor.visible = _prevCursorVisible;
            }
        }

        private void Update()
        {
            if (!IsVisible) return;

            // Escape to cancel
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Hide();
                _onCancelled?.Invoke();
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _windowStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(20, 20, 20, 20)
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                fixedHeight = 40,
                margin = new RectOffset(5, 5, 3, 3)
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (!IsVisible) return;

            InitStyles();

            if (IsMainMenuMode)
            {
                DrawMainMenuPanel();
            }
            else
            {
                DrawInGameOverlay();
            }
        }

        /// <summary>Right-aligned side panel for main menu mode.</summary>
        private void DrawMainMenuPanel()
        {
            float panelW = 400f;
            float panelH = Mathf.Min(Screen.height * 0.7f, 600f);
            float panelX = Screen.width - panelW - 30f;
            float panelY = (Screen.height - panelH) / 2f;
            Rect panelRect = new Rect(panelX, panelY, panelW, panelH);

            // Semi-transparent background for the panel only
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Box(panelRect, "", _windowStyle);

            GUILayout.BeginArea(new Rect(panelX + 20, panelY + 15, panelW - 40, panelH - 30));

            GUILayout.Label("Player 2 - Select Character", _headerStyle);
            GUILayout.Space(5);

            var subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };
            GUILayout.Label("Press F10 or Escape to cancel", subtitleStyle);
            GUILayout.Space(10);

            DrawProfileList(panelH - 170);

            GUILayout.EndArea();
        }

        /// <summary>Centered full-screen overlay for in-game mode.</summary>
        private void DrawInGameOverlay()
        {
            // Semi-transparent background overlay
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Center panel
            float panelW = 450f;
            float panelH = Mathf.Min(Screen.height * 0.7f, 600f);
            float panelX = (Screen.width - panelW) / 2f;
            float panelY = (Screen.height - panelH) / 2f;
            Rect panelRect = new Rect(panelX, panelY, panelW, panelH);

            GUI.Box(panelRect, "", _windowStyle);

            GUILayout.BeginArea(new Rect(panelX + 20, panelY + 15, panelW - 40, panelH - 30));

            GUILayout.Label("Select Character for Player 2", _headerStyle);
            GUILayout.Space(10);

            DrawProfileList(panelH - 150);

            GUILayout.EndArea();
        }

        /// <summary>Shared profile list + buttons used by both modes.</summary>
        private void DrawProfileList(float listHeight)
        {
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(listHeight));

            if (_profiles != null && _profiles.Count > 0)
            {
                foreach (var profile in _profiles)
                {
                    string displayName = profile.GetName();
                    string filename = profile.GetFilename();

                    string label = displayName;
                    if (!string.Equals(displayName, filename, StringComparison.OrdinalIgnoreCase))
                    {
                        label = $"{displayName} ({filename})";
                    }

                    if (GUILayout.Button(label, _buttonStyle))
                    {
                        Hide();
                        _onSelected?.Invoke(profile);
                    }
                }
            }
            else
            {
                GUILayout.Label("No saved characters found.", _labelStyle);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(8);

            // Create New button
            if (GUILayout.Button("+ Create New Character", _buttonStyle))
            {
                Hide();
                _onSelected?.Invoke(null);
            }

            // Cancel button
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
            Instance = null;
        }
    }
}
