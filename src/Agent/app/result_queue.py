from __future__ import annotations

from collections import deque
from typing import Iterable

from app.models import ResultItem


class ResultQueue:
    def __init__(self) -> None:
        self._items: deque[ResultItem] = deque()

    def enqueue(self, result: ResultItem) -> None:
        self._items.append(result)

    def dequeue_batch(self, max_items: int) -> list[ResultItem]:
        batch: list[ResultItem] = []
        while self._items and len(batch) < max_items:
            batch.append(self._items.popleft())
        return batch

    def extend(self, items: Iterable[ResultItem]) -> None:
        for item in items:
            self.enqueue(item)

    def requeue_front(self, items: Iterable[ResultItem]) -> None:
        buffered = list(items)
        for item in reversed(buffered):
            self._items.appendleft(item)

    def __len__(self) -> int:
        return len(self._items)
