import { HttpParams } from '@angular/common/http';

/** Offset-paginated list envelope (admin CRUD lists). */
export interface Page<T> {
  readonly data: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

/** Keyset/cursor-paginated list envelope (the customer ledger). */
export interface CursorPage<T> {
  readonly data: readonly T[];
  readonly nextCursor: string | null;
  /** Rows matching the filters across all pages, where the endpoint counts them. */
  readonly total?: number;
}

export interface PageQuery {
  page: number;
  pageSize: number;
}

export interface CursorQuery {
  cursor?: string | null;
  pageSize: number;
}

export function emptyPage<T>(pageSize: number): Page<T> {
  return { data: [], page: 1, pageSize, total: 0 };
}

export function totalPages(page: Page<unknown>): number {
  return Math.max(1, Math.ceil(page.total / Math.max(1, page.pageSize)));
}

/**
 * Builds `HttpParams`, dropping `null` / `undefined` / `''` and expanding arrays into
 * repeated params (`?tag=a&tag=b`).
 */
function toParam(value: unknown): string | null {
  if (value === null || value === undefined || value === '') return null;
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean' || typeof value === 'bigint') {
    return String(value);
  }
  return null; // objects are not sensible query params
}

export function buildHttpParams(source: Record<string, unknown>): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(source)) {
    if (Array.isArray(value)) {
      for (const item of value as unknown[]) {
        const s = toParam(item);
        if (s !== null) params = params.append(key, s);
      }
    } else {
      const s = toParam(value);
      if (s !== null) params = params.set(key, s);
    }
  }
  return params;
}
