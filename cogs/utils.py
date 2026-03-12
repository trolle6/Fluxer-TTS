"""
Bot Utilities - Shared Components for All Cogs
Ported from WaveTechToolBoxx - platform agnostic
"""

import asyncio
import json
import logging
import time
from collections import OrderedDict, deque
from pathlib import Path
from typing import Any, Generic, Optional, TypeVar

T = TypeVar("T")
logger = logging.getLogger("bot")

__all__ = ["RateLimiter", "CircuitBreaker", "LRUCache", "JsonFile", "RequestCache"]


class RateLimiter:
    """Token bucket rate limiter."""

    def __init__(self, limit: int, window: int):
        self.limit = limit
        self.window = window
        self.tokens: dict[str, deque[float]] = {}
        self._lock = asyncio.Lock()

    async def check(self, key: str) -> bool:
        async with self._lock:
            now = time.time()
            if key not in self.tokens:
                self.tokens[key] = deque()
            dq = self.tokens[key]
            while dq and now - dq[0] >= self.window:
                dq.popleft()
            if len(dq) < self.limit:
                dq.append(now)
                return True
            return False

    async def reset(self, key: str):
        async with self._lock:
            self.tokens.pop(key, None)


class CircuitBreaker:
    """Circuit breaker for API failure protection."""

    STATE_CLOSED = "CLOSED"
    STATE_OPEN = "OPEN"
    STATE_HALF_OPEN = "HALF_OPEN"

    def __init__(self, failure_threshold: int = 5, recovery_timeout: int = 60, success_threshold: int = 2):
        self.failure_threshold = failure_threshold
        self.recovery_timeout = recovery_timeout
        self.success_threshold = success_threshold
        self.failures = 0
        self.last_failure: Optional[float] = None
        self.state = self.STATE_CLOSED
        self.success_count = 0
        self._lock = asyncio.Lock()

    async def record_success(self):
        async with self._lock:
            if self.state == self.STATE_HALF_OPEN:
                self.success_count += 1
                if self.success_count >= self.success_threshold:
                    self.state = self.STATE_CLOSED
                    self.failures = 0
                    self.success_count = 0
            else:
                self.failures = max(0, self.failures - 1)

    async def record_failure(self):
        async with self._lock:
            self.failures += 1
            self.last_failure = time.time()
            if self.failures >= self.failure_threshold:
                self.state = self.STATE_OPEN
            elif self.state == self.STATE_HALF_OPEN:
                self.state = self.STATE_OPEN
                self.success_count = 0

    async def can_attempt(self) -> bool:
        async with self._lock:
            if self.state == self.STATE_CLOSED:
                return True
            if self.state == self.STATE_OPEN:
                if self.last_failure and time.time() - self.last_failure > self.recovery_timeout:
                    self.state = self.STATE_HALF_OPEN
                    self.success_count = 0
                    return True
                return False
            return True


class LRUCache(Generic[T]):
    """LRU cache with TTL."""

    def __init__(self, max_size: int = 100, ttl: int = 3600):
        self.max_size = max_size
        self.ttl = ttl
        self._cache: OrderedDict[str, tuple[T, float]] = OrderedDict()
        self._hits = 0
        self._misses = 0
        self._lock = asyncio.Lock()

    async def get(self, key: str) -> Optional[T]:
        async with self._lock:
            if key in self._cache:
                value, timestamp = self._cache[key]
                if time.time() - timestamp < self.ttl:
                    self._cache.move_to_end(key)
                    self._hits += 1
                    return value
                del self._cache[key]
            self._misses += 1
            return None

    async def set(self, key: str, value: T):
        async with self._lock:
            now = time.time()
            if key in self._cache:
                self._cache[key] = (value, now)
                self._cache.move_to_end(key)
            else:
                if len(self._cache) >= self.max_size:
                    self._cache.popitem(last=False)
                self._cache[key] = (value, now)

    async def cleanup(self):
        async with self._lock:
            now = time.time()
            expired = [k for k, (_, ts) in self._cache.items() if now - ts >= self.ttl]
            for k in expired:
                del self._cache[k]


class JsonFile:
    """Thread-safe JSON file operations."""

    def __init__(self, path: str):
        self.path = Path(path)
        self.lock = asyncio.Lock()

    async def load(self, default: Any = None) -> Any:
        async with self.lock:
            if self.path.exists():
                try:
                    return json.loads(self.path.read_text(encoding="utf-8"))
                except (json.JSONDecodeError, OSError, UnicodeDecodeError):
                    return default if default is not None else {}
            return default if default is not None else {}

    async def save(self, data: Any):
        async with self.lock:
            self.path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


class RequestCache:
    """Simple deduplication cache."""

    def __init__(self, ttl: int = 3600, max_size: Optional[int] = 1000):
        self.cache: dict[str, tuple[Any, float]] = {}
        self.ttl = ttl
        self.max_size = max_size
        self._lock = asyncio.Lock()

    async def get(self, key: str) -> Optional[Any]:
        async with self._lock:
            if key in self.cache:
                val, expires = self.cache[key]
                if time.time() < expires:
                    return val
                del self.cache[key]
            return None

    async def set(self, key: str, value: Any):
        async with self._lock:
            if self.max_size and key not in self.cache and len(self.cache) >= self.max_size:
                soonest = min(self.cache, key=lambda k: self.cache[k][1])
                del self.cache[soonest]
            self.cache[key] = (value, time.time() + self.ttl)
