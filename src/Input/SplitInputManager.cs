using UnityEngine;
using UnityEngine.InputSystem;
using ValheimSplitscreen.Config;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.Input
{
    /// <summary>
    /// Manages input routing for splitscreen.
    ///
    /// KeyboardMouse mode (default): Player 1 = Keyboard+Mouse, Player 2 = first gamepad (or IJKL fallback)
    /// Gamepad mode:                 Player 1 = Gamepad 0, Player 2 = Gamepad 1
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SplitInputManager : MonoBehaviour
    {
        public static SplitInputManager Instance { get; private set; }

        private PlayerInputState[] _playerInputs = new PlayerInputState[2];

        // Rate-limit logging
        private float _lastInputLogTime;

        /// <summary>
        /// How many gamepads are available.
        /// </summary>
        public int GamepadCount => Gamepad.all.Count;

        /// <summary>
        /// True if Player 1 uses keyboard+mouse instead of a gamepad.
        /// Determined by config setting (default: KeyboardMouse).
        /// When true, ZInput patches let keyboard/mouse through and zero out gamepad reads.
        /// </summary>
        public bool Player1UsesKeyboard
        {
            get
            {
                var config = SplitscreenPlugin.Instance?.SplitConfig;
                if (config == null) return true; // default to keyboard
                // In shared controller mode, P1 uses the gamepad too
                if (config.SharedController.Value) return false;
                return config.P1InputMode.Value == Player1InputMode.KeyboardMouse;
            }
        }

        /// <summary>
        /// True if shared controller mode is active — both players use the same gamepad.
        /// Useful for testing with a single controller.
        /// </summary>
        public bool SharedControllerMode
        {
            get
            {
                var config = SplitscreenPlugin.Instance?.SplitConfig;
                return config != null && config.SharedController.Value;
            }
        }

        /// <summary>
        /// Get the gamepad assigned to a player index based on current routing mode.
        /// </summary>
        public Gamepad GetGamepad(int playerIndex)
        {
            int count = Gamepad.all.Count;
            if (count == 0) return null;

            // Shared controller: both players use the same gamepad
            if (SharedControllerMode)
            {
                return Gamepad.all[0];
            }

            if (Player1UsesKeyboard)
            {
                // P1 = keyboard, P2 = first available gamepad
                if (playerIndex == 1 && count >= 1) return Gamepad.all[0];
                return null; // P1 has no gamepad
            }
            else
            {
                // P1 = gamepad 0, P2 = gamepad 1
                if (playerIndex == 0 && count >= 1) return Gamepad.all[0];
                if (playerIndex == 1 && count >= 2) return Gamepad.all[1];
                return null;
            }
        }

        public PlayerInputState GetInputState(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= 2) return _playerInputs[0];
            return _playerInputs[playerIndex];
        }

        private void Awake()
        {
            Instance = this;
            _playerInputs[0] = new PlayerInputState();
            _playerInputs[1] = new PlayerInputState();
        }

        private void Update()
        {
            if (SplitScreenManager.Instance == null || !SplitScreenManager.Instance.SplitscreenActive) return;

            if (SharedControllerMode)
            {
                // Both players read from the same gamepad
                var gp = Gamepad.all.Count > 0 ? Gamepad.all[0] : null;
                if (gp != null)
                {
                    _playerInputs[0].ReadFromGamepad(gp);
                    _playerInputs[1].ReadFromGamepad(gp);
                }
                else
                {
                    _playerInputs[0].Clear();
                    _playerInputs[1].ReadFromKeyboardFallback();
                }
            }
            else
            {
                // Player 1 input: only needs state tracking if using a gamepad
                var gp0 = GetGamepad(0);
                if (gp0 != null)
                    _playerInputs[0].ReadFromGamepad(gp0);
                else
                    _playerInputs[0].Clear(); // Player 1 on keyboard, handled by normal ZInput

                // Player 2 input: gamepad if available, otherwise keyboard fallback
                var gp1 = GetGamepad(1);
                if (gp1 != null)
                    _playerInputs[1].ReadFromGamepad(gp1);
                else
                    _playerInputs[1].ReadFromKeyboardFallback(); // IJKL + numpad keys
            }

            // Periodic logging of input state
            if (Time.time - _lastInputLogTime > 15f)
            {
                _lastInputLogTime = Time.time;
                var p2input = _playerInputs[1];
                var p2gp = GetGamepad(1);
                string p2Desc = p2gp != null ? p2gp.displayName : "KeyboardFallback";
                Debug.Log($"[Splitscreen][Input] Gamepads={GamepadCount}, P1={( Player1UsesKeyboard ? "KB+Mouse" : "Gamepad0")}, P2={p2Desc}, SharedCtrl={SharedControllerMode}");
                Debug.Log($"[Splitscreen][Input] P2 raw input: move=({p2input.MoveAxis.x:F2},{p2input.MoveAxis.y:F2}), look=({p2input.LookAxis.x:F2},{p2input.LookAxis.y:F2}), A={p2input.ButtonSouth}, B={p2input.ButtonEast}, RB={p2input.RightShoulder}, LB={p2input.LeftShoulder}");
            }
        }

        public void OnSplitscreenActivated()
        {
            Debug.Log($"[Splitscreen][Input] Activated - gamepads={GamepadCount}, P1UsesKeyboard={Player1UsesKeyboard}, P1InputMode={SplitscreenPlugin.Instance?.SplitConfig?.P1InputMode?.Value}");
            for (int i = 0; i < Gamepad.all.Count; i++)
            {
                Debug.Log($"[Splitscreen][Input]   Gamepad[{i}]: {Gamepad.all[i].displayName} (id={Gamepad.all[i].deviceId})");
            }
            _playerInputs[0].Clear();
            _playerInputs[1].Clear();
        }

        public void OnSplitscreenDeactivated()
        {
            Debug.Log("[Splitscreen][Input] Deactivated");
            _playerInputs[0].Clear();
            _playerInputs[1].Clear();
        }

        private void OnDestroy()
        {
            Instance = null;
        }
    }
}
