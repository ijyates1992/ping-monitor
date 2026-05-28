from __future__ import annotations

import importlib
import logging
import platform
import re
import subprocess
import time
from datetime import UTC, datetime
from types import ModuleType
from typing import Any

from app.models import AssignmentModel, ResultItem

PYTHONPING_FALLBACK_WARNING = "pythonping ICMP backend unavailable or not permitted; falling back to subprocess ping backend."


class CheckRunner:
    _WINDOWS_TIME_RE = re.compile(r"time[=<]\s*(\d+)\s*ms", re.IGNORECASE)
    _UNIX_TIME_RE = re.compile(r"time[=<]?\s*(\d+(?:\.\d+)?)\s*ms", re.IGNORECASE)

    def __init__(self, icmp_backend: str = "auto") -> None:
        normalized_backend = (icmp_backend or "auto").strip().lower()
        if normalized_backend not in {"auto", "pythonping", "subprocess"}:
            raise ValueError("ICMP backend must be one of: auto, pythonping, subprocess.")

        self._configured_backend = normalized_backend
        self._active_backend: str | None = "subprocess" if normalized_backend == "subprocess" else None
        self._pythonping_module: ModuleType | None = None
        self._backend_logged = False
        self._fallback_warning_logged = False

    def run_icmp_check(self, assignment: AssignmentModel) -> ResultItem:
        if self._configured_backend == "subprocess":
            self._log_active_backend_once("subprocess")
            return self._run_subprocess_ping_check(assignment)

        pythonping_module = self._get_pythonping_module()
        if pythonping_module is not None:
            result = self._try_run_pythonping_check(assignment, pythonping_module)
            if result is not None:
                self._active_backend = "pythonping"
                self._log_active_backend_once("pythonping")
                return result

        self._active_backend = "subprocess"
        self._log_pythonping_fallback_once()
        self._log_active_backend_once("subprocess")
        return self._run_subprocess_ping_check(assignment)

    def _get_pythonping_module(self) -> ModuleType | None:
        if self._active_backend == "subprocess":
            return None
        if self._pythonping_module is not None:
            return self._pythonping_module

        try:
            self._pythonping_module = importlib.import_module("pythonping")
            return self._pythonping_module
        except (ImportError, ModuleNotFoundError, OSError) as ex:
            logging.debug("pythonping ICMP backend import failed: %s", ex)
            return None

    def _try_run_pythonping_check(self, assignment: AssignmentModel, pythonping_module: ModuleType) -> ResultItem | None:
        checked_at_utc = _utc_now()
        timeout_ms = max(1, assignment.timeout_ms)
        timeout_seconds = max(0.001, timeout_ms / 1000.0)

        logging.info(
            "Running ICMP check assignment=%s target=%s timeoutMs=%d backend=pythonping",
            assignment.assignment_id,
            assignment.target,
            timeout_ms,
        )

        try:
            response_list = pythonping_module.ping(assignment.target, count=1, timeout=timeout_seconds, verbose=False)
        except Exception as ex:  # pythonping can surface platform/socket errors from several exception types.
            logging.debug("pythonping ICMP backend check failed: %s", ex)
            return None

        try:
            successful_responses = [response for response in response_list if getattr(response, "success", False)]
        except TypeError:
            successful_responses = []

        if successful_responses:
            round_trip_ms = _pythonping_response_time_ms(successful_responses[0])
            logging.info(
                "ICMP success assignment=%s target=%s roundTripMs=%s backend=pythonping",
                assignment.assignment_id,
                assignment.target,
                round_trip_ms,
            )
            return ResultItem(
                assignment_id=assignment.assignment_id,
                endpoint_id=assignment.endpoint_id,
                check_type=assignment.check_type,
                checked_at_utc=checked_at_utc,
                success=True,
                round_trip_ms=round_trip_ms,
                error_code=None,
                error_message=None,
            )

        logging.warning(
            "ICMP failure assignment=%s target=%s errorCode=PING_TIMEOUT message=Ping request timed out. backend=pythonping",
            assignment.assignment_id,
            assignment.target,
        )
        return _failure_result(assignment, checked_at_utc, "PING_TIMEOUT", "Ping request timed out.")

    def _run_subprocess_ping_check(self, assignment: AssignmentModel) -> ResultItem:
        checked_at_utc = _utc_now()
        timeout_ms = max(1, assignment.timeout_ms)
        ping_command = _build_ping_command(assignment.target, timeout_ms)
        timeout_seconds = max(1.0, timeout_ms / 1000.0 + 1.0)

        logging.info(
            "Running ICMP check assignment=%s target=%s timeoutMs=%d backend=subprocess",
            assignment.assignment_id,
            assignment.target,
            timeout_ms,
        )

        started = time.monotonic()
        try:
            completed = subprocess.run(  # noqa: S603
                ping_command,
                capture_output=True,
                text=True,
                timeout=timeout_seconds,
                check=False,
            )
        except subprocess.TimeoutExpired:
            elapsed_ms = int((time.monotonic() - started) * 1000)
            logging.warning(
                "ICMP timeout assignment=%s target=%s elapsedMs=%d backend=subprocess",
                assignment.assignment_id,
                assignment.target,
                elapsed_ms,
            )
            return _failure_result(
                assignment,
                checked_at_utc,
                "PING_TIMEOUT",
                f"Ping command timed out after approximately {timeout_ms} ms.",
            )
        except OSError as ex:
            logging.error(
                "ICMP execution error assignment=%s target=%s error=%s backend=subprocess",
                assignment.assignment_id,
                assignment.target,
                ex,
            )
            return _failure_result(
                assignment,
                checked_at_utc,
                "PING_EXECUTION_ERROR",
                f"Failed to execute ping command: {ex}",
            )

        output = "\n".join(part for part in [completed.stdout.strip(), completed.stderr.strip()] if part).strip()
        elapsed_ms = int((time.monotonic() - started) * 1000)

        if completed.returncode == 0:
            round_trip_ms = self._parse_round_trip_ms(output)
            logging.info(
                "ICMP success assignment=%s target=%s elapsedMs=%d roundTripMs=%s backend=subprocess",
                assignment.assignment_id,
                assignment.target,
                elapsed_ms,
                round_trip_ms,
            )
            return ResultItem(
                assignment_id=assignment.assignment_id,
                endpoint_id=assignment.endpoint_id,
                check_type=assignment.check_type,
                checked_at_utc=checked_at_utc,
                success=True,
                round_trip_ms=round_trip_ms,
                error_code=None,
                error_message=None,
            )

        error_code, error_message = _classify_ping_failure(output)
        logging.warning(
            "ICMP failure assignment=%s target=%s returnCode=%d errorCode=%s message=%s backend=subprocess",
            assignment.assignment_id,
            assignment.target,
            completed.returncode,
            error_code,
            error_message,
        )
        return _failure_result(assignment, checked_at_utc, error_code, error_message)

    def _parse_round_trip_ms(self, output: str) -> float | None:
        windows_match = self._WINDOWS_TIME_RE.search(output)
        if windows_match:
            return float(windows_match.group(1))

        unix_match = self._UNIX_TIME_RE.search(output)
        if unix_match:
            return float(unix_match.group(1))

        return None

    def _log_active_backend_once(self, backend: str) -> None:
        if self._backend_logged:
            return
        logging.info("Active ICMP backend: %s", backend)
        self._backend_logged = True

    def _log_pythonping_fallback_once(self) -> None:
        if self._fallback_warning_logged:
            return
        logging.warning(PYTHONPING_FALLBACK_WARNING)
        self._fallback_warning_logged = True


def _pythonping_response_time_ms(response: Any) -> float | None:
    value = getattr(response, "time_elapsed_ms", None)
    if value is not None:
        return float(value)

    value = getattr(response, "time_elapsed", None)
    if value is not None:
        return float(value) * 1000.0

    return None


def _build_ping_command(target: str, timeout_ms: int) -> list[str]:
    if platform.system().lower() == "windows":
        return ["ping", "-n", "1", "-w", str(timeout_ms), target]

    timeout_seconds = max(1, int(round(timeout_ms / 1000.0)))
    return ["ping", "-n", "-c", "1", "-W", str(timeout_seconds), target]


def _classify_ping_failure(output: str) -> tuple[str, str]:
    lowered = output.lower()

    if "request timed out" in lowered or "100% packet loss" in lowered or "100.0% packet loss" in lowered:
        return "PING_TIMEOUT", "Ping request timed out."

    if "destination host unreachable" in lowered or "general failure" in lowered:
        return "HOST_UNREACHABLE", "Host is unreachable."

    if (
        "could not find host" in lowered
        or "name or service not known" in lowered
        or "temporary failure in name resolution" in lowered
        or "unknown host" in lowered
    ):
        return "HOST_UNREACHABLE", "Host could not be resolved."

    if "unreachable" in lowered:
        return "HOST_UNREACHABLE", "Host is unreachable."

    trimmed = output.strip()
    if trimmed:
        return "PING_FAILED", trimmed.splitlines()[0]

    return "PING_FAILED", "Ping command failed."


def _failure_result(assignment: AssignmentModel, checked_at_utc: str, error_code: str, error_message: str) -> ResultItem:
    return ResultItem(
        assignment_id=assignment.assignment_id,
        endpoint_id=assignment.endpoint_id,
        check_type=assignment.check_type,
        checked_at_utc=checked_at_utc,
        success=False,
        round_trip_ms=None,
        error_code=error_code,
        error_message=error_message,
    )


def _utc_now() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")
