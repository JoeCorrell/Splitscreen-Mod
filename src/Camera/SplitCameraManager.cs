using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimSplitscreen.Config;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.Input;

namespace ValheimSplitscreen.Camera
{
    /// <summary>
    /// Manages dual-camera rendering for splitscreen using RenderTextures.
    /// Each player's cameras (sky + main) render to their own RT with a dedicated depth buffer,
    /// eliminating dimension mismatch errors from shared depth buffers.
    /// A compositor camera then blits both RTs to the screen.
    /// </summary>
    public class SplitCameraManager : MonoBehaviour
    {
        public static int Player2HudLayer { get; private set; } = 31;

        public static SplitCameraManager Instance { get; private set; }

        // Per-player RenderTextures with their own depth buffers
        private RenderTexture _p1RT;
        private RenderTexture _p2RT;

        // Player 2's camera objects
        private GameObject _p2CameraObj;
        private UnityEngine.Camera _p2Camera;
        private UnityEngine.Camera _p2SkyCamera;

        // UI-only cameras (render HUD without world post-processing)
        private GameObject _p1UiCameraObj;
        private UnityEngine.Camera _p1UiCamera;
        private GameObject _p2UiCameraObj;
        private UnityEngine.Camera _p2UiCamera;

        // Compositor camera that blits RTs to screen
        private GameObject _compositorObj;
        private UnityEngine.Camera _compositorCamera;
        private SplitscreenCompositor _compositor;

        // Camera state for Player 2 (mirrors GameCamera logic)
        private float _p2Distance = 4f;
        private Vector3 _p2PlayerPos;
        private Vector3 _p2CurrentBaseOffset;
        private Vector3 _p2OffsetBaseVel;
        private Vector3 _p2PlayerVel;
        private Vector3 _p2SmoothedCameraUp = Vector3.up;
        private Vector3 _p2SmoothedCameraUpVel;

        // Cached reference to the original camera
        private UnityEngine.Camera _originalCamera;
        private Rect _originalViewport;
        private DepthTextureMode _savedDepthTextureMode;
        private DepthTextureMode _savedSkyDepthTextureMode;
        private RenderTexture _savedTargetTexture;
        private int _savedMainCullingMask;
        private int _savedSkyCullingMask = -1;
        private int _uiLayerMask;

        // Logging rate limiter
        private float _lastCamLogTime;

        public UnityEngine.Camera Player2Camera => _p2Camera;
        public UnityEngine.Camera OriginalCamera => _originalCamera;
        public UnityEngine.Camera Player1UiCamera => _p1UiCamera != null ? _p1UiCamera : _originalCamera;
        public UnityEngine.Camera Player2UiCamera => _p2UiCamera != null ? _p2UiCamera : _p2Camera;
        public RenderTexture P1RenderTexture => _p1RT;
        public RenderTexture P2RenderTexture => _p2RT;

        private void Awake()
        {
            Instance = this;
            Debug.Log("[Splitscreen][Camera] SplitCameraManager.Awake");
        }

        public void OnSplitscreenActivated()
        {
            Debug.Log("[Splitscreen][Camera] === OnSplitscreenActivated START ===");

            if (GameCamera.instance == null)
            {
                Debug.LogError("[Splitscreen][Camera] GameCamera.instance is NULL! Cannot set up cameras.");
                return;
            }
            Debug.Log($"[Splitscreen][Camera] GameCamera.instance found: {GameCamera.instance.gameObject.name}");

            _originalCamera = GameCamera.instance.GetComponent<UnityEngine.Camera>();
            if (_originalCamera == null)
            {
                Debug.LogError("[Splitscreen][Camera] Could not get Camera component from GameCamera!");
                return;
            }

            _originalViewport = _originalCamera.rect;
            _savedTargetTexture = _originalCamera.targetTexture;
            _savedDepthTextureMode = _originalCamera.depthTextureMode;
            _savedMainCullingMask = _originalCamera.cullingMask;
            _uiLayerMask = LayerMask.GetMask("UI", "GUI");
            if (_uiLayerMask == 0)
            {
                // Fallback to Unity's default UI layer index.
                _uiLayerMask = 1 << 5;
            }
            if (Hud.instance?.m_rootObject != null)
            {
                _uiLayerMask |= 1 << Hud.instance.m_rootObject.layer;
            }
            ResolvePlayer2HudLayer();

            Debug.Log($"[Splitscreen][Camera] Original camera state:");
            Debug.Log($"[Splitscreen][Camera]   name={_originalCamera.gameObject.name}");
            Debug.Log($"[Splitscreen][Camera]   depth={_originalCamera.depth}");
            Debug.Log($"[Splitscreen][Camera]   rect={_originalCamera.rect}");
            Debug.Log($"[Splitscreen][Camera]   fov={_originalCamera.fieldOfView}");
            Debug.Log($"[Splitscreen][Camera]   depthTexMode={_originalCamera.depthTextureMode}");
            Debug.Log($"[Splitscreen][Camera]   targetTexture={(_originalCamera.targetTexture != null ? _originalCamera.targetTexture.name : "null")}");
            Debug.Log($"[Splitscreen][Camera]   clearFlags={_originalCamera.clearFlags}");
            Debug.Log($"[Splitscreen][Camera]   cullingMask={_originalCamera.cullingMask}");
            Debug.Log($"[Splitscreen][Camera]   enabled={_originalCamera.enabled}");

            var config = SplitscreenPlugin.Instance.SplitConfig;
            bool horizontal = config.Orientation.Value == SplitOrientation.Horizontal;
            Debug.Log($"[Splitscreen][Camera] Orientation: {(horizontal ? "Horizontal (top/bottom)" : "Vertical (left/right)")}");
            Debug.Log($"[Splitscreen][Camera] Screen: {Screen.width}x{Screen.height}");

            // Step A: Create RenderTextures
            Debug.Log("[Splitscreen][Camera] Step A: Creating RenderTextures...");
            CreateRenderTextures(horizontal);
            Debug.Log($"[Splitscreen][Camera] Step A DONE: _p1RT={_p1RT != null}, _p2RT={_p2RT != null}");

            // Step B: Set up P1 cameras to render to P1's RT
            Debug.Log("[Splitscreen][Camera] Step B: Setting up P1 cameras...");
            SetupPlayer1Cameras();
            Debug.Log("[Splitscreen][Camera] Step B DONE");

            // Step C: Create Player 2 cameras rendering to P2's RT
            Debug.Log("[Splitscreen][Camera] Step C: Creating P2 cameras...");
            CreatePlayer2Camera();
            Debug.Log($"[Splitscreen][Camera] Step C DONE: _p2Camera={_p2Camera != null}, _p2SkyCamera={_p2SkyCamera != null}");

            // Step C2: Copy post-processing components to P2 camera (for AO, bloom, etc.)
            Debug.Log("[Splitscreen][Camera] Step C2: Copying post-processing to P2...");
            CopyPostProcessingToP2();
            Debug.Log("[Splitscreen][Camera] Step C2 DONE");

            // Step D: Create UI-only cameras for HUD rendering
            Debug.Log("[Splitscreen][Camera] Step D: Creating UI cameras...");
            CreateUICameras();
            Debug.Log($"[Splitscreen][Camera] Step D DONE: _p1UiCamera={_p1UiCamera != null}, _p2UiCamera={_p2UiCamera != null}");

            // Step E: Create compositor camera
            Debug.Log("[Splitscreen][Camera] Step E: Creating compositor...");
            CreateCompositor(horizontal);
            Debug.Log($"[Splitscreen][Camera] Step E DONE: _compositor={_compositor != null}");

            // Summary
            Debug.Log("[Splitscreen][Camera] === CAMERA SETUP SUMMARY ===");
            Debug.Log($"[Splitscreen][Camera]   P1 main: targetRT={_originalCamera.targetTexture?.name}, depth={_originalCamera.depth}, rect={_originalCamera.rect}");
            var skyCam = GameCamera.instance.m_skyCamera;
            if (skyCam != null)
                Debug.Log($"[Splitscreen][Camera]   P1 sky:  targetRT={skyCam.targetTexture?.name}, depth={skyCam.depth}, rect={skyCam.rect}");
            Debug.Log($"[Splitscreen][Camera]   P1 UI:   targetRT={_p1UiCamera?.targetTexture?.name}, depth={_p1UiCamera?.depth}, cullMask={_p1UiCamera?.cullingMask}");
            Debug.Log($"[Splitscreen][Camera]   P2 main: targetRT={_p2Camera?.targetTexture?.name}, depth={_p2Camera?.depth}, rect={_p2Camera?.rect}");
            Debug.Log($"[Splitscreen][Camera]   P2 sky:  targetRT={_p2SkyCamera?.targetTexture?.name}, depth={_p2SkyCamera?.depth}, rect={_p2SkyCamera?.rect}");
            Debug.Log($"[Splitscreen][Camera]   P2 UI:   targetRT={_p2UiCamera?.targetTexture?.name}, depth={_p2UiCamera?.depth}, cullMask={_p2UiCamera?.cullingMask}");
            Debug.Log($"[Splitscreen][Camera]   Compositor: depth={_compositorCamera?.depth}, targetRT={(_compositorCamera?.targetTexture != null ? _compositorCamera.targetTexture.name : "SCREEN")}");
            Debug.Log("[Splitscreen][Camera] === OnSplitscreenActivated END ===");
        }

        private void ResolvePlayer2HudLayer()
        {
            int mainUiLayer = Hud.instance?.m_rootObject != null
                ? Hud.instance.m_rootObject.layer
                : LayerMask.NameToLayer("UI");
            if (mainUiLayer < 0)
            {
                mainUiLayer = 5;
            }

            int selectedLayer = FindUnusedHudLayer(mainUiLayer);
            Player2HudLayer = selectedLayer;

            string mainUiName = LayerMask.LayerToName(mainUiLayer);
            string selectedName = LayerMask.LayerToName(selectedLayer);
            Debug.Log($"[Splitscreen][Camera] P2 HUD layer selected: {selectedLayer} ('{selectedName}'), P1 HUD layer: {mainUiLayer} ('{mainUiName}')");
        }

        private static int FindUnusedHudLayer(int excludedLayer)
        {
            for (int i = 31; i >= 8; i--)
            {
                if (i == excludedLayer)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                {
                    return i;
                }
            }

            if (31 != excludedLayer) return 31;
            if (30 != excludedLayer) return 30;
            return 29;
        }

        private void CreateRenderTextures(bool horizontal)
        {
            int p1Width;
            int p1Height;
            int p2Width;
            int p2Height;

            if (horizontal)
            {
                p1Width = Screen.width;
                p2Width = Screen.width;
                p1Height = Screen.height / 2;
                p2Height = Screen.height - p1Height;
            }
            else
            {
                p1Width = Screen.width / 2;
                p2Width = Screen.width - p1Width;
                p1Height = Screen.height;
                p2Height = Screen.height;
            }

            p1Width = Mathf.Max(1, p1Width);
            p1Height = Mathf.Max(1, p1Height);
            p2Width = Mathf.Max(1, p2Width);
            p2Height = Mathf.Max(1, p2Height);

            int aa = QualitySettings.antiAliasing;
            if (aa < 1) aa = 1;

            Debug.Log($"[Splitscreen][Camera] Creating RT P1: {p1Width}x{p1Height}, depth=24, AA={aa}, format=Default");
            _p1RT = new RenderTexture(p1Width, p1Height, 24, RenderTextureFormat.Default);
            _p1RT.name = "SplitscreenRT_P1";
            ConfigureRenderTexture(_p1RT, aa);
            _p1RT.Create();
            Debug.Log($"[Splitscreen][Camera] P1 RT created: IsCreated={_p1RT.IsCreated()}, width={_p1RT.width}, height={_p1RT.height}");

            Debug.Log($"[Splitscreen][Camera] Creating RT P2: {p2Width}x{p2Height}, depth=24, AA={aa}, format=Default");
            _p2RT = new RenderTexture(p2Width, p2Height, 24, RenderTextureFormat.Default);
            _p2RT.name = "SplitscreenRT_P2";
            ConfigureRenderTexture(_p2RT, aa);
            _p2RT.Create();
            Debug.Log($"[Splitscreen][Camera] P2 RT created: IsCreated={_p2RT.IsCreated()}, width={_p2RT.width}, height={_p2RT.height}");
        }

        private static void ConfigureRenderTexture(RenderTexture rt, int aa)
        {
            rt.antiAliasing = aa;
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.useMipMap = false;
            rt.autoGenerateMips = false;
        }

        private void SetupPlayer1Cameras()
        {
            int excludedUiMask = _uiLayerMask | (1 << Player2HudLayer);

            Debug.Log($"[Splitscreen][Camera] Setting P1 main camera targetTexture to {_p1RT.name}");
            _originalCamera.targetTexture = _p1RT;
            _originalCamera.rect = new Rect(0f, 0f, 1f, 1f);
            _originalCamera.cullingMask &= ~excludedUiMask;
            Debug.Log($"[Splitscreen][Camera] P1 main camera SET: targetTexture={_originalCamera.targetTexture?.name}, rect={_originalCamera.rect}, depthTexMode={_originalCamera.depthTextureMode}");

            if (GameCamera.instance.m_skyCamera != null)
            {
                _savedSkyDepthTextureMode = GameCamera.instance.m_skyCamera.depthTextureMode;
                _savedSkyCullingMask = GameCamera.instance.m_skyCamera.cullingMask;
                Debug.Log($"[Splitscreen][Camera] P1 sky camera BEFORE: targetTexture={GameCamera.instance.m_skyCamera.targetTexture?.name}, rect={GameCamera.instance.m_skyCamera.rect}, depth={GameCamera.instance.m_skyCamera.depth}");
                GameCamera.instance.m_skyCamera.targetTexture = _p1RT;
                GameCamera.instance.m_skyCamera.rect = new Rect(0f, 0f, 1f, 1f);
                GameCamera.instance.m_skyCamera.cullingMask &= ~excludedUiMask;
                Debug.Log($"[Splitscreen][Camera] P1 sky camera SET: targetTexture={GameCamera.instance.m_skyCamera.targetTexture?.name}, rect={GameCamera.instance.m_skyCamera.rect}");
            }
            else
            {
                Debug.LogWarning("[Splitscreen][Camera] P1 sky camera (m_skyCamera) is NULL!");
            }
        }

        private void CreatePlayer2Camera()
        {
            Debug.Log("[Splitscreen][Camera] Creating P2 camera GameObject...");
            _p2CameraObj = new GameObject("SplitscreenCamera_P2");

            Debug.Log("[Splitscreen][Camera] Adding Camera component to P2...");
            _p2Camera = _p2CameraObj.AddComponent<UnityEngine.Camera>();
            Debug.Log("[Splitscreen][Camera] CopyFrom P1 camera...");
            _p2Camera.CopyFrom(_originalCamera);
            _p2Camera.targetTexture = _p2RT;
            _p2Camera.rect = new Rect(0f, 0f, 1f, 1f);
            _p2Camera.cullingMask &= ~(_uiLayerMask | (1 << Player2HudLayer));

            var config = SplitscreenPlugin.Instance.SplitConfig;
            _p2Camera.fieldOfView = config.CameraFOV.Value;
            _p2Camera.depth = _originalCamera.depth + 10;

            Debug.Log($"[Splitscreen][Camera] P2 main camera created:");
            Debug.Log($"[Splitscreen][Camera]   targetTexture={_p2Camera.targetTexture?.name}");
            Debug.Log($"[Splitscreen][Camera]   rect={_p2Camera.rect}");
            Debug.Log($"[Splitscreen][Camera]   depth={_p2Camera.depth}");
            Debug.Log($"[Splitscreen][Camera]   fov={_p2Camera.fieldOfView}");
            Debug.Log($"[Splitscreen][Camera]   clearFlags={_p2Camera.clearFlags}");
            Debug.Log($"[Splitscreen][Camera]   depthTexMode={_p2Camera.depthTextureMode}");

            // Sky camera for P2
            Debug.Log("[Splitscreen][Camera] Creating P2 sky camera...");
            var skyCamObj = new GameObject("SplitscreenSkyCamera_P2");
            skyCamObj.transform.SetParent(_p2CameraObj.transform);
            _p2SkyCamera = skyCamObj.AddComponent<UnityEngine.Camera>();
            if (GameCamera.instance.m_skyCamera != null)
            {
                _p2SkyCamera.CopyFrom(GameCamera.instance.m_skyCamera);
                Debug.Log("[Splitscreen][Camera] P2 sky camera copied from original sky camera");
            }
            else
            {
                Debug.LogWarning("[Splitscreen][Camera] No original sky camera to copy from!");
            }
            _p2SkyCamera.targetTexture = _p2RT;
            _p2SkyCamera.rect = new Rect(0f, 0f, 1f, 1f);
            _p2SkyCamera.depth = _originalCamera.depth + 9;
            _p2SkyCamera.cullingMask &= ~(_uiLayerMask | (1 << Player2HudLayer));

            Debug.Log($"[Splitscreen][Camera] P2 sky camera created:");
            Debug.Log($"[Splitscreen][Camera]   targetTexture={_p2SkyCamera.targetTexture?.name}");
            Debug.Log($"[Splitscreen][Camera]   depth={_p2SkyCamera.depth}");
            Debug.Log($"[Splitscreen][Camera]   clearFlags={_p2SkyCamera.clearFlags}");
            Debug.Log($"[Splitscreen][Camera]   cullingMask={_p2SkyCamera.cullingMask}");

            // Audio listener only on P1 camera
            var mainListener = GameCamera.instance?.GetComponentInChildren<AudioListener>();
            Debug.Log($"[Splitscreen][Camera] Audio listener on P1: {mainListener != null && mainListener.enabled}");
            if (mainListener != null) mainListener.enabled = true;
        }

        private void CreateUICameras()
        {
            if (_originalCamera == null || _p2Camera == null)
            {
                Debug.LogWarning("[Splitscreen][Camera] Cannot create UI cameras, base cameras are missing");
                return;
            }

            _p1UiCameraObj = new GameObject("SplitscreenUICamera_P1");
            _p1UiCameraObj.transform.SetParent(_originalCamera.transform, false);
            _p1UiCamera = _p1UiCameraObj.AddComponent<UnityEngine.Camera>();
            SetupUiCamera(_p1UiCamera, _originalCamera, _p1RT, _originalCamera.depth + 50f, _uiLayerMask);

            _p2UiCameraObj = new GameObject("SplitscreenUICamera_P2");
            _p2UiCameraObj.transform.SetParent(_p2Camera.transform, false);
            _p2UiCamera = _p2UiCameraObj.AddComponent<UnityEngine.Camera>();
            SetupUiCamera(_p2UiCamera, _p2Camera, _p2RT, _p2Camera.depth + 50f, 1 << Player2HudLayer);

            Debug.Log($"[Splitscreen][Camera] UI cameras created: P1={_p1UiCamera.name}, P2={_p2UiCamera.name}, uiMask={_uiLayerMask}");
        }

        private void SetupUiCamera(UnityEngine.Camera uiCamera, UnityEngine.Camera sourceCamera, RenderTexture target, float depth, int cullingMask)
        {
            uiCamera.CopyFrom(sourceCamera);
            uiCamera.targetTexture = target;
            uiCamera.rect = new Rect(0f, 0f, 1f, 1f);
            uiCamera.depth = depth;
            uiCamera.clearFlags = CameraClearFlags.Depth;
            uiCamera.backgroundColor = Color.clear;
            uiCamera.cullingMask = cullingMask;
            uiCamera.allowMSAA = false;
            uiCamera.allowHDR = false;
            uiCamera.useOcclusionCulling = false;
        }

        private void CopyPostProcessingToP2()
        {
            var p1Components = GameCamera.instance.GetComponents<Component>();
            Debug.Log($"[Splitscreen][Camera] P1 camera has {p1Components.Length} components:");
            foreach (var comp in p1Components)
            {
                if (comp == null) continue;
                Debug.Log($"[Splitscreen][Camera]   {comp.GetType().FullName} (enabled={IsEnabled(comp)})");
            }

            _p2Camera.depthTextureMode = _originalCamera.depthTextureMode;
            Debug.Log($"[Splitscreen][Camera] P2 depthTextureMode set to {_p2Camera.depthTextureMode}");

            foreach (var comp in p1Components)
            {
                if (comp == null) continue;

                Type type = comp.GetType();
                string typeName = type.Name;
                string fullName = type.FullName ?? "";

                if (comp is UnityEngine.Camera || comp is AudioListener || comp is Transform)
                    continue;
                if (fullName.Contains("assembly_valheim") || typeName == "GameCamera")
                    continue;

                bool shouldCopy = typeName.Contains("PostProcess") ||
                                  typeName.Contains("SSAO") ||
                                  typeName.Contains("AmbientOcclusion") ||
                                  typeName.Contains("Volume") ||
                                  fullName.Contains("PostProcessing") ||
                                  fullName.Contains("Rendering.PostProcessing") ||
                                  fullName.Contains("Rendering.Universal");

                if (!shouldCopy) continue;

                if (_p2CameraObj.GetComponent(type) != null)
                {
                    Debug.Log($"[Splitscreen][Camera] P2 already has {typeName}, skipping");
                    continue;
                }

                try
                {
                    var newComp = _p2CameraObj.AddComponent(type);
                    if (newComp != null)
                    {
                        CopyComponentFields(comp, newComp, type);
                        Debug.Log($"[Splitscreen][Camera] Copied {typeName} to P2 camera");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Splitscreen][Camera] Failed to copy {typeName}: {e.Message}");
                }
            }
        }

        private void CopyComponentFields(Component source, Component dest, Type type)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                try
                {
                    if (typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
                    field.SetValue(dest, field.GetValue(source));
                }
                catch { }
            }

            var privateFields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in privateFields)
            {
                if (field.GetCustomAttribute<SerializeField>() == null) continue;
                try
                {
                    if (typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
                    field.SetValue(dest, field.GetValue(source));
                }
                catch { }
            }

            if (type.Name == "PostProcessLayer")
            {
                try
                {
                    var triggerField = type.GetField("volumeTrigger", BindingFlags.Public | BindingFlags.Instance);
                    if (triggerField != null)
                    {
                        triggerField.SetValue(dest, _p2CameraObj.transform);
                        Debug.Log("[Splitscreen][Camera] Set PostProcessLayer.volumeTrigger to P2 camera transform");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Splitscreen][Camera] Failed to set volumeTrigger: {e.Message}");
                }
            }
        }

        private bool IsEnabled(Component comp)
        {
            if (comp is Behaviour b) return b.enabled;
            if (comp is Renderer r) return r.enabled;
            return true;
        }

        private void CreateCompositor(bool horizontal)
        {
            Debug.Log("[Splitscreen][Camera] Creating compositor GameObject...");
            _compositorObj = new GameObject("SplitscreenCompositor");

            Debug.Log("[Splitscreen][Camera] Adding Camera to compositor...");
            _compositorCamera = _compositorObj.AddComponent<UnityEngine.Camera>();
            _compositorCamera.depth = 100;
            _compositorCamera.clearFlags = CameraClearFlags.SolidColor;
            _compositorCamera.backgroundColor = Color.black;
            _compositorCamera.cullingMask = 0;
            _compositorCamera.targetTexture = null;
            Debug.Log($"[Splitscreen][Camera] Compositor camera: depth={_compositorCamera.depth}, clearFlags={_compositorCamera.clearFlags}, cullingMask={_compositorCamera.cullingMask}, targetTexture=SCREEN");

            Debug.Log("[Splitscreen][Camera] Adding SplitscreenCompositor component...");
            _compositor = _compositorObj.AddComponent<SplitscreenCompositor>();
            Debug.Log("[Splitscreen][Camera] Calling compositor.Initialize...");
            _compositor.Initialize(_p1RT, _p2RT, horizontal);
            Debug.Log($"[Splitscreen][Camera] Compositor initialized OK: horizontal={horizontal}");
        }

        public void OnSplitscreenDeactivated()
        {
            Debug.Log("[Splitscreen][Camera] === OnSplitscreenDeactivated START ===");

            // Restore P1 cameras
            if (_originalCamera != null)
            {
                Debug.Log("[Splitscreen][Camera] Restoring P1 main camera...");
                _originalCamera.targetTexture = _savedTargetTexture;
                _originalCamera.rect = new Rect(0f, 0f, 1f, 1f);
                _originalCamera.depthTextureMode = _savedDepthTextureMode;
                _originalCamera.cullingMask = _savedMainCullingMask;
                Debug.Log($"[Splitscreen][Camera] P1 main restored: targetTexture={(_originalCamera.targetTexture != null ? _originalCamera.targetTexture.name : "null")}, rect={_originalCamera.rect}");

                if (GameCamera.instance != null && GameCamera.instance.m_skyCamera != null)
                {
                    Debug.Log("[Splitscreen][Camera] Restoring P1 sky camera...");
                    GameCamera.instance.m_skyCamera.targetTexture = null;
                    GameCamera.instance.m_skyCamera.rect = new Rect(0f, 0f, 1f, 1f);
                    GameCamera.instance.m_skyCamera.depthTextureMode = _savedSkyDepthTextureMode;
                    if (_savedSkyCullingMask >= 0)
                    {
                        GameCamera.instance.m_skyCamera.cullingMask = _savedSkyCullingMask;
                    }
                    Debug.Log("[Splitscreen][Camera] P1 sky restored");
                }
            }
            else
            {
                Debug.LogWarning("[Splitscreen][Camera] _originalCamera is null during deactivation!");
            }

            // Destroy UI cameras
            if (_p1UiCameraObj != null)
            {
                Destroy(_p1UiCameraObj);
                _p1UiCameraObj = null;
                _p1UiCamera = null;
            }
            if (_p2UiCameraObj != null)
            {
                Destroy(_p2UiCameraObj);
                _p2UiCameraObj = null;
                _p2UiCamera = null;
            }

            // Destroy compositor
            if (_compositorObj != null)
            {
                Debug.Log("[Splitscreen][Camera] Destroying compositor...");
                Destroy(_compositorObj);
                _compositorObj = null;
                _compositorCamera = null;
                _compositor = null;
                Debug.Log("[Splitscreen][Camera] Compositor destroyed");
            }

            // Destroy P2 cameras
            if (_p2CameraObj != null)
            {
                Debug.Log("[Splitscreen][Camera] Destroying P2 cameras...");
                Destroy(_p2CameraObj);
                _p2CameraObj = null;
                _p2Camera = null;
                _p2SkyCamera = null;
                Debug.Log("[Splitscreen][Camera] P2 cameras destroyed");
            }

            // Release RenderTextures
            Debug.Log("[Splitscreen][Camera] Releasing RenderTextures...");
            if (_p1RT != null)
            {
                _p1RT.Release();
                Destroy(_p1RT);
                _p1RT = null;
                Debug.Log("[Splitscreen][Camera] P1 RT released");
            }
            if (_p2RT != null)
            {
                _p2RT.Release();
                Destroy(_p2RT);
                _p2RT = null;
                Debug.Log("[Splitscreen][Camera] P2 RT released");
            }

            // Re-enable audio on main camera
            var mainListener = GameCamera.instance?.GetComponentInChildren<AudioListener>();
            if (mainListener != null) mainListener.enabled = true;

            Debug.Log("[Splitscreen][Camera] === OnSplitscreenDeactivated END ===");
        }

        private void LateUpdate()
        {
            if (SplitScreenManager.Instance == null || !SplitScreenManager.Instance.SplitscreenActive) return;
            if (_p2Camera == null)
            {
                if (Time.time - _lastCamLogTime > 5f)
                {
                    Debug.LogWarning("[Splitscreen][Camera] LateUpdate: _p2Camera is null!");
                    _lastCamLogTime = Time.time;
                }
                return;
            }

            var p2 = SplitScreenManager.Instance.PlayerManager?.Player2;
            if (p2 == null)
            {
                if (Time.time - _lastCamLogTime > 5f)
                {
                    Debug.LogWarning("[Splitscreen][Camera] LateUpdate: Player2 is null!");
                    _lastCamLogTime = Time.time;
                }
                return;
            }

            // Log camera state periodically
            if (Time.time - _lastCamLogTime > 10f)
            {
                _lastCamLogTime = Time.time;
                Debug.Log($"[Splitscreen][Camera] === PERIODIC CAMERA STATUS ===");
                Debug.Log($"[Splitscreen][Camera] P1 main: enabled={_originalCamera?.enabled}, targetRT={_originalCamera?.targetTexture?.name}, rect={_originalCamera?.rect}");
                var skyCam = GameCamera.instance?.m_skyCamera;
                Debug.Log($"[Splitscreen][Camera] P1 sky:  enabled={skyCam?.enabled}, targetRT={skyCam?.targetTexture?.name}");
                Debug.Log($"[Splitscreen][Camera] P1 UI:   enabled={_p1UiCamera?.enabled}, targetRT={_p1UiCamera?.targetTexture?.name}");
                Debug.Log($"[Splitscreen][Camera] P2 main: enabled={_p2Camera?.enabled}, targetRT={_p2Camera?.targetTexture?.name}, rect={_p2Camera?.rect}");
                Debug.Log($"[Splitscreen][Camera] P2 sky:  enabled={_p2SkyCamera?.enabled}, targetRT={_p2SkyCamera?.targetTexture?.name}");
                Debug.Log($"[Splitscreen][Camera] P2 UI:   enabled={_p2UiCamera?.enabled}, targetRT={_p2UiCamera?.targetTexture?.name}");
                Debug.Log($"[Splitscreen][Camera] Compositor: enabled={_compositorCamera?.enabled}, targetRT={(_compositorCamera?.targetTexture != null ? _compositorCamera.targetTexture.name : "SCREEN")}");
                Debug.Log($"[Splitscreen][Camera] P1 RT: isCreated={_p1RT?.IsCreated()}, P2 RT: isCreated={_p2RT?.IsCreated()}");
                Debug.Log($"[Splitscreen][Camera] P2 cam pos={_p2CameraObj.transform.position}, P2 player pos={p2.transform.position}, dist={_p2Distance}");
            }

            UpdatePlayer2Camera(p2, Time.unscaledDeltaTime);
        }

        private void UpdatePlayer2Camera(global::Player player, float dt)
        {
            if (player == null) return;

            var config = SplitscreenPlugin.Instance.SplitConfig;
            _p2Camera.fieldOfView = config.CameraFOV.Value;
            if (_p2SkyCamera != null)
            {
                _p2SkyCamera.fieldOfView = config.CameraFOV.Value;
            }

            var input = SplitInputManager.Instance.GetInputState(1);

            if (input.ButtonNorth)
            {
                if (input.DpadUp) _p2Distance -= 10f * dt;
                if (input.DpadDown) _p2Distance += 10f * dt;
            }
            float maxDist = 6f;
            _p2Distance = Mathf.Clamp(_p2Distance, 0f, maxDist);

            Vector3 baseOffset = GetCameraBaseOffset(player);
            _p2CurrentBaseOffset = Vector3.SmoothDamp(_p2CurrentBaseOffset, baseOffset,
                ref _p2OffsetBaseVel, 0.5f, 999f, dt);

            if (Vector3.Distance(_p2PlayerPos, player.transform.position) > 20f)
            {
                _p2PlayerPos = player.transform.position;
            }
            _p2PlayerPos = Vector3.SmoothDamp(_p2PlayerPos, player.transform.position,
                ref _p2PlayerVel, 0.1f, 999f, dt);

            if (player.IsDead() && player.GetRagdoll() != null)
            {
                Vector3 ragdollPos = player.GetRagdoll().GetAverageBodyPosition();
                _p2CameraObj.transform.LookAt(ragdollPos);
                return;
            }

            if (player.IsAttached() && player.GetAttachCameraPoint() != null)
            {
                Transform attachPoint = player.GetAttachCameraPoint();
                _p2CameraObj.transform.position = attachPoint.position;
                _p2CameraObj.transform.rotation = attachPoint.rotation;
                return;
            }

            Vector3 eyePos = GetOffsetedEyePos(player);
            float distance = _p2Distance;
            Vector3 cameraDir = -player.m_eye.transform.forward;

            if (player.InIntro())
            {
                eyePos = player.transform.position;
                distance = 15f;
            }

            Vector3 targetPos = eyePos + cameraDir * distance;

            if (Physics.SphereCast(player.m_eye.position, 0.35f, cameraDir, out RaycastHit hit,
                distance, GameCamera.instance.m_blockCameraMask))
            {
                targetPos = eyePos + cameraDir * hit.distance;
            }

            float waterLevel = Floating.GetLiquidLevel(targetPos);
            if (targetPos.y < waterLevel + 0.3f)
            {
                targetPos.y = waterLevel + 0.3f;
            }

            _p2CameraObj.transform.position = targetPos;
            _p2CameraObj.transform.rotation = player.m_eye.transform.rotation;

            ApplyCameraTilt(player, dt);
        }

        private Vector3 GetCameraBaseOffset(global::Player player)
        {
            if (player.InBed())
                return player.GetHeadPoint() - player.transform.position;
            if (player.IsAttached() || player.IsSitting())
                return player.GetHeadPoint() + Vector3.up * 0.3f - player.transform.position;
            return player.m_eye.transform.position - player.transform.position;
        }

        private Vector3 GetOffsetedEyePos(global::Player player)
        {
            if (player.GetStandingOnShip() != null || player.IsAttached())
                return player.transform.position + _p2CurrentBaseOffset + GetCameraOffset(player);
            return _p2PlayerPos + _p2CurrentBaseOffset + GetCameraOffset(player);
        }

        private Vector3 GetCameraOffset(global::Player player)
        {
            if (_p2Distance <= 0f)
            {
                return player.m_eye.transform.TransformVector(
                    GameCamera.instance != null ? GameCamera.instance.m_fpsOffset : Vector3.zero);
            }
            if (player.InBed()) return Vector3.zero;

            Vector3 offset = player.UseMeleeCamera()
                ? (GameCamera.instance?.m_3rdCombatOffset ?? Vector3.zero)
                : (GameCamera.instance?.m_3rdOffset ?? Vector3.zero);
            return player.m_eye.transform.TransformVector(offset);
        }

        private void ApplyCameraTilt(global::Player player, float dt)
        {
            if (player.InIntro()) return;

            Ship ship = player.GetStandingOnShip();
            float minDist = GameCamera.instance?.m_minDistance ?? 0f;
            float maxBoatDist = GameCamera.instance?.m_maxDistanceBoat ?? 6f;
            float f = Mathf.Clamp01((_p2Distance - minDist) / (maxBoatDist - minDist));
            f = Mathf.Pow(f, 2f);
            float tiltMin = GameCamera.instance?.m_tiltSmoothnessShipMin ?? 0.1f;
            float tiltMax = GameCamera.instance?.m_tiltSmoothnessShipMax ?? 0.5f;
            float smoothTime = Mathf.Lerp(tiltMin, tiltMax, f);

            Vector3 up = Vector3.up;
            if (ship != null && ship.transform.up.y > 0f)
                up = ship.transform.up;
            else if (player.IsAttached())
                up = player.GetVisual().transform.up;

            Vector3 forward = player.m_eye.transform.forward;
            Vector3 target = Vector3.Lerp(up, Vector3.up, f * 0.5f);
            _p2SmoothedCameraUp = Vector3.SmoothDamp(_p2SmoothedCameraUp, target,
                ref _p2SmoothedCameraUpVel, smoothTime, 99f, dt);

            _p2CameraObj.transform.rotation = Quaternion.LookRotation(forward, _p2SmoothedCameraUp);
        }

        private void OnDestroy()
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Attached to the compositor camera. Blits both player RenderTextures to screen
    /// using GUI.DrawTexture in OnGUI (no shader dependency needed).
    /// The compositor camera clears the screen to black, then OnGUI draws the RTs on top.
    /// </summary>
    public class SplitscreenCompositor : MonoBehaviour
    {
        private RenderTexture _p1RT;
        private RenderTexture _p2RT;
        private bool _horizontal;

        // Divider line
        private Texture2D _dividerTex;

        // Logging
        private bool _loggedFirstRender;
        private float _lastCompositorLogTime;
        private int _renderCount;

        public void Initialize(RenderTexture p1RT, RenderTexture p2RT, bool horizontal)
        {
            _p1RT = p1RT;
            _p2RT = p2RT;
            _horizontal = horizontal;

            Debug.Log($"[Splitscreen][Compositor] Initialize called: horizontal={horizontal}");
            Debug.Log($"[Splitscreen][Compositor]   p1RT: {p1RT.width}x{p1RT.height}, isCreated={p1RT.IsCreated()}, name={p1RT.name}");
            Debug.Log($"[Splitscreen][Compositor]   p2RT: {p2RT.width}x{p2RT.height}, isCreated={p2RT.IsCreated()}, name={p2RT.name}");
        }

        private void OnGUI()
        {
            if (_p1RT == null || _p2RT == null) return;
            if (Event.current.type != EventType.Repaint) return;

            _renderCount++;

            if (!_loggedFirstRender)
            {
                _loggedFirstRender = true;
                Debug.Log($"[Splitscreen][Compositor] FIRST RENDER! Drawing RTs to screen");
                Debug.Log($"[Splitscreen][Compositor]   Screen: {Screen.width}x{Screen.height}");
                Debug.Log($"[Splitscreen][Compositor]   P1 RT: {_p1RT.width}x{_p1RT.height}, isCreated={_p1RT.IsCreated()}");
                Debug.Log($"[Splitscreen][Compositor]   P2 RT: {_p2RT.width}x{_p2RT.height}, isCreated={_p2RT.IsCreated()}");
                Debug.Log($"[Splitscreen][Compositor]   Horizontal={_horizontal}");
            }

            // Periodic compositor status
            if (Time.time - _lastCompositorLogTime > 15f)
            {
                _lastCompositorLogTime = Time.time;
                Debug.Log($"[Splitscreen][Compositor] Status: rendered {_renderCount} frames, p1RT.isCreated={_p1RT.IsCreated()}, p2RT.isCreated={_p2RT.IsCreated()}");
            }

            if (_horizontal)
            {
                int p1Height = Mathf.Clamp(_p1RT.height, 1, Screen.height);
                int p2Height = Mathf.Max(1, Screen.height - p1Height);
                // P1 = top half
                GUI.DrawTexture(new Rect(0, 0, Screen.width, p1Height), _p1RT, ScaleMode.StretchToFill, alphaBlend: false);
                // P2 = bottom half
                GUI.DrawTexture(new Rect(0, p1Height, Screen.width, p2Height), _p2RT, ScaleMode.StretchToFill, alphaBlend: false);
            }
            else
            {
                int p1Width = Mathf.Clamp(_p1RT.width, 1, Screen.width);
                int p2Width = Mathf.Max(1, Screen.width - p1Width);
                // P1 = left half
                GUI.DrawTexture(new Rect(0, 0, p1Width, Screen.height), _p1RT, ScaleMode.StretchToFill, alphaBlend: false);
                // P2 = right half
                GUI.DrawTexture(new Rect(p1Width, 0, p2Width, Screen.height), _p2RT, ScaleMode.StretchToFill, alphaBlend: false);
            }

            // Draw divider line
            if (_dividerTex == null)
            {
                _dividerTex = new Texture2D(1, 1);
                _dividerTex.SetPixel(0, 0, Color.black);
                _dividerTex.Apply();
            }

            if (_horizontal)
            {
                int y = Mathf.Clamp(_p1RT.height, 1, Screen.height - 1);
                GUI.DrawTexture(new Rect(0, y - 1, Screen.width, 3), _dividerTex);
            }
            else
            {
                int x = Mathf.Clamp(_p1RT.width, 1, Screen.width - 1);
                GUI.DrawTexture(new Rect(x - 1, 0, 3, Screen.height), _dividerTex);
            }
        }

        private void OnDestroy()
        {
            Debug.Log($"[Splitscreen][Compositor] OnDestroy, total renders={_renderCount}");
            if (_dividerTex != null) Destroy(_dividerTex);
        }
    }
}
