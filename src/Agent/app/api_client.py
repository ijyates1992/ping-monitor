from __future__ import annotations

from dataclasses import asdict
from typing import Any

import httpx

from app.config import AgentConfig
from app.models import ConfigResponse, HeartbeatRequest, HeartbeatResponse, HelloRequest, HelloResponse, ResultsRequest
from app.version import AGENT_VERSION


class AgentApiClient:
    def __init__(self, config: AgentConfig) -> None:
        self._config = config
        self._client = httpx.Client(
            base_url=self._config.server_url,
            verify=self._config.verify_tls,
            headers={
                "X-Instance-Id": self._config.instance_id,
                "Authorization": f"Bearer {self._config.api_key}",
                "Accept": "application/json",
                "Content-Type": "application/json",
                "User-Agent": f"ping-agent/{AGENT_VERSION}",
            },
            timeout=30.0,
        )

    def close(self) -> None:
        self._client.close()

    def send_hello(self, request: HelloRequest) -> HelloResponse:
        response = self._client.post("/api/v1/agent/hello", json=_to_camel_dict(asdict(request)))
        response.raise_for_status()
        payload = response.json()
        return HelloResponse(**_to_snake_kwargs(payload))

    def fetch_config(self) -> ConfigResponse:
        response = self._client.get("/api/v1/agent/config")
        response.raise_for_status()
        payload = response.json()
        assignments = [
            _assignment_from_dict(item)
            for item in payload.get("assignments", [])
        ]
        return ConfigResponse(
            config_version=payload["configVersion"],
            generated_at_utc=payload["generatedAtUtc"],
            assignments=assignments,
        )

    def send_heartbeat(self, request: HeartbeatRequest) -> HeartbeatResponse:
        response = self._client.post("/api/v1/agent/heartbeat", json=_to_camel_dict(asdict(request)))
        response.raise_for_status()
        return HeartbeatResponse(**_to_snake_kwargs(response.json()))

    def submit_results(self, request: ResultsRequest) -> dict[str, Any]:
        response = self._client.post("/api/v1/agent/results", json=_to_camel_dict(asdict(request)))
        response.raise_for_status()
        return response.json()


def _assignment_from_dict(payload: dict[str, Any]):
    from app.models import AssignmentModel

    snake_payload = _to_snake_kwargs(payload)
    if "depends_on_endpoint_ids" not in snake_payload:
        singular_dependency = snake_payload.pop("depends_on_endpoint_id", None)
        if singular_dependency:
            snake_payload["depends_on_endpoint_ids"] = [singular_dependency]
    return AssignmentModel(**snake_payload)


def _to_snake_kwargs(payload: dict[str, Any]) -> dict[str, Any]:
    converted: dict[str, Any] = {}
    for key, value in payload.items():
        snake_key = []
        for char in key:
            if char.isupper():
                snake_key.append("_")
                snake_key.append(char.lower())
            else:
                snake_key.append(char)
        converted["".join(snake_key).lstrip("_")] = value
    return converted


def _to_camel_dict(payload: dict[str, Any]) -> dict[str, Any]:
    converted: dict[str, Any] = {}
    for key, value in payload.items():
        parts = key.split("_")
        camel_key = parts[0] + "".join(part.capitalize() for part in parts[1:])
        if isinstance(value, list):
            converted[camel_key] = [
                _to_camel_dict(item) if isinstance(item, dict) else item
                for item in value
            ]
        elif isinstance(value, dict):
            converted[camel_key] = _to_camel_dict(value)
        else:
            converted[camel_key] = value
    return converted
