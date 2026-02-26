using BepInEx.Configuration;

namespace ValheimSplitscreen.Config
{
    public class SplitscreenConfig
    {
        public ConfigEntry<SplitOrientation> Orientation { get; }
        public ConfigEntry<Player1InputMode> P1InputMode { get; }
        public ConfigEntry<float> Player2ProfileSlot { get; }
        public ConfigEntry<float> CameraFOV { get; }
        public ConfigEntry<float> GamepadSensitivity { get; }
        public ConfigEntry<bool> SharedMap { get; }
        public ConfigEntry<bool> IndependentInventory { get; }
        public ConfigEntry<string> Player2Name { get; }
        public ConfigEntry<bool> DebugMode { get; }
        public ConfigEntry<bool> ForceLanHosting { get; }
        public ConfigEntry<bool> SharedController { get; }

        public SplitscreenConfig(ConfigFile config)
        {
            Orientation = config.Bind("General", "SplitOrientation", SplitOrientation.Horizontal,
                "How to split the screen. Horizontal = top/bottom, Vertical = left/right.");

            P1InputMode = config.Bind("Input", "Player1InputMode", Player1InputMode.KeyboardMouse,
                "How Player 1 controls. KeyboardMouse = P1 uses keyboard+mouse and P2 uses first gamepad. Gamepad = P1 uses gamepad 0 and P2 uses gamepad 1.");

            Player2Name = config.Bind("General", "Player2Name", "Player 2",
                "Display name for the second player character.");

            DebugMode = config.Bind("General", "DebugMode", false,
                "Allow splitscreen without 2 controllers. Player 1 uses keyboard+mouse, Player 2 uses first gamepad (or IJKL+numpad keys if no gamepad).");

            CameraFOV = config.Bind("Camera", "FieldOfView", 65f,
                "Camera field of view for each viewport.");

            GamepadSensitivity = config.Bind("Input", "GamepadSensitivity", 1.0f,
                "Gamepad look sensitivity multiplier for both players.");

            SharedMap = config.Bind("Gameplay", "SharedMap", true,
                "If true, both players share the same minimap exploration.");

            IndependentInventory = config.Bind("Gameplay", "IndependentInventory", true,
                "Each player has their own independent inventory.");

            Player2ProfileSlot = config.Bind("Gameplay", "Player2ProfileSlot", 1f,
                "Profile slot number for Player 2 save data (not used by Player 1).");

            ForceLanHosting = config.Bind("Network", "ForceLanHosting", true,
                "When splitscreen is active, force the world to be open for LAN connections so other players can join.");

            SharedController = config.Bind("Input", "SharedController", false,
                "Both players use the same gamepad. Useful for testing with a single controller.");
        }
    }

    public enum SplitOrientation
    {
        Horizontal, // Top/Bottom
        Vertical    // Left/Right
    }

    public enum Player1InputMode
    {
        KeyboardMouse, // P1 = keyboard+mouse, P2 = first gamepad (default)
        Gamepad        // P1 = gamepad 0, P2 = gamepad 1
    }
}
