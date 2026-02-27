using UnityEngine;
using UnityEngine.InputSystem;
using Valheim.SettingsGui;

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
        public bool LeftTriggerDown;
        public bool RightTriggerDown;
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
            LeftTriggerDown = LeftTrigger > 0.4f && !_prevLT;
            RightTriggerDown = RightTrigger > 0.4f && !_prevRT;
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
            LeftTriggerDown = RightTriggerDown = false;
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
            if (kb.iKey.isPressed) MoveAxis.y = 1f;   // forward
            if (kb.kKey.isPressed) MoveAxis.y = -1f;  // backward
            if (kb.jKey.isPressed) MoveAxis.x = -1f;
            if (kb.lKey.isPressed) MoveAxis.x = 1f;
            if (MoveAxis.magnitude > 1f) MoveAxis.Normalize();

            // Look: Numpad arrows
            LookAxis = Vector2.zero;
            if (kb.numpad8Key.isPressed) LookAxis.y = 0.7f;
            if (kb.numpad2Key.isPressed) LookAxis.y = -0.7f;
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
            LeftTriggerDown = LeftTrigger > 0.4f && !_prevLT;
            RightTriggerDown = RightTrigger > 0.4f && !_prevRT;
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
        private static bool IsClassicLayout => ZInput.InputLayout == InputLayout.Default;
        private static bool IsAlt1Layout => ZInput.InputLayout == InputLayout.Alternative1;

        private bool GetAltKeysHold()
        {
            // Default/Alt2: LT, Alt1: LB
            return IsAlt1Layout ? LeftShoulder : LeftTrigger > 0.4f;
        }

        private bool GetAttackHold() => IsAlt1Layout ? RightShoulder : RightTrigger > 0.4f;
        private bool GetAttackDown() => IsAlt1Layout ? RightShoulderDown : RightTriggerDown;
        private bool GetSecondaryAttackHold() => IsAlt1Layout ? RightTrigger > 0.4f : RightShoulder;
        private bool GetSecondaryAttackDown() => IsAlt1Layout ? RightTriggerDown : RightShoulderDown;
        private bool GetBlockHold() => IsAlt1Layout ? LeftShoulder : LeftTrigger > 0.4f;
        private bool GetBlockDown() => IsAlt1Layout ? LeftShoulderDown : LeftTriggerDown;
        private bool GetUseHold() => IsClassicLayout ? ButtonSouth : ButtonWest;
        private bool GetUseDown() => IsClassicLayout ? ButtonSouthDown : ButtonWestDown;
        private bool GetBuildMenuHold() => IsClassicLayout ? ButtonSouth : ButtonEast;
        private bool GetBuildMenuDown() => IsClassicLayout ? ButtonSouthDown : ButtonEastDown;
        private bool GetRunHold() => IsClassicLayout ? LeftShoulder : LeftStickPress;
        private bool GetRunDown() => IsClassicLayout ? LeftShoulderDown : LeftStickPressDown;
        private bool GetCrouchHold() => IsClassicLayout ? LeftStickPress : RightStickPress;
        private bool GetCrouchDown() => IsClassicLayout ? LeftStickPressDown : RightStickPressDown;
        private bool GetHideHold() => IsClassicLayout ? RightStickPress : (IsAlt1Layout ? LeftTrigger > 0.4f : LeftShoulder);
        private bool GetHideDown() => IsClassicLayout ? RightStickPressDown : (IsAlt1Layout ? LeftTriggerDown : LeftShoulderDown);
        private bool GetSitHold() => IsClassicLayout ? ButtonWest : DpadDown;
        private bool GetSitDown() => IsClassicLayout ? ButtonWestDown : DpadDownDown;

        public bool GetButton(string name)
        {
            switch (name)
            {
                case "JoyAttack":
                case "Attack":
                    return GetAttackHold();
                case "JoySecondaryAttack":
                case "SecondaryAttack":
                    return GetSecondaryAttackHold();
                case "JoyBlock":
                case "Block":
                    return GetBlockHold();
                case "JoyJump":
                case "Jump":
                    return ButtonSouth;
                case "JoyCrouch":
                case "Crouch":
                    return GetCrouchHold();
                case "JoyRun":
                case "Run":
                    return GetRunHold();
                case "JoyDodge":
                    return ButtonEast && GetAltKeysHold();
                case "Forward":
                    return MoveAxis.y > 0.3f;
                case "Backward":
                    return MoveAxis.y < -0.3f;
                case "Left":
                    return MoveAxis.x < -0.3f;
                case "Right":
                    return MoveAxis.x > 0.3f;
                case "JoyLStickLeft":
                    return MoveAxis.x < -0.3f;
                case "JoyLStickRight":
                    return MoveAxis.x > 0.3f;
                case "JoyLStickUp":
                    return MoveAxis.y > 0.3f;
                case "JoyLStickDown":
                    return MoveAxis.y < -0.3f;
                case "JoyRStickLeft":
                    return LookAxis.x < -0.3f;
                case "JoyRStickRight":
                    return LookAxis.x > 0.3f;
                case "JoyRStickUp":
                    return LookAxis.y > 0.3f;
                case "JoyRStickDown":
                    return LookAxis.y < -0.3f;
                case "JoyDPadLeft":
                    return DpadLeft;
                case "JoyDPadRight":
                    return DpadRight;
                case "JoyDPadUp":
                    return DpadUp;
                case "JoyDPadDown":
                    return DpadDown;
                case "JoyAltKeys":
                    return GetAltKeysHold();
                case "JoyCamZoomIn":
                case "CamZoomIn":
                    return DpadUp;
                case "JoyCamZoomOut":
                case "CamZoomOut":
                    return DpadDown;
                case "MapZoomIn":
                    return DpadRight;
                case "MapZoomOut":
                    return DpadLeft;
                case "JoyTabLeft":
                case "TabLeft":
                    return LeftShoulder;
                case "JoyTabRight":
                case "TabRight":
                    return RightShoulder;
                case "JoyLBumper":
                    return LeftShoulder;
                case "JoyRBumper":
                    return RightShoulder;
                case "JoyLTrigger":
                    return LeftTrigger > 0.4f;
                case "JoyRTrigger":
                    return RightTrigger > 0.4f;
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
                case "JoyRotateRight":
                    return RightTrigger > 0.4f;
                case "JoyPlace":
                    return GetAttackHold();
                case "JoyRemove":
                case "Remove":
                    return GetSecondaryAttackHold();
                case "JoyBuildMenu":
                case "BuildMenu":
                    return GetBuildMenuHold();
                case "AutoRun":
                    return false;
                case "Inventory":
                    return SelectButton || ButtonNorth;
                case "JoyInventory":
                    return ButtonNorth;
                case "JoyMenu":
                case "JoyStart":
                case "Menu":
                    return StartButton;
                case "JoyBack":
                case "JoyMap":
                case "JoyChat":
                case "Map":
                    return SelectButton;
                case "JoyUse":
                case "Use":
                    return GetUseHold();
                case "JoyHide":
                    return GetHideHold();
                case "JoySit":
                    return GetSitHold();
                case "JoyGPower":
                case "JoyGP":
                case "GP":
                case "GPower":
                    return DpadDown;
                case "JoyHotbarLeft":
                    return DpadLeft;
                case "JoyHotbarRight":
                    return DpadRight;
                case "JoyHotbarUse":
                    return DpadUp;
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
                    return GetAttackDown();
                case "JoySecondaryAttack":
                case "SecondaryAttack":
                    return GetSecondaryAttackDown();
                case "JoyBlock":
                case "Block":
                    return GetBlockDown();
                case "JoyJump":
                case "Jump":
                    return ButtonSouthDown;
                case "JoyCrouch":
                case "Crouch":
                    return GetCrouchDown();
                case "JoyRun":
                case "Run":
                    return GetRunDown();
                case "JoyDodge":
                    return ButtonEastDown && GetAltKeysHold();
                case "Forward":
                case "Backward":
                case "Left":
                case "Right":
                case "JoyLStickLeft":
                case "JoyLStickRight":
                case "JoyLStickUp":
                case "JoyLStickDown":
                case "JoyRStickLeft":
                case "JoyRStickRight":
                case "JoyRStickUp":
                case "JoyRStickDown":
                    return false;
                case "JoyDPadLeft":
                    return DpadLeftDown;
                case "JoyDPadRight":
                    return DpadRightDown;
                case "JoyDPadUp":
                    return DpadUpDown;
                case "JoyDPadDown":
                    return DpadDownDown;
                case "JoyAltKeys":
                    return IsAlt1Layout ? LeftShoulderDown : LeftTriggerDown;
                case "JoyCamZoomIn":
                case "CamZoomIn":
                    return DpadUpDown;
                case "JoyCamZoomOut":
                case "CamZoomOut":
                    return DpadDownDown;
                case "MapZoomIn":
                    return DpadRightDown;
                case "MapZoomOut":
                    return DpadLeftDown;
                case "JoyTabLeft":
                case "TabLeft":
                    return LeftShoulderDown;
                case "JoyTabRight":
                case "TabRight":
                    return RightShoulderDown;
                case "JoyLBumper":
                    return LeftShoulderDown;
                case "JoyRBumper":
                    return RightShoulderDown;
                case "JoyLTrigger":
                    return LeftTriggerDown;
                case "JoyRTrigger":
                    return RightTriggerDown;
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
                    return SelectButtonDown || ButtonNorthDown;
                case "JoyInventory":
                    return ButtonNorthDown;
                case "JoyMenu":
                case "JoyStart":
                case "Menu":
                    return StartButtonDown;
                case "JoyBack":
                case "JoyMap":
                case "JoyChat":
                case "Map":
                    return SelectButtonDown;
                case "JoyUse":
                case "Use":
                    return GetUseDown();
                case "JoyBuildMenu":
                case "BuildMenu":
                    return GetBuildMenuDown();
                case "JoyHide":
                    return GetHideDown();
                case "JoySit":
                    return GetSitDown();
                case "JoyRemove":
                case "Remove":
                    return GetSecondaryAttackDown();
                case "JoyHotbarLeft":
                    return DpadLeftDown;
                case "JoyHotbarRight":
                    return DpadRightDown;
                case "JoyHotbarUse":
                    return DpadUpDown;
                case "JoyGPower":
                case "JoyGP":
                case "GP":
                case "GPower":
                    return DpadDownDown;
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
