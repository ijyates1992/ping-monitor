from __future__ import annotations

import time

from app.checks import CheckRunner
from app.models import AssignmentModel, ResultItem


class AssignmentScheduler:
    def __init__(self, check_runner: CheckRunner) -> None:
        self._check_runner = check_runner
        self._next_run_at_by_assignment_id: dict[str, float] = {}

    def run_once(self, assignments: list[AssignmentModel]) -> list[ResultItem]:
        now = time.monotonic()
        results: list[ResultItem] = []
        for assignment in assignments:
            if not assignment.enabled:
                continue
            if assignment.check_type != "icmp":
                continue
            next_run_at = self._next_run_at_by_assignment_id.get(assignment.assignment_id, 0.0)
            if now < next_run_at:
                continue
            results.append(self._check_runner.run_icmp_check(assignment))
            self._next_run_at_by_assignment_id[assignment.assignment_id] = now + max(1, assignment.ping_interval_seconds)
        return results
