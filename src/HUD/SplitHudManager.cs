using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.Config;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.HUD
{
    /// <summary>
    /// Manages HUD duplication for splitscreen.
    /// Clones the vanilla HUD canvas for Player 2, disables all scripts on the clone,
    /// and adds a Player2HudUpdater to keep P2's HUD elements updated from P2's data.
    /// The clone uses ScreenSpaceCamera mode targeting P2's camera so it renders into P2's RT.
    /// </summary>
    public class SplitHudManager : MonoBehaviour
    {
        public static SplitHudManager Instance { get; private set; }

        private GameObject _p2HudClone;
        private Canvas _p2Canvas;
        private Player2HudUpdater _p2Updater;

        private void Awake()
        {
            Instance = this;
            Debug.Log("[Splitscreen][P2HUD] SplitHudManager.Awake");
        }

        public void OnSplitscreenActivated()
        {
            Debug.Log("[Splitscreen][P2HUD] OnSplitscreenActivated - scheduling P2 HUD creation (0.5s delay)");
            Invoke("CreatePlayer2Hud", 0.5f);
        }

        public void OnSplitscreenDeactivated()
        {
            Debug.Log("[Splitscreen][P2HUD] OnSplitscreenDeactivated");
            CancelInvoke("CreatePlayer2Hud");
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

            if (Hud.instance == null)
            {
                Debug.LogWarning("[Splitscreen][P2HUD] Hud.instance is null, can't clone!");
                return;
            }
            Debug.Log($"[Splitscreen][P2HUD] Hud.instance found, m_rootObject={Hud.instance.m_rootObject?.name}");

            var p2Camera = SplitCameraManager.Instance?.Player2Camera;
            if (p2Camera == null)
            {
                Debug.LogWarning("[Splitscreen][P2HUD] P2 camera not available yet!");
                return;
            }
            Debug.Log($"[Splitscreen][P2HUD] P2 camera available: {p2Camera.name}, targetTexture={p2Camera.targetTexture?.name}");

            // Find the root canvas of the vanilla HUD
            var sourceCanvas = Hud.instance.m_rootObject?.GetComponentInParent<Canvas>();
            if (sourceCanvas == null)
            {
                Debug.LogWarning("[Splitscreen][P2HUD] Could not find HUD canvas via GetComponentInParent<Canvas>!");
                return;
            }
            Debug.Log($"[Splitscreen][P2HUD] Source canvas: '{sourceCanvas.gameObject.name}', renderMode={sourceCanvas.renderMode}, worldCamera={sourceCanvas.worldCamera?.name}");
            Debug.Log($"[Splitscreen][P2HUD] Source canvas childCount={sourceCanvas.transform.childCount}");

            // Clone the entire HUD hierarchy
            Debug.Log("[Splitscreen][P2HUD] Instantiating clone...");
            _p2HudClone = Instantiate(sourceCanvas.gameObject);
            _p2HudClone.name = "SplitscreenHUD_P2";
            Debug.Log($"[Splitscreen][P2HUD] Clone created: '{_p2HudClone.name}', childCount={_p2HudClone.transform.childCount}");

            // Disable ALL MonoBehaviours on the clone to prevent singleton conflicts
            var scripts = _p2HudClone.GetComponentsInChildren<MonoBehaviour>(true);
            int disabledCount = 0;
            int skippedCount = 0;
            foreach (var script in scripts)
            {
                if (script is CanvasScaler || script is GraphicRaycaster ||
                    script is LayoutGroup || script is ContentSizeFitter)
                {
                    skippedCount++;
                    continue;
                }
                script.enabled = false;
                disabledCount++;
            }
            Debug.Log($"[Splitscreen][P2HUD] Disabled {disabledCount} scripts, kept {skippedCount} layout/scaler scripts");

            // Set up the clone's canvas
            _p2Canvas = _p2HudClone.GetComponent<Canvas>();
            if (_p2Canvas != null)
            {
                Debug.Log($"[Splitscreen][P2HUD] Clone canvas BEFORE: renderMode={_p2Canvas.renderMode}, worldCamera={_p2Canvas.worldCamera?.name}");
                _p2Canvas.renderMode = RenderMode.ScreenSpaceCamera;
                _p2Canvas.worldCamera = p2Camera;
                _p2Canvas.planeDistance = 1f;
                _p2Canvas.sortingOrder = 0;
                Debug.Log($"[Splitscreen][P2HUD] Clone canvas AFTER: renderMode={_p2Canvas.renderMode}, worldCamera={_p2Canvas.worldCamera?.name}, planeDistance={_p2Canvas.planeDistance}");
            }
            else
            {
                Debug.LogError("[Splitscreen][P2HUD] Clone has no Canvas component!");
            }

            // Disable minimap on clone
            Debug.Log("[Splitscreen][P2HUD] Disabling minimap on clone...");
            DisableMinimapOnClone();

            // Add updater
            Debug.Log("[Splitscreen][P2HUD] Adding Player2HudUpdater...");
            _p2Updater = _p2HudClone.AddComponent<Player2HudUpdater>();

            Debug.Log("[Splitscreen][P2HUD] === CreatePlayer2Hud END (SUCCESS) ===");
        }

        private void DisableMinimapOnClone()
        {
            var transforms = _p2HudClone.GetComponentsInChildren<Transform>(true);
            int disabledMinimap = 0;
            foreach (var t in transforms)
            {
                string name = t.gameObject.name.ToLowerInvariant();
                if (name.Contains("minimap") || name.Contains("MiniMap"))
                {
                    t.gameObject.SetActive(false);
                    disabledMinimap++;
                    Debug.Log($"[Splitscreen][P2HUD] Disabled minimap element: '{t.gameObject.name}'");
                }
            }
            Debug.Log($"[Splitscreen][P2HUD] Disabled {disabledMinimap} minimap elements");
        }

        private void DestroyPlayer2Hud()
        {
            if (_p2HudClone != null)
            {
                Debug.Log("[Splitscreen][P2HUD] Destroying P2 HUD clone...");
                Destroy(_p2HudClone);
                _p2HudClone = null;
                _p2Canvas = null;
                _p2Updater = null;
                Debug.Log("[Splitscreen][P2HUD] P2 HUD clone destroyed");
            }
            else
            {
                Debug.Log("[Splitscreen][P2HUD] No P2 HUD clone to destroy");
            }
        }

        private void OnDestroy()
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Updates the cloned P2 HUD elements each frame to reflect Player 2's actual data.
    /// </summary>
    public class Player2HudUpdater : MonoBehaviour
    {
        private Image _healthBarFill;
        private Image _healthBarSlow;
        private Text _healthText;
        private Image _staminaBarFill;
        private Image _staminaBarSlow;
        private Text _staminaText;
        private Image _eitrBarFill;
        private Image _eitrBarSlow;
        private Transform _hotbarRoot;

        private bool _initialized;
        private float _lastLogTime;
        private float _lastSearchTime;
        private float _lastUpdateLogTime;
        private int _updateCount;

        private void Awake()
        {
            Debug.Log("[Splitscreen][P2HUD] Player2HudUpdater.Awake");
        }

        private void Update()
        {
            if (SplitScreenManager.Instance == null || !SplitScreenManager.Instance.SplitscreenActive) return;

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

            _updateCount++;
            UpdateHealthBar(p2);
            UpdateStaminaBar(p2);
            UpdateEitrBar(p2);

            // Periodic update logging
            if (Time.time - _lastUpdateLogTime > 15f)
            {
                _lastUpdateLogTime = Time.time;
                Debug.Log($"[Splitscreen][P2HUD] Update #{_updateCount}: P2 HP={p2.GetHealth():F0}/{p2.GetMaxHealth():F0}, SP={p2.GetStamina():F0}/{p2.GetMaxStamina():F0}");
                Debug.Log($"[Splitscreen][P2HUD]   healthFill={_healthBarFill?.fillAmount:F2}, staminaFill={_staminaBarFill?.fillAmount:F2}");
            }
        }

        private void FindUIElements()
        {
            Debug.Log("[Splitscreen][P2HUD] === FindUIElements START ===");
            Debug.Log($"[Splitscreen][P2HUD] Searching in transform: '{transform.name}', childCount={transform.childCount}");

            // Log top-level children
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                Debug.Log($"[Splitscreen][P2HUD]   Child[{i}]: '{child.gameObject.name}' active={child.gameObject.activeSelf}, childCount={child.childCount}");
            }

            // Search for health bar
            _healthBarFill = FindImageByPath("healthpanel/Health/health_fast", "darken/Health/health_fast", "Health/health_fast");
            _healthBarSlow = FindImageByPath("healthpanel/Health/health_slow", "darken/Health/health_slow", "Health/health_slow");
            _healthText = FindTextByPath("healthpanel/Health/HealthText", "darken/Health/HealthText", "Health/HealthText");
            Debug.Log($"[Splitscreen][P2HUD] Path search: healthFill={_healthBarFill != null}, healthSlow={_healthBarSlow != null}, healthText={_healthText != null}");

            // Search for stamina bar
            _staminaBarFill = FindImageByPath("healthpanel/staminapanel/Stamina/stamina_fast", "darken/Stamina/stamina_fast", "Stamina/stamina_fast", "staminapanel/Stamina/stamina_fast");
            _staminaBarSlow = FindImageByPath("healthpanel/staminapanel/Stamina/stamina_slow", "darken/Stamina/stamina_slow", "Stamina/stamina_slow", "staminapanel/Stamina/stamina_slow");
            _staminaText = FindTextByPath("healthpanel/staminapanel/StaminaText", "darken/StaminaText", "StaminaText", "staminapanel/StaminaText");
            Debug.Log($"[Splitscreen][P2HUD] Path search: staminaFill={_staminaBarFill != null}, staminaSlow={_staminaBarSlow != null}, staminaText={_staminaText != null}");

            // Search for eitr bar
            _eitrBarFill = FindImageByPath("healthpanel/eitrpanel/Eitr/eitr_fast", "darken/Eitr/eitr_fast", "Eitr/eitr_fast", "eitrpanel/Eitr/eitr_fast");
            _eitrBarSlow = FindImageByPath("healthpanel/eitrpanel/Eitr/eitr_slow", "darken/Eitr/eitr_slow", "Eitr/eitr_slow", "eitrpanel/Eitr/eitr_slow");
            Debug.Log($"[Splitscreen][P2HUD] Path search: eitrFill={_eitrBarFill != null}, eitrSlow={_eitrBarSlow != null}");

            // Search for hotbar
            _hotbarRoot = FindTransformByName("HotKeyBar");
            Debug.Log($"[Splitscreen][P2HUD] Path search: hotbar={_hotbarRoot != null}");

            if (_healthBarFill != null)
            {
                _initialized = true;
                Debug.Log("[Splitscreen][P2HUD] === FindUIElements END (SUCCESS via path search) ===");
            }
            else
            {
                Debug.Log("[Splitscreen][P2HUD] Path search failed, trying broad search...");
                TryBroadSearch();
            }
        }

        private void TryBroadSearch()
        {
            var images = GetComponentsInChildren<Image>(true);
            Debug.Log($"[Splitscreen][P2HUD] Broad search: found {images.Length} Image components in clone");
            foreach (var img in images)
            {
                string name = img.gameObject.name.ToLowerInvariant();
                if (name.Contains("health") && name.Contains("fast") && _healthBarFill == null)
                {
                    _healthBarFill = img;
                    Debug.Log($"[Splitscreen][P2HUD] FOUND health fill: '{img.gameObject.name}', fillAmount={img.fillAmount:F2}, type={img.type}");
                }
                else if (name.Contains("health") && name.Contains("slow") && _healthBarSlow == null)
                {
                    _healthBarSlow = img;
                    Debug.Log($"[Splitscreen][P2HUD] FOUND health slow: '{img.gameObject.name}'");
                }
                else if (name.Contains("stamina") && name.Contains("fast") && _staminaBarFill == null)
                {
                    _staminaBarFill = img;
                    Debug.Log($"[Splitscreen][P2HUD] FOUND stamina fill: '{img.gameObject.name}', fillAmount={img.fillAmount:F2}");
                }
                else if (name.Contains("stamina") && name.Contains("slow") && _staminaBarSlow == null)
                {
                    _staminaBarSlow = img;
                    Debug.Log($"[Splitscreen][P2HUD] FOUND stamina slow: '{img.gameObject.name}'");
                }
                else if (name.Contains("eitr") && name.Contains("fast") && _eitrBarFill == null)
                {
                    _eitrBarFill = img;
                    Debug.Log($"[Splitscreen][P2HUD] FOUND eitr fill: '{img.gameObject.name}'");
                }
                else if (name.Contains("eitr") && name.Contains("slow") && _eitrBarSlow == null)
                {
                    _eitrBarSlow = img;
                    Debug.Log($"[Splitscreen][P2HUD] FOUND eitr slow: '{img.gameObject.name}'");
                }
            }

            var texts = GetComponentsInChildren<Text>(true);
            Debug.Log($"[Splitscreen][P2HUD] Broad search: found {texts.Length} Text components in clone");
            foreach (var txt in texts)
            {
                string name = txt.gameObject.name.ToLowerInvariant();
                if (name.Contains("health") && name.Contains("text") && _healthText == null)
                {
                    _healthText = txt;
                    Debug.Log($"[Splitscreen][P2HUD] FOUND health text: '{txt.gameObject.name}', text='{txt.text}'");
                }
                else if (name.Contains("stamina") && name.Contains("text") && _staminaText == null)
                {
                    _staminaText = txt;
                    Debug.Log($"[Splitscreen][P2HUD] FOUND stamina text: '{txt.gameObject.name}', text='{txt.text}'");
                }
            }

            if (_healthBarFill != null)
            {
                _initialized = true;
                Debug.Log("[Splitscreen][P2HUD] === FindUIElements END (SUCCESS via broad search) ===");
            }
            else
            {
                Debug.LogWarning("[Splitscreen][P2HUD] === FindUIElements END (FAILED - no health bar found!) ===");
                Debug.Log("[Splitscreen][P2HUD] Dumping hierarchy (depth 4)...");
                if (Time.time - _lastLogTime > 10f)
                {
                    _lastLogTime = Time.time;
                    LogHierarchy(transform, 0, 4);
                }
            }
        }

        private void LogHierarchy(Transform t, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            string indent = new string(' ', depth * 2);
            var img = t.GetComponent<Image>();
            string imgInfo = img != null ? $" [Image type={img.type}, fill={img.fillAmount:F2}]" : "";
            var txt = t.GetComponent<Text>();
            string txtInfo = txt != null ? $" [Text='{txt.text}']" : "";
            Debug.Log($"[Splitscreen][P2HUD] {indent}{t.gameObject.name}{imgInfo}{txtInfo} active={t.gameObject.activeSelf}");
            for (int i = 0; i < t.childCount; i++)
            {
                LogHierarchy(t.GetChild(i), depth + 1, maxDepth);
            }
        }

        private void UpdateHealthBar(global::Player p2)
        {
            float health = p2.GetHealth();
            float maxHealth = p2.GetMaxHealth();
            float pct = maxHealth > 0 ? health / maxHealth : 0f;

            if (_healthBarFill != null) _healthBarFill.fillAmount = pct;
            if (_healthBarSlow != null) _healthBarSlow.fillAmount = Mathf.MoveTowards(_healthBarSlow.fillAmount, pct, Time.deltaTime * 0.5f);
            if (_healthText != null) _healthText.text = Mathf.CeilToInt(health).ToString();
        }

        private void UpdateStaminaBar(global::Player p2)
        {
            float stamina = p2.GetStamina();
            float maxStamina = p2.GetMaxStamina();
            float pct = maxStamina > 0 ? stamina / maxStamina : 0f;

            if (_staminaBarFill != null) _staminaBarFill.fillAmount = pct;
            if (_staminaBarSlow != null) _staminaBarSlow.fillAmount = Mathf.MoveTowards(_staminaBarSlow.fillAmount, pct, Time.deltaTime * 0.5f);
            if (_staminaText != null) _staminaText.text = Mathf.CeilToInt(stamina).ToString();
        }

        private void UpdateEitrBar(global::Player p2)
        {
            float maxEitr = p2.GetMaxEitr();
            if (maxEitr <= 0) return;

            float eitr = p2.GetEitr();
            float pct = eitr / maxEitr;

            if (_eitrBarFill != null) _eitrBarFill.fillAmount = pct;
            if (_eitrBarSlow != null) _eitrBarSlow.fillAmount = Mathf.MoveTowards(_eitrBarSlow.fillAmount, pct, Time.deltaTime * 0.5f);
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

        private Transform FindTransformByName(string name)
        {
            var all = GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t.gameObject.name == name) return t;
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
