import unittest
from unittest.mock import patch

import httpx

from app.api_client import AgentApiError
from app.models import AssignmentModel, ConfigResponse, HelloResponse, ResultItem
import app.main as main_module


class _FakeClient:
    def __init__(self, fail_first_hello: bool = True) -> None:
        self._fail_first_hello = fail_first_hello
        self.hello_calls = 0
        self.fetch_calls = 0
        self.submit_calls = 0
        self.heartbeat_calls = 0

    def send_hello(self, request):
        self.hello_calls += 1
        if self._fail_first_hello and self.hello_calls == 1:
            raise httpx.ConnectError("server down")
        return HelloResponse(
            agent_id="agent-1",
            server_time_utc="2026-01-01T00:00:00Z",
            config_refresh_seconds=1,
            heartbeat_interval_seconds=1,
            result_batch_interval_seconds=1,
            max_result_batch_size=10,
            config_version="cfg-1",
        )

    def fetch_config(self):
        self.fetch_calls += 1
        return ConfigResponse(
            config_version="cfg-1",
            generated_at_utc="2026-01-01T00:00:00Z",
            assignments=[
                AssignmentModel(
                    assignment_id="a1",
                    endpoint_id="e1",
                    name="endpoint",
                    target="8.8.8.8",
                    check_type="icmp",
                    enabled=True,
                    ping_interval_seconds=30,
                    retry_interval_seconds=5,
                    timeout_ms=1000,
                    failure_threshold=3,
                    recovery_threshold=2,
                )
            ],
        )

    def send_heartbeat(self, request):
        self.heartbeat_calls += 1
        raise AgentApiError("malformed json")

    def submit_results(self, request):
        self.submit_calls += 1
        raise AgentApiError("submit parse failure")

    def close(self):
        return None


class _FakeScheduler:
    def __init__(self):
        self.calls = 0

    def run_once(self, assignments):
        self.calls += 1
        if self.calls == 1:
            return [
                ResultItem(
                    assignment_id="a1",
                    endpoint_id="e1",
                    check_type="icmp",
                    checked_at_utc="2026-01-01T00:00:00Z",
                    success=False,
                    round_trip_ms=None,
                    error_code="ERR",
                    error_message="failed",
                )
            ]
        return []


class MainResilienceTests(unittest.TestCase):
    def test_send_hello_retries_until_success(self) -> None:
        client = _FakeClient()
        sleep_values: list[int] = []

        with patch("app.main.time.sleep", side_effect=lambda delay: sleep_values.append(delay)):
            response = main_module._send_hello_with_retry(client, "agent-1", "2026-01-01T00:00:00Z")

        self.assertEqual("agent-1", response.agent_id)
        self.assertEqual(2, client.hello_calls)
        self.assertEqual([main_module.INITIAL_HELLO_RETRY_SECONDS], sleep_values)

    def test_config_refresh_parse_failure_keeps_previous_config(self) -> None:
        class _BrokenClient:
            def fetch_config(self):
                raise AgentApiError("bad config json")

        current = ConfigResponse(config_version="cfg-old", generated_at_utc="2026-01-01T00:00:00Z", assignments=[])
        refreshed = main_module._try_refresh_config(_BrokenClient(), current, "scheduled")

        self.assertIs(current, refreshed)

    def test_main_loop_handles_heartbeat_parse_error_and_requeues_failed_results(self) -> None:
        fake_client = _FakeClient(fail_first_hello=False)
        fake_scheduler = _FakeScheduler()
        monotonic_values = iter([0.0, 0.0, 1.0, 1.0])
        sleep_calls = {"count": 0}

        def _sleep(_seconds: float) -> None:
            sleep_calls["count"] += 1
            if sleep_calls["count"] >= 3:
                raise KeyboardInterrupt()

        class _LoadedConfig:
            instance_id = "agent-1"
            log_level = "INFO"

        with patch("app.main.load_config", return_value=_LoadedConfig()), patch(
            "app.main.AgentApiClient", return_value=fake_client
        ), patch("app.main.AssignmentScheduler", return_value=fake_scheduler), patch(
            "app.main.time.monotonic", side_effect=lambda: next(monotonic_values)
        ), patch("app.main.time.sleep", side_effect=_sleep):
            main_module.main()

        self.assertGreaterEqual(fake_client.heartbeat_calls, 1)
        self.assertGreaterEqual(fake_client.submit_calls, 1)

    def test_failed_result_submission_requeues_batch(self) -> None:
        from app.result_queue import ResultQueue

        client = _FakeClient(fail_first_hello=False)
        queue = ResultQueue()
        queue.extend(
            [
                ResultItem(
                    assignment_id="a1",
                    endpoint_id="e1",
                    check_type="icmp",
                    checked_at_utc="2026-01-01T00:00:00Z",
                    success=False,
                    round_trip_ms=None,
                    error_code="ERR",
                    error_message="failed",
                )
            ]
        )

        main_module._submit_result_batch(client, queue, "agent-1", 1.0, 10)

        self.assertEqual(1, len(queue))


if __name__ == "__main__":
    unittest.main()
