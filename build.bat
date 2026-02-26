@echo off
echo ========================================
echo  Valheim Splitscreen Mod - Build Script
echo ========================================
echo.

:: Check for dotnet
where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo ERROR: .NET SDK not found. Please install .NET SDK 6.0+
    echo Download from: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

:: Build the project
echo Building mod...
dotnet build ValheimSplitscreen.csproj -c Release

if %ERRORLEVEL% neq 0 (
    echo.
    echo BUILD FAILED! Check errors above.
    pause
    exit /b 1
)

echo.
echo BUILD SUCCESSFUL!
echo.

:: Copy to BepInEx plugins folder
set "VALHEIM_DIR=C:\Program Files (x86)\Steam\steamapps\common\Valheim"
set "PLUGIN_DIR=%VALHEIM_DIR%\BepInEx\plugins\ValheimSplitscreen"

if exist "%VALHEIM_DIR%" (
    echo Copying to BepInEx plugins folder...
    if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%"
    copy /Y "bin\Release\net48\ValheimSplitscreen.dll" "%PLUGIN_DIR%\"
    echo.
    echo Mod installed to: %PLUGIN_DIR%
) else (
    echo.
    echo Valheim directory not found at: %VALHEIM_DIR%
    echo Please manually copy bin\Release\net48\ValheimSplitscreen.dll
    echo to your BepInEx\plugins\ folder.
)

echo.
echo Done!
pause
