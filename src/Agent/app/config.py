from __future__ import annotations

import os
from dataclasses import dataclass

from dotenv import load_dotenv


@dataclass(slots=True)
class AgentConfig:
    server_url: str
    instance_id: str
    api_key: str
    config_refresh_seconds: int = 300
    heartbeat_interval_seconds: int = 60
    result_batch_interval_seconds: int = 10
    verify_tls: bool = True
    log_level: str = "INFO"
    result_queue_path: str = "./data/result-queue.jsonl"


REQUIRED_KEYS = ("SERVER_URL", "INSTANCE_ID", "API_KEY")


def load_config() -> AgentConfig:
    load_dotenv()

    missing = [key for key in REQUIRED_KEYS if not os.getenv(key)]
    if missing:
        missing_values = ", ".join(missing)
        raise ValueError(f"Missing required environment variables: {missing_values}")

    return AgentConfig(
        server_url=os.environ["SERVER_URL"],
        instance_id=os.environ["INSTANCE_ID"],
        api_key=os.environ["API_KEY"],
        config_refresh_seconds=_get_int("CONFIG_REFRESH_SECONDS", 300),
        heartbeat_interval_seconds=_get_int("HEARTBEAT_INTERVAL_SECONDS", 60),
        result_batch_interval_seconds=_get_int("RESULT_BATCH_INTERVAL_SECONDS", 10),
        verify_tls=_get_bool("VERIFY_TLS", True),
        log_level=os.getenv("LOG_LEVEL", "INFO"),
        result_queue_path=os.getenv("RESULT_QUEUE_PATH", "./data/result-queue.jsonl"),
    )


def _get_int(name: str, default: int) -> int:
    raw_value = os.getenv(name)
    if raw_value is None or raw_value == "":
        return default

    try:
        return int(raw_value)
    except ValueError as exc:
        raise ValueError(f"Environment variable {name} must be an integer.") from exc


def _get_bool(name: str, default: bool) -> bool:
    raw_value = os.getenv(name)
    if raw_value is None or raw_value == "":
        return default

    normalized = raw_value.strip().lower()
    if normalized in {"1", "true", "yes", "on"}:
        return True
    if normalized in {"0", "false", "no", "off"}:
        return False

    raise ValueError(f"Environment variable {name} must be a boolean value.")
