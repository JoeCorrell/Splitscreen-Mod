using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.HUD
{
    /// <summary>
    /// Creates and manages a second HUD canvas for Player 2.
    /// </summary>
    public class SplitHudManager : MonoBehaviour
    {
        public static SplitHudManager Instance { get; private set; }

        private GameObject _p2HudClone;
        private GameObject _p2MinimapSmallClone;
        private Canvas _p2Canvas;
        private Player2HudUpdater _p2Updater;

        private void Awake()
        {
            Instance = this;
            Debug.Log("[Splitscreen][P2HUD] SplitHudManager.Awake");
        }

        public void OnSplitscreenActivated()
        {
            CancelInvoke(nameof(CreatePlayer2Hud));
            Invoke(nameof(CreatePlayer2Hud), 0.5f);
        }

        public void OnSplitscreenDeactivated()
        {
            CancelInvoke(nameof(CreatePlayer2Hud));
            DestroyPlayer2Hud();
        }

        private void CreatePlayer2Hud()
        {
            Debug.Log("[Splitscreen][P2HUD] === CreatePlayer2Hud START ===");

            if (_p2HudClone != null)
            {
                Debug.Log("[Splitscreen][P2HUD] Clone already exists, skipping");
                return;
            }

            if (Hud.instance == null || Hud.instance.m_rootObject == null)
            {
                Debug.LogWarning("[Splitscreen][P2HUD] Hud.instance or m_rootObject is null — will retry");
                Invoke(nameof(CreatePlayer2Hud), 1f);
                return;
            }

            var p2Camera = SplitCameraManager.Instance?.Player2UiCamera;
            if (p2Camera == null)
            {
                Debug.LogWarning("[Splitscreen][P2HUD] P2 UI camera unavailable — will retry");
                Invoke(nameof(CreatePlayer2Hud), 1f);
                return;
            }

            var sourceCanvas = Hud.instance.m_rootObject.GetComponentInParent<Canvas>();
            if (sourceCanvas == null)
            {
                Debug.LogWarning("[Splitscreen][P2HUD] Source HUD canvas not found — will retry");
                Invoke(nameof(CreatePlayer2Hud), 1f);
                return;
            }

            // Diagnostic dump of source state
            Debug.Log($"[Splitscreen][P2HUD] Source canvas: '{sourceCanvas.gameObject.name}' renderMode={sourceCanvas.renderMode}, worldCamera={sourceCanvas.worldCamera?.name ?? "null"}, sortingOrder={sourceCanvas.sortingOrder}");
            Debug.Log($"[Splitscreen][P2HUD] Source m_rootObject: '{Hud.instance.m_rootObject.name}' active={Hud.instance.m_rootObject.activeSelf}, layer={Hud.instance.m_rootObject.layer}");
            var sourceParent = sourceCanvas.transform.parent;
            Canvas parentCanvas = sourceParent != null ? sourceParent.GetComponentInParent<Canvas>() : null;
            Debug.Log($"[Splitscreen][P2HUD] Source canvas parent: '{(sourceParent != null ? sourceParent.name : "null")}', hasParentCanvas={parentCanvas != null}");
            Debug.Log($"[Splitscreen][P2HUD] P2 UI camera: '{p2Camera.name}' enabled={p2Camera.enabled}, targetRT={p2Camera.targetTexture?.name ?? "null"} ({p2Camera.targetTexture?.width}x{p2Camera.targetTexture?.height}), near={p2Camera.nearClipPlane}, far={p2Camera.farClipPlane}");
            Debug.Log($"[Splitscreen][P2HUD] P2 UI camera: cullingMask={p2Camera.cullingMask}, Player2HudLayer={SplitCameraManager.Player2HudLayer}, layerBit={1 << SplitCameraManager.Player2HudLayer}");

            // Create P2 HUD as a ROOT-LEVEL canvas to avoid sub-canvas rendering issues.
            // If parented under another Canvas, ScreenSpaceCamera mode is overridden by the
            // parent's renderMode, preventing rendering into P2's RenderTexture.
            _p2HudClone = new GameObject("SplitscreenHUD_P2",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            // Keep as root object (no parent) so our Canvas is a true root canvas
            _p2HudClone.transform.SetParent(null, false);
            NormalizeRootCanvasRect(_p2HudClone.GetComponent<RectTransform>(), p2Camera);
            Debug.Log("[Splitscreen][P2HUD] Created P2 HUD as root-level canvas (no parent)");

            _p2Canvas = _p2HudClone.GetComponent<Canvas>();
            ConfigureCanvasForP2(_p2Canvas, sourceCanvas, p2Camera);

            // Use ConstantPixelSize so the canvas coordinate space exactly matches
            // the P2 RT dimensions. ScaleWithScreenSize uses Screen.width/height which
            // is the full monitor resolution, causing clipping in the half-screen RT.
            var scaler = _p2HudClone.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            Debug.Log($"[Splitscreen][P2HUD] CanvasScaler: ConstantPixelSize, scaleFactor=1, RT={p2Camera.targetTexture?.width}x{p2Camera.targetTexture?.height}");

            var hudRootClone = Instantiate(Hud.instance.m_rootObject, _p2HudClone.transform, false);
            hudRootClone.name = "HudRoot_P2";
            hudRootClone.SetActive(true);
            NormalizeHudRootRect(hudRootClone.GetComponent<RectTransform>());
            Debug.Log($"[Splitscreen][P2HUD] Cloned hudRoot: active={hudRootClone.activeSelf}, childCount={hudRootClone.transform.childCount}");

            _p2MinimapSmallClone = TryCloneMinimapSmallRoot(_p2HudClone.transform);

            SetLayerRecursively(_p2HudClone, SplitCameraManager.Player2HudLayer);
            ConfigureAllCloneCanvases(_p2HudClone, p2Camera);
            DisableNonRenderingScripts(_p2HudClone);

            // Force cloned HotkeyBars to clear P1's cached data so they refresh with P2's inventory
            ClearClonedHotkeyBarData(_p2HudClone);

            _p2Updater = _p2HudClone.AddComponent<Player2HudUpdater>();
            _p2Updater.ConfigureMinimapMirror(Minimap.instance, _p2MinimapSmallClone);

            // Final diagnostic dump
            Debug.Log($"[Splitscreen][P2HUD] === CREATION COMPLETE ===");
            Debug.Log($"[Splitscreen][P2HUD]   Canvas: renderMode={_p2Canvas.renderMode}, worldCamera={_p2Canvas.worldCamera?.name ?? "null"}, enabled={_p2Canvas.enabled}, isRootCanvas={_p2Canvas.isRootCanvas}");
            Debug.Log($"[Splitscreen][P2HUD]   Canvas: sortingOrder={_p2Canvas.sortingOrder}, planeDistance={_p2Canvas.planeDistance}");
            Debug.Log($"[Splitscreen][P2HUD]   Clone: active={_p2HudClone.activeSelf}, layer={_p2HudClone.layer}, children={_p2HudClone.transform.childCount}");
            Debug.Log($"[Splitscreen][P2HUD]   HudRoot: active={hudRootClone.activeSelf}, children={hudRootClone.transform.childCount}");
            var allCanvases = _p2HudClone.GetComponentsInChildren<Canvas>(true);
            Debug.Log($"[Splitscreen][P2HUD]   Total canvases in clone hierarchy: {allCanvases.Length}");
            for (int i = 0; i < allCanvases.Length && i < 8; i++)
            {
                var c = allCanvases[i];
                Debug.Log($"[Splitscreen][P2HUD]     Canvas[{i}]: '{c.gameObject.name}' renderMode={c.renderMode} worldCam={c.worldCamera?.name ?? "null"} enabled={c.enabled} isRoot={c.isRootCanvas} layer={c.gameObject.layer}");
            }
            // Dump hotbar state
            var bars = _p2HudClone.GetComponentsInChildren<HotkeyBar>(true);
            Debug.Log($"[Splitscreen][P2HUD]   HotkeyBars found: {bars.Length}");
            for (int i = 0; i < bars.Length; i++)
            {
                var bar = bars[i];
                var barRT = bar.GetComponent<RectTransform>();
                Debug.Log($"[Splitscreen][P2HUD]     Bar[{i}]: '{bar.gameObject.name}' active={bar.gameObject.activeSelf} enabled={bar.enabled} layer={bar.gameObject.layer} children={bar.transform.childCount} anchoredPos={barRT?.anchoredPosition} prefab={bar.m_elementPrefab != null}");
            }
            // Dump top-level children of HudRoot
            Debug.Log($"[Splitscreen][P2HUD]   HudRoot top-level children:");
            for (int i = 0; i < hudRootClone.transform.childCount && i < 15; i++)
            {
                var child = hudRootClone.transform.GetChild(i);
                Debug.Log($"[Splitscreen][P2HUD]     [{i}] '{child.name}' active={child.gameObject.activeSelf} layer={child.gameObject.layer}");
            }
        }

        private static void ClearClonedHotkeyBarData(GameObject root)
        {
            var bars = root.GetComponentsInChildren<HotkeyBar>(true);
            var flags = System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance;
            var elementsField = typeof(HotkeyBar).GetField("m_elements", flags);
            var itemsField = typeof(HotkeyBar).GetField("m_items", flags);
            var selectedField = typeof(HotkeyBar).GetField("m_selected", flags);

            foreach (var bar in bars)
            {
                // Remove stale slot children from the cloned bar while preserving template prefab child (if any).
                var prefabTemplate = bar.m_elementPrefab;
                for (int i = bar.transform.childCount - 1; i >= 0; i--)
                {
                    var child = bar.transform.GetChild(i).gameObject;
                    if (prefabTemplate != null && child == prefabTemplate)
                        continue;
                    Destroy(child);
                }

                // Clear internal runtime lists so UpdateIcons rebuilds from P2 inventory cleanly.
                if (elementsField?.GetValue(bar) is System.Collections.IList elements)
                    elements.Clear();
                if (itemsField?.GetValue(bar) is System.Collections.IList items)
                    items.Clear();
                selectedField?.SetValue(bar, 0);

                bar.gameObject.SetActive(true);
                Debug.Log($"[Splitscreen][P2HUD] Cleared cloned HotkeyBar '{bar.gameObject.name}'");
            }
        }

        private static GameObject TryCloneMinimapSmallRoot(Transform parent)
        {
            var sourceMinimap = Minimap.instance;
            if (sourceMinimap == null || sourceMinimap.m_smallRoot == null)
            {
                return null;
            }

            var minimapClone = Instantiate(sourceMinimap.m_smallRoot, parent, false);
            minimapClone.name = "P2_MinimapSmall";
            minimapClone.SetActive(true);
            return minimapClone;
        }

        private static void ConfigureCanvasForP2(Canvas targetCanvas, Canvas sourceCanvas, UnityEngine.Camera p2Camera)
        {
            targetCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            targetCanvas.worldCamera = p2Camera;
            targetCanvas.planeDistance = 1f;
            targetCanvas.sortingLayerID = sourceCanvas.sortingLayerID;
            targetCanvas.sortingOrder = sourceCanvas.sortingOrder;
            targetCanvas.pixelPerfect = sourceCanvas.pixelPerfect;
            targetCanvas.additionalShaderChannels = sourceCanvas.additionalShaderChannels;
        }

        private static void ConfigureAllCloneCanvases(GameObject root, UnityEngine.Camera p2Camera)
        {
            var canvases = root.GetComponentsInChildren<Canvas>(true);
            foreach (var canvas in canvases)
            {
                if (!canvas.isRootCanvas && !canvas.overrideSorting)
                {
                    // Nested canvases inherit parent canvas state; overriding them can break hierarchy layout/rendering.
                    continue;
                }

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = p2Camera;
                canvas.planeDistance = 1f;
                canvas.sortingOrder = 0;
            }
        }

        private static void NormalizeRootCanvasRect(RectTransform rect, UnityEngine.Camera p2Camera)
        {
            if (rect == null) return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;

            if (p2Camera != null && p2Camera.targetTexture != null)
            {
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, p2Camera.targetTexture.width);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, p2Camera.targetTexture.height);
            }
        }

        private static void NormalizeHudRootRect(RectTransform rect)
        {
            if (rect == null) return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void CopyCanvasScalerSettings(CanvasScaler source, CanvasScaler target)
        {
            if (target == null)
            {
                return;
            }

            if (source == null)
            {
                target.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                target.referenceResolution = new Vector2(1920f, 1080f);
                target.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                target.matchWidthOrHeight = 0.5f;
                return;
            }

            target.uiScaleMode = source.uiScaleMode;
            target.referenceResolution = source.referenceResolution;
            target.screenMatchMode = source.screenMatchMode;
            target.matchWidthOrHeight = source.matchWidthOrHeight;
            target.referencePixelsPerUnit = source.referencePixelsPerUnit;
            target.dynamicPixelsPerUnit = source.dynamicPixelsPerUnit;
            target.physicalUnit = source.physicalUnit;
            target.fallbackScreenDPI = source.fallbackScreenDPI;
            target.defaultSpriteDPI = source.defaultSpriteDPI;
        }

        private static void DisableNonRenderingScripts(GameObject root)
        {
            var scripts = root.GetComponentsInChildren<MonoBehaviour>(true);
            int disabled = 0;
            foreach (var script in scripts)
            {
                if (script == null) continue;

                // Keep clone visual logic intact; only disable singleton-risk scripts.
                if (script is Minimap)
                {
                    script.enabled = false;
                    disabled++;
                }
            }
            Debug.Log($"[Splitscreen][P2HUD] Disabled {disabled} singleton scripts on P2 clone");
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].gameObject.layer = layer;
            }
        }

        private void DestroyPlayer2Hud()
        {
            if (_p2HudClone == null)
            {
                return;
            }

            Destroy(_p2HudClone);
            _p2HudClone = null;
            _p2MinimapSmallClone = null;
            _p2Canvas = null;
            _p2Updater = null;
        }

        private void OnDestroy()
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Updates selected HUD pieces in the cloned P2 HUD tree.
    /// Handles health, stamina, eitr, hotbar, minimap, messages, status effects, food, and hover text.
    /// </summary>
    public class Player2HudUpdater : MonoBehaviour
    {
        public static Player2HudUpdater Instance { get; private set; }

        private Image _healthBarFill;
        private Image _healthBarSlow;
        private Text _healthText;
        private TMP_Text _healthTextTMP;
        private Image _staminaBarFill;
        private Image _staminaBarSlow;
        private Text _staminaText;
        private TMP_Text _staminaTextTMP;
        private Image _eitrBarFill;
        private Image _eitrBarSlow;
        private global::Hud _cloneHud;
        private Minimap _cloneMinimap;

        private HotkeyBar[] _hotkeyBars = Array.Empty<HotkeyBar>();

        private bool _initialized;
        private bool _staticPanelsConfigured;
        private float _lastSearchTime;
        private float _lastHotbarSearchTime;
        private int _searchAttempts;
        private float _lastMinimapConfigTime;
        private float _lastSourceMinimapSearchTime;
        private float _lastDiagnosticLogTime;

        // Optional minimap mirror path when the cloned HUD does not contain a Minimap component.
        private GameObject _mirrorMinimapRoot;
        private RawImage _mirrorMapImageSmall;
        private RectTransform _mirrorSmallMarker;
        private RectTransform _mirrorShipMarker;
        private TMP_Text _mirrorBiomeNameSmall;

        private RawImage _sourceMapImageSmall;
        private RectTransform _sourceSmallMarker;
        private RectTransform _sourceShipMarker;
        private TMP_Text _sourceBiomeNameSmall;

        // --- P2 Message Display ---
        private TMP_Text _centerMessageText;
        private TMP_Text _topLeftMessageText;
        private Sprite _lastMessageIcon;
        private float _centerMessageTimer;
        private float _topLeftMessageTimer;
        private const float MessageFadeTime = 4f;

        // --- P2 Hover Text ---
        private TMP_Text _hoverNameText;
        private GameObject _crosshairObj;

        // Rate-limit expensive reflection calls
        private float _lastStatusEffectUpdate;
        private float _lastFoodIconUpdate;
        private float _lastGuardianPowerUpdate;
        private const float StatusFoodUpdateInterval = 0.5f;

        private void Awake()
        {
            Instance = this;
            _cloneHud = GetComponentInChildren<global::Hud>(true);
            _cloneMinimap = GetComponentInChildren<Minimap>(true);
            FindMessageElements();
            FindHoverElements();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void ConfigureMinimapMirror(Minimap sourceMinimap, GameObject mirrorSmallRoot)
        {
            _mirrorMinimapRoot = mirrorSmallRoot;
            if (_mirrorMinimapRoot != null)
            {
                _mirrorMapImageSmall = FindRawImageByNameToken(_mirrorMinimapRoot.transform, "map");
                _mirrorBiomeNameSmall = FindTmpTextByNameToken(_mirrorMinimapRoot.transform, "biome");

                // Fallback: if token search failed, try finding any RawImage in the minimap clone
                if (_mirrorMapImageSmall == null)
                {
                    var allRawImages = _mirrorMinimapRoot.GetComponentsInChildren<RawImage>(true);
                    if (allRawImages.Length > 0)
                    {
                        _mirrorMapImageSmall = allRawImages[0];
                        Debug.Log($"[Splitscreen][P2HUD] Minimap mirror: token search failed, using fallback RawImage '{_mirrorMapImageSmall.gameObject.name}'");
                    }
                }

                Debug.Log($"[Splitscreen][P2HUD] Minimap mirror binding: root={_mirrorMinimapRoot.name}, mapImage={(_mirrorMapImageSmall != null ? _mirrorMapImageSmall.gameObject.name : "NULL")}, biome={(_mirrorBiomeNameSmall != null ? _mirrorBiomeNameSmall.gameObject.name : "NULL")}");
            }
            else
            {
                Debug.LogWarning("[Splitscreen][P2HUD] Minimap mirror root is null!");
            }

            BindSourceMinimap(sourceMinimap);
            TryBindMirrorMarkersByName();
            ConfigureMinimapVisibility();

            Debug.Log($"[Splitscreen][P2HUD] Source minimap binding: mapImage={(_sourceMapImageSmall != null ? _sourceMapImageSmall.gameObject.name : "NULL")}, marker={(_sourceSmallMarker != null ? _sourceSmallMarker.gameObject.name : "NULL")}");
        }

        private void Update()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null) return;

            // Periodic diagnostic logging to verify canvas/camera state
            if (Time.time - _lastDiagnosticLogTime > 10f)
            {
                _lastDiagnosticLogTime = Time.time;
                var canvas = GetComponentInParent<Canvas>();
                var p2Cam = SplitCameraManager.Instance?.Player2UiCamera;
                Debug.Log($"[Splitscreen][P2HUD] === PERIODIC DIAGNOSTIC ===");
                Debug.Log($"[Splitscreen][P2HUD]   GO active={gameObject.activeSelf}, canvas={canvas != null}");
                if (canvas != null)
                {
                    Debug.Log($"[Splitscreen][P2HUD]   Canvas: renderMode={canvas.renderMode}, worldCamera={canvas.worldCamera?.name ?? "null"}, enabled={canvas.enabled}, isRoot={canvas.isRootCanvas}");
                    Debug.Log($"[Splitscreen][P2HUD]   Canvas: planeDistance={canvas.planeDistance}, sortingOrder={canvas.sortingOrder}");
                }
                Debug.Log($"[Splitscreen][P2HUD]   P2 UICam: {(p2Cam != null ? $"enabled={p2Cam.enabled} targetRT={p2Cam.targetTexture?.name ?? "null"} ({p2Cam.targetTexture?.width}x{p2Cam.targetTexture?.height})" : "NULL")}");
                Debug.Log($"[Splitscreen][P2HUD]   Initialized={_initialized}, health={_healthBarFill != null}, stamina={_staminaBarFill != null}, hotbars={_hotkeyBars?.Length ?? 0}");
                Debug.Log($"[Splitscreen][P2HUD]   P2: alive={!p2.IsDead()}, health={p2.GetHealth():F0}/{p2.GetMaxHealth():F0}");
            }

            if (!_initialized)
            {
                if (Time.time - _lastSearchTime > 2f)
                {
                    _lastSearchTime = Time.time;
                    FindUIElements();
                }
                if (!_initialized) return;
            }

            UpdateHealthBar(p2);
            UpdateStaminaBar(p2);
            UpdateEitrBar(p2);
            UpdateHotbar(p2);
            UpdateMinimapMirror();
            UpdateMessages();
            UpdateHoverText(p2);

            // Rate-limit expensive reflection calls to reduce FPS impact
            if (Time.time - _lastStatusEffectUpdate > StatusFoodUpdateInterval)
            {
                _lastStatusEffectUpdate = Time.time;
                UpdateStatusEffects(p2);
            }
            if (Time.time - _lastFoodIconUpdate > StatusFoodUpdateInterval)
            {
                _lastFoodIconUpdate = Time.time;
                UpdateFoodIcons(p2);
            }
            if (Time.time - _lastGuardianPowerUpdate > StatusFoodUpdateInterval)
            {
                _lastGuardianPowerUpdate = Time.time;
                UpdateGuardianPower(p2);
            }
            UpdateCrosshair(p2);

            if (Time.time - _lastMinimapConfigTime > 2f)
            {
                _lastMinimapConfigTime = Time.time;
                ConfigureMinimapVisibility();
            }
        }

        private void FindUIElements()
        {
            _healthBarFill = FindImageByPath("healthpanel/Health/health_fast", "darken/Health/health_fast", "Health/health_fast");
            _healthBarSlow = FindImageByPath("healthpanel/Health/health_slow", "darken/Health/health_slow", "Health/health_slow");
            _healthText = FindTextByPath("healthpanel/Health/HealthText", "darken/Health/HealthText", "Health/HealthText");

            _staminaBarFill = FindImageByPath("healthpanel/staminapanel/Stamina/stamina_fast", "darken/Stamina/stamina_fast", "Stamina/stamina_fast", "staminapanel/Stamina/stamina_fast");
            _staminaBarSlow = FindImageByPath("healthpanel/staminapanel/Stamina/stamina_slow", "darken/Stamina/stamina_slow", "Stamina/stamina_slow", "staminapanel/Stamina/stamina_slow");
            _staminaText = FindTextByPath("healthpanel/staminapanel/StaminaText", "darken/StaminaText", "StaminaText", "staminapanel/StaminaText");

            _eitrBarFill = FindImageByPath("healthpanel/eitrpanel/Eitr/eitr_fast", "darken/Eitr/eitr_fast", "Eitr/eitr_fast", "eitrpanel/Eitr/eitr_fast");
            _eitrBarSlow = FindImageByPath("healthpanel/eitrpanel/Eitr/eitr_slow", "darken/Eitr/eitr_slow", "Eitr/eitr_slow", "eitrpanel/Eitr/eitr_slow");

            FindHotkeyBars();
            ConfigureStaticPanels();
            ConfigureMinimapVisibility();

            TryBroadSearch();

            // Initialize as soon as we find any element, or force after 3 attempts
            bool foundSomething = _healthBarFill != null || (_hotkeyBars != null && _hotkeyBars.Length > 0);
            int attempts = Mathf.RoundToInt(Time.time - _lastSearchTime);
            if (foundSomething || _searchAttempts >= 3)
            {
                _initialized = true;
                Debug.Log($"[Splitscreen][P2HUD] Initialized after {_searchAttempts} attempts: health={_healthBarFill != null}, stamina={_staminaBarFill != null}, healthTxt={_healthText != null || _healthTextTMP != null}, staminaTxt={_staminaText != null || _staminaTextTMP != null}, hotbars={_hotkeyBars?.Length ?? 0}");
            }
            _searchAttempts++;
        }

        private void TryBroadSearch()
        {
            var images = GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                string name = img.gameObject.name.ToLowerInvariant();
                if (name.Contains("health") && name.Contains("fast") && _healthBarFill == null) _healthBarFill = img;
                else if (name.Contains("health") && name.Contains("slow") && _healthBarSlow == null) _healthBarSlow = img;
                else if (name.Contains("stamina") && name.Contains("fast") && _staminaBarFill == null) _staminaBarFill = img;
                else if (name.Contains("stamina") && name.Contains("slow") && _staminaBarSlow == null) _staminaBarSlow = img;
                else if (name.Contains("eitr") && name.Contains("fast") && _eitrBarFill == null) _eitrBarFill = img;
                else if (name.Contains("eitr") && name.Contains("slow") && _eitrBarSlow == null) _eitrBarSlow = img;
            }

            var texts = GetComponentsInChildren<Text>(true);
            foreach (var txt in texts)
            {
                string name = txt.gameObject.name.ToLowerInvariant();
                if (name.Contains("health") && name.Contains("text") && _healthText == null) _healthText = txt;
                else if (name.Contains("stamina") && name.Contains("text") && _staminaText == null) _staminaText = txt;
            }

            // Valheim may use TMP_Text instead of legacy Text
            if (_healthText == null || _staminaText == null)
            {
                var tmpTexts = GetComponentsInChildren<TMP_Text>(true);
                foreach (var tmp in tmpTexts)
                {
                    string name = tmp.gameObject.name.ToLowerInvariant();
                    if (name.Contains("health") && name.Contains("text") && _healthTextTMP == null) _healthTextTMP = tmp;
                    else if (name.Contains("stamina") && name.Contains("text") && _staminaTextTMP == null) _staminaTextTMP = tmp;
                }
            }

            // Diagnostic: dump all Image names on first search to help identify correct element names
            if (_searchAttempts == 0)
            {
                Debug.Log($"[Splitscreen][P2HUD] === DIAGNOSTIC: All Image components in P2 HUD clone ({images.Length} total) ===");
                foreach (var img in images)
                {
                    string path = GetHierarchyPath(img.transform);
                    Debug.Log($"[Splitscreen][P2HUD]   Image: {path} (type={img.type}, fillAmount={img.fillAmount:F2})");
                }
            }
        }

        private string GetHierarchyPath(Transform t)
        {
            string path = t.gameObject.name;
            Transform current = t.parent;
            int depth = 0;
            while (current != null && current != transform && depth < 5)
            {
                path = current.gameObject.name + "/" + path;
                current = current.parent;
                depth++;
            }
            return path;
        }

        private void UpdateHealthBar(global::Player p2)
        {
            float health = p2.GetHealth();
            float maxHealth = p2.GetMaxHealth();
            float pct = maxHealth > 0f ? health / maxHealth : 0f;

            if (_healthBarFill != null) _healthBarFill.fillAmount = pct;
            if (_healthBarSlow != null) _healthBarSlow.fillAmount = Mathf.MoveTowards(_healthBarSlow.fillAmount, pct, Time.deltaTime * 0.5f);
            if (_healthText != null) _healthText.text = Mathf.CeilToInt(health).ToString();
            else if (_healthTextTMP != null) _healthTextTMP.text = Mathf.CeilToInt(health).ToString();
        }

        private void UpdateStaminaBar(global::Player p2)
        {
            float stamina = p2.GetStamina();
            float maxStamina = p2.GetMaxStamina();
            float pct = maxStamina > 0f ? stamina / maxStamina : 0f;

            if (_staminaBarFill != null) _staminaBarFill.fillAmount = pct;
            if (_staminaBarSlow != null) _staminaBarSlow.fillAmount = Mathf.MoveTowards(_staminaBarSlow.fillAmount, pct, Time.deltaTime * 0.5f);
            if (_staminaText != null) _staminaText.text = Mathf.CeilToInt(stamina).ToString();
            else if (_staminaTextTMP != null) _staminaTextTMP.text = Mathf.CeilToInt(stamina).ToString();
        }

        private void UpdateEitrBar(global::Player p2)
        {
            float maxEitr = p2.GetMaxEitr();
            if (maxEitr <= 0f) return;

            float eitr = p2.GetEitr();
            float pct = eitr / maxEitr;
            if (_eitrBarFill != null) _eitrBarFill.fillAmount = pct;
            if (_eitrBarSlow != null) _eitrBarSlow.fillAmount = Mathf.MoveTowards(_eitrBarSlow.fillAmount, pct, Time.deltaTime * 0.5f);
        }

        private void ConfigureStaticPanels()
        {
            if (_staticPanelsConfigured) return;

            HideUnsupportedBranches();

            if (_cloneHud == null)
            {
                _cloneHud = GetComponentInChildren<global::Hud>(true);
                if (_cloneHud == null)
                {
                    DisableByNameTokens(
                        "food", "status", "guardian", "event", "ship", "build",
                        "crosshair", "hover", "target", "save", "connection",
                        "loading", "mount", "action", "beta", "piece");
                    _staticPanelsConfigured = true;
                    return;
                }
            }

            SafeSetActive(_cloneHud.m_buildHud, false);
            SafeSetActive(_cloneHud.m_saveIcon, false);
            SafeSetActive(_cloneHud.m_badConnectionIcon, false);
            SafeSetActive(_cloneHud.m_betaText, false);
            SafeSetActive(_cloneHud.m_actionBarRoot, false);
            SafeSetActive(_cloneHud.m_mountPanel, false);
            SafeSetActive(_cloneHud.m_shipHudRoot, false);
            SafeSetActive(_cloneHud.m_shipControlsRoot, false);
            SafeSetActive(_cloneHud.m_eventBar, false);
            SafeSetActive(_cloneHud.m_targetedAlert, false);
            SafeSetActive(_cloneHud.m_targeted, false);
            SafeSetActive(_cloneHud.m_hidden, false);
            SafeSetActive(_cloneHud.m_pieceSelectionWindow, false);

            // Keep status effects, food bar, crosshair/hover, and guardian power ENABLED for P2
            // They are updated each frame via RunWithP2HudFields swap pattern
            if (_cloneHud.m_loadingScreen != null) SafeSetActive(_cloneHud.m_loadingScreen.gameObject, false);
            if (_cloneHud.m_damageScreen != null) SafeSetActive(_cloneHud.m_damageScreen.gameObject, false);
            if (_cloneHud.m_lavaWarningScreen != null) SafeSetActive(_cloneHud.m_lavaWarningScreen.gameObject, false);

            _staticPanelsConfigured = true;
        }

        private void HideUnsupportedBranches()
        {
            // Keep everything visible by default.
            // ConfigureStaticPanels() will disable specific panels we don't need.
            // Ensure all top-level children are active so the HUD root renders.
            for (int i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).gameObject.SetActive(true);
            }
            Debug.Log($"[Splitscreen][P2HUD] HideUnsupportedBranches: enabled {transform.childCount} top-level children");
        }

        private void CollectTopLevelRoot(Transform leaf, HashSet<Transform> roots)
        {
            if (leaf == null) return;

            var current = leaf;
            while (current.parent != null && current.parent != transform)
            {
                current = current.parent;
            }

            if (current.parent == transform)
            {
                roots.Add(current);
            }
        }

        private void DisableByNameTokens(params string[] tokens)
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var go = transforms[i].gameObject;
                string name = go.name.ToLowerInvariant();
                bool match = false;
                for (int t = 0; t < tokens.Length; t++)
                {
                    if (name.Contains(tokens[t]))
                    {
                        match = true;
                        break;
                    }
                }
                if (!match) continue;

                if (name.Contains("health") || name.Contains("stamina") || name.Contains("eitr") ||
                    name.Contains("hotkey") || name.Contains("minimap") || name.Contains("food") ||
                    name.Contains("guardian") || name.Contains("status") || name.Contains("crosshair") ||
                    name.Contains("action") || name.Contains("stagger"))
                {
                    continue;
                }

                go.SetActive(false);
            }
        }

        private void BindSourceMinimap(Minimap sourceMinimap)
        {
            if (sourceMinimap == null)
            {
                return;
            }

            _sourceMapImageSmall = sourceMinimap.m_mapImageSmall;
            _sourceSmallMarker = sourceMinimap.m_smallMarker;
            _sourceShipMarker = sourceMinimap.m_smallShipMarker;
            _sourceBiomeNameSmall = sourceMinimap.m_biomeNameSmall;
        }

        private void TryBindMirrorMarkersByName()
        {
            if (_mirrorMinimapRoot == null)
            {
                return;
            }

            if (_sourceSmallMarker != null)
            {
                _mirrorSmallMarker = FindRectTransformByName(_mirrorMinimapRoot.transform, _sourceSmallMarker.gameObject.name);
            }
            if (_sourceShipMarker != null)
            {
                _mirrorShipMarker = FindRectTransformByName(_mirrorMinimapRoot.transform, _sourceShipMarker.gameObject.name);
            }
            if (_sourceBiomeNameSmall != null && _mirrorBiomeNameSmall == null)
            {
                _mirrorBiomeNameSmall = FindTmpTextByName(_mirrorMinimapRoot.transform, _sourceBiomeNameSmall.gameObject.name);
            }
        }

        private void UpdateMinimapMirror()
        {
            if (_mirrorMinimapRoot == null)
            {
                // Try to create mirror root if it doesn't exist yet
                if (Time.time - _lastSourceMinimapSearchTime > 3f)
                {
                    _lastSourceMinimapSearchTime = Time.time;
                    if (Minimap.instance != null && Minimap.instance.m_smallRoot != null)
                    {
                        _mirrorMinimapRoot = Instantiate(Minimap.instance.m_smallRoot, transform, false);
                        _mirrorMinimapRoot.name = "P2_MinimapSmall_Retry";
                        _mirrorMinimapRoot.SetActive(true);
                        SplitHudManager.SetLayerRecursively(_mirrorMinimapRoot, SplitCameraManager.Player2HudLayer);
                        ConfigureMinimapMirror(Minimap.instance, _mirrorMinimapRoot);
                        Debug.Log("[Splitscreen][P2HUD] Created minimap mirror root on retry");
                    }
                }
                return;
            }

            if (_sourceMapImageSmall == null && Time.time - _lastSourceMinimapSearchTime > 2f)
            {
                _lastSourceMinimapSearchTime = Time.time;
                BindSourceMinimap(Minimap.instance);
                TryBindMirrorMarkersByName();
                if (_mirrorMapImageSmall == null)
                {
                    // Re-search for RawImage in mirror
                    var allRaw = _mirrorMinimapRoot.GetComponentsInChildren<RawImage>(true);
                    if (allRaw.Length > 0)
                    {
                        _mirrorMapImageSmall = allRaw[0];
                        Debug.Log($"[Splitscreen][P2HUD] Re-bound mirror RawImage: '{_mirrorMapImageSmall.gameObject.name}'");
                    }
                }
            }

            _mirrorMinimapRoot.SetActive(true);

            var p2 = SplitScreenManager.Instance?.PlayerManager?.Player2;
            var minimap = Minimap.instance;

            if (SplitscreenLog.ShouldLog("Minimap.mirror", 10f))
            {
                SplitscreenLog.Log("Minimap", $"UpdateMirror: mirrorMap={(_mirrorMapImageSmall != null)}, sourceMap={(_sourceMapImageSmall != null)}, p2={p2 != null}, minimap={minimap != null}");
            }

            // Copy map texture and material from P1's minimap
            if (_mirrorMapImageSmall != null && _sourceMapImageSmall != null)
            {
                _mirrorMapImageSmall.texture = _sourceMapImageSmall.texture;
                if (_sourceMapImageSmall.material != null)
                {
                    _mirrorMapImageSmall.material = _sourceMapImageSmall.material;
                }

                // Center the minimap on P2's position instead of P1's
                if (p2 != null && minimap != null)
                {
                    float texSize = minimap.m_textureSize;
                    float pixSize = minimap.m_pixelSize;
                    float half = texSize / 2f;

                    float uvX = (p2.transform.position.x / pixSize + half) / texSize;
                    float uvY = (p2.transform.position.z / pixSize + half) / texSize;

                    // Use same zoom as P1's minimap
                    float zoom = _sourceMapImageSmall.uvRect.width;
                    _mirrorMapImageSmall.uvRect = new Rect(uvX - zoom / 2f, uvY - zoom / 2f, zoom, zoom);
                }
                else
                {
                    // Fallback: copy P1's uvRect
                    _mirrorMapImageSmall.uvRect = _sourceMapImageSmall.uvRect;
                }
            }

            // Player marker: center on minimap, rotate to P2's facing
            if (_mirrorSmallMarker != null)
            {
                _mirrorSmallMarker.anchoredPosition = Vector2.zero;
                var cam = SplitCameraManager.Instance?.Player2Camera;
                if (cam != null)
                {
                    float angle = -cam.transform.rotation.eulerAngles.y;
                    _mirrorSmallMarker.localRotation = Quaternion.Euler(0, 0, angle);
                }
            }

            // Ship marker: position relative to P2's map center
            if (_mirrorShipMarker != null)
            {
                Ship controlledShip = p2 != null ? p2.GetControlledShip() : null;
                if (controlledShip != null && minimap != null && _mirrorMapImageSmall != null)
                {
                    _mirrorShipMarker.gameObject.SetActive(true);
                    float texSize = minimap.m_textureSize;
                    float pixSize = minimap.m_pixelSize;
                    float half = texSize / 2f;

                    // Ship and P2 positions in UV space
                    Vector3 shipPos = controlledShip.transform.position;
                    float shipUvX = (shipPos.x / pixSize + half) / texSize;
                    float shipUvY = (shipPos.z / pixSize + half) / texSize;
                    float p2UvX = (p2.transform.position.x / pixSize + half) / texSize;
                    float p2UvY = (p2.transform.position.z / pixSize + half) / texSize;

                    // Offset in UV space, then convert to minimap UI coordinates
                    float zoom = _mirrorMapImageSmall.uvRect.width;
                    RectTransform mapRT = _mirrorMapImageSmall.rectTransform;
                    float mapWidth = mapRT.rect.width;
                    float mapHeight = mapRT.rect.height;

                    float relX = (shipUvX - p2UvX) / zoom * mapWidth;
                    float relY = (shipUvY - p2UvY) / zoom * mapHeight;
                    _mirrorShipMarker.anchoredPosition = new Vector2(relX, relY);

                    float shipAngle = -controlledShip.transform.rotation.eulerAngles.y;
                    _mirrorShipMarker.localRotation = Quaternion.Euler(0, 0, shipAngle);
                }
                else
                {
                    _mirrorShipMarker.gameObject.SetActive(false);
                }
            }

            // Biome name based on P2's position
            if (_mirrorBiomeNameSmall != null)
            {
                if (p2 != null)
                {
                    Heightmap.Biome biome = Heightmap.FindBiome(p2.transform.position);
                    _mirrorBiomeNameSmall.text = Localization.instance.Localize("$biome_" + biome.ToString().ToLower());
                }
                else if (_sourceBiomeNameSmall != null)
                {
                    _mirrorBiomeNameSmall.text = _sourceBiomeNameSmall.text;
                }
            }
        }

        private static void CopyMarker(RectTransform source, RectTransform target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.anchoredPosition = source.anchoredPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null) return;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                transforms[i].gameObject.layer = layer;
        }

        private void ConfigureMinimapVisibility()
        {
            if (_cloneMinimap == null)
            {
                _cloneMinimap = GetComponentInChildren<Minimap>(true);
            }

            if (_cloneMinimap != null)
            {
                if (_cloneMinimap.m_smallRoot != null) _cloneMinimap.m_smallRoot.SetActive(true);
                if (_cloneMinimap.m_largeRoot != null) _cloneMinimap.m_largeRoot.SetActive(false);
                if (_cloneMinimap.m_mapImageSmall != null) _cloneMinimap.m_mapImageSmall.gameObject.SetActive(true);
                if (_cloneMinimap.m_mapImageLarge != null) _cloneMinimap.m_mapImageLarge.gameObject.SetActive(false);
                if (_cloneMinimap.m_biomeNameSmall != null) _cloneMinimap.m_biomeNameSmall.gameObject.SetActive(true);
                if (_cloneMinimap.m_gamepadCrosshair != null) _cloneMinimap.m_gamepadCrosshair.gameObject.SetActive(false);
            }

            if (_mirrorMinimapRoot != null)
            {
                _mirrorMinimapRoot.SetActive(true);
            }
        }

        private static void SafeSetActive(GameObject go, bool active)
        {
            if (go != null)
            {
                go.SetActive(active);
            }
        }

        private void FindHotkeyBars()
        {
            _hotkeyBars = GetComponentsInChildren<HotkeyBar>(true);
        }

        private void UpdateHotbar(global::Player p2)
        {
            // HotkeyBar.Update() now runs naturally via Harmony patch (HotkeyBarPatches)
            // which swaps Player.m_localPlayer to P2 for bars on Player2HudLayer.
            // We just need to find bars for HideUnsupportedBranches and periodic re-search.
            if ((_hotkeyBars == null || _hotkeyBars.Length == 0) && Time.time - _lastHotbarSearchTime > 2f)
            {
                _lastHotbarSearchTime = Time.time;
                FindHotkeyBars();
            }

            if (_hotkeyBars == null) return;

            for (int i = 0; i < _hotkeyBars.Length; i++)
            {
                var bar = _hotkeyBars[i];
                if (bar == null) continue;

                if (!bar.gameObject.activeSelf)
                    bar.gameObject.SetActive(true);

                if (bar.gameObject.layer != SplitCameraManager.Player2HudLayer)
                    SplitHudManager.SetLayerRecursively(bar.gameObject, SplitCameraManager.Player2HudLayer);
            }
        }

        // =====================================================================
        // P2 MESSAGE DISPLAY
        // =====================================================================

        private void FindMessageElements()
        {
            // Search the cloned HUD for text elements that can serve as message displays
            var allTexts = GetComponentsInChildren<TMP_Text>(true);
            foreach (var txt in allTexts)
            {
                string name = txt.gameObject.name.ToLowerInvariant();
                if (name.Contains("message") && name.Contains("center") && _centerMessageText == null)
                    _centerMessageText = txt;
                else if ((name.Contains("message") && name.Contains("top")) && _topLeftMessageText == null)
                    _topLeftMessageText = txt;
            }

            SplitscreenLog.Log("P2HUD", $"FindMessageElements: center={_centerMessageText != null}, topLeft={_topLeftMessageText != null}");
        }

        /// <summary>
        /// Called by MessagePatches when Player 2 receives a message.
        /// </summary>
        public void ShowMessage(MessageHud.MessageType type, string msg, int amount, Sprite icon)
        {
            if (amount > 0) msg = $"{msg} x{amount}";
            else if (amount < 0) msg = $"{msg} ({amount})";

            if (type == MessageHud.MessageType.Center)
            {
                if (_centerMessageText != null)
                {
                    _centerMessageText.gameObject.SetActive(true);
                    _centerMessageText.text = msg;
                    _centerMessageTimer = MessageFadeTime;
                }
            }
            else // TopLeft corner message
            {
                if (_topLeftMessageText != null)
                {
                    _topLeftMessageText.gameObject.SetActive(true);
                    _topLeftMessageText.text = msg;
                    _topLeftMessageTimer = MessageFadeTime;
                }
            }

            _lastMessageIcon = icon;
        }

        private void UpdateMessages()
        {
            if (_centerMessageText != null && _centerMessageTimer > 0f)
            {
                _centerMessageTimer -= Time.deltaTime;
                if (_centerMessageTimer <= 0f)
                {
                    _centerMessageText.text = "";
                    _centerMessageText.gameObject.SetActive(false);
                }
                else if (_centerMessageTimer < 1f)
                {
                    // Fade out
                    var c = _centerMessageText.color;
                    c.a = _centerMessageTimer;
                    _centerMessageText.color = c;
                }
                else
                {
                    var c = _centerMessageText.color;
                    c.a = 1f;
                    _centerMessageText.color = c;
                }
            }

            if (_topLeftMessageText != null && _topLeftMessageTimer > 0f)
            {
                _topLeftMessageTimer -= Time.deltaTime;
                if (_topLeftMessageTimer <= 0f)
                {
                    _topLeftMessageText.text = "";
                    _topLeftMessageText.gameObject.SetActive(false);
                }
                else if (_topLeftMessageTimer < 1f)
                {
                    var c = _topLeftMessageText.color;
                    c.a = _topLeftMessageTimer;
                    _topLeftMessageText.color = c;
                }
                else
                {
                    var c = _topLeftMessageText.color;
                    c.a = 1f;
                    _topLeftMessageText.color = c;
                }
            }
        }

        // =====================================================================
        // P2 HOVER TEXT + CROSSHAIR
        // =====================================================================

        private void FindHoverElements()
        {
            if (_cloneHud != null)
            {
                if (_cloneHud.m_hoverName != null)
                    _hoverNameText = _cloneHud.m_hoverName.GetComponent<TMP_Text>();
                if (_cloneHud.m_crosshair != null)
                    _crosshairObj = _cloneHud.m_crosshair.gameObject;
            }

            if (_hoverNameText == null)
            {
                // Fallback: search by name
                var allTexts = GetComponentsInChildren<TMP_Text>(true);
                foreach (var txt in allTexts)
                {
                    if (txt.gameObject.name.ToLowerInvariant().Contains("hovername"))
                    {
                        _hoverNameText = txt;
                        break;
                    }
                }
            }

            SplitscreenLog.Log("P2HUD", $"FindHoverElements: hoverText={_hoverNameText != null}, crosshair={_crosshairObj != null}");
        }

        private void UpdateHoverText(global::Player p2)
        {
            if (_hoverNameText == null) return;

            // Temporarily set m_localPlayer to P2 to get correct hover info
            string hoverText = null;
            SplitscreenLog.ExecuteAsPlayer(p2, () =>
            {
                var hoverObj = p2.GetHoverObject();
                if (hoverObj != null)
                {
                    var hoverable = hoverObj.GetComponentInParent<Hoverable>();
                    if (hoverable != null)
                    {
                        try { hoverText = hoverable.GetHoverText(); }
                        catch { /* Some hoverables may throw */ }
                    }
                }
            });

            if (!string.IsNullOrEmpty(hoverText))
            {
                _hoverNameText.gameObject.SetActive(true);
                // Strip rich text formatting that may not render well
                _hoverNameText.text = hoverText;
            }
            else
            {
                _hoverNameText.gameObject.SetActive(false);
                _hoverNameText.text = "";
            }
        }

        // =====================================================================
        // P2 STATUS EFFECTS
        // =====================================================================

        private static readonly System.Reflection.MethodInfo _hudUpdateStatusEffects =
            typeof(global::Hud).GetMethod("UpdateStatusEffects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        private void UpdateStatusEffects(global::Player p2)
        {
            if (_hudUpdateStatusEffects == null) return;

            if (_cloneHud != null && _cloneHud.m_statusEffectListRoot != null)
            {
                SplitscreenLog.ExecuteAsPlayer(p2, () =>
                {
                    try
                    {
                        var seEffects = p2.GetSEMan().GetStatusEffects();
                        _hudUpdateStatusEffects.Invoke(_cloneHud, new object[] { seEffects });
                    }
                    catch { }
                });
                return;
            }

            RunWithP2HudFields(() =>
            {
                SplitscreenLog.ExecuteAsPlayer(p2, () =>
                {
                    try
                    {
                        var seEffects = p2.GetSEMan().GetStatusEffects();
                        _hudUpdateStatusEffects.Invoke(global::Hud.instance, new object[] { seEffects });
                    }
                    catch { }
                });
            });
        }

        // =====================================================================
        // P2 FOOD ICONS
        // =====================================================================

        private static readonly System.Reflection.MethodInfo _hudUpdateFood =
            typeof(global::Hud).GetMethod("UpdateFood", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        // General Hud field swap cache — maps Hud.instance UI fields to P2 clone equivalents
        private bool _hudSwapCached;
        private System.Reflection.FieldInfo[] _hudSwapFields;
        private object[] _p2SwapValues;

        /// <summary>
        /// Temporarily swaps ALL Hud.instance UI fields to P2's cloned elements,
        /// runs the given action, then restores. Same pattern as the HotkeyBar player swap.
        /// </summary>
        private void RunWithP2HudFields(Action action)
        {
            var hud = global::Hud.instance;
            if (hud == null) return;

            if (!_hudSwapCached)
            {
                CacheHudSwapFields(hud);
                _hudSwapCached = true;
            }

            if (_hudSwapFields == null || _hudSwapFields.Length == 0) return;

            var saved = new object[_hudSwapFields.Length];
            for (int i = 0; i < _hudSwapFields.Length; i++)
            {
                saved[i] = _hudSwapFields[i].GetValue(hud);
                _hudSwapFields[i].SetValue(hud, _p2SwapValues[i]);
            }
            try
            {
                action();
            }
            finally
            {
                for (int i = 0; i < _hudSwapFields.Length; i++)
                {
                    _hudSwapFields[i].SetValue(hud, saved[i]);
                }
            }
        }

        private void UpdateFoodIcons(global::Player p2)
        {
            if (_hudUpdateFood == null) return;

            if (_cloneHud != null && _cloneHud.m_foodBarRoot != null)
            {
                SplitscreenLog.ExecuteAsPlayer(p2, () =>
                {
                    try { _hudUpdateFood.Invoke(_cloneHud, new object[] { p2 }); }
                    catch { }
                });
                return;
            }

            RunWithP2HudFields(() =>
            {
                SplitscreenLog.ExecuteAsPlayer(p2, () =>
                {
                    try { _hudUpdateFood.Invoke(global::Hud.instance, new object[] { p2 }); }
                    catch { }
                });
            });
        }

        // =====================================================================
        // P2 GUARDIAN POWER
        // =====================================================================

        private static readonly System.Reflection.MethodInfo _hudUpdateGuardianPower =
            typeof(global::Hud).GetMethod("UpdateGuardianPower", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        private void UpdateGuardianPower(global::Player p2)
        {
            if (_hudUpdateGuardianPower == null) return;

            var paramCount = _hudUpdateGuardianPower.GetParameters().Length;

            if (_cloneHud != null && _cloneHud.m_gpRoot != null)
            {
                SplitscreenLog.ExecuteAsPlayer(p2, () =>
                {
                    try
                    {
                        var args = paramCount == 1 ? new object[] { p2 } : new object[0];
                        _hudUpdateGuardianPower.Invoke(_cloneHud, args);
                    }
                    catch { }
                });
                return;
            }

            RunWithP2HudFields(() =>
            {
                SplitscreenLog.ExecuteAsPlayer(p2, () =>
                {
                    try
                    {
                        var args = paramCount == 1 ? new object[] { p2 } : new object[0];
                        _hudUpdateGuardianPower.Invoke(global::Hud.instance, args);
                    }
                    catch { }
                });
            });
        }

        // =====================================================================
        // P2 CROSSHAIR
        // =====================================================================

        private static readonly System.Reflection.MethodInfo _hudUpdateCrosshair =
            typeof(global::Hud).GetMethod("UpdateCrosshair", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        private void UpdateCrosshair(global::Player p2)
        {
            if (_hudUpdateCrosshair == null) return;

            var paramInfos = _hudUpdateCrosshair.GetParameters();

            // Build args based on actual method signature
            object[] BuildArgs()
            {
                if (paramInfos.Length == 0) return new object[0];
                if (paramInfos.Length == 1) return new object[] { p2 };
                // Most likely (Player, ItemDrop.ItemData)
                return new object[] { p2, p2.GetCurrentWeapon() };
            }

            if (_cloneHud != null && _cloneHud.m_crosshair != null)
            {
                SplitscreenLog.ExecuteAsPlayer(p2, () =>
                {
                    try { _hudUpdateCrosshair.Invoke(_cloneHud, BuildArgs()); }
                    catch { }
                });
                return;
            }

            RunWithP2HudFields(() =>
            {
                SplitscreenLog.ExecuteAsPlayer(p2, () =>
                {
                    try { _hudUpdateCrosshair.Invoke(global::Hud.instance, BuildArgs()); }
                    catch { }
                });
            });
        }

        // =====================================================================
        // HUD FIELD SWAP CACHE
        // =====================================================================

        /// <summary>
        /// Discovers ALL Hud fields that reference UI elements within the hudroot hierarchy
        /// and maps them to equivalent P2 clone elements by relative path.
        /// </summary>
        private void CacheHudSwapFields(global::Hud hud)
        {
            var p1Root = hud.m_rootObject?.transform;
            if (p1Root == null)
            {
                Debug.LogWarning("[Splitscreen][P2HUD] CacheHudSwapFields: m_rootObject is null");
                return;
            }

            Transform p2Root = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name.Contains("HudRoot"))
                {
                    p2Root = child;
                    break;
                }
            }
            if (p2Root == null)
            {
                Debug.LogWarning("[Splitscreen][P2HUD] CacheHudSwapFields: could not find HudRoot_P2");
                return;
            }

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var allFields = typeof(global::Hud).GetFields(flags);

            var fieldList = new List<System.Reflection.FieldInfo>();
            var p2ValueList = new List<object>();
            int matched = 0, skipped = 0;

            foreach (var field in allFields)
            {
                object p1Value = field.GetValue(hud);
                if (p1Value == null) { continue; }

                object p2Value = FindP2Equivalent(p1Value, p1Root, p2Root);
                if (p2Value != null)
                {
                    fieldList.Add(field);
                    p2ValueList.Add(p2Value);
                    matched++;
                }
                else
                {
                    // Only log UI-type fields that failed to match (skip primitives/colors)
                    if (p1Value is Component || p1Value is GameObject || p1Value is System.Array)
                        skipped++;
                }
            }

            _hudSwapFields = fieldList.ToArray();
            _p2SwapValues = p2ValueList.ToArray();
            Debug.Log($"[Splitscreen][P2HUD] Hud field swap cached: {matched} matched, {skipped} UI fields skipped");

            // Log matched fields for diagnostics
            for (int i = 0; i < _hudSwapFields.Length; i++)
                Debug.Log($"[Splitscreen][P2HUD]   Swappable: {_hudSwapFields[i].Name} ({_hudSwapFields[i].FieldType.Name})");
        }

        /// <summary>
        /// Given a P1 element (Component or GameObject), find the equivalent in P2's clone
        /// by computing the relative path from P1's root and looking it up in P2's root.
        /// </summary>
        private object FindP2Equivalent(object p1Value, Transform p1Root, Transform p2Root)
        {
            if (p1Value is Component comp)
            {
                string relPath = GetRelativePath(p1Root, comp.transform);
                if (relPath == null) return null;
                var p2Transform = FindDeepChild(p2Root, relPath);
                if (p2Transform == null) return null;
                return p2Transform.GetComponent(comp.GetType());
            }
            if (p1Value is GameObject go)
            {
                string relPath = GetRelativePath(p1Root, go.transform);
                if (relPath == null) return null;
                var p2Transform = FindDeepChild(p2Root, relPath);
                return p2Transform?.gameObject;
            }
            // For arrays, swap each element
            if (p1Value is System.Array arr && arr.Length > 0)
            {
                var elemType = p1Value.GetType().GetElementType();
                var p2Arr = System.Array.CreateInstance(elemType, arr.Length);
                bool anyMatch = false;
                for (int i = 0; i < arr.Length; i++)
                {
                    var elem = arr.GetValue(i);
                    if (elem != null)
                    {
                        var p2Elem = FindP2Equivalent(elem, p1Root, p2Root);
                        if (p2Elem != null) { p2Arr.SetValue(p2Elem, i); anyMatch = true; }
                    }
                }
                return anyMatch ? p2Arr : null;
            }
            return null;
        }

        private string GetRelativePath(Transform root, Transform child)
        {
            if (child == root) return "";
            var parts = new List<string>();
            Transform current = child;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            if (current != root) return null;
            parts.Reverse();
            return string.Join("/", parts);
        }

        private Image FindImageByPath(params string[] paths)
        {
            foreach (var path in paths)
            {
                var t = FindDeepChild(transform, path);
                if (t != null)
                {
                    var img = t.GetComponent<Image>();
                    if (img != null) return img;
                }
            }
            return null;
        }

        private Text FindTextByPath(params string[] paths)
        {
            foreach (var path in paths)
            {
                var t = FindDeepChild(transform, path);
                if (t != null)
                {
                    var txt = t.GetComponent<Text>();
                    if (txt != null) return txt;
                }
            }
            return null;
        }

        private static RawImage FindRawImageByNameToken(Transform root, string token)
        {
            if (root == null) return null;
            token = token.ToLowerInvariant();
            var images = root.GetComponentsInChildren<RawImage>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i].gameObject.name.ToLowerInvariant().Contains(token))
                {
                    return images[i];
                }
            }
            return images.Length > 0 ? images[0] : null;
        }

        private static TMP_Text FindTmpTextByNameToken(Transform root, string token)
        {
            if (root == null) return null;
            token = token.ToLowerInvariant();
            var texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].gameObject.name.ToLowerInvariant().Contains(token))
                {
                    return texts[i];
                }
            }
            return null;
        }

        private static TMP_Text FindTmpTextByName(Transform root, string name)
        {
            if (root == null) return null;
            var texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].gameObject.name == name)
                {
                    return texts[i];
                }
            }
            return null;
        }

        private static RectTransform FindRectTransformByName(Transform root, string name)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].gameObject.name == name)
                {
                    return all[i];
                }
            }
            return null;
        }

        private Transform FindDeepChild(Transform parent, string path)
        {
            var result = parent.Find(path);
            if (result != null) return result;

            string[] parts = path.Split('/');
            if (parts.Length == 0) return null;

            return FindDeepChildRecursive(parent, parts, 0);
        }

        private Transform FindDeepChildRecursive(Transform current, string[] pathParts, int partIndex)
        {
            if (partIndex >= pathParts.Length) return current;

            string targetName = pathParts[partIndex];

            for (int i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                if (child.gameObject.name == targetName)
                {
                    var found = FindDeepChildRecursive(child, pathParts, partIndex + 1);
                    if (found != null) return found;
                }

                if (partIndex == 0)
                {
                    var found = FindDeepChildRecursive(child, pathParts, 0);
                    if (found != null) return found;
                }
            }

            return null;
        }
    }
}
