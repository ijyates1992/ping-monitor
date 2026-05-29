import unittest

import httpx

from app.api_client import AgentApiClient, AgentApiError, _assignment_from_dict
from app.config import AgentConfig
from app.models import HeartbeatRequest, ResultsRequest
from app.version import AGENT_VERSION

class AgentApiClientTests(unittest.TestCase):
    def test_assignment_parsing_reads_dependency_id_list(self) -> None:
        payload = {
            "assignmentId": "a1",
            "endpointId": "e1",
            "name": "endpoint-1",
            "target": "8.8.8.8",
            "checkType": "icmp",
            "enabled": True,
            "pingIntervalSeconds": 30,
            "retryIntervalSeconds": 5,
            "timeoutMs": 1000,
            "failureThreshold": 3,
            "recoveryThreshold": 2,
            "dependsOnEndpointIds": ["e-parent-1", "e-parent-2"],
            "tags": ["prod"],
        }

        assignment = _assignment_from_dict(payload)

        self.assertEqual(["e-parent-1", "e-parent-2"], assignment.depends_on_endpoint_ids)

    def test_assignment_parsing_keeps_legacy_single_dependency_compatibility(self) -> None:
        payload = {
            "assignmentId": "a1",
            "endpointId": "e1",
            "name": "endpoint-1",
            "target": "8.8.8.8",
            "checkType": "icmp",
            "enabled": True,
            "pingIntervalSeconds": 30,
            "retryIntervalSeconds": 5,
            "timeoutMs": 1000,
            "failureThreshold": 3,
            "recoveryThreshold": 2,
            "dependsOnEndpointId": "e-parent-1",
            "tags": [],
        }

        assignment = _assignment_from_dict(payload)

        self.assertEqual(["e-parent-1"], assignment.depends_on_endpoint_ids)

    def test_submit_results_accepts_200_empty_body(self) -> None:
        client = self._build_client(lambda request: httpx.Response(200, text="", request=request))

        response = client.submit_results(self._results_request())

        self.assertEqual({}, response)
        client.close()

    def test_submit_results_accepts_204_no_content(self) -> None:
        client = self._build_client(lambda request: httpx.Response(204, request=request))

        response = client.submit_results(self._results_request())

        self.assertEqual({}, response)
        client.close()

    def test_send_heartbeat_raises_agent_api_error_for_malformed_json(self) -> None:
        client = self._build_client(
            lambda request: httpx.Response(
                200,
                headers={"content-type": "text/html"},
                text="<html>not-json</html>",
                request=request,
            )
        )

        with self.assertRaises(AgentApiError):
            client.send_heartbeat(
                HeartbeatRequest(
                    agent_version="1.0.0",
                    sent_at_utc="2026-01-01T00:00:00Z",
                    config_version="v1",
                    active_assignments=0,
                    queued_result_count=0,
                    status="online",
                )
            )

        client.close()

    def test_user_agent_uses_shared_agent_version(self) -> None:
        client = self._build_client(lambda request: httpx.Response(204, request=request))

        user_agent = client._client.headers["User-Agent"]

        self.assertEqual(f"ping-agent/{AGENT_VERSION}", user_agent)
        self.assertEqual("ping-agent/0.1.4", user_agent)
        client.close()

    def _build_client(self, handler) -> AgentApiClient:
        config = AgentConfig(server_url="https://example.test", instance_id="agent-1", api_key="secret")
        client = AgentApiClient(config)
        client._client.close()
        client._client = httpx.Client(
            transport=httpx.MockTransport(handler),
            base_url=config.server_url,
            headers={
                "X-Instance-Id": config.instance_id,
                "Authorization": f"Bearer {config.api_key}",
                "Accept": "application/json",
                "Content-Type": "application/json",
                "User-Agent": f"ping-agent/{AGENT_VERSION}",
            },
        )
        return client

    def _results_request(self) -> ResultsRequest:
        return ResultsRequest(sent_at_utc="2026-01-01T00:00:00Z", batch_id="batch-1", results=[])

if __name__ == "__main__":
    unittest.main()
