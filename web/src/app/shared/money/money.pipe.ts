import { Pipe, PipeTransform } from '@angular/core';
import { DecimalString } from './decimal-string';

export interface MoneyDisplayConfig {
  /** ISO 4217 for CASH wallets; omitted for POINTS/STAMP. */
  currency?: string | null;
  decimalPlaces?: number;
  /** Suffix for points wallets, e.g. `★`. */
  unit?: string | null;
}

/**
 * Formats a decimal string for display only — parses a *copy* into a number purely for
 * `Intl.NumberFormat` grouping. The underlying value stays a string.
 */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(value: DecimalString | string | null | undefined, config: MoneyDisplayConfig = {}): string {
    if (value === null || value === undefined || value === '') return '—';

    const decimals = config.decimalPlaces ?? (config.currency ? 2 : 0);
    const n = Number(value);
    const body = Number.isFinite(n)
      ? new Intl.NumberFormat(undefined, {
          minimumFractionDigits: decimals,
          maximumFractionDigits: decimals,
        }).format(n)
      : String(value);

    if (config.currency) return `${body} ${config.currency}`;
    if (config.unit) return `${body} ${config.unit}`;
    return body;
  }
}
