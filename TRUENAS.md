# TrueNAS SCALE — install Fluxer-TTS as an App

> **SSH `docker pull` does NOT install an app.** It only caches the image on disk.  
> To see it under **Apps → Installed Applications**, you must deploy through the TrueNAS UI.

---

## Method A — Launch Docker Image (easiest)

Use this if your SCALE version has **Launch Docker Image** on the Discover Apps page.

### 1. Remove the old failed app

**Apps → Installed Applications** → if `fluxertts` exists (Stopped/Failed) → **Delete**.

### 2. Start the wizard

**Apps → Discover Apps** → **Launch Docker Image**  
*(If you don't see it: use Method B below.)*

### 3. Application name

| Field | Value |
|-------|--------|
| **Application Name** | `fluxer-bot` |

### 4. Image

| Field | Value |
|-------|--------|
| **Image Repository** | `ghcr.io/trolle6/fluxer-tts` |
| **Image Tag** | `latest` |
| **Pull Policy** | Pull if not present *(or Always)* |

### 5. Container settings

**Restart Policy:** `Unless Stopped`

**Environment variables** — click **Add** for each:

| Name | Value |
|------|--------|
| `FLUXER_BOT_TOKEN` | *(your Fluxer bot token)* |
| `OPENAI_API_KEY` | *(your OpenAI key)* |
| `DATA_ROOT` | `/app/Data` |
| `FLUXER_LOG_CHANNEL_ID` | *(your log channel ID)* |
| `FLUXER_MODERATOR_ROLE_ID` | *(your mod role ID)* |
| `FLUXER_CHANNEL_ID` | *(your main/TTS channel ID)* |

Leave optional vars empty if you don't use them.

### 6. Storage (pick your HDD / pool)

**Add** → **Host Path**:

| Field | Value |
|-------|--------|
| **Host Path** | e.g. `/mnt/tank/apps/fluxer-bot/data` — browse to your pool |
| **Mount Path** | `/app/Data` |
| **Enable ACL** | Off (unless you know you need it) |
| **Create directory if missing** | On |

Create the folder first if you like:
```bash
sudo mkdir -p /mnt/tank/apps/fluxer-bot/data
```
*(Replace `tank` with your pool name.)*

### 7. Install

Click **Save** / **Install**. Wait until status shows **Running**.

### 8. Check logs

**Apps → Installed Applications → fluxer-bot → Logs**

You should see the bot connect. If it crashes immediately, you're usually missing `FLUXER_BOT_TOKEN` or `OPENAI_API_KEY`.

---

## Method B — Custom App (Compose from GitHub)

Use this if **Launch Docker Image** isn't available, or Method A failed.

### 1. Delete failed app (same as above)

### 2. Custom App

**Apps → Discover Apps → Custom App**

### 3. Install via Compose

Choose **Install via Compose** (or paste YAML).

**Compose file URL** (raw GitHub):
```
https://raw.githubusercontent.com/trolle6/Fluxer-TTS/master/docker-compose.truenas.yml
```

Or paste the contents of `docker-compose.truenas.yml` from the repo.

**Do NOT** use the root `docker-compose.yml` — it used to include `build:` and breaks on TrueNAS.

### 4. Environment

In the **Environment** section of the wizard, set the same variables as Method A step 5.  
TrueNAS substitutes `${FLUXER_BOT_TOKEN}` etc. from what you enter here.

### 5. Storage

Same as Method A step 6 — **Host Path** → `/app/Data`.

Compose file intentionally has **no volumes** block; TrueNAS adds storage in the UI.

### 6. Install and check logs

---

## GHCR must be public

Your `docker pull` already worked — good. If the **App** install fails on pull:

GitHub → **Packages** → **fluxer-tts** → **Package settings** → **Public**

---

## If install still fails (EFAULT)

SSH to the NAS:

```bash
tail -n 200 /var/log/app_lifecycle.log
```

| Log says | Fix |
|----------|-----|
| `build` / `Dockerfile` | Wrong compose file — use `docker-compose.truenas.yml` |
| `pull access denied` | Make GHCR package public |
| `invalid compose` | Use Method A (Launch Docker Image) instead |
| Container exits | Fix env vars; check App **Logs** tab |

---

## Migrating old bot data

Copy into the host path you mounted (e.g. `/mnt/tank/apps/fluxer-bot/data/`):

```
secret_santa_state.json
archive/
distributed_files/
distributed_files_metadata.json
```

Stop the app → copy files → start the app.

---

## SSH test (optional — NOT a TrueNAS App)

This runs the bot outside the Apps system (won't show in Installed Applications):

```bash
sudo mkdir -p /mnt/tank/apps/fluxer-bot/data

sudo docker run -d --name fluxer-test \
  -e FLUXER_BOT_TOKEN="YOUR_TOKEN" \
  -e OPENAI_API_KEY="YOUR_KEY" \
  -e DATA_ROOT=/app/Data \
  -v /mnt/tank/apps/fluxer-bot/data:/app/Data \
  --restart unless-stopped \
  ghcr.io/trolle6/fluxer-tts:latest

sudo docker logs -f fluxer-test
```

Use this only to verify the image works; for a managed app, use Method A or B above.
