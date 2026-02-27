using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimSplitscreen.UI
{
    /// <summary>
    /// Controls the visual menu split: confines the game's menu UI to P1's half
    /// using a RectMask2D container, letting the 3D background show through on P2's half.
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

        // Menu confinement: reparent game menu content into a clipped container
        private GameObject _menuClipContainer;
        private GameObject _innerFullScreen; // full-screen child of clip so content isn't squished
        private Canvas _confinedCanvas;
        private List<Transform> _reparentedChildren;
        private Dictionary<Transform, int> _originalSiblingIndices;

        // Sort orders
        private const int OverlaySortOrder = 100;  // Above game UI (typically 0-10)
        public const int CharSelectSortOrder = 110; // Above our overlay

        private void Awake()
        {
            Instance = this;
        }

        public void Activate(bool horizontal)
        {
            if (IsActive) return;

            _horizontal = horizontal;
            _p2Ready = false;
            _p2CharacterName = null;
            IsActive = true;

            Debug.Log($"[Splitscreen][MenuSplit] Activating, horizontal={horizontal}");

            CreateOverlayCanvas();
            ConfineGameMenuToP1Half();

            Debug.Log("[Splitscreen][MenuSplit] Active: overlay canvas created");
        }

        public void Deactivate()
        {
            if (!IsActive) return;

            Debug.Log("[Splitscreen][MenuSplit] Deactivating");

            RestoreGameMenu();
            DestroyOverlayCanvas();

            _p2Ready = false;
            _p2CharacterName = null;
            IsActive = false;
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

        // ===== GAME MENU CONFINEMENT =====

        private void ConfineGameMenuToP1Half()
        {
            // Find the game's main GUI canvas
            var fejd = FejdStartup.instance;
            if (fejd == null)
            {
                Debug.LogWarning("[Splitscreen][MenuSplit] FejdStartup.instance is null, cannot confine menu");
                return;
            }

            _confinedCanvas = fejd.GetComponentInParent<Canvas>();
            if (_confinedCanvas == null)
            {
                // Search for the root ScreenSpaceOverlay canvas
                var allCanvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                foreach (var c in allCanvases)
                {
                    if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay &&
                        c.gameObject.name != "SplitscreenMenuOverlay" &&
                        c.gameObject.name != "P2_CharSelectCanvas")
                    {
                        _confinedCanvas = c;
                        break;
                    }
                }
            }

            if (_confinedCanvas == null)
            {
                Debug.LogWarning("[Splitscreen][MenuSplit] Could not find game menu canvas to confine");
                return;
            }

            Debug.Log($"[Splitscreen][MenuSplit] Confining canvas '{_confinedCanvas.gameObject.name}' children to P1 half");

            // Create a clip container anchored to P1's half
            _menuClipContainer = new GameObject("P1_MenuClip");
            _menuClipContainer.transform.SetParent(_confinedCanvas.transform, false);
            var clipRT = _menuClipContainer.AddComponent<RectTransform>();
            SetP1Anchors(clipRT);
            _menuClipContainer.AddComponent<RectMask2D>();

            // Ensure the clip container is at index 0 so it's behind everything
            _menuClipContainer.transform.SetAsFirstSibling();

            // Create a full-screen inner container inside the clip.
            // Without this, children anchored to (0,0)-(1,1) fill only the clip (half screen)
            // and get squished. The inner container extends beyond the clip to full-screen size,
            // and the RectMask2D clips the overflow.
            _innerFullScreen = new GameObject("P1_InnerFull");
            _innerFullScreen.transform.SetParent(_menuClipContainer.transform, false);
            var innerRT = _innerFullScreen.AddComponent<RectTransform>();

            if (_horizontal)
            {
                // Clip = top half, anchors (0, 0.5)-(1, 1) in canvas.
                // In clip-local coords: full screen = (0, -1) to (1, 1)
                innerRT.anchorMin = new Vector2(0, -1);
                innerRT.anchorMax = new Vector2(1, 1);
            }
            else
            {
                // Clip = left half, anchors (0, 0)-(0.5, 1) in canvas.
                // In clip-local coords: full screen = (0, 0) to (2, 1)
                innerRT.anchorMin = new Vector2(0, 0);
                innerRT.anchorMax = new Vector2(2, 1);
            }
            innerRT.offsetMin = Vector2.zero;
            innerRT.offsetMax = Vector2.zero;

            // Collect existing children (skip our containers)
            _reparentedChildren = new List<Transform>();
            _originalSiblingIndices = new Dictionary<Transform, int>();
            var childCount = _confinedCanvas.transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var child = _confinedCanvas.transform.GetChild(i);
                if (child.gameObject == _menuClipContainer) continue;
                _reparentedChildren.Add(child);
                _originalSiblingIndices[child] = i;
            }

            // Reparent all children under the inner full-screen container
            foreach (var child in _reparentedChildren)
            {
                child.SetParent(_innerFullScreen.transform, false);
            }

            Debug.Log($"[Splitscreen][MenuSplit] Reparented {_reparentedChildren.Count} children under clip container (with full-screen inner)");
        }

        private void RestoreGameMenu()
        {
            if (_menuClipContainer == null || _confinedCanvas == null) return;

            Debug.Log("[Splitscreen][MenuSplit] Restoring game menu from clip container");

            // Reparent children back to the original canvas
            if (_reparentedChildren != null)
            {
                foreach (var child in _reparentedChildren)
                {
                    if (child != null)
                        child.SetParent(_confinedCanvas.transform, false);
                }

                // Restore original sibling order
                foreach (var child in _reparentedChildren)
                {
                    if (child != null && _originalSiblingIndices.TryGetValue(child, out int idx))
                        child.SetSiblingIndex(idx);
                }
            }

            Destroy(_menuClipContainer);
            _menuClipContainer = null;
            _confinedCanvas = null;
            _reparentedChildren = null;
            _originalSiblingIndices = null;
        }

        // ===== OVERLAY CANVAS =====

        private void CreateOverlayCanvas()
        {
            _overlayRoot = new GameObject("SplitscreenMenuOverlay");
            DontDestroyOnLoad(_overlayRoot);

            _overlayCanvas = _overlayRoot.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = OverlaySortOrder;

            // No GraphicRaycaster needed — RectMask2D on P1_MenuClip handles raycast blocking

            var scaler = _overlayRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // No dark panel — 3D background shows through on P2's half.
            // The game menu is confined to P1's half via RectMask2D (ConfineGameMenuToP1Half).

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

            // Ready status group (hidden initially)
            CreateReadyStatusGroup();
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

        private void SetP1Anchors(RectTransform rt)
        {
            if (_horizontal)
            {
                rt.anchorMin = new Vector2(0, 0.5f);
                rt.anchorMax = new Vector2(1, 1);
            }
            else
            {
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(0.5f, 1);
            }
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
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

        private void OnDestroy()
        {
            if (IsActive)
            {
                RestoreGameMenu();
                DestroyOverlayCanvas();
            }
            Instance = null;
        }
    }
}
