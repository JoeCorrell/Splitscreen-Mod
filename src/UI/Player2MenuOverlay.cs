using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.HUD;
using ValheimSplitscreen.Input;

namespace ValheimSplitscreen.UI
{
    /// <summary>
    /// Cloned pause menu for Player 2.
    /// Clones the real game Menu panel and renders it on P2's UI camera.
    /// Triggered by P2's Start/Menu button.
    /// </summary>
    public class Player2MenuOverlay : MonoBehaviour
    {
        public static Player2MenuOverlay Instance { get; private set; }

        public bool IsVisible { get; private set; }

        // Cloned menu UI
        private GameObject _menuClone;
        private Canvas _menuCanvas;

        // Input state
        private float _lastToggleTime;
        private const float ToggleCooldown = 0.3f;

        // Diagnostic logging
        private float _lastDiagLogTime;

        private void Awake()
        {
            Instance = this;
            Debug.Log("[Splitscreen][P2Menu] Player2MenuOverlay.Awake");
        }

        private void Update()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (SplitInputManager.Instance == null) return;

            var p2Input = SplitInputManager.Instance.GetInputState(1);
            if (p2Input == null) return;

            // Periodic diagnostic logging
            if (Time.unscaledTime - _lastDiagLogTime > 10f)
            {
                _lastDiagLogTime = Time.unscaledTime;
                Debug.Log($"[Splitscreen][P2Menu] Update running: visible={IsVisible}, menuClone={(_menuClone != null)}, StartBtn={p2Input.StartButton}, Menu.instance={Menu.instance != null}");
            }

            // Toggle on Start/Menu button press
            bool menuPressed = p2Input.GetButtonDown("JoyMenu") || p2Input.GetButtonDown("Menu");
            if (menuPressed && Time.unscaledTime - _lastToggleTime > ToggleCooldown)
            {
                Debug.Log($"[Splitscreen][P2Menu] Menu button detected! visible={IsVisible}");
                _lastToggleTime = Time.unscaledTime;
                if (IsVisible)
                    Hide();
                else
                    Show();
            }

            // Close on B button or Escape
            if (IsVisible && (p2Input.GetButtonDown("JoyButtonB")))
            {
                Hide();
                Debug.Log("[Splitscreen][P2Menu] Closed via B button");
            }
        }

        public void Show()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            if (_menuClone == null)
            {
                CreateMenuClone();
            }

            if (_menuClone != null)
            {
                _menuClone.SetActive(true);
                IsVisible = true;
                Debug.Log("[Splitscreen][P2Menu] Show (cloned menu)");
            }
        }

        public void Hide()
        {
            if (_menuClone != null)
                _menuClone.SetActive(false);
            IsVisible = false;
        }

        /// <summary>
        /// Clone the real game Menu panel and configure it for P2.
        /// </summary>
        private void CreateMenuClone()
        {
            var menuInst = Menu.instance;
            if (menuInst == null)
            {
                Debug.LogWarning("[Splitscreen][P2Menu] Menu.instance is null, cannot clone");
                return;
            }

            var p2Camera = SplitCameraManager.Instance?.Player2UiCamera;
            if (p2Camera == null)
            {
                Debug.LogWarning("[Splitscreen][P2Menu] P2 UI camera is null, cannot create menu");
                return;
            }

            // Find the Menu's canvas (parent of the Menu component)
            var sourceCanvas = menuInst.GetComponentInParent<Canvas>();
            if (sourceCanvas == null)
            {
                Debug.LogWarning("[Splitscreen][P2Menu] Menu canvas not found");
                return;
            }

            Debug.Log($"[Splitscreen][P2Menu] Cloning Menu from '{sourceCanvas.gameObject.name}' renderMode={sourceCanvas.renderMode}");

            // Clone the entire menu canvas hierarchy
            _menuClone = Instantiate(sourceCanvas.gameObject);
            _menuClone.name = "SplitscreenMenu_P2";
            _menuClone.transform.SetParent(null, false);
            DontDestroyOnLoad(_menuClone);

            // Remove the Menu singleton script to avoid conflicts
            var menuScript = _menuClone.GetComponentInChildren<Menu>(true);
            if (menuScript != null)
            {
                Debug.Log($"[Splitscreen][P2Menu] Removing cloned Menu component");
                Destroy(menuScript);
            }

            // Strip all non-essential MonoBehaviours (keep only UI rendering components)
            StripNonEssentialScripts(_menuClone);

            // Set P2's HUD layer on all objects
            SplitHudManager.SetLayerRecursively(_menuClone, SplitCameraManager.Player2HudLayer);

            // Configure the canvas for P2's camera
            _menuCanvas = _menuClone.GetComponent<Canvas>();
            if (_menuCanvas != null)
            {
                _menuCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                _menuCanvas.worldCamera = p2Camera;
                _menuCanvas.planeDistance = 1f;
                _menuCanvas.sortingOrder = 20; // Above HUD
                Debug.Log($"[Splitscreen][P2Menu] Canvas configured: cam={p2Camera.name}, nearClip={p2Camera.nearClipPlane}, farClip={p2Camera.farClipPlane}");
            }

            // Configure CanvasScaler for RT dimensions
            var scaler = _menuClone.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
            }

            // Configure sub-canvases
            var subCanvases = _menuClone.GetComponentsInChildren<Canvas>(true);
            foreach (var sc in subCanvases)
            {
                if (sc == _menuCanvas) continue;
                sc.worldCamera = p2Camera;
                sc.sortingOrder = 20;
            }

            // Rewire buttons for P2's context
            WireUpButtons();

            // Relabel the title if possible
            RelabelForP2();

            // Start hidden
            _menuClone.SetActive(false);

            Debug.Log($"[Splitscreen][P2Menu] Menu clone created: canvas={_menuCanvas != null}, worldCam={_menuCanvas?.worldCamera?.name}");
        }

        private void StripNonEssentialScripts(GameObject root)
        {
            int stripped = 0;
            var allComponents = root.GetComponentsInChildren<Component>(true);
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                // Keep essential UI components
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
                if (comp is Dropdown) continue;
                if (comp is InputField) continue;

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
            Debug.Log($"[Splitscreen][P2Menu] Stripped {stripped} non-essential components from menu clone");
        }

        /// <summary>
        /// Rewire button onClick events for P2's context.
        /// </summary>
        private void WireUpButtons()
        {
            var buttons = _menuClone.GetComponentsInChildren<Button>(true);
            Debug.Log($"[Splitscreen][P2Menu] Found {buttons.Length} buttons in menu clone");

            foreach (var btn in buttons)
            {
                string name = btn.gameObject.name.ToLowerInvariant();
                string label = GetButtonLabel(btn);
                string labelLower = label?.ToLowerInvariant() ?? "";

                // Replace onClick entirely to clear persistent (serialized) listeners
                btn.onClick = new Button.ButtonClickedEvent();

                if (name.Contains("continue") || name.Contains("resume") ||
                    labelLower.Contains("continue") || labelLower.Contains("resume"))
                {
                    btn.onClick.AddListener(OnResumeClicked);
                    Debug.Log($"[Splitscreen][P2Menu]   '{btn.gameObject.name}' -> Resume");
                }
                else if (name.Contains("logout") || name.Contains("log out") ||
                         labelLower.Contains("log out") || labelLower.Contains("logout"))
                {
                    SetButtonLabel(btn, "Disconnect P2");
                    btn.onClick.AddListener(OnDisconnectClicked);
                    Debug.Log($"[Splitscreen][P2Menu]   '{btn.gameObject.name}' -> Disconnect P2");
                }
                else if (name.Contains("quit") || name.Contains("exit") ||
                         labelLower.Contains("quit") || labelLower.Contains("exit"))
                {
                    btn.gameObject.SetActive(false);
                    Debug.Log($"[Splitscreen][P2Menu]   '{btn.gameObject.name}' -> Hidden (quit)");
                }
                else if (name.Contains("settings") || labelLower.Contains("settings"))
                {
                    btn.interactable = false;
                    Debug.Log($"[Splitscreen][P2Menu]   '{btn.gameObject.name}' -> Disabled (settings)");
                }
                else
                {
                    btn.onClick.AddListener(OnResumeClicked);
                    Debug.Log($"[Splitscreen][P2Menu]   '{btn.gameObject.name}' (label='{label}') -> Default (resume)");
                }
            }
        }

        private void RelabelForP2()
        {
            // Try to change the title/header text to indicate this is P2's menu
            var allTexts = _menuClone.GetComponentsInChildren<TMP_Text>(true);
            foreach (var txt in allTexts)
            {
                string name = txt.gameObject.name.ToLowerInvariant();
                if (name.Contains("title") || name.Contains("header"))
                {
                    txt.text = "Player 2 Menu";
                    break;
                }
            }
        }

        private void OnResumeClicked()
        {
            Debug.Log("[Splitscreen][P2Menu] Resume clicked");
            Hide();
        }

        private void OnDisconnectClicked()
        {
            Debug.Log("[Splitscreen][P2Menu] Disconnect P2 clicked");
            Hide();
            StartCoroutine(DeferredDisconnect());
        }

        private System.Collections.IEnumerator DeferredDisconnect()
        {
            yield return null;
            var mgr = SplitScreenManager.Instance;
            if (mgr != null && mgr.SplitscreenActive)
            {
                Debug.Log("[Splitscreen][P2Menu] Executing deferred disconnect");
                mgr.RequestDeactivate();
            }
        }

        private static string GetButtonLabel(Button btn)
        {
            var text = btn.GetComponentInChildren<Text>(true);
            if (text != null) return text.text;

            var tmp = btn.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) return tmp.text;

            return null;
        }

        private static void SetButtonLabel(Button btn, string label)
        {
            var text = btn.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = label;
                return;
            }

            var tmp = btn.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = label;
            }
        }

        private void OnDestroy()
        {
            if (_menuClone != null) Destroy(_menuClone);
            Instance = null;
        }
    }
}
