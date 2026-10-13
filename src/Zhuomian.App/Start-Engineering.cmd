@echo off
cd /d "%~dp0"
echo Zhuomian E2-1 engineering verification only - not an installer or daily-use release.
echo Windows 11 x64 / ONE monitor required. No administrative rights required.
echo Opaque Space may cover icons. Press Win+D to expose desktop; keep this console running.
echo Click Space Exit to stop. A hover=20ms; B click=instantaneous, NOT B0 animation.
echo Starting executable. Diagnostics saved to engineering-session.log in this extracted folder.
Zhuomian.App.exe > engineering-session.log 2>&1
set "zhuomian_result=%errorlevel%"
type engineering-session.log
echo Exit code: %zhuomian_result%
pause
exit /b %zhuomian_result%
