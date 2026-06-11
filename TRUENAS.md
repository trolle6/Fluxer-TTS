# TrueNAS — paste repo, install (30 seconds)

**Custom App → Git repo:** `https://github.com/trolle6/Fluxer-TTS`  
**Branch:** `master` · **Compose:** `docker-compose.yml`

Then in the wizard only:

**Environment:** `FLUXER_BOT_TOKEN`, `OPENAI_API_KEY`, `DATA_ROOT`=`/app/Data`, plus channel/role IDs  

**Storage:** Host path on your pool → mount `/app/Data`

Full copy-paste list: **[TRUENAS-INSTALL.txt](TRUENAS-INSTALL.txt)**

---

# TrueNAS SCALE — detailed notes

The UI error `EFAULT` is generic. Real errors: `tail -n 200 /var/log/app_lifecycle.log`

## Why the compose file is tiny

TrueNAS breaks on `${VAR:-default}`, `build:`, `container_name`, and `./data` volumes.  
Root `docker-compose.yml` is only:

```yaml
services:
  fluxer:
    image: ghcr.io/trolle6/fluxer-tts:latest
    restart: unless-stopped
```

All secrets and storage go in the **TrueNAS wizard**, not in the YAML.

## GHCR public

GitHub → Packages → fluxer-tts → Package settings → **Public**

## Local Docker (not TrueNAS)

Use `docker-compose.ghcr.yml` or `docker-compose.build.yml` instead.
