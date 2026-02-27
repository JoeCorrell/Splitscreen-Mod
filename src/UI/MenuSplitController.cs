using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ValheimSplitscreen.UI
{
    /// <summary>
    /// Controls the visual menu split: confines the game's menu UI to P1's half
    /// using a dedicated ScreenSpaceCamera with a viewport rect, letting the 3D
    /// background show through on P2's half.
    /// An overlay canvas provides the divider line and ready status display.
    /// </summary>
    public class MenuSplitController : MonoBehaviour
    {
        public static MenuSplitController Instance { get; private set; }

        public bool IsActive { get; private set; }

        private bool _horizontal;
        private bool _p2Ready;
        private string _p2CharacterName;

        // Overlay canvas for divider line and ready status (no dark panel — 3D scene shows through)
        private GameObject _overlayRoot;
        private Canvas _overlayCanvas;

        // Ready status text elements (shown when P2 has selected a character)
        private GameObject _readyGroup;
        private TMP_Text _readyName;

        // Camera-based confinement: switch game canvas to ScreenSpaceCamera
        // with a camera whose viewport rect covers only P1's half.
        private GameObject _p1UICamObj;
        private UnityEngine.Camera _p1UICam;
        private Canvas _confinedCanvas;
        private RenderMode _originalRenderMode;
        private UnityEngine.Camera _originalWorldCamera;
        private float _originalPlaneDistance;
        private CanvasScaler _confinedCanvasScaler;
        private CanvasScaler.ScaleMode _originalScaleMode;
        private CanvasScaler.ScreenMatchMode _originalScreenMatchMode;
        private Vector2 _originalReferenceResolution;
        private float _originalMatchWidthOrHeight;
        private float _originalScaleFactor;
        private bool _hasCanvasScalerOverride;
        private readonly List<GuiScaler> _disabledGuiScalers = new List<GuiScaler>();
        private readonly List<UnityEngine.Camera> _suppressedBlackoutCameras = new List<UnityEngine.Camera>();
        private UnityEngine.Camera _menuWorldCamera;
        private Rect _menuWorldOriginalRect;
        private DepthTextureMode _menuWorldOriginalDepthTextureMode;
        private readonly Dictionary<CameraEvent, List<CommandBuffer>> _menuWorldCommandBuffers =
            new Dictionary<CameraEvent, List<CommandBuffer>>();
        private readonly List<Behaviour> _disabledMenuWorldBehaviours = new List<Behaviour>();
        private GameObject _p1WorldCamObj;
        private UnityEngine.Camera _p1WorldCam;

        private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo _fejdMainCamera = typeof(FejdStartup).GetField("m_mainCamera", BF);

        // Sort orders
        private const int OverlaySortOrder = 100;  // Above game UI (typically 0-10)
        public const int CharSelectSortOrder = 110; // Above our overlay
        // Valheim's menu camera stack binds full-screen depth surfaces; splitting
        // the world camera rect causes persistent color/depth surface mismatches.
        // Keep world rendering fullscreen and only split UI cameras.
        private static readonly bool UseMenuWorldViewportSplit = false;

        private void Awake()
        {
            Instance = this;
        }

        public void Activate(bool horizontal)
        {
            if (IsActive)
            {
                Debug.LogWarning("[Splitscreen][MenuSplit] Activate called but already active, skipping");
                return;
            }

            _horizontal = horizontal;
            _p2Ready = false;
            _p2CharacterName = null;
            IsActive = true;

            Debug.Log($"[Splitscreen][MenuSplit] === ACTIVATING === horizontal={horizontal}, screen={Screen.width}x{Screen.height}");

            CreateOverlayCanvas();
            ConfineMenuWithCamera();
            StartCoroutine(LogPostActivateState());

            Debug.Log($"[Splitscreen][MenuSplit] === ACTIVATED === p1Cam={(_p1UICam != null ? "OK" : "NULL")}, " +
                $"confinedCanvas={(_confinedCanvas != null ? _confinedCanvas.gameObject.name : "NULL")}");
        }

        public void Deactivate()
        {
            if (!IsActive)
            {
                Debug.LogWarning("[Splitscreen][MenuSplit] Deactivate called but not active, skipping");
                return;
            }

            Debug.Log("[Splitscreen][MenuSplit] === DEACTIVATING ===");

            RestoreCanvasMode();
            DestroyOverlayCanvas();

            _p2Ready = false;
            _p2CharacterName = null;
            IsActive = false;
            Debug.Log("[Splitscreen][MenuSplit] === DEACTIVATED ===");
        }

        public void SetP2Ready(string characterName)
        {
            _p2Ready = true;
            _p2CharacterName = characterName;
            Debug.Log($"[Splitscreen][MenuSplit] P2 ready: {characterName}");
            UpdateReadyStatus();
        }

        /// <summary>
        /// Returns the screen rect for P2's half (in screen coordinates).
        /// </summary>
        public Rect GetP2ScreenRect()
        {
            if (_horizontal)
            {
                float halfH = Screen.height / 2f;
                return new Rect(0, halfH, Screen.width, halfH);
            }
            else
            {
                float halfW = Screen.width / 2f;
                return new Rect(halfW, 0, halfW, Screen.height);
            }
        }

        public bool IsHorizontal => _horizontal;

        /// <summary>Returns the P1 UI camera (for clone to know source canvas state).</summary>
        public UnityEngine.Camera P1UICamera => _p1UICam;

        // ===== CAMERA-BASED CONFINEMENT =====

        private void ConfineMenuWithCamera()
        {
            Debug.Log("[Splitscreen][MenuSplit] === ConfineMenuWithCamera START ===");

            var fejd = FejdStartup.instance;
            if (fejd == null)
            {
                Debug.LogError("[Splitscreen][MenuSplit] FAIL: FejdStartup.instance is null");
                return;
            }

            LogCandidateCanvases("before_select");

            _confinedCanvas = fejd.GetComponentInParent<Canvas>();
            if (_confinedCanvas != null)
            {
                Debug.Log($"[Splitscreen][MenuSplit] Found canvas via GetComponentInParent: '{_confinedCanvas.gameObject.name}', renderMode={_confinedCanvas.renderMode}");
            }
            else
            {
                var allCanvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                foreach (var c in allCanvases)
                {
                    if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay &&
                        c.gameObject.name != "SplitscreenMenuOverlay" &&
                        c.gameObject.name != "P2_CharSelectCanvas")
                    {
                        _confinedCanvas = c;
                        Debug.Log($"[Splitscreen][MenuSplit] Found canvas by search: '{c.gameObject.name}'");
                        break;
                    }
                }
            }

            if (_confinedCanvas == null)
            {
                Debug.LogError("[Splitscreen][MenuSplit] FAIL: Could not find game menu canvas");
                return;
            }

            // Save original state
            _originalRenderMode = _confinedCanvas.renderMode;
            _originalWorldCamera = _confinedCanvas.worldCamera;
            _originalPlaneDistance = _confinedCanvas.planeDistance;
            Debug.Log($"[Splitscreen][MenuSplit] Saved original: renderMode={_originalRenderMode}, worldCamera={(_originalWorldCamera != null ? _originalWorldCamera.name : "null")}");
            LogCanvasDetails(_confinedCanvas, "source_before");

            // Create P1 UI camera with viewport rect for P1's half
            _p1UICamObj = new GameObject("P1_UICamera");
            DontDestroyOnLoad(_p1UICamObj);
            _p1UICam = _p1UICamObj.AddComponent<UnityEngine.Camera>();
            _p1UICam.clearFlags = CameraClearFlags.Depth;
            _p1UICam.cullingMask = BuildLayerMask(_confinedCanvas.gameObject);
            _p1UICam.depth = 50;
            _p1UICam.orthographic = true;
            _p1UICam.nearClipPlane = 0.1f;
            _p1UICam.farClipPlane = 1000f;

            if (_horizontal)
                _p1UICam.rect = new Rect(0, 0, 1, 0.5f); // bottom half (P1)
            else
                _p1UICam.rect = new Rect(0, 0, 0.5f, 1); // left half

            Debug.Log($"[Splitscreen][MenuSplit] Created P1_UICamera: viewport={_p1UICam.rect}");

            // Switch game canvas from ScreenSpaceOverlay to ScreenSpaceCamera.
            // This confines all canvas rendering (including nested sub-canvases)
            // to the camera's viewport rect.
            _confinedCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _confinedCanvas.worldCamera = _p1UICam;
            _confinedCanvas.planeDistance = 1f;
            ConfigureConfinedCanvasScaler();
            Debug.Log($"[Splitscreen][MenuSplit] Canvas '{_confinedCanvas.gameObject.name}' switched to ScreenSpaceCamera " +
                $"(planeDistance={_confinedCanvas.planeDistance}, camNear={_p1UICam.nearClipPlane}, camFar={_p1UICam.farClipPlane})");

            ConfigureMenuWorldCameras(fejd);
            SuppressBlackoutCameras();
            LogCanvasDetails(_confinedCanvas, "source_after");
            LogActiveCameras("after_confine");

            Debug.Log("[Splitscreen][MenuSplit] === ConfineMenuWithCamera END ===");
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

        private void ConfigureConfinedCanvasScaler()
        {
            _hasCanvasScalerOverride = false;
            _confinedCanvasScaler = null;
            DisableConfinedGuiScalers();

            if (_confinedCanvas == null) return;

            var scaler = _confinedCanvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                Debug.Log("[Splitscreen][MenuSplit] No CanvasScaler found on confined canvas");
                return;
            }

            _confinedCanvasScaler = scaler;
            _originalScaleMode = scaler.uiScaleMode;
            _originalScreenMatchMode = scaler.screenMatchMode;
            _originalReferenceResolution = scaler.referenceResolution;
            _originalMatchWidthOrHeight = scaler.matchWidthOrHeight;
            _originalScaleFactor = scaler.scaleFactor;
            _hasCanvasScalerOverride = true;

            // Keep ScaleWithScreenSize mode but double the reference resolution
            // in the split dimension. CanvasScaler computes scale from Screen
            // dimensions (not camera viewport), so doubling the reference makes
            // it produce the same scale as if the game ran at half-screen resolution.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var newRef = _originalReferenceResolution;
            if (_horizontal)
            {
                newRef.y *= 2f;
                scaler.matchWidthOrHeight = 1f; // match height — the dimension we split
            }
            else
            {
                newRef.x *= 2f;
                scaler.matchWidthOrHeight = 0f; // match width — the dimension we split
            }
            scaler.referenceResolution = newRef;

            Debug.Log(
                $"[Splitscreen][MenuSplit] Adjusted canvas scaler for split: mode={_originalScaleMode}->{scaler.uiScaleMode}, " +
                $"ref={_originalReferenceResolution.x:F0}x{_originalReferenceResolution.y:F0}->{newRef.x:F0}x{newRef.y:F0}, " +
                $"matchMode={scaler.screenMatchMode}, match={scaler.matchWidthOrHeight:F2}");

            LogScalerState(scaler, "P1_after_configure");
        }

        private void RestoreConfinedCanvasScaler()
        {
            RestoreConfinedGuiScalers();

            if (!_hasCanvasScalerOverride || _confinedCanvasScaler == null)
            {
                _hasCanvasScalerOverride = false;
                _confinedCanvasScaler = null;
                return;
            }

            _confinedCanvasScaler.uiScaleMode = _originalScaleMode;
            _confinedCanvasScaler.screenMatchMode = _originalScreenMatchMode;
            _confinedCanvasScaler.referenceResolution = _originalReferenceResolution;
            _confinedCanvasScaler.matchWidthOrHeight = _originalMatchWidthOrHeight;
            _confinedCanvasScaler.scaleFactor = _originalScaleFactor;

            Debug.Log(
                $"[Splitscreen][MenuSplit] Restored canvas scaler: mode={_confinedCanvasScaler.uiScaleMode}, " +
                $"scale={_confinedCanvasScaler.scaleFactor:F2}, matchMode={_confinedCanvasScaler.screenMatchMode}, match={_confinedCanvasScaler.matchWidthOrHeight:F2}, " +
                $"ref={_confinedCanvasScaler.referenceResolution.x:F0}x{_confinedCanvasScaler.referenceResolution.y:F0}");

            _hasCanvasScalerOverride = false;
            _confinedCanvasScaler = null;
        }

        private void DisableConfinedGuiScalers()
        {
            _disabledGuiScalers.Clear();

            if (_confinedCanvas == null) return;

            var guiScalers = _confinedCanvas.GetComponentsInChildren<GuiScaler>(true);
            for (int i = 0; i < guiScalers.Length; i++)
            {
                var gs = guiScalers[i];
                if (gs == null || !gs.enabled) continue;
                gs.enabled = false;
                _disabledGuiScalers.Add(gs);
                Debug.Log($"[Splitscreen][MenuSplit] Disabled GuiScaler on '{gs.gameObject.name}'");
            }

            if (_disabledGuiScalers.Count > 0)
            {
                Debug.Log($"[Splitscreen][MenuSplit] Disabled {_disabledGuiScalers.Count} GuiScaler component(s) for split");
            }
        }

        private void RestoreConfinedGuiScalers()
        {
            for (int i = 0; i < _disabledGuiScalers.Count; i++)
            {
                var gs = _disabledGuiScalers[i];
                if (gs == null) continue;
                gs.enabled = true;
                Debug.Log($"[Splitscreen][MenuSplit] Restored GuiScaler on '{gs.gameObject.name}'");
            }
            _disabledGuiScalers.Clear();
        }

        private void RestoreCanvasMode()
        {
            if (_confinedCanvas != null)
            {
                LogCanvasDetails(_confinedCanvas, "restore_before");
                Debug.Log($"[Splitscreen][MenuSplit] Restoring canvas '{_confinedCanvas.gameObject.name}' to {_originalRenderMode}");
                _confinedCanvas.renderMode = _originalRenderMode;
                if (_originalRenderMode == RenderMode.ScreenSpaceCamera)
                    _confinedCanvas.worldCamera = _originalWorldCamera;
                _confinedCanvas.planeDistance = _originalPlaneDistance;
                RestoreConfinedCanvasScaler();
                LogCanvasDetails(_confinedCanvas, "restore_after");
                _confinedCanvas = null;
            }

            if (_p1UICamObj != null)
            {
                Destroy(_p1UICamObj);
                _p1UICamObj = null;
                _p1UICam = null;
                Debug.Log("[Splitscreen][MenuSplit] Destroyed P1_UICamera");
            }

            RestoreMenuWorldCameras();
            RestoreSuppressedCameras();
            LogActiveCameras("after_restore");
        }

        // ===== OVERLAY CANVAS =====

        private void CreateOverlayCanvas()
        {
            _overlayRoot = new GameObject("SplitscreenMenuOverlay");
            DontDestroyOnLoad(_overlayRoot);

            _overlayCanvas = _overlayRoot.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = OverlaySortOrder;

            var scaler = _overlayRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // Divider line between halves
            var divObj = new GameObject("Divider");
            divObj.transform.SetParent(_overlayRoot.transform, false);
            var divLine = divObj.AddComponent<Image>();
            divLine.color = new Color(0.35f, 0.35f, 0.35f, 1f);
            divLine.raycastTarget = false;

            var divRT = divObj.GetComponent<RectTransform>();
            if (_horizontal)
            {
                divRT.anchorMin = new Vector2(0, 0.5f);
                divRT.anchorMax = new Vector2(1, 0.5f);
                divRT.sizeDelta = new Vector2(0, 3);
            }
            else
            {
                divRT.anchorMin = new Vector2(0.5f, 0);
                divRT.anchorMax = new Vector2(0.5f, 1);
                divRT.sizeDelta = new Vector2(3, 0);
            }
            divRT.anchoredPosition = Vector2.zero;

            // Keep overlay minimal (divider only) to preserve vanilla menu visuals.
            Debug.Log($"[Splitscreen][MenuSplit] Overlay created: renderMode={_overlayCanvas.renderMode}, sort={_overlayCanvas.sortingOrder}, root='{_overlayRoot.name}'");
        }

        private void CreateReadyStatusGroup()
        {
            _readyGroup = new GameObject("ReadyStatus");
            _readyGroup.transform.SetParent(_overlayRoot.transform, false);

            var groupRT = _readyGroup.GetComponent<RectTransform>();
            if (groupRT == null) groupRT = _readyGroup.AddComponent<RectTransform>();
            SetP2Anchors(groupRT);

            // Semi-transparent background so text is readable over the 3D scene
            var bgImage = _readyGroup.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.55f);
            bgImage.raycastTarget = false;

            var layout = _readyGroup.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 10;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(40, 40, 0, 0);

            TMP_FontAsset font = FindValheimFont();

            CreateTMPText(_readyGroup.transform, "HeaderText",
                "PLAYER 2 READY", 32, FontStyles.Bold, new Color(0.4f, 1f, 0.4f), font);

            _readyName = CreateTMPText(_readyGroup.transform, "NameText",
                "", 26, FontStyles.Normal, Color.white, font);

            CreateTMPText(_readyGroup.transform, "SubtitleText",
                "Waiting for Player 1 to start a world", 16, FontStyles.Italic,
                new Color(0.6f, 0.6f, 0.6f), font);

            CreateTMPText(_readyGroup.transform, "CancelText",
                "Press F10 to cancel", 14, FontStyles.Normal,
                new Color(0.5f, 0.5f, 0.5f), font);

            _readyGroup.SetActive(false);
        }

        private TMP_Text CreateTMPText(Transform parent, string name, string text,
            float fontSize, FontStyles style, Color color, TMP_FontAsset font)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            if (font != null) tmp.font = font;

            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = fontSize + 12;

            return tmp;
        }

        private TMP_FontAsset FindValheimFont()
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var f in fonts)
            {
                if (f.name.Contains("Valheim") || f.name.Contains("Prstartk") || f.name.Contains("Norse"))
                    return f;
            }
            return fonts.Length > 0 ? fonts[0] : null;
        }

        private void UpdateReadyStatus()
        {
            if (_readyGroup == null) return;

            if (_p2Ready)
            {
                _readyName.text = _p2CharacterName ?? "New Character";
                _readyGroup.SetActive(true);
            }
            else
            {
                _readyGroup.SetActive(false);
            }
        }

        private void DestroyOverlayCanvas()
        {
            if (_overlayRoot != null)
            {
                Destroy(_overlayRoot);
                _overlayRoot = null;
                _overlayCanvas = null;
                _readyGroup = null;
                _readyName = null;
            }
        }

        private void SetP2Anchors(RectTransform rt)
        {
            if (_horizontal)
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
        }

        /// <summary>Log CanvasScaler state for a single scaler.</summary>
        private static void LogScalerState(CanvasScaler scaler, string tag)
        {
            if (scaler == null)
            {
                Debug.Log($"[Splitscreen][Scale][Diag] Scaler '{tag}': null");
                return;
            }

            var canvas = scaler.GetComponent<Canvas>();
            var rt = scaler.GetComponent<RectTransform>();
            string rtSize = rt != null ? $"{rt.rect.width:F1}x{rt.rect.height:F1}" : "n/a";
            string localScale = rt != null ? $"({rt.localScale.x:F4},{rt.localScale.y:F4},{rt.localScale.z:F4})" : "n/a";
            string camInfo = "none";
            if (canvas != null && canvas.worldCamera != null)
            {
                var cam = canvas.worldCamera;
                camInfo = $"'{cam.name}' pixelRect={cam.pixelRect} viewport={cam.rect}";
            }

            Debug.Log($"[Splitscreen][Scale][Diag] Scaler '{tag}': name='{scaler.name}', mode={scaler.uiScaleMode}, " +
                $"scaleFactor={scaler.scaleFactor:F4}, ref={scaler.referenceResolution.x:F0}x{scaler.referenceResolution.y:F0}, " +
                $"matchMode={scaler.screenMatchMode}, match={scaler.matchWidthOrHeight:F2}, " +
                $"canvasRTSize={rtSize}, canvasLocalScale={localScale}, camera=[{camInfo}]");
        }

        /// <summary>
        /// Log comprehensive scaling diagnostics for both P1 and P2 canvases.
        /// Captures: screen size, camera pixel rects, canvas rect transform sizes,
        /// canvas local scales (the effective scale factor applied by CanvasScaler),
        /// and the CanvasScaler configuration.
        /// </summary>
        private void LogScalingDiagnostics(string tag)
        {
            Debug.Log($"[Splitscreen][Scale][Diag] === Scaling diagnostics ({tag}) ===");
            Debug.Log($"[Splitscreen][Scale][Diag] Screen: {Screen.width}x{Screen.height}, " +
                $"currentRes={Screen.currentResolution.width}x{Screen.currentResolution.height}, " +
                $"dpi={Screen.dpi:F1}, fullScreen={Screen.fullScreen}");

            // P1 UI camera
            if (_p1UICam != null)
            {
                Debug.Log($"[Splitscreen][Scale][Diag] P1_UICamera: pixelRect={_p1UICam.pixelRect}, " +
                    $"pixelW={_p1UICam.pixelWidth}, pixelH={_p1UICam.pixelHeight}, " +
                    $"viewport={_p1UICam.rect}, enabled={_p1UICam.enabled}");
            }
            else
            {
                Debug.Log("[Splitscreen][Scale][Diag] P1_UICamera: NULL");
            }

            // P1 confined canvas
            if (_confinedCanvas != null)
            {
                var rt = _confinedCanvas.GetComponent<RectTransform>();
                Debug.Log($"[Splitscreen][Scale][Diag] P1 Canvas '{_confinedCanvas.name}': " +
                    $"renderMode={_confinedCanvas.renderMode}, " +
                    $"rtSize={rt?.rect.width:F1}x{rt?.rect.height:F1}, " +
                    $"localScale=({rt?.localScale.x:F4},{rt?.localScale.y:F4}), " +
                    $"scaleFactor={_confinedCanvas.scaleFactor:F4}, " +
                    $"referencePixelsPerUnit={_confinedCanvas.referencePixelsPerUnit:F1}");

                if (_confinedCanvasScaler != null)
                    LogScalerState(_confinedCanvasScaler, $"P1_canvas_{tag}");

                // Check if GuiScaler is active (it might re-enable itself)
                var guiScalers = _confinedCanvas.GetComponents<GuiScaler>();
                for (int i = 0; i < guiScalers.Length; i++)
                    Debug.Log($"[Splitscreen][Scale][Diag] P1 GuiScaler[{i}]: enabled={guiScalers[i].enabled}");
            }

            // P2 camera + canvas (if CharacterSelectUI has created it)
            var charUI = CharacterSelectUI.Instance;
            if (charUI != null && charUI.IsVisible)
            {
                Debug.Log("[Splitscreen][Scale][Diag] CharacterSelectUI is visible, checking P2 state...");
                // We can't access private fields directly, but the clone's canvas is findable
                var p2Canvas = GameObject.Find("P2_CharSelectCanvas");
                if (p2Canvas != null)
                {
                    var canvas = p2Canvas.GetComponent<Canvas>();
                    var rt = p2Canvas.GetComponent<RectTransform>();
                    var scaler = p2Canvas.GetComponent<CanvasScaler>();
                    Debug.Log($"[Splitscreen][Scale][Diag] P2 Canvas '{p2Canvas.name}': " +
                        $"renderMode={canvas?.renderMode}, " +
                        $"rtSize={rt?.rect.width:F1}x{rt?.rect.height:F1}, " +
                        $"localScale=({rt?.localScale.x:F4},{rt?.localScale.y:F4}), " +
                        $"scaleFactor={canvas?.scaleFactor:F4}, " +
                        $"worldCam={(canvas?.worldCamera != null ? canvas.worldCamera.name : "null")}");

                    if (scaler != null)
                        LogScalerState(scaler, $"P2_canvas_{tag}");

                    if (canvas?.worldCamera != null)
                    {
                        var cam = canvas.worldCamera;
                        Debug.Log($"[Splitscreen][Scale][Diag] P2_UICamera: pixelRect={cam.pixelRect}, " +
                            $"pixelW={cam.pixelWidth}, pixelH={cam.pixelHeight}, " +
                            $"viewport={cam.rect}, enabled={cam.enabled}");
                    }
                }
                else
                {
                    Debug.Log("[Splitscreen][Scale][Diag] P2_CharSelectCanvas not found by name");
                }
            }

            Debug.Log($"[Splitscreen][Scale][Diag] === End scaling diagnostics ({tag}) ===");
        }

        private void OnDestroy()
        {
            if (IsActive)
            {
                RestoreCanvasMode();
                DestroyOverlayCanvas();
            }
            Instance = null;
        }

        private IEnumerator LogPostActivateState()
        {
            yield return null;
            LogCanvasDetails(_confinedCanvas, "post_activate_frame1");
            LogActiveCameras("post_activate_frame1");
            LogScalingDiagnostics("post_activate_frame1");

            yield return null;
            LogCanvasDetails(_confinedCanvas, "post_activate_frame2");
            LogActiveCameras("post_activate_frame2");
            LogScalingDiagnostics("post_activate_frame2");
        }

        private static void LogCanvasDetails(Canvas canvas, string tag)
        {
            if (canvas == null)
            {
                Debug.Log($"[Splitscreen][MenuSplit][Diag] Canvas '{tag}' is null");
                return;
            }

            var rt = canvas.GetComponent<RectTransform>();
            string anchors = rt != null
                ? $"anchors=({rt.anchorMin.x:F2},{rt.anchorMin.y:F2})-({rt.anchorMax.x:F2},{rt.anchorMax.y:F2}), size=({rt.rect.width:F1}x{rt.rect.height:F1})"
                : "anchors=n/a";

            int childCanvasCount = canvas.GetComponentsInChildren<Canvas>(true).Length;
            int graphics = canvas.GetComponentsInChildren<Graphic>(true).Length;
            int activeGraphics = 0;
            var allGraphics = canvas.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < allGraphics.Length; i++)
            {
                if (allGraphics[i] != null && allGraphics[i].gameObject.activeInHierarchy)
                {
                    activeGraphics++;
                }
            }

            Debug.Log($"[Splitscreen][MenuSplit][Diag] Canvas '{tag}': name='{canvas.name}', active={canvas.gameObject.activeInHierarchy}, " +
                $"enabled={canvas.enabled}, renderMode={canvas.renderMode}, worldCamera={(canvas.worldCamera != null ? canvas.worldCamera.name : "null")}, " +
                $"planeDistance={canvas.planeDistance}, sortLayer={canvas.sortingLayerID}, sortOrder={canvas.sortingOrder}, " +
                $"{anchors}, childCanvases={childCanvasCount}, graphics={graphics}, activeGraphics={activeGraphics}");
        }

        private static void LogCandidateCanvases(string tag)
        {
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Debug.Log($"[Splitscreen][MenuSplit][Diag] Canvas scan ({tag}): total={canvases.Length}");
            for (int i = 0; i < canvases.Length; i++)
            {
                var c = canvases[i];
                if (c == null || !c.isRootCanvas) continue;
                Debug.Log($"[Splitscreen][MenuSplit][Diag]   RootCanvas: name='{c.name}', active={c.gameObject.activeInHierarchy}, " +
                    $"renderMode={c.renderMode}, worldCamera={(c.worldCamera != null ? c.worldCamera.name : "null")}, " +
                    $"sortOrder={c.sortingOrder}, planeDistance={c.planeDistance}");
            }
        }

        private static void LogActiveCameras(string tag)
        {
            var cams = Object.FindObjectsByType<UnityEngine.Camera>(FindObjectsSortMode.None);
            Debug.Log($"[Splitscreen][MenuSplit][Diag] Camera scan ({tag}): total={cams.Length}");
            for (int i = 0; i < cams.Length; i++)
            {
                var cam = cams[i];
                if (cam == null) continue;
                Debug.Log($"[Splitscreen][MenuSplit][Diag]   Cam: name='{cam.name}', active={cam.gameObject.activeInHierarchy}, enabled={cam.enabled}, " +
                    $"depth={cam.depth}, clear={cam.clearFlags}, rect={cam.rect}, cull={cam.cullingMask}, target={(cam.targetTexture != null ? cam.targetTexture.name : "SCREEN")}");
            }
        }

        private void SuppressBlackoutCameras()
        {
            _suppressedBlackoutCameras.Clear();
            var cams = Object.FindObjectsByType<UnityEngine.Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cams.Length; i++)
            {
                var cam = cams[i];
                if (cam == null || cam == _p1UICam) continue;
                if (cam == _menuWorldCamera || cam == _p1WorldCam) continue;
                if (!cam.gameObject.activeInHierarchy || !cam.enabled) continue;
                if (cam.targetTexture != null) continue;
                if (cam.clearFlags != CameraClearFlags.SolidColor) continue;
                if (cam.cullingMask != 0) continue;
                if (!IsFullScreenRect(cam.rect)) continue;

                _suppressedBlackoutCameras.Add(cam);
                cam.enabled = false;
                Debug.Log($"[Splitscreen][MenuSplit] Suppressed blackout camera: '{cam.name}' depth={cam.depth} color={cam.backgroundColor}");
            }
        }

        private void RestoreSuppressedCameras()
        {
            for (int i = 0; i < _suppressedBlackoutCameras.Count; i++)
            {
                var cam = _suppressedBlackoutCameras[i];
                if (cam == null) continue;
                cam.enabled = true;
                Debug.Log($"[Splitscreen][MenuSplit] Restored camera: '{cam.name}'");
            }
            _suppressedBlackoutCameras.Clear();
        }

        private static bool IsFullScreenRect(Rect rect)
        {
            const float eps = 0.001f;
            return Mathf.Abs(rect.x) < eps &&
                   Mathf.Abs(rect.y) < eps &&
                   Mathf.Abs(rect.width - 1f) < eps &&
                   Mathf.Abs(rect.height - 1f) < eps;
        }

        private void ConfigureMenuWorldCameras(FejdStartup fejd)
        {
            _menuWorldCamera = ResolveMenuWorldCamera(fejd);
            if (_menuWorldCamera == null)
            {
                Debug.LogWarning("[Splitscreen][MenuSplit] Could not resolve menu world camera for split");
                return;
            }

            _menuWorldOriginalRect = _menuWorldCamera.rect;
            _menuWorldOriginalDepthTextureMode = _menuWorldCamera.depthTextureMode;

            if (!UseMenuWorldViewportSplit)
            {
                Debug.Log($"[Splitscreen][MenuSplit] Leaving menu world camera fullscreen to avoid render-target mismatch: '{_menuWorldCamera.name}' rect={_menuWorldCamera.rect}");
                return;
            }

            SaveAndRemoveCommandBuffers(_menuWorldCamera);
            DisableMenuWorldBehaviours(_menuWorldCamera);

            _menuWorldCamera.depthTextureMode = DepthTextureMode.None;
            _menuWorldCamera.rect = _horizontal
                ? new Rect(0f, 0.5f, 1f, 0.5f)   // top half = P2
                : new Rect(0.5f, 0f, 0.5f, 1f);  // right half

            _p1WorldCamObj = new GameObject("P1_MenuWorldCamera");
            DontDestroyOnLoad(_p1WorldCamObj);
            _p1WorldCam = _p1WorldCamObj.AddComponent<UnityEngine.Camera>();
            _p1WorldCam.CopyFrom(_menuWorldCamera);
            _p1WorldCam.rect = _horizontal
                ? new Rect(0f, 0f, 1f, 0.5f)     // bottom half = P1
                : new Rect(0f, 0f, 0.5f, 1f);    // left half
            _p1WorldCam.depth = _menuWorldCamera.depth - 0.01f;
            _p1WorldCam.depthTextureMode = DepthTextureMode.None;
            _p1WorldCam.RemoveAllCommandBuffers();

            var listener = _p1WorldCamObj.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;

            Debug.Log($"[Splitscreen][MenuSplit] Configured menu world cameras (safe): main='{_menuWorldCamera.name}' rect={_menuWorldCamera.rect}, " +
                $"depthTex={_menuWorldCamera.depthTextureMode}, p1='{_p1WorldCam.name}' rect={_p1WorldCam.rect}, depthTex={_p1WorldCam.depthTextureMode}");
        }

        private void RestoreMenuWorldCameras()
        {
            if (_menuWorldCamera != null)
            {
                _menuWorldCamera.rect = _menuWorldOriginalRect;
                _menuWorldCamera.depthTextureMode = _menuWorldOriginalDepthTextureMode;
                RestoreCommandBuffers(_menuWorldCamera);
                RestoreMenuWorldBehaviours();
                Debug.Log($"[Splitscreen][MenuSplit] Restored menu world camera: '{_menuWorldCamera.name}' rect={_menuWorldCamera.rect}, depthTex={_menuWorldCamera.depthTextureMode}");
                _menuWorldCamera = null;
            }

            if (_p1WorldCamObj != null)
            {
                Destroy(_p1WorldCamObj);
                _p1WorldCamObj = null;
                _p1WorldCam = null;
                Debug.Log("[Splitscreen][MenuSplit] Destroyed P1_MenuWorldCamera");
            }

            _menuWorldCommandBuffers.Clear();
        }

        private void SaveAndRemoveCommandBuffers(UnityEngine.Camera camera)
        {
            _menuWorldCommandBuffers.Clear();
            foreach (CameraEvent ev in System.Enum.GetValues(typeof(CameraEvent)))
            {
                var buffers = camera.GetCommandBuffers(ev);
                if (buffers == null || buffers.Length == 0) continue;

                var list = new List<CommandBuffer>(buffers.Length);
                for (int i = 0; i < buffers.Length; i++)
                {
                    var buffer = buffers[i];
                    if (buffer == null) continue;
                    list.Add(buffer);
                    camera.RemoveCommandBuffer(ev, buffer);
                }

                if (list.Count > 0)
                {
                    _menuWorldCommandBuffers[ev] = list;
                    Debug.Log($"[Splitscreen][MenuSplit] Removed {list.Count} command buffer(s) from '{camera.name}' event={ev}");
                }
            }
        }

        private void RestoreCommandBuffers(UnityEngine.Camera camera)
        {
            foreach (var kv in _menuWorldCommandBuffers)
            {
                var list = kv.Value;
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var buffer = list[i];
                    if (buffer == null) continue;
                    camera.AddCommandBuffer(kv.Key, buffer);
                }
            }
        }

        private void DisableMenuWorldBehaviours(UnityEngine.Camera camera)
        {
            _disabledMenuWorldBehaviours.Clear();
            var behaviours = camera.GetComponents<Behaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b == null || !b.enabled) continue;
                if (b is UnityEngine.Camera) continue;
                if (b is AudioListener) continue;

                b.enabled = false;
                _disabledMenuWorldBehaviours.Add(b);
                Debug.Log($"[Splitscreen][MenuSplit] Disabled menu camera behaviour: {b.GetType().FullName}");
            }
        }

        private void RestoreMenuWorldBehaviours()
        {
            for (int i = 0; i < _disabledMenuWorldBehaviours.Count; i++)
            {
                var b = _disabledMenuWorldBehaviours[i];
                if (b == null) continue;
                b.enabled = true;
                Debug.Log($"[Splitscreen][MenuSplit] Restored menu camera behaviour: {b.GetType().FullName}");
            }
            _disabledMenuWorldBehaviours.Clear();
        }

        private UnityEngine.Camera ResolveMenuWorldCamera(FejdStartup fejd)
        {
            if (fejd != null && _fejdMainCamera != null)
            {
                var camObj = _fejdMainCamera.GetValue(fejd) as GameObject;
                var cam = camObj != null ? camObj.GetComponent<UnityEngine.Camera>() : null;
                if (cam != null)
                {
                    Debug.Log($"[Splitscreen][MenuSplit] Resolved menu world camera from FejdStartup: '{cam.name}'");
                    return cam;
                }
            }

            var cams = Object.FindObjectsByType<UnityEngine.Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cams.Length; i++)
            {
                var cam = cams[i];
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if (cam.cullingMask == 0) continue;
                if (cam.targetTexture != null) continue;
                if (cam.clearFlags == CameraClearFlags.Depth) continue;
                if (cam == _p1UICam) continue;

                Debug.Log($"[Splitscreen][MenuSplit] Resolved menu world camera by scan: '{cam.name}'");
                return cam;
            }

            return null;
        }
    }
}

