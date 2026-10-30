import { intlLocale } from '@/shared/i18n';

/** Display a UTC ISO timestamp in the portal's language. Empty/invalid input renders as an em dash. */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? '—'
    : new Intl.DateTimeFormat(intlLocale(), { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

/** A date (or the date part of a timestamp) in the portal's language; empty renders as an em dash. */
export function formatDate(value: string | null | undefined): string {
  if (!value) return '—';
  // A bare `yyyy-mm-dd` is a calendar date, not a UTC midnight: don't let the zone shift it.
  const date = /^\d{4}-\d{2}-\d{2}$/.test(value) ? new Date(`${value}T00:00:00`) : new Date(value);
  return Number.isNaN(date.getTime())
    ? '—'
    : new Intl.DateTimeFormat(intlLocale(), { dateStyle: 'medium' }).format(date);
}
