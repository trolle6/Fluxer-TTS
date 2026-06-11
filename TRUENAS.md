# TrueNAS SCALE — Fluxer-TTS

The UI error `EFAULT` / `Failed 'up' action` is generic. The **real** error is almost always one of the items below.

## Do this first (SSH or SCALE Shell)

```bash
tail -n 200 /var/log/app_lifecycle.log
```

Scroll to the **first** `ERROR` (not just the final `EFAULT` line).

---

## Correct setup (Custom App)

### 1. Use the GHCR image — do NOT use `build:`

TrueNAS **cannot** run `build: .` from the GitHub repo. You must pull a pre-built image:

```
ghcr.io/trolle6/fluxer-tts:latest
```

In **Custom App** → use compose file **`docker-compose.truenas.yml`** from the repo,  
**or** paste the YAML from that file.  
Do **not** deploy with the root `docker-compose.yml` (it tries to build locally).

### 2. Make the GHCR package public (one time)

If pull fails with **401 / denied / manifest unknown**:

1. Open [github.com/trolle6?tab=packages](https://github.com/trolle6?tab=packages)
2. Click **fluxer-tts**
3. **Package settings** → **Change visibility** → **Public**

Test from the NAS shell:

```bash
docker pull ghcr.io/trolle6/fluxer-tts:latest
```

That must succeed before the app will start.

### 3. Environment variables (required)

Set these in the app **Environment** section (exact names):

| Variable | Required |
|----------|----------|
| `FLUXER_BOT_TOKEN` | Yes |
| `OPENAI_API_KEY` | Yes |
| `FLUXER_LOG_CHANNEL_ID` | Recommended |
| `FLUXER_MODERATOR_ROLE_ID` | Recommended |
| `FLUXER_CHANNEL_ID` | Recommended |

Also set (fixed value):

| Variable | Value |
|----------|--------|
| `DATA_ROOT` | `/app/Data` |

Optional tuning vars are in `config.env.example`; the bot uses sane defaults if omitted.

### 4. Storage (pick your HDD / pool)

**Storage** → **Add** → **Host Path**:

| Field | Value |
|-------|--------|
| **Host Path** | e.g. `/mnt/tank/apps/fluxer-bot/data` (browse to your pool) |
| **Mount Path** | `/app/Data` |
| **Type** | Directory |

This is where Secret Santa + distribute files live. Copy old `cogs/` data here before first run if migrating.

### 5. Deploy

Save → **Install**. If it fails again, re-check `app_lifecycle.log`.

---

## Common `app_lifecycle.log` errors

| Log message | Fix |
|-------------|-----|
| `build` / `Dockerfile` / `unable to prepare context` | You used `docker-compose.yml` with `build:` — switch to `docker-compose.truenas.yml` or image-only compose |
| `pull access denied` / `401` | Make GHCR package **public** or add registry credentials in TrueNAS |
| `manifest unknown` | Image not published yet — check [GitHub Actions](https://github.com/trolle6/Fluxer-TTS/actions) for green build |
| `invalid compose` / `mapping values` | Use `docker-compose.truenas.yml` (no `${VAR:-default}` syntax) |
| Container starts then exits | Missing `FLUXER_BOT_TOKEN` or `OPENAI_API_KEY` — check app **Logs** tab |

---

## Manual test (SSH)

```bash
docker pull ghcr.io/trolle6/fluxer-tts:latest

mkdir -p /mnt/tank/apps/fluxer-bot/data

docker run -d --name fluxer-test \
  -e FLUXER_BOT_TOKEN="your_token" \
  -e OPENAI_API_KEY="your_key" \
  -e DATA_ROOT=/app/Data \
  -v /mnt/tank/apps/fluxer-bot/data:/app/Data \
  --restart unless-stopped \
  ghcr.io/trolle6/fluxer-tts:latest

docker logs -f fluxer-test
```

If that works, mirror the same image, env, and volume in the TrueNAS UI.
