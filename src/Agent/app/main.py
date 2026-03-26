from __future__ import annotations

import logging
import platform
import time
from datetime import UTC, datetime

import httpx

from app.api_client import AgentApiClient
from app.checks import CheckRunner
from app.config import load_config
from app.models import ConfigResponse, HeartbeatRequest, HelloRequest, ResultsRequest
from app.result_queue import ResultQueue
from app.scheduler import AssignmentScheduler

BOOTSTRAP_CONFIG_VERSION = "bootstrap-pending"


def main() -> None:
    config = load_config()
    logging.basicConfig(level=config.log_level)
    logging.info("Starting agent instance %s", config.instance_id)

    client = AgentApiClient(config)
    queue = ResultQueue()
    scheduler = AssignmentScheduler(CheckRunner())

    started_at = _utc_now()
    hello_response = client.send_hello(
        HelloRequest(
            agent_version="0.1.0",
            machine_name=platform.node() or config.instance_id,
            platform=platform.system().lower() or "unknown",
            capabilities=["icmp"],
            started_at_utc=started_at,
        )
    )

    current_config = _initialize_config(client, hello_response.config_version)
    logging.info(
        "Agent %s connected with config version %s containing %d assignments",
        hello_response.agent_id,
        current_config.config_version,
        len(current_config.assignments),
    )

    config_refresh_seconds = max(1, hello_response.config_refresh_seconds)
    heartbeat_interval_seconds = max(1, hello_response.heartbeat_interval_seconds)
    result_batch_interval_seconds = max(1, hello_response.result_batch_interval_seconds)
    max_result_batch_size = max(1, hello_response.max_result_batch_size)
    logging.info(
        "Agent loop intervals: config_refresh=%ss heartbeat=%ss result_batch=%ss",
        config_refresh_seconds,
        heartbeat_interval_seconds,
        result_batch_interval_seconds,
    )

    previous_active_assignments = -1
    previous_total_assignments = -1

    last_config_refresh = 0.0
    last_heartbeat = 0.0
    last_result_batch = 0.0

    try:
        while True:
            now = time.monotonic()

            if now - last_config_refresh >= config_refresh_seconds:
                current_config = _try_refresh_config(client, current_config, "scheduled")
                last_config_refresh = now

            active_assignments = len([assignment for assignment in current_config.assignments if assignment.enabled])
            if len(current_config.assignments) != previous_total_assignments or active_assignments != previous_active_assignments:
                logging.info(
                    "Assignment counts changed: total=%d active=%d",
                    len(current_config.assignments),
                    active_assignments,
                )
                if active_assignments == 0:
                    logging.info("No active assignments currently configured. Agent will keep heartbeating and polling for config.")
                elif previous_active_assignments == 0:
                    logging.info("Assignments are now active. Agent will begin executing checks automatically.")
                previous_total_assignments = len(current_config.assignments)
                previous_active_assignments = active_assignments

            if active_assignments > 0:
                results = scheduler.run_once(current_config.assignments)
                if results:
                    logging.info("Executed %d checks in this cycle", len(results))
                queue.extend(results)
            if now - last_result_batch >= result_batch_interval_seconds:
                batch = queue.dequeue_batch(max_result_batch_size)
                if batch:
                    try:
                        client.submit_results(
                            ResultsRequest(
                                sent_at_utc=_utc_now(),
                                batch_id=f"results-{config.instance_id}-{int(now)}",
                                results=batch,
                            )
                        )
                    except httpx.HTTPError as ex:
                        logging.error("Result submission failed and will be retried: %s", _format_http_error(ex))
                        queue.requeue_front(batch)
                else:
                    logging.debug("No queued results to submit in this cycle.")
                last_result_batch = now

            if now - last_heartbeat >= heartbeat_interval_seconds:
                try:
                    heartbeat_response = client.send_heartbeat(
                        HeartbeatRequest(
                            agent_version="0.1.0",
                            sent_at_utc=_utc_now(),
                            config_version=current_config.config_version,
                            active_assignments=active_assignments,
                            queued_result_count=len(queue),
                            status="online",
                        )
                    )
                    logging.info(
                        "Heartbeat sent: active_assignments=%d queued_results=%d",
                        active_assignments,
                        len(queue),
                    )
                    if heartbeat_response.config_changed:
                        current_config = _try_refresh_config(client, current_config, "heartbeat-config-changed")
                        last_config_refresh = now
                except httpx.HTTPError as ex:
                    logging.error("Heartbeat failed: %s", _format_http_error(ex))
                last_heartbeat = now

            time.sleep(1)
    except KeyboardInterrupt:
        logging.info("Agent shutdown requested. Exiting.")
    finally:
        client.close()


def _utc_now() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _format_http_error(error: httpx.HTTPError) -> str:
    if isinstance(error, httpx.HTTPStatusError):
        response_body = error.response.text.strip()
        if response_body:
            return f"{error}; response={response_body}"
    return str(error)


def _try_refresh_config(client: AgentApiClient, current_config: ConfigResponse, reason: str) -> ConfigResponse:
    try:
        updated_config = client.fetch_config()
        logging.info(
            "Fetched config (%s): version=%s assignments=%d",
            reason,
            updated_config.config_version,
            len(updated_config.assignments),
        )
        return updated_config
    except httpx.HTTPError as ex:
        logging.error(
            "Config refresh failed (%s). Continuing with previous config version %s: %s",
            reason,
            current_config.config_version,
            _format_http_error(ex),
        )
        return current_config


def _initialize_config(client: AgentApiClient, hello_config_version: str) -> ConfigResponse:
    bootstrap_config = ConfigResponse(
        config_version=hello_config_version or BOOTSTRAP_CONFIG_VERSION,
        generated_at_utc=_utc_now(),
        assignments=[],
    )
    return _try_refresh_config(client, bootstrap_config, "startup")


if __name__ == "__main__":
    main()
