using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using ValheimSplitscreen.Core;

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

            // Change the label
            SetButtonLabel(_splitscreenButton, "Splitscreen");

            // Wire up the click handler
            var button = _splitscreenButton.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OnSplitscreenButtonClicked);
            }

            Debug.Log($"[Splitscreen][Menu] Splitscreen button created at sibling index {settingsIndex + 1}");
            UpdateButtonLabel();
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
                    // Open P2 character select
                    mgr.State = SplitscreenState.PendingCharSelect;
                    mgr.CharacterSelect.IsMainMenuMode = true;
                    mgr.CharacterSelect.Show(
                        onSelected: (profile) =>
                        {
                            mgr.OnP2CharacterSelected(profile);
                            UpdateButtonLabel();
                        },
                        onCancelled: () =>
                        {
                            mgr.State = SplitscreenState.Disabled;
                            UpdateButtonLabel();
                        }
                    );
                    break;

                case SplitscreenState.PendingCharSelect:
                    // Cancel character select
                    mgr.CharacterSelect.Hide();
                    mgr.State = SplitscreenState.Disabled;
                    UpdateButtonLabel();
                    break;

                case SplitscreenState.Armed:
                    // Disarm
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
        }
    }
}
