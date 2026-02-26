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
            if (_p2HudClone != null)
            {
                return;
            }

            if (Hud.instance == null || Hud.instance.m_rootObject == null)
            {
                Debug.LogWarning("[Splitscreen][P2HUD] Hud.instance or m_rootObject is null");
                return;
            }

            var p2Camera = SplitCameraManager.Instance?.Player2UiCamera;
            if (p2Camera == null)
            {
                Debug.LogWarning("[Splitscreen][P2HUD] P2 UI camera unavailable");
                return;
            }

            var sourceCanvas = Hud.instance.m_rootObject.GetComponentInParent<Canvas>();
            if (sourceCanvas == null)
            {
                Debug.LogWarning("[Splitscreen][P2HUD] Source HUD canvas not found");
                return;
            }

            _p2HudClone = new GameObject("SplitscreenHUD_P2",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            var srcRect = sourceCanvas.GetComponent<RectTransform>();
            var dstRect = _p2HudClone.GetComponent<RectTransform>();
            if (srcRect != null)
            {
                dstRect.SetParent(srcRect.parent, false);
                dstRect.anchorMin = srcRect.anchorMin;
                dstRect.anchorMax = srcRect.anchorMax;
                dstRect.pivot = srcRect.pivot;
                dstRect.anchoredPosition = srcRect.anchoredPosition;
                dstRect.sizeDelta = srcRect.sizeDelta;
                dstRect.localScale = srcRect.localScale;
                dstRect.localRotation = srcRect.localRotation;
            }
            else
            {
                dstRect.SetParent(sourceCanvas.transform.parent, false);
                dstRect.anchorMin = Vector2.zero;
                dstRect.anchorMax = Vector2.one;
                dstRect.pivot = new Vector2(0.5f, 0.5f);
                dstRect.anchoredPosition = Vector2.zero;
                dstRect.sizeDelta = Vector2.zero;
            }

            _p2Canvas = _p2HudClone.GetComponent<Canvas>();
            ConfigureCanvasForP2(_p2Canvas, sourceCanvas, p2Camera);
            CopyCanvasScalerSettings(sourceCanvas.GetComponent<CanvasScaler>(), _p2HudClone.GetComponent<CanvasScaler>());

            var hudRootClone = Instantiate(Hud.instance.m_rootObject, _p2HudClone.transform, false);
            hudRootClone.name = "HudRoot_P2";

            _p2MinimapSmallClone = TryCloneMinimapSmallRoot(_p2HudClone.transform);

            SetLayerRecursively(_p2HudClone, SplitCameraManager.Player2HudLayer);
            ConfigureAllCloneCanvases(_p2HudClone, p2Camera);
            DisableNonRenderingScripts(_p2HudClone);

            // Force cloned HotkeyBars to clear P1's cached data so they refresh with P2's inventory
            ClearClonedHotkeyBarData(_p2HudClone);

            _p2Updater = _p2HudClone.AddComponent<Player2HudUpdater>();
            _p2Updater.ConfigureMinimapMirror(Minimap.instance, _p2MinimapSmallClone);
            Debug.Log("[Splitscreen][P2HUD] Player 2 HUD created");
        }

        private static void ClearClonedHotkeyBarData(GameObject root)
        {
            var bars = root.GetComponentsInChildren<HotkeyBar>(true);
            foreach (var bar in bars)
            {
                // Destroy existing icon children (they show P1's stale items)
                for (int i = bar.transform.childCount - 1; i >= 0; i--)
                    Destroy(bar.transform.GetChild(i).gameObject);
                // Clear internal item list via reflection (field may be private)
                var itemsField = typeof(HotkeyBar).GetField("m_items",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (itemsField != null)
                {
                    var list = itemsField.GetValue(bar) as System.Collections.IList;
                    list?.Clear();
                }
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
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = p2Camera;
                canvas.planeDistance = 1f;
                canvas.sortingOrder = 0;
            }
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
                if (ShouldKeepScriptEnabled(script)) continue;
                script.enabled = false;
                disabled++;
            }
            Debug.Log($"[Splitscreen][P2HUD] Disabled {disabled} non-render scripts on P2 clone");
        }

        private static bool ShouldKeepScriptEnabled(MonoBehaviour script)
        {
            return script is CanvasScaler
                || script is GraphicRaycaster
                || script is LayoutGroup
                || script is ContentSizeFitter
                || script is Graphic
                || script is Mask
                || script is RectMask2D
                || script is TMP_Text
                || script is HotkeyBar
                || script is Player2HudUpdater;
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
        private Image _staminaBarFill;
        private Image _staminaBarSlow;
        private Text _staminaText;
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
        private const float StatusFoodUpdateInterval = 0.5f;

        private void Awake()
        {
            Instance = this;
            _cloneHud = GetComponent<global::Hud>();
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
                Debug.Log($"[Splitscreen][P2HUD] Initialized after {_searchAttempts} attempts: health={_healthBarFill != null}, hotbars={_hotkeyBars?.Length ?? 0}");
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
        }

        private void UpdateHealthBar(global::Player p2)
        {
            float health = p2.GetHealth();
            float maxHealth = p2.GetMaxHealth();
            float pct = maxHealth > 0f ? health / maxHealth : 0f;

            if (_healthBarFill != null) _healthBarFill.fillAmount = pct;
            if (_healthBarSlow != null) _healthBarSlow.fillAmount = Mathf.MoveTowards(_healthBarSlow.fillAmount, pct, Time.deltaTime * 0.5f);
            if (_healthText != null) _healthText.text = Mathf.CeilToInt(health).ToString();
        }

        private void UpdateStaminaBar(global::Player p2)
        {
            float stamina = p2.GetStamina();
            float maxStamina = p2.GetMaxStamina();
            float pct = maxStamina > 0f ? stamina / maxStamina : 0f;

            if (_staminaBarFill != null) _staminaBarFill.fillAmount = pct;
            if (_staminaBarSlow != null) _staminaBarSlow.fillAmount = Mathf.MoveTowards(_staminaBarSlow.fillAmount, pct, Time.deltaTime * 0.5f);
            if (_staminaText != null) _staminaText.text = Mathf.CeilToInt(stamina).ToString();
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
                _cloneHud = GetComponent<global::Hud>();
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

            // Keep status effects, food bar, and crosshair/hover ENABLED for P2
            // They are updated each frame in UpdateStatusEffects, UpdateFoodIcons, UpdateHoverText
            if (_cloneHud.m_gpRoot != null) SafeSetActive(_cloneHud.m_gpRoot.gameObject, false);
            if (_cloneHud.m_loadingScreen != null) SafeSetActive(_cloneHud.m_loadingScreen.gameObject, false);
            if (_cloneHud.m_crosshairBow != null) SafeSetActive(_cloneHud.m_crosshairBow.gameObject, false);
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
                    name.Contains("hotkey") || name.Contains("minimap"))
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
            if (_cloneHud == null) return;
            if (_cloneHud.m_statusEffectListRoot == null) return;
            if (_hudUpdateStatusEffects == null) return;

            // Call Hud's status effect update with m_localPlayer swapped to P2
            SplitscreenLog.ExecuteAsPlayer(p2, () =>
            {
                try
                {
                    var seEffects = p2.GetSEMan().GetStatusEffects();
                    _hudUpdateStatusEffects.Invoke(_cloneHud, new object[] { seEffects });
                }
                catch
                {
                    // Keep robust — signature may differ across Valheim versions
                }
            });
        }

        // =====================================================================
        // P2 FOOD ICONS
        // =====================================================================

        private static readonly System.Reflection.MethodInfo _hudUpdateFood =
            typeof(global::Hud).GetMethod("UpdateFood", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        private void UpdateFoodIcons(global::Player p2)
        {
            if (_cloneHud == null) return;
            if (_cloneHud.m_foodBarRoot == null) return;
            if (_hudUpdateFood == null) return;

            // Call Hud's food update with m_localPlayer set to P2
            SplitscreenLog.ExecuteAsPlayer(p2, () =>
            {
                try
                {
                    _hudUpdateFood.Invoke(_cloneHud, new object[] { p2 });
                }
                catch
                {
                    // Keep robust — signature may differ across Valheim versions
                }
            });
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
