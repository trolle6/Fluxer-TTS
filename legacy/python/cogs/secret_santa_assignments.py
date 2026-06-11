"""
Secret Santa assignments – FluxerTools

Algorithm, history, no-repeat pairings. Uses secrets.SystemRandom.
"""

import secrets
from collections import Counter
from pathlib import Path
from typing import Dict, List, Optional

from .secret_santa_storage import ARCHIVE_DIR, load_json


def load_history_from_archives(
    archive_dir: Path,
    exclude_years: Optional[List[int]] = None,
    logger=None,
) -> tuple[Dict[str, List[int]], List[int]]:
    """Load assignment history from archives. Returns (history dict, available years)."""
    exclude_years = exclude_years or []
    history: Dict[str, List[int]] = {}
    available_years: List[int] = []

    for archive_file in archive_dir.glob("[0-9]*.json"):
        if "backups" in archive_file.parts:
            continue
        try:
            year_str = archive_file.stem
            if not year_str.isdigit() or len(year_str) != 4:
                continue
            year = int(year_str)
            available_years.append(year)
            if year in exclude_years:
                if logger:
                    logger.info("Excluding year %s from history", year)
                continue

            data = load_json(archive_file)
            if not isinstance(data, dict):
                continue

            event_data = data.get("event")
            if isinstance(event_data, dict):
                event_assignments = event_data.get("assignments", {})
                if isinstance(event_assignments, dict):
                    for giver, receiver in event_assignments.items():
                        try:
                            receiver_int = int(receiver)
                            history.setdefault(str(giver), []).append(receiver_int)
                        except (ValueError, TypeError):
                            continue
            elif "assignments" in data and isinstance(data["assignments"], list):
                for a in data["assignments"]:
                    if not isinstance(a, dict):
                        continue
                    giver_id = a.get("giver_id")
                    receiver_id = a.get("receiver_id")
                    if giver_id and receiver_id:
                        try:
                            receiver_int = int(receiver_id)
                            history.setdefault(str(giver_id), []).append(receiver_int)
                        except (ValueError, TypeError):
                            continue
        except Exception as e:
            if logger:
                logger.warning("Error loading archive %s: %s", archive_file, e)

    available_years.sort()
    return history, available_years


def validate_assignment_possibility(
    participants: List[int],
    history: Dict[str, List[int]],
) -> Optional[str]:
    """Check if assignments are possible. Returns error message or None."""
    if len(participants) < 2:
        return "Need at least 2 participants for Secret Santa"

    problematic = []
    for giver in participants:
        unacceptable = set(history.get(str(giver), []))
        available = [p for p in participants if p not in unacceptable and p != giver]
        if not available:
            problematic.append(str(giver))

    if problematic:
        return (
            f"Assignment impossible - users {', '.join(problematic)} have no valid receivers. "
            "Use fallback or clear history."
        )
    return None


def _validate_assignment_integrity(
    assignments: Dict[int, int],
    participants: List[int],
) -> None:
    """Validate assignment integrity. Raises ValueError on failure."""
    if not assignments:
        raise ValueError("No assignments")
    if len(assignments) != len(participants):
        raise ValueError(
            f"Mismatch: {len(assignments)} assignments for {len(participants)} participants"
        )
    givers = set(assignments.keys())
    expected = set(participants)
    if givers != expected:
        raise ValueError(f"Giver mismatch: missing {expected - givers}, extra {givers - expected}")
    receivers = list(assignments.values())
    if set(receivers) != expected:
        raise ValueError("Receiver mismatch")
    if len(receivers) != len(set(receivers)):
        dups = {r: c for r, c in Counter(receivers).items() if c > 1}
        raise ValueError(f"Duplicate receivers: {dups}")
    for g, r in assignments.items():
        if g == r:
            raise ValueError(f"Self-assignment: {g}")
        if g not in expected or r not in expected:
            raise ValueError(f"Invalid participant in assignment: {g} -> {r}")


def make_assignments(
    participants: List[int],
    history: Dict[str, List[int]],
    logger=None,
) -> Dict[int, int]:
    """Create assignments avoiding history repeats. Returns {giver_id: receiver_id}."""
    if len(participants) < 2:
        raise ValueError("Need at least 2 participants")

    rng = secrets.SystemRandom()

    # 2 participants: simple exchange
    if len(participants) == 2:
        p1, p2 = participants[0], participants[1]
        h1 = set(history.get(str(p1), []))
        h2 = set(history.get(str(p2), []))
        if p2 in h1 or p1 in h2:
            raise ValueError("2-person assignment failed: already paired before")
        result = {p1: p2, p2: p1}
        history.setdefault(str(p1), []).append(p2)
        history.setdefault(str(p2), []).append(p1)
        return result

    # 3+ participants
    max_attempts = max(10, len(participants))
    for attempt in range(max_attempts):
        try:
            result: Dict[int, int] = {}
            temp_history = {
                k: list(v) if isinstance(v, list) else []
                for k, v in history.items()
            }
            shuffled = participants.copy()
            rng.shuffle(shuffled)

            for giver in shuffled:
                unacceptable = set(temp_history.get(str(giver), []))
                for g, r in result.items():
                    if r == giver:
                        unacceptable.add(g)
                    unacceptable.add(r)
                available = [p for p in participants if p not in unacceptable and p != giver]
                if not available:
                    raise ValueError(f"No valid receivers for giver {giver}")
                receiver = rng.choice(available)
                result[giver] = receiver
                temp_history.setdefault(str(giver), []).append(receiver)

            _validate_assignment_integrity(result, participants)
            for g, r in result.items():
                history.setdefault(str(g), []).append(r)
            return result
        except ValueError:
            if attempt == max_attempts - 1:
                raise ValueError("Assignment failed with current history constraints")
    raise ValueError("Assignment failed")
