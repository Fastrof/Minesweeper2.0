@echo off
rem 启动游戏（unpackaged 版本，存档在 %LOCALAPPDATA%\MineSweeper2\，不再放程序目录）
chcp 65001 >nul
setlocal
cd /d "%~dp0"
set "EXE=src\MineSweeper.App\bin\x64\Debug\net10.0-windows10.0.19041.0\MineSweeper2.exe"
if not exist "%EXE%" (
  echo 还没编译，先跑 build.cmd
  exit /b 1
)
start "" "%EXE%"
endlocal
