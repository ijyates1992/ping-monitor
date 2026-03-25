from __future__ import annotations

import logging
import platform
import re
import subprocess
import time
from datetime import UTC, datetime

from app.models import AssignmentModel, ResultItem


class CheckRunner:
    _WINDOWS_TIME_RE = re.compile(r"time[=<]\s*(\d+)\s*ms", re.IGNORECASE)
    _UNIX_TIME_RE = re.compile(r"time[=<]?\s*(\d+(?:\.\d+)?)\s*ms", re.IGNORECASE)

    def run_icmp_check(self, assignment: AssignmentModel) -> ResultItem:
        checked_at_utc = _utc_now()
        timeout_ms = max(1, assignment.timeout_ms)
        ping_command = _build_ping_command(assignment.target, timeout_ms)
        timeout_seconds = max(1.0, timeout_ms / 1000.0 + 1.0)

        logging.info(
            "Running ICMP check assignment=%s target=%s timeoutMs=%d",
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
                "ICMP timeout assignment=%s target=%s elapsedMs=%d",
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
                "ICMP execution error assignment=%s target=%s error=%s",
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
                "ICMP success assignment=%s target=%s elapsedMs=%d roundTripMs=%s",
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
            "ICMP failure assignment=%s target=%s returnCode=%d errorCode=%s message=%s",
            assignment.assignment_id,
            assignment.target,
            completed.returncode,
            error_code,
            error_message,
        )
        return _failure_result(assignment, checked_at_utc, error_code, error_message)

    def _parse_round_trip_ms(self, output: str) -> int | None:
        windows_match = self._WINDOWS_TIME_RE.search(output)
        if windows_match:
            return int(windows_match.group(1))

        unix_match = self._UNIX_TIME_RE.search(output)
        if unix_match:
            return int(round(float(unix_match.group(1))))

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
