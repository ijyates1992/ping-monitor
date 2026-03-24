@echo off
setlocal

rem Run the PingMonitor Python agent on Windows.
rem - Creates src/Agent/.venv if it does not exist
rem - Upgrades pip and installs requirements.txt
rem - Starts the existing agent entry point in the foreground

set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"
if errorlevel 1 (
  echo [ERROR] Unable to switch to agent directory: %SCRIPT_DIR%
  goto :fail
)

echo [INFO] Agent directory: %CD%

set "PYTHON_CMD="
py --version >nul 2>&1
if not errorlevel 1 (
  set "PYTHON_CMD=py"
)

if not defined PYTHON_CMD (
  python --version >nul 2>&1
  if not errorlevel 1 (
    set "PYTHON_CMD=python"
  )
)

if not defined PYTHON_CMD (
  echo [ERROR] Python was not found. Install Python 3 and ensure ^"py^" or ^"python^" is on PATH.
  goto :fail
)

echo [INFO] Using Python launcher: %PYTHON_CMD%

if not exist ".venv\Scripts\python.exe" (
  echo [INFO] Creating virtual environment at %CD%\.venv
  %PYTHON_CMD% -m venv .venv
  if errorlevel 1 (
    echo [ERROR] Failed to create virtual environment.
    goto :fail
  )
) else (
  echo [INFO] Reusing existing virtual environment at %CD%\.venv
)

call ".venv\Scripts\activate.bat"
if errorlevel 1 (
  echo [ERROR] Failed to activate virtual environment.
  goto :fail
)

echo [INFO] Upgrading pip
python -m pip install --upgrade pip
if errorlevel 1 (
  echo [ERROR] Failed to upgrade pip.
  goto :fail
)

echo [INFO] Installing dependencies from requirements.txt
python -m pip install -r requirements.txt
if errorlevel 1 (
  echo [ERROR] Failed to install dependencies.
  goto :fail
)

echo [INFO] Starting agent module (python -m app.main)
python -m app.main
if errorlevel 1 (
  echo [ERROR] Agent exited with an error.
  goto :fail
)

echo [INFO] Agent exited successfully.
exit /b 0

:fail
echo [ERROR] run-agent.cmd failed.
pause
exit /b 1
