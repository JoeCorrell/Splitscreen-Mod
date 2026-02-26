@echo off
echo ========================================
echo  Valheim Splitscreen Mod - Installer
echo ========================================
echo.

set "VALHEIM_DIR=C:\Program Files (x86)\Steam\steamapps\common\Valheim"

:: Check if Valheim exists
if not exist "%VALHEIM_DIR%\valheim.exe" (
    echo Valheim not found at: %VALHEIM_DIR%
    set /p VALHEIM_DIR="Enter your Valheim installation path: "
)

if not exist "%VALHEIM_DIR%\valheim.exe" (
    echo ERROR: valheim.exe not found in %VALHEIM_DIR%
    pause
    exit /b 1
)

:: Check if BepInEx is installed
if not exist "%VALHEIM_DIR%\BepInEx" (
    echo.
    echo ERROR: BepInEx is not installed!
    echo.
    echo Please install BepInEx first:
    echo 1. Download BepInEx 5.x from https://github.com/BepInEx/BepInEx/releases
    echo 2. Extract to your Valheim folder
    echo 3. Run the game once to generate BepInEx folders
    echo 4. Run this installer again
    echo.
    pause
    exit /b 1
)

:: Create plugin directory
set "PLUGIN_DIR=%VALHEIM_DIR%\BepInEx\plugins\ValheimSplitscreen"
if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%"

:: Copy the DLL
if exist "bin\Release\net48\ValheimSplitscreen.dll" (
    copy /Y "bin\Release\net48\ValheimSplitscreen.dll" "%PLUGIN_DIR%\"
    echo.
    echo SUCCESS! Mod installed to:
    echo %PLUGIN_DIR%\ValheimSplitscreen.dll
) else (
    echo.
    echo ERROR: ValheimSplitscreen.dll not found.
    echo Please build the mod first using build.bat
    pause
    exit /b 1
)

echo.
echo ========================================
echo  Installation Complete!
echo ========================================
echo.
echo USAGE:
echo  1. Connect 2 controllers to your PC
echo  2. Launch Valheim
echo  3. Start/load a world
echo  4. Press F10 to activate splitscreen
echo  5. Player 2 spawns with Controller 2
echo.
echo CONFIG:
echo  Edit: %VALHEIM_DIR%\BepInEx\config\com.splitscreen.valheim.cfg
echo.
pause
