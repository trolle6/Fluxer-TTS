"""
Secret Santa storage – FluxerTools

File I/O, state, and archives. Crash-safe, atomic writes.
"""

import datetime as dt
import json
import logging
import time
from pathlib import Path
from typing import Any, Dict, Optional

ROOT: Path = Path(__file__).parent  # cogs/
STATE_FILE: Path = ROOT / "secret_santa_state.json"
ARCHIVE_DIR: Path = ROOT / "archive"
BACKUPS_DIR: Path = ARCHIVE_DIR / "backups"

ARCHIVE_DIR.mkdir(parents=True, exist_ok=True)
BACKUPS_DIR.mkdir(parents=True, exist_ok=True)

LOAD_JSON_MAX_BYTES = 10 * 1024 * 1024  # 10MB


def load_json(path: Path, default: Any = None) -> Any:
    """Load JSON from disk. Returns default on failure."""
    fallback = default if default is not None else {}
    if path is None or not hasattr(path, "exists"):
        return fallback
    if not path.exists():
        return fallback
    try:
        if path.stat().st_size > LOAD_JSON_MAX_BYTES:
            return fallback
        text = path.read_text(encoding="utf-8", errors="replace").strip()
        if not text:
            return fallback
        return json.loads(text)
    except (json.JSONDecodeError, OSError, UnicodeDecodeError):
        return fallback


def save_json(path: Path, data: Any, logger: Optional[logging.Logger] = None) -> None:
    """Save JSON atomically (write-temp-replace)."""
    temp = path.with_suffix(".tmp")
    try:
        temp.write_text(
            json.dumps(data, indent=2, ensure_ascii=False),
            encoding="utf-8",
        )
        temp.replace(path)
    except Exception as e:
        if temp.exists():
            try:
                temp.unlink()
            except Exception:
                pass
        if logger:
            logger.error("Failed to save JSON to %s: %s", path, e)
        raise


def get_default_state() -> dict:
    """Canonical empty state structure."""
    return {
        "current_year": dt.date.today().year,
        "pair_history": {},
        "current_event": None,
    }


def validate_state_structure(state: dict, logger: Optional[logging.Logger] = None) -> dict:
    """Ensure state has required keys and valid types."""
    if not isinstance(state, dict):
        if logger:
            logger.error("State is not a dict, using defaults")
        return get_default_state()

    today_year = dt.date.today().year
    raw_year = state.get("current_year")
    if not isinstance(raw_year, int) or raw_year < 2000 or raw_year > 2100:
        if raw_year is not None and logger:
            logger.warning("Invalid current_year %r, resetting to %s", raw_year, today_year)
        state["current_year"] = today_year
    if "pair_history" not in state:
        state["pair_history"] = {}
    if "current_event" not in state:
        state["current_event"] = None

    current_event = state.get("current_event")
    if current_event:
        if not isinstance(current_event, dict):
            if logger:
                logger.error("Invalid event state - not a dict, resetting")
            state["current_event"] = None
        elif not isinstance(current_event.get("participants"), dict):
            if logger:
                logger.error("Invalid event state - participants not a dict, resetting")
            state["current_event"] = None

    return state


def load_state_with_fallback(logger: Optional[logging.Logger] = None) -> dict:
    """Load state: main → .backup → defaults."""
    try:
        state = load_json(STATE_FILE, get_default_state())
        state = validate_state_structure(state, logger)
        if logger:
            ev = state.get("current_event")
            active = bool(ev and ev.get("active")) if isinstance(ev, dict) else False
            logger.info("State loaded. Active event: %s", active)
        return state
    except Exception as e:
        if logger:
            logger.error("Failed to load state: %s, trying backup", e, exc_info=True)
        backup_path = STATE_FILE.with_suffix(".backup")
        if backup_path.exists():
            try:
                if logger:
                    logger.info("Attempting to load from backup...")
                state = load_json(backup_path, get_default_state())
                state = validate_state_structure(state, logger)
                if logger:
                    logger.info("Backup state loaded")
                return state
            except Exception as backup_error:
                if logger:
                    logger.error("Backup load failed: %s", backup_error)
        if logger:
            logger.warning("Using clean default state")
        return get_default_state()


def save_state(state: dict, logger: Optional[logging.Logger] = None) -> bool:
    """Persist state. On main failure, try .backup."""
    try:
        save_json(STATE_FILE, state, logger)
        return True
    except Exception as e:
        if logger:
            logger.error("CRITICAL: Failed to save state: %s", e, exc_info=True)
        try:
            backup_path = STATE_FILE.with_suffix(".backup")
            save_json(backup_path, state, logger)
            if logger:
                logger.warning("Saved to backup: %s", backup_path)
        except Exception as backup_error:
            if logger:
                logger.error("Backup save failed: %s", backup_error)
            return False
        return False


def load_all_archives(logger: Optional[logging.Logger] = None) -> Dict[int, dict]:
    """Load all year archives into {year: data}."""
    archives = {}
    for archive_file in ARCHIVE_DIR.glob("[0-9]*.json"):
        if "backups" in archive_file.parts:
            continue
        year_str = archive_file.stem
        if not year_str.isdigit() or len(year_str) != 4:
            continue
        try:
            year_int = int(year_str)
            data = load_json(archive_file)
            if data and "event" in data:
                archives[year_int] = data
            elif data and "assignments" in data and isinstance(data["assignments"], list):
                participants = {}
                gifts = {}
                assignments_map = {}
                for a in data["assignments"]:
                    if not isinstance(a, dict):
                        continue
                    gid = a.get("giver_id", "")
                    gname = a.get("giver_name", "Unknown")
                    rid = a.get("receiver_id", "")
                    rname = a.get("receiver_name", "Unknown")
                    gift = a.get("gift")
                    if isinstance(gift, str) and gift.strip():
                        gifts[gid] = {"gift": gift, "receiver_name": rname, "receiver_id": rid}
                    participants[gid] = gname
                    if rid:
                        participants[rid] = rname
                    if gid and rid:
                        assignments_map[gid] = rid
                archives[year_int] = {
                    "year": year_int,
                    "event": {
                        "participants": participants,
                        "gift_submissions": gifts,
                        "assignments": assignments_map,
                    },
                }
        except Exception as e:
            if logger:
                logger.warning("Error loading archive %s: %s", archive_file, e)
    return archives


def archive_event(event: Dict[str, Any], year: int, logger: Optional[logging.Logger] = None) -> str:
    """Archive completed event to archive/YYYY.json. Backup if exists."""
    if not event or not isinstance(event, dict):
        if logger:
            logger.error("archive_event: event must be non-empty dict")
        raise ValueError("event must be a non-empty dict")

    today_year = dt.date.today().year
    if not isinstance(year, int) or year < 2000 or year > 2100:
        if logger:
            logger.warning("Invalid year %r, using %s", year, today_year)
        year = today_year

    archive_data = {
        "year": year,
        "event": event.copy(),
        "archived_at": time.time(),
        "timestamp": dt.datetime.now().isoformat(),
    }
    archive_path = ARCHIVE_DIR / f"{year}.json"

    if archive_path.exists():
        ts = dt.datetime.now().strftime("%Y%m%d_%H%M%S")
        backup_path = ARCHIVE_DIR / f"{year}_backup_{ts}.json"
        save_json(backup_path, archive_data, logger)
        if logger:
            logger.warning("Archive %s exists! Saved to %s", archive_path.name, backup_path.name)
        return backup_path.name
    save_json(archive_path, archive_data, logger)
    if logger:
        logger.info("Archived Secret Santa %s -> %s", year, archive_path.name)
    return archive_path.name


__all__ = [
    "ROOT", "STATE_FILE", "ARCHIVE_DIR", "BACKUPS_DIR",
    "load_json", "save_json", "get_default_state", "validate_state_structure",
    "load_state_with_fallback", "save_state", "load_all_archives", "archive_event",
]
