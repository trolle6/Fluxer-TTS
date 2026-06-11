# FluxerTools Troubleshooting

## TrueNAS SCALE: `CallError` / `EFAULT` / Failed `up` action for app

TrueNAS wraps `docker compose up`. The UI error is generic — the real error is in **`/var/log/app_lifecycle.log`** on the NAS.

### Common causes

1. **Compose file unsupported or invalid**  
   Older Compose on SCALE may reject newer YAML (e.g. extended `env_file` forms). This repo uses plain `environment: ${VAR}` so variables come from the TrueNAS app **Environment** UI or from `docker compose --env-file config.env`.

2. **Missing required environment variables**  
   The container needs at least: `FLUXER_TOKEN`, `FLUXER_GUILD_ID`, `FLUXER_CHANNEL_ID`, `FLUXER_LOG_CHANNEL_ID`, `FLUXER_MODERATOR_ROLE_ID`, `OPENAI_API_KEY`.  
   If any are empty, the bot exits immediately — check app logs: `docker compose logs` (or SCALE app logs).

3. **Build failure**  
   Network/DNS during `pip install` or `apt-get` in the Dockerfile. Check `app_lifecycle.log` for the first `ERROR` from `docker build` or `compose`.

### What to do

1. SSH to the NAS (or use SCALE shell) and run:
   ```bash
   tail -n 200 /var/log/app_lifecycle.log
   ```
2. Fix the **first** compose/build error shown (not only the final `EFAULT` line).
3. In the TrueNAS app, set **Environment** variables to match `config.env.example` (names must match `docker-compose.yml`).

## "Invalid session (resumable=False)"

The Fluxer gateway rejected the bot's authentication. Try these steps:

### 1. Regenerate your bot token

1. Go to [web.fluxer.app](https://web.fluxer.app) → **User Settings** (bottom left) → **Applications**
2. Select your application
3. Click **Regenerate** on the bot token
4. Copy the new token
5. Update `config.env` with the new `FLUXER_TOKEN`

### 2. Ensure the bot is invited to your community

Per [Fluxer Quickstart](https://docs.fluxer.app/quickstart):
- In Applications, check **bot** and copy the **Authorize URL**
- Open that URL in your browser
- Select your community and add the bot
- Without this step, the bot cannot connect

### 3. Confirm you're on the right Fluxer instance

- If you use **web.fluxer.app** (official): `api.fluxer.app` is correct
- If you use a **self-hosted** Fluxer: You need a different API URL (set `FLUXER_API_URL` in config and pass it to the bot)

### 4. Test with the Node.js quickstart

To rule out fluxer.py issues, try the [Fluxer Node.js quickstart](https://docs.fluxer.app/quickstart):
- If Node works with your token → likely a fluxer.py compatibility issue
- If Node also fails → token or invite is the problem
