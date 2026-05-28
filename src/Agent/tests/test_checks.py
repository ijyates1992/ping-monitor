import types
import unittest
from unittest.mock import MagicMock, patch

from app.checks import CheckRunner
from app.models import AssignmentModel


def _assignment() -> AssignmentModel:
    return AssignmentModel(
        assignment_id="a1",
        endpoint_id="e1",
        name="endpoint",
        target="192.0.2.1",
        check_type="icmp",
        enabled=True,
        ping_interval_seconds=30,
        retry_interval_seconds=5,
        timeout_ms=1000,
        failure_threshold=3,
        recovery_threshold=2,
    )


class _PythonPingResponse:
    def __init__(self, success: bool, elapsed_ms: float = 0.0) -> None:
        self.success = success
        self.time_elapsed_ms = elapsed_ms


class CheckRunnerTests(unittest.TestCase):
    def test_auto_mode_uses_pythonping_when_available(self) -> None:
        module = types.SimpleNamespace(ping=MagicMock(return_value=[_PythonPingResponse(True, 1.234)]))
        runner = CheckRunner("auto")

        runner._pythonping_module = module

        with patch("app.checks.subprocess.run") as subprocess_run:
            result = runner.run_icmp_check(_assignment())

        self.assertTrue(result.success)
        self.assertEqual(1.234, result.round_trip_ms)
        module.ping.assert_called_once()
        subprocess_run.assert_not_called()

    def test_auto_mode_falls_back_when_pythonping_raises_permission_error(self) -> None:
        module = types.SimpleNamespace(ping=MagicMock(side_effect=PermissionError("raw socket denied")))
        completed = types.SimpleNamespace(returncode=0, stdout="64 bytes time=2.345 ms", stderr="")
        runner = CheckRunner("auto")

        runner._pythonping_module = module

        with patch("app.checks.subprocess.run", return_value=completed) as subprocess_run:
            result = runner.run_icmp_check(_assignment())

        self.assertTrue(result.success)
        self.assertEqual(2.345, result.round_trip_ms)
        subprocess_run.assert_called_once()

    def test_subprocess_mode_uses_existing_backend(self) -> None:
        completed = types.SimpleNamespace(returncode=0, stdout="64 bytes time=3.456 ms", stderr="")
        runner = CheckRunner("subprocess")

        with patch("app.checks.subprocess.run", return_value=completed) as subprocess_run:
            result = runner.run_icmp_check(_assignment())

        self.assertTrue(result.success)
        self.assertEqual(3.456, result.round_trip_ms)
        subprocess_run.assert_called_once()

    def test_failed_pythonping_ping_produces_failure_result(self) -> None:
        module = types.SimpleNamespace(ping=MagicMock(return_value=[_PythonPingResponse(False)]))
        runner = CheckRunner("auto")

        runner._pythonping_module = module

        result = runner.run_icmp_check(_assignment())

        self.assertFalse(result.success)
        self.assertIsNone(result.round_trip_ms)
        self.assertEqual("PING_TIMEOUT", result.error_code)

    def test_agent_does_not_crash_when_pythonping_import_fails(self) -> None:
        completed = types.SimpleNamespace(returncode=1, stdout="", stderr="100% packet loss")
        runner = CheckRunner("auto")

        with patch.object(runner, "_get_pythonping_module", return_value=None), patch(
            "app.checks.subprocess.run", return_value=completed
        ):
            result = runner.run_icmp_check(_assignment())

        self.assertFalse(result.success)
        self.assertEqual("PING_TIMEOUT", result.error_code)


if __name__ == "__main__":
    unittest.main()
