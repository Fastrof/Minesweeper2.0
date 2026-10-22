@echo off
rem ============================================================
rem  Minesweeper 2.0 - Linux build launcher (one click)
rem
rem  Requires: WSLg enabled (guiApplications is not set to false
rem  in %USERPROFILE%\.wslconfig), and `wsl --shutdown` already run.
rem ============================================================
setlocal
chcp 65001 >nul

set "DISTRO=Ubuntu"
set "REPO=/mnt/d/zack/Desktop/MineSweeper2.0/MineSweeper2.0-Windows/linux"

echo [build] Building Linux version in WSL...
wsl -d %DISTRO% -- bash "%REPO%/scripts/build.sh"
if errorlevel 1 (
  echo.
  echo [error] Build failed. See the log above.
  pause
  exit /b 1
)

echo.
echo [ok] Build finished. You can now run run-linux.cmd
pause
endlocal
