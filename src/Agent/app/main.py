from __future__ import annotations

import logging
import platform
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

    results = scheduler.run_once(current_config.assignments)
    queue.extend(results)

    batch = queue.dequeue_batch(hello_response.max_result_batch_size)
    if batch:
        client.submit_results(
            ResultsRequest(
                sent_at_utc=_utc_now(),
                batch_id=f"bootstrap-{config.instance_id}",
                results=batch,
            )
        )

    client.send_heartbeat(
        HeartbeatRequest(
            agent_version="0.1.0",
            sent_at_utc=_utc_now(),
            config_version=current_config.config_version,
            active_assignments=len([assignment for assignment in current_config.assignments if assignment.enabled]),
            queued_result_count=len(queue),
            status="online",
        )
    )

    client.close()


def _utc_now() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


if __name__ == "__main__":
    main()
