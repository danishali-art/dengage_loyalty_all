/** IANA timezone identifiers, for the streak-config timezone picker. No browser default. */
export function listIanaTimezones(): string[] {
  const withSupported = Intl as unknown as { supportedValuesOf?: (key: string) => string[] };
  if (typeof withSupported.supportedValuesOf === 'function') {
    return withSupported.supportedValuesOf('timeZone');
  }
  return FALLBACK_TIMEZONES;
}

export function isValidIanaTimezone(id: string): boolean {
  if (!id) return false;
  try {
    // Throws RangeError for an unknown zone.
    new Intl.DateTimeFormat('en-US', { timeZone: id });
    return true;
  } catch {
    return false;
  }
}

const FALLBACK_TIMEZONES = [
  'UTC',
  'Europe/Istanbul',
  'Europe/London',
  'Asia/Riyadh',
  'Asia/Dubai',
  'America/New_York',
  'America/Los_Angeles',
];
