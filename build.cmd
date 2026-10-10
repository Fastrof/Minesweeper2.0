@echo off
rem 编译整个解决方案（WinUI 项目必须指定平台 x64）
chcp 65001 >nul
setlocal
call :trydotnet "%ProgramFiles%\dotnet"
call :trydotnet "%DOTNET_ROOT%"
call :trydotnet "%ProgramFiles(x86)%\dotnet"
cd /d "%~dp0"
dotnet build MineSweeper2.sln -p:Platform=x64
set "RC=%ERRORLEVEL%"
endlocal & exit /b %RC%

rem 找不到就什么都不做，直接用 PATH 里的 dotnet。
rem 刻意不写死某一台机器的 SDK 路径，换机器也不用改脚本。
:trydotnet
if not defined DOTNET_ROOT if exist "%~1\dotnet.exe" (
  set "DOTNET_ROOT=%~1"
  set "PATH=%~1;%PATH%"
)
exit /b 0
