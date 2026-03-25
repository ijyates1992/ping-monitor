import unittest

from app.models import ResultItem
from app.result_queue import ResultQueue


def _result(assignment_id: str) -> ResultItem:
    return ResultItem(
        assignment_id=assignment_id,
        endpoint_id=f"endpoint-{assignment_id}",
        check_type="icmp",
        checked_at_utc="2026-03-25T00:00:00Z",
        success=False,
        round_trip_ms=None,
        error_code="ERR",
        error_message="error",
    )


class ResultQueueTests(unittest.TestCase):
    def test_requeue_front_preserves_original_order(self) -> None:
        queue = ResultQueue()
        queue.extend([_result("a"), _result("b"), _result("c")])

        batch = queue.dequeue_batch(2)
        queue.requeue_front(batch)

        result_ids = [item.assignment_id for item in queue.dequeue_batch(3)]
        self.assertEqual(["a", "b", "c"], result_ids)


if __name__ == "__main__":
    unittest.main()
