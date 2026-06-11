@echo off
REM FluxerTools - run the bot
cd /d "%~dp0"

if not exist ".venv\Scripts\python.exe" (
    echo Run setup.bat first to create venv and install dependencies.
    pause
    exit /b 1
)

if not exist "config.env" (
    echo Missing config.env! Copy config.env.example to config.env and fill in your values.
    pause
    exit /b 1
)

.venv\Scripts\python.exe main.py
pause
