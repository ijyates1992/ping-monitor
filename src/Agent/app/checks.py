from __future__ import annotations

from datetime import UTC, datetime

from app.models import AssignmentModel, ResultItem


class CheckRunner:
    def run_icmp_check(self, assignment: AssignmentModel) -> ResultItem:
        # TODO: Implement ICMP execution and return raw facts only.
        return ResultItem(
            assignment_id=assignment.assignment_id,
            endpoint_id=assignment.endpoint_id,
            check_type=assignment.check_type,
            checked_at_utc=datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
            success=False,
            round_trip_ms=None,
            error_code="NOT_IMPLEMENTED",
            error_message="ICMP check execution is not implemented in the phase 1 skeleton.",
        )
