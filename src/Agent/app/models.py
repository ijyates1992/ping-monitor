from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional


@dataclass(slots=True)
class HelloRequest:
    agent_version: str
    machine_name: str
    platform: str
    capabilities: list[str]
    started_at_utc: str


@dataclass(slots=True)
class HelloResponse:
    agent_id: str
    server_time_utc: str
    config_refresh_seconds: int
    heartbeat_interval_seconds: int
    result_batch_interval_seconds: int
    max_result_batch_size: int
    config_version: str


@dataclass(slots=True)
class AssignmentModel:
    assignment_id: str
    endpoint_id: str
    name: str
    target: str
    check_type: str
    enabled: bool
    ping_interval_seconds: int
    retry_interval_seconds: int
    timeout_ms: int
    failure_threshold: int
    recovery_threshold: int
    depends_on_endpoint_id: Optional[str]
    tags: list[str] = field(default_factory=list)


@dataclass(slots=True)
class ConfigResponse:
    config_version: str
    generated_at_utc: str
    assignments: list[AssignmentModel] = field(default_factory=list)


@dataclass(slots=True)
class HeartbeatRequest:
    agent_version: str
    sent_at_utc: str
    config_version: str
    active_assignments: int
    queued_result_count: int
    status: str


@dataclass(slots=True)
class HeartbeatResponse:
    ok: bool
    server_time_utc: str
    config_changed: bool


@dataclass(slots=True)
class ResultItem:
    assignment_id: str
    endpoint_id: str
    check_type: str
    checked_at_utc: str
    success: bool
    round_trip_ms: Optional[int]
    error_code: Optional[str]
    error_message: Optional[str]


@dataclass(slots=True)
class ResultsRequest:
    sent_at_utc: str
    batch_id: str
    results: list[ResultItem] = field(default_factory=list)
