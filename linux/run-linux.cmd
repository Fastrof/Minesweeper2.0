@echo off
rem ============================================================
rem  Minesweeper 2.0 - Linux launcher
rem
rem  Double-click this file to open the Linux build inside WSLg.
rem  All messages are English on purpose: Windows cmd defaults to
rem  GBK, and mixing UTF-8 Chinese into a .cmd is what causes the
rem  garbled text people see. chcp 65001 keeps WSL output readable.
rem ============================================================
setlocal
chcp 65001 >nul

set "DISTRO=Ubuntu"
set "REPO=/mnt/d/zack/Desktop/MineSweeper2.0/MineSweeper2.0-Windows/linux"

rem ---- 1. WSLg available? ----
wsl -d %DISTRO% -- bash -lc "test -n \"$DISPLAY\"" >nul 2>&1
if errorlevel 1 (
  echo [error] No graphical session in WSL (DISPLAY is empty^).
  echo.
  echo   Fix:
  echo     1^) Open %%USERPROFILE%%\.wslconfig
  echo     2^) Make sure [wsl2] does NOT contain: guiApplications=false
  echo     3^) Run: wsl --shutdown
  echo     4^) Run this file again
  echo.
  pause
  exit /b 1
)

rem ---- 2. Launch ----
echo Starting Minesweeper 2.0 (Linux)...
wsl -d %DISTRO% -- bash "%REPO%/scripts/run.sh"
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
  echo.
  echo [error] Launch failed ^(exit code %RC%^).
  echo.
  echo   Try building first:
  echo     build-linux.cmd
  echo   Or install the package:
  echo     wsl -d %DISTRO% -- sudo apt install /mnt/d/zack/Desktop/MineSweeper2.0/MineSweeper2.0-Windows/linux/dist/minesweeper2_1.5.2_amd64.deb
  echo.
  pause
)

endlocal
