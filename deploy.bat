@echo off
setlocal
cd /d "%~dp0"

if not exist config.env (
  echo Copy config.env.example to config.env and set FLUXER_BOT_TOKEN + OPENAI_API_KEY.
  if exist config.env.example copy /Y config.env.example config.env
  echo Created config.env — edit it, then run deploy.bat again.
  exit /b 1
)

if not exist data mkdir data

echo Building and starting fluxer-bot...
docker compose -f docker-compose.build.yml --env-file config.env up -d --build
if errorlevel 1 exit /b 1

echo.
echo Bot is running.
echo   Logs: docker compose logs -f
echo   Stop: docker compose down
