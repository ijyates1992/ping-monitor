from __future__ import annotations

from app.checks import CheckRunner
from app.models import AssignmentModel, ResultItem


class AssignmentScheduler:
    def __init__(self, check_runner: CheckRunner) -> None:
        self._check_runner = check_runner

    def run_once(self, assignments: list[AssignmentModel]) -> list[ResultItem]:
        results: list[ResultItem] = []
        for assignment in assignments:
            if not assignment.enabled:
                continue
            if assignment.check_type != "icmp":
                continue
            results.append(self._check_runner.run_icmp_check(assignment))
        return results
