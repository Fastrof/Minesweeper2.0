@echo off
rem 跑 Core 层单测（不需要 UI）
chcp 65001 >nul
setlocal
call :trydotnet "%ProgramFiles%\dotnet"
call :trydotnet "%DOTNET_ROOT%"
call :trydotnet "%ProgramFiles(x86)%\dotnet"
cd /d "%~dp0"
dotnet test tests\MineSweeper.Core.Tests\MineSweeper.Core.Tests.csproj
set "RC=%ERRORLEVEL%"
endlocal & exit /b %RC%

:trydotnet
if not defined DOTNET_ROOT if exist "%~1\dotnet.exe" (
  set "DOTNET_ROOT=%~1"
  set "PATH=%~1;%PATH%"
)
exit /b 0
