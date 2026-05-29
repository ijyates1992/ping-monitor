from __future__ import annotations

import logging
import platform
import time
from datetime import UTC, datetime
from json import JSONDecodeError

import httpx

from app.api_client import AgentApiClient, AgentApiError
from app.checks import CheckRunner
from app.config import load_config
from app.models import ConfigResponse, HeartbeatRequest, HelloRequest, HelloResponse, ResultsRequest
from app.result_queue import ResultQueue
from app.scheduler import AssignmentScheduler
from app.version import AGENT_VERSION

BOOTSTRAP_CONFIG_VERSION = "bootstrap-pending"
INITIAL_HELLO_RETRY_SECONDS = 2
MAX_HELLO_RETRY_SECONDS = 30


def main() -> None:
    config = load_config()
    logging.basicConfig(level=config.log_level)
    logging.info("Starting agent instance %s", config.instance_id)

    client = AgentApiClient(config)
    queue = ResultQueue()
    scheduler = AssignmentScheduler(CheckRunner(getattr(config, "icmp_backend", "auto")))

    started_at = _utc_now()
    hello_response = _send_hello_with_retry(client, config.instance_id, started_at)

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
                _submit_result_batch(client, queue, config.instance_id, now, max_result_batch_size)
                last_result_batch = now

            if now - last_heartbeat >= heartbeat_interval_seconds:
                try:
                    heartbeat_response = client.send_heartbeat(
                        HeartbeatRequest(
                            agent_version=AGENT_VERSION,
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
                except _retryable_api_exceptions() as ex:
                    logging.error("Heartbeat failed: %s", _format_api_error(ex))
                last_heartbeat = now

            time.sleep(1)
    except KeyboardInterrupt:
        logging.info("Agent shutdown requested. Exiting.")
    finally:
        client.close()


def _utc_now() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")



def _submit_result_batch(
    client: AgentApiClient,
    queue: ResultQueue,
    instance_id: str,
    now: float,
    max_result_batch_size: int,
) -> None:
    batch = queue.dequeue_batch(max_result_batch_size)
    if not batch:
        logging.debug("No queued results to submit in this cycle.")
        return

    try:
        client.submit_results(
            ResultsRequest(
                sent_at_utc=_utc_now(),
                batch_id=f"results-{instance_id}-{int(now)}",
                results=batch,
            )
        )
    except _retryable_api_exceptions() as ex:
        if _is_hydration_ingestion_unavailable(ex):
            logging.info("Result submission deferred while server hydrates rolling metrics. Batch will be retried: %s", _format_api_error(ex))
        else:
            logging.error("Result submission failed and will be retried: %s", _format_api_error(ex))
        queue.requeue_front(batch)


def _retryable_api_exceptions() -> tuple[type[BaseException], ...]:
    return httpx.HTTPError, AgentApiError, ValueError, JSONDecodeError


def _is_hydration_ingestion_unavailable(error: BaseException) -> bool:
    if not isinstance(error, httpx.HTTPStatusError):
        return False

    if error.response.status_code != 503:
        return False

    response_body = error.response.text.lower()
    return "hydration" in response_body and "ingestion" in response_body


def _format_api_error(error: BaseException) -> str:
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
    except _retryable_api_exceptions() as ex:
        logging.error(
            "Config refresh failed (%s). Continuing with previous config version %s: %s",
            reason,
            current_config.config_version,
            _format_api_error(ex),
        )
        return current_config


def _initialize_config(client: AgentApiClient, hello_config_version: str) -> ConfigResponse:
    bootstrap_config = ConfigResponse(
        config_version=hello_config_version or BOOTSTRAP_CONFIG_VERSION,
        generated_at_utc=_utc_now(),
        assignments=[],
    )
    return _try_refresh_config(client, bootstrap_config, "startup")


def _send_hello_with_retry(client: AgentApiClient, instance_id: str, started_at: str) -> HelloResponse:
    retry_delay_seconds = INITIAL_HELLO_RETRY_SECONDS
    while True:
        try:
            return client.send_hello(
                HelloRequest(
                    agent_version=AGENT_VERSION,
                    machine_name=platform.node() or instance_id,
                    platform=platform.system().lower() or "unknown",
                    capabilities=["icmp"],
                    started_at_utc=started_at,
                )
            )
        except _retryable_api_exceptions() as ex:
            logging.warning(
                "Initial hello failed. Server may be unavailable or restarting. Retrying in %ss: %s",
                retry_delay_seconds,
                _format_api_error(ex),
            )
            time.sleep(retry_delay_seconds)
            retry_delay_seconds = min(MAX_HELLO_RETRY_SECONDS, retry_delay_seconds * 2)


if __name__ == "__main__":
    main()
