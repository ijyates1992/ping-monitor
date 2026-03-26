import unittest

from app.api_client import _assignment_from_dict


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


if __name__ == "__main__":
    unittest.main()
