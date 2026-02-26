using UnityEngine;
using UnityEngine.InputSystem;

namespace ValheimSplitscreen.Input
{
    /// <summary>
    /// Captures a snapshot of one gamepad's input state each frame.
    /// Used to feed into the per-player PlayerController / camera look logic.
    /// </summary>
    public class PlayerInputState
    {
        // Movement (left stick)
        public Vector2 MoveAxis;

        // Camera look (right stick)
        public Vector2 LookAxis;

        // Triggers
        public float LeftTrigger;
        public float RightTrigger;

        // Buttons - current frame state
        public bool ButtonSouth;       // A / Cross - Jump
        public bool ButtonWest;        // X / Square - Secondary Attack
        public bool ButtonNorth;       // Y / Triangle - alt keys
        public bool ButtonEast;        // B / Circle - use/dodge

        public bool LeftShoulder;      // LB - Block
        public bool RightShoulder;     // RB - Attack

        public bool DpadUp;
        public bool DpadDown;
        public bool DpadLeft;
        public bool DpadRight;

        public bool LeftStickPress;
        public bool RightStickPress;

        public bool StartButton;
        public bool SelectButton;

        // Button pressed this frame (edge detection)
        public bool ButtonSouthDown;
        public bool ButtonWestDown;
        public bool ButtonNorthDown;
        public bool ButtonEastDown;
        public bool LeftShoulderDown;
        public bool RightShoulderDown;
        public bool DpadUpDown;
        public bool DpadDownDown;
        public bool DpadLeftDown;
        public bool DpadRightDown;
        public bool LeftStickPressDown;
        public bool RightStickPressDown;
        public bool StartButtonDown;
        public bool SelectButtonDown;

        // Previous frame state for edge detection
        private bool _prevSouth, _prevWest, _prevNorth, _prevEast;
        private bool _prevLB, _prevRB;
        private bool _prevRT, _prevLT; // Trigger edge detection
        private bool _prevUp, _prevDown, _prevLeft, _prevRight;
        private bool _prevLS, _prevRS;
        private bool _prevStart, _prevSelect;

        public void ReadFromGamepad(Gamepad gp)
        {
            if (gp == null) { Clear(); return; }

            MoveAxis = gp.leftStick.ReadValue();
            LookAxis = gp.rightStick.ReadValue();

            // Apply a small deadzone
            if (MoveAxis.magnitude < 0.15f) MoveAxis = Vector2.zero;
            if (LookAxis.magnitude < 0.1f) LookAxis = Vector2.zero;

            LeftTrigger = gp.leftTrigger.ReadValue();
            RightTrigger = gp.rightTrigger.ReadValue();

            // Current frame held state
            ButtonSouth = gp.buttonSouth.isPressed;
            ButtonWest = gp.buttonWest.isPressed;
            ButtonNorth = gp.buttonNorth.isPressed;
            ButtonEast = gp.buttonEast.isPressed;
            LeftShoulder = gp.leftShoulder.isPressed;
            RightShoulder = gp.rightShoulder.isPressed;
            DpadUp = gp.dpad.up.isPressed;
            DpadDown = gp.dpad.down.isPressed;
            DpadLeft = gp.dpad.left.isPressed;
            DpadRight = gp.dpad.right.isPressed;
            LeftStickPress = gp.leftStickButton.isPressed;
            RightStickPress = gp.rightStickButton.isPressed;
            StartButton = gp.startButton.isPressed;
            SelectButton = gp.selectButton.isPressed;

            // Edge detection: pressed this frame
            ButtonSouthDown = ButtonSouth && !_prevSouth;
            ButtonWestDown = ButtonWest && !_prevWest;
            ButtonNorthDown = ButtonNorth && !_prevNorth;
            ButtonEastDown = ButtonEast && !_prevEast;
            LeftShoulderDown = LeftShoulder && !_prevLB;
            RightShoulderDown = RightShoulder && !_prevRB;
            DpadUpDown = DpadUp && !_prevUp;
            DpadDownDown = DpadDown && !_prevDown;
            DpadLeftDown = DpadLeft && !_prevLeft;
            DpadRightDown = DpadRight && !_prevRight;
            LeftStickPressDown = LeftStickPress && !_prevLS;
            RightStickPressDown = RightStickPress && !_prevRS;
            StartButtonDown = StartButton && !_prevStart;
            SelectButtonDown = SelectButton && !_prevSelect;

            // Store for next frame
            _prevSouth = ButtonSouth;
            _prevWest = ButtonWest;
            _prevNorth = ButtonNorth;
            _prevEast = ButtonEast;
            _prevLB = LeftShoulder;
            _prevRB = RightShoulder;
            _prevRT = RightTrigger > 0.4f;
            _prevLT = LeftTrigger > 0.4f;
            _prevUp = DpadUp;
            _prevDown = DpadDown;
            _prevLeft = DpadLeft;
            _prevRight = DpadRight;
            _prevLS = LeftStickPress;
            _prevRS = RightStickPress;
            _prevStart = StartButton;
            _prevSelect = SelectButton;
        }

        public void Clear()
        {
            MoveAxis = Vector2.zero;
            LookAxis = Vector2.zero;
            LeftTrigger = 0;
            RightTrigger = 0;
            ButtonSouth = ButtonWest = ButtonNorth = ButtonEast = false;
            LeftShoulder = RightShoulder = false;
            DpadUp = DpadDown = DpadLeft = DpadRight = false;
            LeftStickPress = RightStickPress = false;
            StartButton = SelectButton = false;
            ButtonSouthDown = ButtonWestDown = ButtonNorthDown = ButtonEastDown = false;
            LeftShoulderDown = RightShoulderDown = false;
            DpadUpDown = DpadDownDown = DpadLeftDown = DpadRightDown = false;
            LeftStickPressDown = RightStickPressDown = false;
            StartButtonDown = SelectButtonDown = false;
            _prevSouth = _prevWest = _prevNorth = _prevEast = false;
            _prevLB = _prevRB = false;
            _prevRT = _prevLT = false;
            _prevUp = _prevDown = _prevLeft = _prevRight = false;
            _prevLS = _prevRS = false;
            _prevStart = _prevSelect = false;
        }

        /// <summary>
        /// Reads Player 2 input from keyboard when no second gamepad is available.
        /// Layout:
        ///   Move:   I/J/K/L
        ///   Look:   Numpad 4/6/8/2
        ///   Attack: Numpad 0          Block: Numpad Enter
        ///   Jump:   Right Shift       Dodge: Right Ctrl
        ///   Use:    Numpad 5           Menu: Numpad +
        ///   Run:    Numpad .           Crouch: Numpad 1
        /// </summary>
        public void ReadFromKeyboardFallback()
        {
            var kb = Keyboard.current;
            if (kb == null) { Clear(); return; }

            // Movement: IJKL
            MoveAxis = Vector2.zero;
            if (kb.iKey.isPressed) MoveAxis.y = -1f;  // forward (Valheim Y is inverted for sticks)
            if (kb.kKey.isPressed) MoveAxis.y = 1f;   // backward
            if (kb.jKey.isPressed) MoveAxis.x = -1f;
            if (kb.lKey.isPressed) MoveAxis.x = 1f;
            if (MoveAxis.magnitude > 1f) MoveAxis.Normalize();

            // Look: Numpad arrows
            LookAxis = Vector2.zero;
            if (kb.numpad8Key.isPressed) LookAxis.y = -0.7f;
            if (kb.numpad2Key.isPressed) LookAxis.y = 0.7f;
            if (kb.numpad4Key.isPressed) LookAxis.x = -0.7f;
            if (kb.numpad6Key.isPressed) LookAxis.x = 0.7f;

            // Triggers
            LeftTrigger = kb.numpadEnterKey.isPressed ? 1f : 0f;   // Block
            RightTrigger = kb.numpad3Key.isPressed ? 1f : 0f;      // Secondary attack

            // Buttons
            ButtonSouth = kb.rightShiftKey.isPressed;   // Jump
            ButtonWest = kb.numpad5Key.isPressed;        // Use/Interact
            ButtonNorth = kb.numpad7Key.isPressed;       // Alt keys
            ButtonEast = kb.rightCtrlKey.isPressed;      // Dodge
            LeftShoulder = kb.numpadEnterKey.isPressed;  // Block
            RightShoulder = kb.numpad0Key.isPressed;     // Attack
            DpadUp = kb.upArrowKey.isPressed;
            DpadDown = kb.downArrowKey.isPressed;
            DpadLeft = kb.leftArrowKey.isPressed;
            DpadRight = kb.rightArrowKey.isPressed;
            LeftStickPress = kb.numpadPeriodKey.isPressed;  // Run
            RightStickPress = kb.numpad1Key.isPressed;      // Crouch
            StartButton = kb.numpadPlusKey.isPressed;       // Menu
            SelectButton = kb.numpadMinusKey.isPressed;     // Inventory

            // Edge detection
            ButtonSouthDown = ButtonSouth && !_prevSouth;
            ButtonWestDown = ButtonWest && !_prevWest;
            ButtonNorthDown = ButtonNorth && !_prevNorth;
            ButtonEastDown = ButtonEast && !_prevEast;
            LeftShoulderDown = LeftShoulder && !_prevLB;
            RightShoulderDown = RightShoulder && !_prevRB;
            DpadUpDown = DpadUp && !_prevUp;
            DpadDownDown = DpadDown && !_prevDown;
            DpadLeftDown = DpadLeft && !_prevLeft;
            DpadRightDown = DpadRight && !_prevRight;
            LeftStickPressDown = LeftStickPress && !_prevLS;
            RightStickPressDown = RightStickPress && !_prevRS;
            StartButtonDown = StartButton && !_prevStart;
            SelectButtonDown = SelectButton && !_prevSelect;

            // Store previous state
            _prevSouth = ButtonSouth;
            _prevWest = ButtonWest;
            _prevNorth = ButtonNorth;
            _prevEast = ButtonEast;
            _prevLB = LeftShoulder;
            _prevRB = RightShoulder;
            _prevRT = RightTrigger > 0.4f;
            _prevLT = LeftTrigger > 0.4f;
            _prevUp = DpadUp;
            _prevDown = DpadDown;
            _prevLeft = DpadLeft;
            _prevRight = DpadRight;
            _prevLS = LeftStickPress;
            _prevRS = RightStickPress;
            _prevStart = StartButton;
            _prevSelect = SelectButton;
        }

        /// <summary>
        /// Maps a Valheim ZInput button name to this player's gamepad state.
        /// Returns whether the button is currently held.
        /// </summary>
        public bool GetButton(string name)
        {
            switch (name)
            {
                case "JoyAttack":
                case "Attack":
                    return RightShoulder;
                case "JoySecondaryAttack":
                case "SecondaryAttack":
                    return RightTrigger > 0.4f;
                case "JoyBlock":
                case "Block":
                    return LeftShoulder;
                case "JoyJump":
                case "Jump":
                    return ButtonSouth;
                case "JoyCrouch":
                case "Crouch":
                    return RightStickPress;
                case "JoyRun":
                case "Run":
                    return LeftStickPress;
                case "JoyDodge":
                    return ButtonEast;
                case "Forward":
                    return MoveAxis.y > 0.3f;
                case "Backward":
                    return MoveAxis.y < -0.3f;
                case "Left":
                    return MoveAxis.x < -0.3f;
                case "Right":
                    return MoveAxis.x > 0.3f;
                case "JoyAltKeys":
                    return ButtonNorth;
                case "JoyCamZoomIn":
                    return DpadUp;
                case "JoyCamZoomOut":
                    return DpadDown;
                case "JoyTabLeft":
                    return DpadLeft;
                case "JoyTabRight":
                    return DpadRight;
                case "JoyButtonY":
                    return ButtonNorth;
                case "JoyButtonX":
                    return ButtonWest;
                case "JoyButtonB":
                    return ButtonEast;
                case "JoyButtonA":
                    return ButtonSouth;
                case "JoyLStick":
                    return LeftStickPress;
                case "JoyRStick":
                    return RightStickPress;
                case "JoyRotate":
                    return LeftTrigger > 0.4f;
                case "AutoRun":
                    return false;
                case "Inventory":
                    return SelectButton;
                case "JoyMenu":
                case "Menu":
                    return StartButton;
                case "JoyUse":
                case "Use":
                    return ButtonWest;
                case "JoyHide":
                    return DpadRight;
                case "JoySit":
                    return DpadDown;
                case "JoyGPower":
                    return ButtonNorth && LeftShoulder;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Maps a Valheim ZInput button name to pressed-this-frame.
        /// </summary>
        public bool GetButtonDown(string name)
        {
            switch (name)
            {
                case "JoyAttack":
                case "Attack":
                    return RightShoulderDown;
                case "JoySecondaryAttack":
                case "SecondaryAttack":
                    return RightTrigger > 0.4f && !_prevRT;
                case "JoyBlock":
                case "Block":
                    return LeftShoulderDown;
                case "JoyJump":
                case "Jump":
                    return ButtonSouthDown;
                case "JoyCrouch":
                case "Crouch":
                    return RightStickPressDown;
                case "JoyRun":
                case "Run":
                    return LeftStickPressDown;
                case "JoyDodge":
                    return ButtonEastDown;
                case "JoyAltKeys":
                    return ButtonNorthDown;
                case "JoyCamZoomIn":
                    return DpadUpDown;
                case "JoyCamZoomOut":
                    return DpadDownDown;
                case "JoyTabLeft":
                    return DpadLeftDown;
                case "JoyTabRight":
                    return DpadRightDown;
                case "JoyButtonY":
                    return ButtonNorthDown;
                case "JoyButtonX":
                    return ButtonWestDown;
                case "JoyButtonB":
                    return ButtonEastDown;
                case "JoyButtonA":
                    return ButtonSouthDown;
                case "JoyLStick":
                    return LeftStickPressDown;
                case "JoyRStick":
                    return RightStickPressDown;
                case "Inventory":
                    return SelectButtonDown;
                case "JoyMenu":
                case "Menu":
                    return StartButtonDown;
                case "JoyUse":
                case "Use":
                    return ButtonWestDown;
                default:
                    return false;
            }
        }

        public float GetJoyLeftStickX() => MoveAxis.x;
        public float GetJoyLeftStickY() => -MoveAxis.y; // Valheim inverts Y
        public float GetJoyRightStickX() => LookAxis.x;
        public float GetJoyRightStickY() => -LookAxis.y;
        public float GetJoyLTrigger() => LeftTrigger;
        public float GetJoyRTrigger() => RightTrigger;

        /// <summary>
        /// Get the pressed-time for a button (approximate, returns 0 or deltaTime).
        /// Valheim uses this for crouch hold detection.
        /// </summary>
        public float GetButtonPressedTimer(string name)
        {
            // Simplified: if held, return a large-ish value
            return GetButton(name) ? 1.0f : 0f;
        }
    }
}
