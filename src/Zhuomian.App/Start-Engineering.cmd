@echo off
cd /d "%~dp0"
echo Zhuomian UI-I2 WPF ENGINEERING ONLY - not an installer or daily-use release.
echo Windows 11 x64 / ONE monitor required. No administrative rights required.
echo Opaque Space may cover icons. Press Win+D to expose desktop; keep this console running.
echo Use expanded Space Exit or close this console to stop. A hover=20ms; B click=240ms geometry candidate, Reduced Motion=instant. NOT accepted B0.
echo Starting executable. Diagnostics saved to engineering-session.log in this extracted folder.
if not exist "%~dp0Zhuomian.App.exe" (
  echo Missing Zhuomian.App.exe. Extract the complete engineering package.
  exit /b 2
)
"%~dp0Zhuomian.App.exe" > "%~dp0engineering-session.log" 2>&1
set "zhuomian_result=%errorlevel%"
type "%~dp0engineering-session.log"
echo Exit code: %zhuomian_result%
pause
exit /b %zhuomian_result%
