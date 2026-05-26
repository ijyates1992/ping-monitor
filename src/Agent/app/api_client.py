from __future__ import annotations

from dataclasses import asdict
from typing import Any

import httpx

from app.config import AgentConfig
from app.models import ConfigResponse, HeartbeatRequest, HeartbeatResponse, HelloRequest, HelloResponse, ResultsRequest
from app.version import AGENT_VERSION


class AgentApiError(Exception):
    def __init__(
        self,
        message: str,
        *,
        status_code: int | None = None,
        content_type: str | None = None,
        body_preview: str | None = None,
    ) -> None:
        self.status_code = status_code
        self.content_type = content_type
        self.body_preview = body_preview
        super().__init__(self._build_message(message))

    def _build_message(self, message: str) -> str:
        details: list[str] = []
        if self.status_code is not None:
            details.append(f"status={self.status_code}")
        if self.content_type:
            details.append(f"content_type={self.content_type}")
        if self.body_preview:
            details.append(f"body_preview={self.body_preview}")
        if not details:
            return message
        return f"{message} ({', '.join(details)})"


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
        payload = _parse_json_dict_response(response, "hello")
        return HelloResponse(**_to_snake_kwargs(payload))

    def fetch_config(self) -> ConfigResponse:
        response = self._client.get("/api/v1/agent/config")
        response.raise_for_status()
        payload = _parse_json_dict_response(response, "config")
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
        payload = _parse_json_dict_response(response, "heartbeat")
        return HeartbeatResponse(**_to_snake_kwargs(payload))

    def submit_results(self, request: ResultsRequest) -> dict[str, Any]:
        response = self._client.post("/api/v1/agent/results", json=_to_camel_dict(asdict(request)))
        response.raise_for_status()
        response_text = response.text.strip()
        if not response_text:
            return {}
        try:
            payload = response.json()
        except ValueError:
            return {}
        if isinstance(payload, dict):
            return payload
        return {}


def _parse_json_dict_response(response: httpx.Response, operation: str) -> dict[str, Any]:
    response_text = response.text.strip()
    if not response_text:
        raise AgentApiError(
            f"Expected JSON response body for {operation}, but received an empty response",
            status_code=response.status_code,
            content_type=response.headers.get("content-type"),
            body_preview="<empty>",
        )

    try:
        payload = response.json()
    except ValueError as ex:
        raise AgentApiError(
            f"Expected valid JSON response body for {operation}, but parsing failed",
            status_code=response.status_code,
            content_type=response.headers.get("content-type"),
            body_preview=_body_preview(response_text),
        ) from ex

    if not isinstance(payload, dict):
        raise AgentApiError(
            f"Expected JSON object response body for {operation}, but received {type(payload).__name__}",
            status_code=response.status_code,
            content_type=response.headers.get("content-type"),
            body_preview=_body_preview(response_text),
        )
    return payload


def _body_preview(body: str, limit: int = 200) -> str:
    normalized = " ".join(body.split())
    if len(normalized) <= limit:
        return normalized
    return f"{normalized[:limit]}..."


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
