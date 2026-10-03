import { signal } from '@angular/core';

/** Every customer grid starts at the same page size, and offers the same "page view" choices. */
export const CUSTOMER_PAGE_SIZE = 10;
export const CUSTOMER_PAGE_SIZES: readonly number[] = [10, 20, 30];

/**
 * Drives the standard `app-paginator` (Prev / Page n of m / Next) over a cursor-paged endpoint.
 * The paginator only moves one page at a time, so the cursor for any page it asks for is always
 * known: page n is fetched with the `nextCursor` that page n−1 returned.
 */
export class CursorPager {
  readonly page = signal(1);
  readonly total = signal(0);
  readonly pageSize = signal(CUSTOMER_PAGE_SIZE);
  private cursors: (string | null)[] = [null];

  /** Back to page 1, e.g. after the filters change. */
  reset(): void {
    this.page.set(1);
    this.total.set(0);
    this.cursors = [null];
  }

  /** A new page size: back to page 1 (cursors depend on the size). */
  setPageSize(size: number): void {
    this.pageSize.set(size);
    this.reset();
  }

  /** The cursor that fetches `page`; undefined if that page was never reached. */
  cursorFor(page: number): string | null | undefined {
    return this.cursors[page - 1];
  }

  /** Record a fetched page: where it is, how many rows match, and how to reach the next one. */
  record(page: number, result: { nextCursor?: string | null; total?: number }): void {
    this.page.set(page);
    this.total.set(result.total ?? 0);
    this.cursors[page] = result.nextCursor ?? null;
  }
}

/** One page of a list the API returns in full (rewards, tier history, streak history). */
export function pageSlice<T>(list: readonly T[], page: number, size = CUSTOMER_PAGE_SIZE): T[] {
  const start = (Math.max(1, page) - 1) * size;
  return list.slice(start, start + size);
}
