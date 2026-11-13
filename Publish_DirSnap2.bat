@echo off
rem ============================================================
rem  DirSnap 2 - Release build for distribution
rem  Creates publish\DirSnap2\ (self-contained: no .NET or
rem  Windows App SDK to install) and publish\DirSnap2_vX.Y.Z_win-x64.zip
rem  ready to attach to a GitHub Release.
rem ============================================================
setlocal
cd /d "%~dp0"

for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content 'DirSnap2.csproj')).Project.PropertyGroup.Version"`) do set "VER=%%v"
if "%VER%"=="" (
    echo Cannot read the version from DirSnap2.csproj
    exit /b 1
)

set "OUT=publish\DirSnap2"
set "ZIP=publish\DirSnap2_v%VER%_win-x64.zip"

echo.
echo === DirSnap 2 v%VER% - Release publish ===
echo.

if exist "%OUT%" rmdir /s /q "%OUT%"
if exist "%ZIP%" del /q "%ZIP%"

dotnet publish DirSnap2.csproj -c Release -r win-x64 --self-contained true -p:DebugType=none -o "%OUT%"
if errorlevel 1 (
    echo.
    echo Publish FAILED.
    exit /b 1
)

dotnet build DirSnap2.csproj -c Debug -p:Platform=x64
if errorlevel 1 (
    echo.
    echo Debug build FAILED.
    exit /b 1
)

powershell -NoProfile -Command "Compress-Archive -Path '%OUT%' -DestinationPath '%ZIP%' -CompressionLevel Optimal"
if errorlevel 1 (
    echo.
    echo Zip FAILED.
    exit /b 1
)

echo.
echo Done:
echo   Folder: %OUT%\DirSnap2.exe
echo   Zip:    %ZIP%
echo.
endlocal
