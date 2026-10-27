/**
 * All API timestamps are UTC ISO 8601. `datetime-local` inputs are treated as UTC
 * wall-clock (matching the engine, which checks `active_from/to` against `UtcNow`).
 */

/** `2026-09-02T14:30` (local-input format) -> `2026-09-02T14:30:00Z`. */
export function localInputToUtcIso(value: string | null | undefined): string | null {
  if (!value) return null;
  // The value has no zone; append seconds + Z so it is read as UTC.
  const withSeconds = value.length === 16 ? `${value}:00` : value;
  return withSeconds.endsWith('Z') ? withSeconds : `${withSeconds}Z`;
}

/** `2026-09-02T14:30:00Z` -> `2026-09-02T14:30` for a `datetime-local` input. */
export function utcIsoToLocalInput(iso: string | null | undefined): string {
  if (!iso) return '';
  const match = /^(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2})/.exec(iso);
  return match ? `${match[1]}T${match[2]}` : '';
}

export function nowUtcIso(): string {
  return new Date().toISOString();
}

/**
 * For filters an admin fills in their own local time (CR 2026-10-02, Customer 360): a
 * `datetime-local` value is read as the browser's local wall-clock time and sent as UTC.
 * (Unlike `localInputToUtcIso`, which treats the input itself as UTC for rule windows.)
 */
export function localDateTimeToUtcIso(value: string | null | undefined): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** The `datetime-local` value for the start of the local day `days` days before `now`. */
export function localDaysAgoInput(days: number, now: Date = new Date()): string {
  const d = new Date(now.getFullYear(), now.getMonth(), now.getDate() - days);
  const pad = (n: number): string => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T00:00`;
}
