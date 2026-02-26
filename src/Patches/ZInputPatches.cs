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
        /// Get the gamepad that Player 1 should use, or null if Player 1 is on keyboard.
        /// </summary>
        private static Gamepad GetPlayer1Gamepad()
        {
            var inputMgr = SplitInputManager.Instance;
            if (inputMgr == null) return null;

            // If Player 1 uses keyboard (< 2 gamepads), they have NO gamepad
            if (inputMgr.Player1UsesKeyboard) return null;

            // With 2+ gamepads, Player 1 uses Gamepad 0
            if (Gamepad.all.Count > 0) return Gamepad.all[0];
            return null;
        }

        /// <summary>
        /// Determines if a Joy-prefixed (gamepad) button read should be blocked.
        /// Blocks when Player 1 is on keyboard UNLESS we're currently executing
        /// Player 2's Update/FixedUpdate (detected by m_localPlayer being swapped to P2).
        /// </summary>
        private static bool ShouldBlockJoyButton(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!name.StartsWith("Joy", StringComparison.Ordinal)) return false;

            var inputMgr = SplitInputManager.Instance;
            if (inputMgr == null) return false;

            // When Player 1 is on keyboard, block gamepad button reads in P1's context
            if (inputMgr.Player1UsesKeyboard)
            {
                // Allow through if we're in Player 2's update context
                // (m_localPlayer is temporarily swapped to P2 during their Update/FixedUpdate)
                var mgr = SplitScreenManager.Instance?.PlayerManager;
                if (mgr?.Player2 != null && global::Player.m_localPlayer == mgr.Player2)
                {
                    return false; // P2's code is running, let Joy buttons through
                }
                return true; // P1's context, block gamepad buttons
            }

            return false;
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

            float original = __result;
            var gp = GetPlayer1Gamepad();
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

            var gp = GetPlayer1Gamepad();
            if (gp != null)
            {
                float val = gp.leftStick.y.ReadValue();
                // Pass through raw value - vanilla ZInput already applies any needed transformations
                __result = Mathf.Abs(val) > 0.15f ? val : 0f;
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

            var gp = GetPlayer1Gamepad();
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

            var gp = GetPlayer1Gamepad();
            if (gp != null)
            {
                float val = gp.rightStick.y.ReadValue();
                // Pass through raw value - vanilla ZInput already applies any needed transformations
                __result = Mathf.Abs(val) > 0.1f ? val : 0f;
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
            var gp = GetPlayer1Gamepad();
            __result = gp?.leftTrigger.ReadValue() ?? 0f;
        }

        [HarmonyPatch(typeof(ZInput), "GetJoyRTrigger")]
        [HarmonyPostfix]
        public static void GetJoyRTrigger_Postfix(ref float __result)
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return;
            var gp = GetPlayer1Gamepad();
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
            if (SplitInputManager.Instance != null && SplitInputManager.Instance.Player1UsesKeyboard)
            {
                __result = false;
            }
            else
            {
                __result = true;
            }

            if (Time.time - _lastModeLogTime > 15f)
            {
                _lastModeLogTime = Time.time;
                Debug.Log($"[Splitscreen][ZInput] IsGamepadActive: original={original} -> {__result}, P1Keyboard={SplitInputManager.Instance?.Player1UsesKeyboard}, gamepads={Gamepad.all.Count}");
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

            if (SplitInputManager.Instance != null && SplitInputManager.Instance.Player1UsesKeyboard)
            {
                // Player 1 uses mouse - keep it active
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

            if (SplitInputManager.Instance != null && SplitInputManager.Instance.Player1UsesKeyboard)
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

            if (SplitInputManager.Instance != null && SplitInputManager.Instance.Player1UsesKeyboard)
            {
                return; // Let scroll through for Player 1
            }

            __result = 0f;
        }
    }
}
