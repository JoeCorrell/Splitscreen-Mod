using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.Input;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Patches for ZInput to isolate gamepad input per player.
    ///
    /// With 2 gamepads: filter ZInput to only read Gamepad 0 for Player 1.
    /// With 1 gamepad:  Player 1 is on keyboard+mouse, so zero out ALL gamepad reads
    ///                  from ZInput (the one gamepad belongs to Player 2 only).
    /// With 0 gamepads: Player 1 is keyboard+mouse, no gamepad filtering needed.
    ///
    /// Player 2's input is always handled by SplitPlayerManager reading PlayerInputState directly,
    /// so these patches only affect Player 1's input pipeline through ZInput.
    /// </summary>
    [HarmonyPatch]
    public static class ZInputPatches
    {
        // Rate-limit logging for per-frame patches
        private static float _lastButtonBlockLogTime;
        private static float _lastStickLogTime;
        private static float _lastModeLogTime;
        private static int _blockedButtonCount;
        private static string _lastBlockedButton;

        /// <summary>
        /// Get the player index currently executing input code.
        /// Uses the temporary m_localPlayer swap done for Player 2 in PlayerPatches.
        /// </summary>
        private static int GetContextPlayerIndex()
        {
            var playerMgr = SplitScreenManager.Instance?.PlayerManager;
            if (playerMgr?.Player2 != null && global::Player.m_localPlayer == playerMgr.Player2)
            {
                return 1;
            }
            return 0;
        }

        /// <summary>
        /// Get the gamepad assigned to the player context currently being processed.
        /// </summary>
        private static Gamepad GetContextGamepad()
        {
            var inputMgr = SplitInputManager.Instance;
            if (inputMgr == null) return null;
            return inputMgr.GetGamepad(GetContextPlayerIndex());
        }

        /// <summary>
        /// Get the per-player input snapshot for the currently executing context.
        /// </summary>
        private static PlayerInputState GetContextInputState()
        {
            var inputMgr = SplitInputManager.Instance;
            if (inputMgr == null) return null;
            return inputMgr.GetInputState(GetContextPlayerIndex());
        }

        private static bool IsJoyButton(string name)
        {
            return !string.IsNullOrEmpty(name) && name.StartsWith("Joy", StringComparison.Ordinal);
        }

        /// <summary>
        /// Non-Joy action names that must be routed from the active player context,
        /// otherwise Player 1 keyboard input can leak into Player 2 logic when
        /// m_localPlayer is temporarily swapped.
        /// </summary>
        private static bool IsContextRoutedAction(string name)
        {
            switch (name)
            {
                case "Forward":
                case "Backward":
                case "Left":
                case "Right":
                case "Attack":
                case "SecondaryAttack":
                case "Block":
                case "Jump":
                case "Crouch":
                case "Run":
                case "AutoRun":
                case "Use":
                case "Inventory":
                case "Menu":
                case "MapZoomIn":
                case "MapZoomOut":
                case "Sit":
                case "Hide":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Determines if a Joy-prefixed (gamepad) button read should be blocked.
        /// Blocks only in Player 1 context when P1 is keyboard+mouse.
        /// </summary>
        private static bool ShouldBlockJoyButton(string name)
        {
            if (!IsJoyButton(name)) return false;

            var inputMgr = SplitInputManager.Instance;
            if (inputMgr == null) return false;

            return inputMgr.Player1UsesKeyboard && GetContextPlayerIndex() == 0;
        }

        // ──────────────── Button patches ────────────────

        [HarmonyPatch(typeof(ZInput), "GetButton")]
        [HarmonyPostfix]
        public static void GetButton_Postfix(string name, ref bool __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (ShouldBlockJoyButton(name))
            {
                if (__result) // Only log when we actually block a true value
                {
                    _blockedButtonCount++;
                    _lastBlockedButton = name;
                    if (Time.time - _lastButtonBlockLogTime > 5f)
                    {
                        Debug.Log($"[Splitscreen][ZInput] BLOCKED {_blockedButtonCount} Joy button reads in last 5s (last: GetButton('{_lastBlockedButton}')=true->false)");
                        _lastButtonBlockLogTime = Time.time;
                        _blockedButtonCount = 0;
                    }
                }
                __result = false;
                return;
            }

            int context = GetContextPlayerIndex();
            bool shouldRoute = IsJoyButton(name) || (context == 1 && IsContextRoutedAction(name));
            if (shouldRoute)
            {
                var state = GetContextInputState();
                if (state != null)
                {
                    __result = state.GetButton(name);
                }
            }
        }

        [HarmonyPatch(typeof(ZInput), "GetButtonDown")]
        [HarmonyPostfix]
        public static void GetButtonDown_Postfix(string name, ref bool __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (ShouldBlockJoyButton(name))
            {
                if (__result)
                {
                    Debug.Log($"[Splitscreen][ZInput] BLOCKED GetButtonDown('{name}')=true->false in P1 context");
                }
                __result = false;
                return;
            }

            int context = GetContextPlayerIndex();
            bool shouldRoute = IsJoyButton(name) || (context == 1 && IsContextRoutedAction(name));
            if (shouldRoute)
            {
                var state = GetContextInputState();
                if (state != null)
                {
                    __result = state.GetButtonDown(name);
                }
            }
        }

        [HarmonyPatch(typeof(ZInput), "GetButtonPressedTimer")]
        [HarmonyPostfix]
        public static void GetButtonPressedTimer_Postfix(string name, ref float __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (ShouldBlockJoyButton(name))
            {
                __result = 0f;
                return;
            }

            int context = GetContextPlayerIndex();
            bool shouldRoute = IsJoyButton(name) || (context == 1 && IsContextRoutedAction(name));
            if (!shouldRoute) return;

            var state = GetContextInputState();
            if (state != null)
            {
                __result = state.GetButtonPressedTimer(name);
            }
        }

        [HarmonyPatch(typeof(ZInput), "GetButtonUp")]
        [HarmonyPostfix]
        public static void GetButtonUp_Postfix(string name, ref bool __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            if (ShouldBlockJoyButton(name)) __result = false;
        }

        // ──────────────── Stick patches ────────────────

        [HarmonyPatch(typeof(ZInput), "GetJoyLeftStickX")]
        [HarmonyPostfix]
        public static void GetJoyLeftStickX_Postfix(ref float __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            int context = GetContextPlayerIndex();
            var contextState = GetContextInputState();
            if (context == 1 && contextState != null)
            {
                __result = contextState.GetJoyLeftStickX();
                return;
            }

            float original = __result;
            var gp = GetContextGamepad();
            if (gp != null)
            {
                float val = gp.leftStick.x.ReadValue();
                __result = Mathf.Abs(val) > 0.15f ? val : 0f;
            }
            else
            {
                __result = 0f; // Player 1 on keyboard, zero out gamepad input
            }

            if (Time.time - _lastStickLogTime > 10f && (Mathf.Abs(original) > 0.1f || Mathf.Abs(__result) > 0.1f))
            {
                _lastStickLogTime = Time.time;
                Debug.Log($"[Splitscreen][ZInput] Stick override: LeftStickX original={original:F2} -> {__result:F2}, P1Keyboard={SplitInputManager.Instance?.Player1UsesKeyboard}");
            }
        }

        [HarmonyPatch(typeof(ZInput), "GetJoyLeftStickY")]
        [HarmonyPostfix]
        public static void GetJoyLeftStickY_Postfix(ref float __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            int context = GetContextPlayerIndex();
            var contextState = GetContextInputState();
            if (context == 1 && contextState != null)
            {
                __result = contextState.GetJoyLeftStickY();
                return;
            }

            var gp = GetContextGamepad();
            if (gp != null)
            {
                float val = gp.leftStick.y.ReadValue();
                // Vanilla ZInput returns inverted Y for sticks.
                __result = Mathf.Abs(val) > 0.15f ? -val : 0f;
            }
            else
            {
                __result = 0f;
            }
        }

        [HarmonyPatch(typeof(ZInput), "GetJoyRightStickX")]
        [HarmonyPostfix]
        public static void GetJoyRightStickX_Postfix(ref float __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            int context = GetContextPlayerIndex();
            var contextState = GetContextInputState();
            if (context == 1 && contextState != null)
            {
                __result = contextState.GetJoyRightStickX();
                return;
            }

            var gp = GetContextGamepad();
            if (gp != null)
            {
                float val = gp.rightStick.x.ReadValue();
                __result = Mathf.Abs(val) > 0.1f ? val : 0f;
            }
            else
            {
                __result = 0f;
            }
        }

        [HarmonyPatch(typeof(ZInput), "GetJoyRightStickY")]
        [HarmonyPostfix]
        public static void GetJoyRightStickY_Postfix(ref float __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            int context = GetContextPlayerIndex();
            var contextState = GetContextInputState();
            if (context == 1 && contextState != null)
            {
                __result = contextState.GetJoyRightStickY();
                return;
            }

            var gp = GetContextGamepad();
            if (gp != null)
            {
                float val = gp.rightStick.y.ReadValue();
                // Vanilla ZInput returns inverted Y for sticks.
                __result = Mathf.Abs(val) > 0.1f ? -val : 0f;
            }
            else
            {
                __result = 0f;
            }
        }

        // ──────────────── Trigger patches ────────────────

        [HarmonyPatch(typeof(ZInput), "GetJoyLTrigger")]
        [HarmonyPostfix]
        public static void GetJoyLTrigger_Postfix(ref float __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            int context = GetContextPlayerIndex();
            var contextState = GetContextInputState();
            if (context == 1 && contextState != null)
            {
                __result = contextState.GetJoyLTrigger();
                return;
            }

            var gp = GetContextGamepad();
            __result = gp?.leftTrigger.ReadValue() ?? 0f;
        }

        [HarmonyPatch(typeof(ZInput), "GetJoyRTrigger")]
        [HarmonyPostfix]
        public static void GetJoyRTrigger_Postfix(ref float __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            int context = GetContextPlayerIndex();
            var contextState = GetContextInputState();
            if (context == 1 && contextState != null)
            {
                __result = contextState.GetJoyRTrigger();
                return;
            }

            var gp = GetContextGamepad();
            __result = gp?.rightTrigger.ReadValue() ?? 0f;
        }

        // ──────────────── Input mode patches ────────────────

        /// <summary>
        /// When Player 1 is on keyboard, keep gamepad INACTIVE so ZInput
        /// uses keyboard+mouse for Player 1's controls normally.
        /// When both players have gamepads, force gamepad active.
        /// </summary>
        [HarmonyPatch(typeof(ZInput), "IsGamepadActive")]
        [HarmonyPostfix]
        public static void IsGamepadActive_Postfix(ref bool __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            bool original = __result;
            var inputMgr = SplitInputManager.Instance;
            if (inputMgr == null) return;

            int context = GetContextPlayerIndex();
            if (context == 0)
            {
                __result = !inputMgr.Player1UsesKeyboard;
            }
            else
            {
                __result = inputMgr.GetGamepad(1) != null;
            }

            if (Time.time - _lastModeLogTime > 15f)
            {
                _lastModeLogTime = Time.time;
                Debug.Log($"[Splitscreen][ZInput] IsGamepadActive: original={original} -> {__result}, context=P{context + 1}, P1Keyboard={inputMgr.Player1UsesKeyboard}, gamepads={Gamepad.all.Count}");
            }
        }

        /// <summary>
        /// When Player 1 is on keyboard, keep mouse active for their look controls.
        /// </summary>
        [HarmonyPatch(typeof(ZInput), "IsMouseActive")]
        [HarmonyPostfix]
        public static void IsMouseActive_Postfix(ref bool __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var inputMgr = SplitInputManager.Instance;
            if (inputMgr == null) return;

            // Mouse is only for Player 1 in keyboard mode.
            if (GetContextPlayerIndex() == 0 && inputMgr.Player1UsesKeyboard)
            {
                return;
            }

            __result = false;
        }

        /// <summary>
        /// When Player 1 is on keyboard, let mouse delta through normally for their camera.
        /// When both on gamepads, zero out mouse.
        /// </summary>
        [HarmonyPatch(typeof(ZInput), "GetMouseDelta")]
        [HarmonyPostfix]
        public static void GetMouseDelta_Postfix(ref Vector2 __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var inputMgr = SplitInputManager.Instance;
            if (inputMgr == null) return;

            if (GetContextPlayerIndex() == 0 && inputMgr.Player1UsesKeyboard)
            {
                return; // Let mouse delta through for Player 1
            }

            __result = Vector2.zero;
        }

        /// <summary>
        /// When Player 1 is on keyboard, let scroll through for zoom.
        /// </summary>
        [HarmonyPatch(typeof(ZInput), "GetMouseScrollWheel")]
        [HarmonyPostfix]
        public static void GetMouseScrollWheel_Postfix(ref float __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;

            var inputMgr = SplitInputManager.Instance;
            if (inputMgr == null) return;

            if (GetContextPlayerIndex() == 0 && inputMgr.Player1UsesKeyboard)
            {
                return; // Let scroll through for Player 1
            }

            __result = 0f;
        }
    }
}
