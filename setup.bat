@echo off
REM FluxerTools - first-time setup
REM Creates .venv and installs dependencies

cd /d "%~dp0"

if not exist ".venv" (
    echo Creating virtual environment...
    python -m venv .venv
    if errorlevel 1 (
        echo ERROR: Could not create venv. Install Python 3.10+ first.
        exit /b 1
    )
)

echo Activating and installing dependencies...
call .venv\Scripts\activate.bat
pip install -r requirements.txt
if errorlevel 1 (
    echo ERROR: pip install failed.
    exit /b 1
)

echo.
echo Setup complete. Run: run.bat
echo Or in PyCharm: set interpreter to FluxerTools\.venv\Scripts\python.exe
pause
