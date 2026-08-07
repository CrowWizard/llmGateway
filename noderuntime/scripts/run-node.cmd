@echo off
setlocal

if defined CODEX_NODE_PATH if exist "%CODEX_NODE_PATH%" (
  "%CODEX_NODE_PATH%" %*
  exit /b %ERRORLEVEL%
)

set "MANAGED_NODE=%~dp0..\runtime\win-x64\node.exe"
if exist "%MANAGED_NODE%" (
  "%MANAGED_NODE%" %*
  exit /b %ERRORLEVEL%
)

where node.exe >nul 2>nul
if %ERRORLEVEL% equ 0 (
  node.exe %*
  exit /b %ERRORLEVEL%
)

echo Error: managed Node.js is not installed and node.exe was not found in PATH. 1>&2
echo Run the LlmGateway Desktop managed Node.js installation action. 1>&2
exit /b 1