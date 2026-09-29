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
