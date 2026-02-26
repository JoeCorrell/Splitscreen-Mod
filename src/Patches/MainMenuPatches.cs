using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using ValheimSplitscreen.Config;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.UI;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Adds a "Splitscreen" button to the main menu, directly below "Settings".
    /// Clones the Settings button as a template to match the game's visual style.
    /// </summary>
    [HarmonyPatch]
    public static class MainMenuPatches
    {
        private static GameObject _splitscreenButton;

        [HarmonyPatch(typeof(FejdStartup), "Awake")]
        [HarmonyPostfix]
        public static void FejdStartup_Awake_Postfix(FejdStartup __instance)
        {
            Debug.Log("[Splitscreen][Menu] FejdStartup.Awake postfix — will create button after UI initializes");
            // Delay button creation slightly to ensure UI is fully built
            __instance.StartCoroutine(DelayedCreateButton(__instance));
        }

        private static System.Collections.IEnumerator DelayedCreateButton(FejdStartup fejd)
        {
            // Wait one frame for the UI hierarchy to be fully set up
            yield return null;
            yield return null;
            Debug.Log("[Splitscreen][Menu] Delayed button creation running");
            CreateSplitscreenButton(fejd);
        }

        /// <summary>
        /// Also re-check when the main menu panel is shown, in case the UI
        /// was rebuilt or our button was destroyed during scene transitions.
        /// </summary>
        [HarmonyPatch(typeof(FejdStartup), "ShowStartGame")]
        [HarmonyPostfix]
        public static void ShowStartGame_Postfix()
        {
            UpdateButtonLabel();
        }

        private static void CreateSplitscreenButton(FejdStartup fejd)
        {
            if (_splitscreenButton != null)
            {
                Debug.Log("[Splitscreen][Menu] Splitscreen button already exists, skipping");
                return;
            }

            // Find the Settings button by searching all Button components in the FejdStartup hierarchy
            Button settingsButton = null;
            var allButtons = fejd.GetComponentsInChildren<Button>(true);
            Debug.Log($"[Splitscreen][Menu] Found {allButtons.Length} buttons in FejdStartup hierarchy");

            foreach (var btn in allButtons)
            {
                // Check for Text or TMPro text matching "Settings"
                string label = GetButtonLabel(btn);
                Debug.Log($"[Splitscreen][Menu]   Button: '{btn.gameObject.name}' label='{label}' parent='{btn.transform.parent?.name}'");

                if (label != null && label.ToLowerInvariant().Contains("settings"))
                {
                    settingsButton = btn;
                    Debug.Log($"[Splitscreen][Menu] Found Settings button: '{btn.gameObject.name}'");
                    break;
                }
            }

            if (settingsButton == null)
            {
                // Fallback: try finding by GameObject name
                foreach (var btn in allButtons)
                {
                    if (btn.gameObject.name.ToLowerInvariant().Contains("settings"))
                    {
                        settingsButton = btn;
                        Debug.Log($"[Splitscreen][Menu] Found Settings button by name: '{btn.gameObject.name}'");
                        break;
                    }
                }
            }

            if (settingsButton == null)
            {
                Debug.LogWarning("[Splitscreen][Menu] Could not find Settings button! Dumping full hierarchy:");
                DumpHierarchy(fejd.transform, 0);
                return;
            }

            // Clone the settings button
            Transform parent = settingsButton.transform.parent;
            int settingsIndex = settingsButton.transform.GetSiblingIndex();

            _splitscreenButton = Object.Instantiate(settingsButton.gameObject, parent);
            _splitscreenButton.name = "SplitscreenButton";
            _splitscreenButton.transform.SetSiblingIndex(settingsIndex + 1);

            // Strip all non-UI scripts from the clone. The cloned Settings button has
            // Valheim-specific components (ButtonSfx, UITooltip, animation triggers, etc.)
            // that still reference the Settings panel and will open it on click.
            StripNonEssentialComponents(_splitscreenButton);

            // Change the label
            SetButtonLabel(_splitscreenButton, "Splitscreen");

            // Wire up the click handler — replace onClick entirely to clear
            // persistent (serialized) listeners copied from the Settings button
            var button = _splitscreenButton.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(OnSplitscreenButtonClicked);
            }

            // Wire into gamepad navigation chain so controller can reach this button.
            // Insert between Settings and whatever was below it.
            SetupControllerNavigation(settingsButton, button);

            Debug.Log($"[Splitscreen][Menu] Splitscreen button created at sibling index {settingsIndex + 1}");
            UpdateButtonLabel();
        }

        /// <summary>
        /// Remove all MonoBehaviour components from the cloned button that aren't
        /// essential for rendering and interaction. Keeps: Button, Image, Text/TMP,
        /// layout components, CanvasRenderer. Destroys everything else (ButtonSfx,
        /// UITooltip, event triggers, animation scripts, etc.) so clicking the
        /// cloned button doesn't also trigger the original Settings behavior.
        /// </summary>
        private static void StripNonEssentialComponents(GameObject root)
        {
            int stripped = 0;
            var allComponents = root.GetComponentsInChildren<Component>(true);
            foreach (var comp in allComponents)
            {
                if (comp == null) continue;
                // Keep essential UI components
                if (comp is RectTransform) continue;
                if (comp is CanvasRenderer) continue;
                if (comp is Button) continue;
                if (comp is Image) continue;
                if (comp is Text) continue;
                if (comp is LayoutElement) continue;
                if (comp is LayoutGroup) continue;
                if (comp is ContentSizeFitter) continue;
                if (comp is Selectable) continue;
                if (comp is Mask) continue;
                if (comp is RectMask2D) continue;

                // Keep TextMeshPro (check by type name since it's in a separate assembly)
                string typeName = comp.GetType().Name;
                if (typeName.Contains("TMP_") || typeName.Contains("TextMeshPro")) continue;

                // Destroy everything else (ButtonSfx, UITooltip, EventTrigger, Animator, etc.)
                if (comp is MonoBehaviour mb)
                {
                    Object.Destroy(mb);
                    stripped++;
                }
                else if (comp is Animator anim)
                {
                    Object.Destroy(anim);
                    stripped++;
                }
            }
            Debug.Log($"[Splitscreen][Menu] Stripped {stripped} non-essential components from cloned button");
        }

        /// <summary>
        /// Insert the splitscreen button into the gamepad navigation chain.
        /// Settings → Splitscreen → (whatever Settings originally pointed down to).
        /// </summary>
        private static void SetupControllerNavigation(Button settingsBtn, Button splitBtn)
        {
            if (settingsBtn == null || splitBtn == null) return;

            // Get Settings' current navigation
            var settingsNav = settingsBtn.navigation;
            Selectable belowSettings = settingsNav.selectOnDown;

            // Settings → down → Splitscreen
            settingsNav.mode = Navigation.Mode.Explicit;
            settingsNav.selectOnDown = splitBtn;
            settingsBtn.navigation = settingsNav;

            // Splitscreen → up → Settings, down → whatever was below Settings
            var splitNav = splitBtn.navigation;
            splitNav.mode = Navigation.Mode.Explicit;
            splitNav.selectOnUp = settingsBtn;
            splitNav.selectOnDown = belowSettings;
            // Copy left/right from Settings so horizontal navigation still works
            splitNav.selectOnLeft = settingsNav.selectOnLeft;
            splitNav.selectOnRight = settingsNav.selectOnRight;
            splitBtn.navigation = splitNav;

            // Whatever was below Settings → up → Splitscreen (instead of Settings)
            if (belowSettings != null)
            {
                var belowNav = belowSettings.navigation;
                if (belowNav.mode == Navigation.Mode.Explicit)
                {
                    belowNav.selectOnUp = splitBtn;
                    belowSettings.navigation = belowNav;
                }
            }

            Debug.Log($"[Splitscreen][Menu] Navigation: Settings↓Splitscreen↓{belowSettings?.gameObject.name ?? "null"}");
        }

        private static void OnSplitscreenButtonClicked()
        {
            Debug.Log("[Splitscreen][Menu] Splitscreen button clicked");
            var mgr = SplitScreenManager.Instance;
            if (mgr == null)
            {
                Debug.LogWarning("[Splitscreen][Menu] SplitScreenManager not found");
                return;
            }

            // Toggle through states
            switch (mgr.State)
            {
                case SplitscreenState.Disabled:
                    // Enter menu split mode
                    Debug.Log("[Splitscreen][Menu] Entering MenuSplit from button");
                    mgr.State = SplitscreenState.MenuSplit;
                    var config = SplitscreenPlugin.Instance?.SplitConfig;
                    bool horizontal = config?.Orientation?.Value == ValheimSplitscreen.Config.SplitOrientation.Horizontal;
                    mgr.MenuSplit.Activate(horizontal);
                    mgr.CharacterSelect.IsMenuSplitMode = true;
                    mgr.CharacterSelect.IsMainMenuMode = false;
                    mgr.CharacterSelect.Show(
                        onSelected: (profile) =>
                        {
                            mgr.OnP2CharacterSelected(profile);
                            mgr.MenuSplit.SetP2Ready(profile?.GetName() ?? "New Character");
                            UpdateButtonLabel();
                        },
                        onCancelled: () =>
                        {
                            mgr.MenuSplit.Deactivate();
                            mgr.State = SplitscreenState.Disabled;
                            UpdateButtonLabel();
                        }
                    );
                    UpdateButtonLabel();
                    break;

                case SplitscreenState.MenuSplit:
                    // Cancel menu split
                    mgr.CharacterSelect.Hide();
                    mgr.MenuSplit.Deactivate();
                    mgr.State = SplitscreenState.Disabled;
                    mgr.PendingP2Profile = null;
                    UpdateButtonLabel();
                    break;

                case SplitscreenState.PendingCharSelect:
                    // Legacy cancel
                    mgr.CharacterSelect.Hide();
                    mgr.State = SplitscreenState.Disabled;
                    UpdateButtonLabel();
                    break;

                case SplitscreenState.Armed:
                    // Disarm and restore full screen
                    mgr.MenuSplit.Deactivate();
                    mgr.State = SplitscreenState.Disabled;
                    mgr.PendingP2Profile = null;
                    UpdateButtonLabel();
                    break;
            }
        }

        private static void UpdateButtonLabel()
        {
            if (_splitscreenButton == null) return;
            var mgr = SplitScreenManager.Instance;
            if (mgr == null) return;

            string label;
            switch (mgr.State)
            {
                case SplitscreenState.Armed:
                    string name = mgr.PendingP2Profile?.GetName() ?? "New Character";
                    label = $"Splitscreen: {name}";
                    break;
                case SplitscreenState.MenuSplit:
                    label = "Splitscreen: Selecting...";
                    break;
                case SplitscreenState.PendingCharSelect:
                    label = "Splitscreen: Selecting...";
                    break;
                default:
                    label = "Splitscreen";
                    break;
            }

            SetButtonLabel(_splitscreenButton, label);
        }

        /// <summary>Get the text label from a button (supports both legacy Text and TextMeshPro).</summary>
        private static string GetButtonLabel(Button btn)
        {
            // Try legacy Text first
            var text = btn.GetComponentInChildren<Text>(true);
            if (text != null) return text.text;

            // Try TextMeshPro
            var tmpType = System.Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
            if (tmpType != null)
            {
                var tmp = btn.GetComponentInChildren(tmpType, true);
                if (tmp != null)
                {
                    var textProp = tmpType.GetProperty("text");
                    if (textProp != null)
                        return textProp.GetValue(tmp) as string;
                }
            }

            return null;
        }

        /// <summary>Set the text label on a button (supports both legacy Text and TextMeshPro).</summary>
        private static void SetButtonLabel(GameObject buttonObj, string label)
        {
            // Try legacy Text
            var text = buttonObj.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = label;
                return;
            }

            // Try TextMeshPro
            var tmpType = System.Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
            if (tmpType != null)
            {
                var tmp = buttonObj.GetComponentInChildren(tmpType, true);
                if (tmp != null)
                {
                    var textProp = tmpType.GetProperty("text");
                    textProp?.SetValue(tmp, label);
                }
            }
        }

        /// <summary>Debug helper: dump UI hierarchy to log.</summary>
        private static void DumpHierarchy(Transform t, int depth)
        {
            if (depth > 5) return; // Limit depth
            string indent = new string(' ', depth * 2);
            var btn = t.GetComponent<Button>();
            string extra = btn != null ? $" [BUTTON label='{GetButtonLabel(btn)}']" : "";
            Debug.Log($"[Splitscreen][Menu] {indent}{t.name}{extra}");
            for (int i = 0; i < t.childCount && i < 20; i++)
            {
                DumpHierarchy(t.GetChild(i), depth + 1);
            }
        }

        /// <summary>Clean up when scene changes.</summary>
        [HarmonyPatch(typeof(FejdStartup), "OnDestroy")]
        [HarmonyPostfix]
        public static void FejdStartup_OnDestroy_Postfix()
        {
            _splitscreenButton = null;
            // Clean up menu split if still active when leaving main menu
            SplitScreenManager.Instance?.MenuSplit?.Deactivate();
        }
    }
}
