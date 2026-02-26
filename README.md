# Valheim Splitscreen Mod

A BepInEx mod that adds local splitscreen co-op to Valheim, allowing 2 players to play together on one PC with 2 controllers.

## Features

- **True Splitscreen** - Horizontal (top/bottom) or vertical (left/right) screen split
- **Dual Controller Support** - Each player uses their own gamepad (Xbox, PlayStation, etc.)
- **Independent Characters** - Each player has their own character, inventory, and skills
- **Separate Save Data** - Player 2 has their own profile that persists between sessions
- **Full Gameplay** - Both players can fight, build, craft, and explore independently
- **Custom HUD** - Player 2 gets their own health, stamina, and status display
- **Difficulty Scaling** - Enemy difficulty scales for 2 players as in normal multiplayer
- **Ward Sharing** - Player 2 automatically has access to Player 1's protected areas

## Requirements

- **Valheim** (Steam version)
- **BepInEx 5.4.x** - [Download here](https://github.com/BepInEx/BepInEx/releases)
- **2 Game Controllers** (Xbox, PlayStation, Switch Pro, or any XInput/DirectInput gamepad)
- **.NET SDK 6.0+** (for building from source)

## Installation

### Pre-built (Recommended)
1. Install BepInEx 5.x into your Valheim folder
2. Run Valheim once to generate BepInEx folders
3. Copy `ValheimSplitscreen.dll` to `Valheim/BepInEx/plugins/ValheimSplitscreen/`
4. Launch Valheim

### From Source
1. Clone this repository
2. Update paths in `ValheimSplitscreen.csproj` if needed (Valheim install location)
3. Run `build.bat` or `dotnet build -c Release`
4. Run `install.bat` or manually copy the DLL

## Usage

1. Connect **2 controllers** to your PC
2. Launch Valheim and load into a world (singleplayer or as host)
3. Press **F10** to activate splitscreen
4. Player 2 spawns near Player 1 with Controller 2
5. Press **F10** again to deactivate splitscreen

## Controller Layout

Both players use standard Valheim gamepad controls:

| Action | Button |
|--------|--------|
| Move | Left Stick |
| Camera | Right Stick |
| Attack | RB (Right Shoulder) |
| Secondary Attack | RT (Right Trigger) |
| Block | LB (Left Shoulder) |
| Jump | A / Cross |
| Dodge | B / Circle |
| Run | Left Stick Click |
| Crouch | Right Stick Click |
| Use/Interact | X / Square |
| Alt Keys | Y / Triangle |
| Inventory | Select/Back |
| Menu | Start |
| Zoom In/Out | D-Pad Up/Down (hold Y) |
| Tab Left/Right | D-Pad Left/Right |

## Configuration

After first run, edit `BepInEx/config/com.splitscreen.valheim.cfg`:

```ini
[General]
## How to split the screen: Horizontal (top/bottom) or Vertical (left/right)
SplitOrientation = Horizontal

## Display name for Player 2
Player2Name = Player 2

[Camera]
## Field of view for each viewport
FieldOfView = 65

[Input]
## Gamepad look sensitivity multiplier
GamepadSensitivity = 1.0

[Gameplay]
## Both players share map exploration
SharedMap = true

## Each player has independent inventory
IndependentInventory = true
```

## How It Works

The mod uses Harmony to patch Valheim's core systems:

- **Input System** - Routes Gamepad 0 to Player 1 and Gamepad 1 to Player 2, preventing input bleed
- **Camera System** - Creates a second camera with split viewports, each following their respective player
- **Player Management** - Spawns a second character with its own ZDO, profile, and network identity
- **HUD System** - Repositions Player 1's HUD to their viewport and renders Player 2's HUD via IMGUI overlay
- **Network Layer** - Player 2 exists as a real networked entity (ZDO-backed) on the same peer
- **Game Logic** - Prevents pausing, adjusts difficulty scaling, handles sleep/portals for both players

## Known Limitations

- Both players must use controllers (no keyboard+controller split)
- Player 2's HUD is simplified (IMGUI) compared to Player 1's native UI
- Some UI screens (inventory, crafting) are only accessible to one player at a time
- Minimap shows only Player 1's position (Player 2 gets coordinate display)
- Performance may be affected since the game renders two viewports

## Troubleshooting

**"Need 2 controllers" message:**
- Ensure both controllers are connected before pressing F10
- Check Windows Game Controllers (joy.cpl) to verify detection

**Player 2 not spawning:**
- Make sure you're in-game (loaded into a world), not in the main menu
- Check BepInEx logs: `BepInEx/LogOutput.log`

**Controller input mixed up:**
- Controller order is determined by Windows detection order
- Try disconnecting and reconnecting controllers in the desired order

**Performance issues:**
- Lower graphics settings (both cameras render the full scene)
- Reduce view distance
- Try vertical split (renders less horizontal FOV per camera)

## Building from Source

### Prerequisites
- .NET SDK 6.0 or later
- Valheim installed with BepInEx 5.x

### Build
```bash
dotnet build ValheimSplitscreen.csproj -c Release
```

The output DLL is in `bin/Release/net48/ValheimSplitscreen.dll`.

## License

This mod is provided as-is for personal use. Not affiliated with Iron Gate Studio or Coffee Stain Publishing.
