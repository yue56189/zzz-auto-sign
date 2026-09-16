@echo off
rem ===============================================================
rem  CredentialGrabber - optional browser kernel installer
rem
rem  You usually do NOT need this. The tool drives the browser you
rem  already have: it tries Google Chrome first, then Microsoft Edge
rem  (which ships with Windows). Only if neither is installed does it
rem  fall back to the Chromium kernel downloaded by this script.
rem
rem  Usage:
rem    install-browser.cmd              download the Chromium kernel
rem    install-browser.cmd --dry-run    show what it would do
rem
rem  The download is about 170 MB and goes to
rem    %USERPROFILE%\AppData\Local\ms-playwright
rem
rem  Note: this file is deliberately ASCII-only so that cmd.exe can read
rem  it whatever the console code page is. Do not put Chinese text here.
rem ===============================================================

setlocal

rem Playwright's driver prints UTF-8. Without this, a CJK path such as
rem C:\Users\<Chinese name>\... renders garbled under the default GBK
rem console code page.
chcp 65001 >nul 2>&1

set "HERE=%~dp0"
set "NODE=%HERE%.playwright\node\win32_x64\node.exe"
set "CLI=%HERE%.playwright\package\cli.js"

if not exist "%NODE%" goto :nodriver
if not exist "%CLI%" goto :nodriver

echo Playwright driver : %CLI%
echo.
"%NODE%" "%CLI%" install chromium %*
set "RC=%ERRORLEVEL%"
echo.

if not "%RC%"=="0" goto :failed

echo [OK] Installer finished with exit code 0.
if /i "%1"=="--dry-run" echo      --dry-run was given, so nothing was actually downloaded.
echo      Start CredentialGrabber.exe and click "Open browser to log in".
exit /b 0

:failed
echo [FAIL] The installer exited with code %RC%.
echo        If this is a network or proxy problem, you can instead install
echo        Google Chrome or Microsoft Edge and use that as the browser.
exit /b %RC%

:nodriver
echo [ERROR] Playwright driver not found:
echo         %NODE%
echo.
echo         The .playwright folder is missing or incomplete.
echo         Extract the WHOLE zip archive, not only CredentialGrabber.exe.
exit /b 1
