from __future__ import annotations

import logging
import platform
import time
from datetime import UTC, datetime

from app.api_client import AgentApiClient
from app.checks import CheckRunner
from app.config import load_config
from app.models import HeartbeatRequest, HelloRequest, ResultsRequest
from app.result_queue import ResultQueue
from app.scheduler import AssignmentScheduler


def main() -> None:
    config = load_config()
    logging.basicConfig(level=config.log_level)

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

    current_config = client.fetch_config()
    logging.info("Agent %s connected with config version %s", hello_response.agent_id, current_config.config_version)

    config_refresh_seconds = max(1, hello_response.config_refresh_seconds)
    heartbeat_interval_seconds = max(1, hello_response.heartbeat_interval_seconds)
    result_batch_interval_seconds = max(1, hello_response.result_batch_interval_seconds)
    max_result_batch_size = max(1, hello_response.max_result_batch_size)

    last_config_refresh = 0.0
    last_heartbeat = 0.0
    last_result_batch = 0.0

    try:
        while True:
            now = time.monotonic()

            if now - last_config_refresh >= config_refresh_seconds:
                current_config = client.fetch_config()
                logging.info("Fetched config version %s with %d assignments", current_config.config_version, len(current_config.assignments))
                last_config_refresh = now

            results = scheduler.run_once(current_config.assignments)
            queue.extend(results)

            if now - last_result_batch >= result_batch_interval_seconds:
                batch = queue.dequeue_batch(max_result_batch_size)
                if batch:
                    client.submit_results(
                        ResultsRequest(
                            sent_at_utc=_utc_now(),
                            batch_id=f"results-{config.instance_id}-{int(now)}",
                            results=batch,
                        )
                    )
                last_result_batch = now

            if now - last_heartbeat >= heartbeat_interval_seconds:
                heartbeat_response = client.send_heartbeat(
                    HeartbeatRequest(
                        agent_version="0.1.0",
                        sent_at_utc=_utc_now(),
                        config_version=current_config.config_version,
                        active_assignments=len([assignment for assignment in current_config.assignments if assignment.enabled]),
                        queued_result_count=len(queue),
                        status="online",
                    )
                )
                last_heartbeat = now
                if heartbeat_response.config_changed:
                    current_config = client.fetch_config()
                    logging.info("Server reported config change. Updated to version %s", current_config.config_version)
                    last_config_refresh = now

            time.sleep(1)
    except KeyboardInterrupt:
        logging.info("Agent shutdown requested. Exiting.")
    finally:
        client.close()


def _utc_now() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


if __name__ == "__main__":
    main()
