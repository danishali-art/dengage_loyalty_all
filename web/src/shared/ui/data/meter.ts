/**
 * Display-only share of a value in a maximum, as a 0–100 percentage. Inputs stay strings
 * (DecimalString on the wire); the numbers parsed here only size a bar, never a value sent back.
 */
export function meterPercent(value: string | number, max: string | number | null | undefined): number {
  const v = Number(value);
  const m = Number(max);
  if (!Number.isFinite(v) || !Number.isFinite(m) || m <= 0) return 0;
  return Math.min(100, Math.max(0, (v / m) * 100));
}
