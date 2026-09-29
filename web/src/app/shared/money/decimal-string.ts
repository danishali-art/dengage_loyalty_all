/**
 * Money and points are `numeric(20,4)` server-side and travel as JSON strings.
 * Keep them as strings end-to-end — never parse into a JS `number` for storage or submit.
 */
declare const brand: unique symbol;
export type DecimalString = string & { readonly [brand]: 'DecimalString' };

/** Up to 16 integer digits + up to 4 fractional (matches numeric(20,4)). */
const DECIMAL_RE = /^-?\d{1,16}(\.\d{1,4})?$/;

export function isDecimalString(value: unknown): value is DecimalString {
  return typeof value === 'string' && DECIMAL_RE.test(value.trim());
}

/** Smart constructor. Throws on malformed input — call after form validation. */
export function decimalString(value: string | number): DecimalString {
  const s = typeof value === 'number' ? String(value) : value.trim();
  if (!DECIMAL_RE.test(s)) {
    throw new Error(`Not a valid decimal string: "${value}"`);
  }
  return s as DecimalString;
}

/** Nullable/blank-tolerant parse for optional form fields. */
export function toDecimalStringOrNull(value: string | number | null | undefined): DecimalString | null {
  if (value === null || value === undefined || value === '') return null;
  return isDecimalString(String(value).trim()) ? (String(value).trim() as DecimalString) : null;
}

export const ZERO = '0' as DecimalString;
