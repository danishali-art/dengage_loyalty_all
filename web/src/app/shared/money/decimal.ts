import Big from 'big.js';
import { DecimalString, decimalString } from './decimal-string';

/**
 * Arbitrary-precision helpers for **live-preview arithmetic only** (e.g. showing
 * "1,055.99 × 0.1 = 105"). The server does the real math on submit — never send a
 * value computed here.
 */

Big.RM = Big.roundDown; // engine floors earnings

export function multiplyFloor(amount: DecimalString | string, rate: DecimalString | string, decimalPlaces = 0): DecimalString {
  const result = new Big(amount).times(new Big(rate)).round(decimalPlaces, Big.roundDown);
  return decimalString(result.toFixed(decimalPlaces));
}

export function sum(values: readonly (DecimalString | string)[]): DecimalString {
  const total = values.reduce((acc, v) => acc.plus(new Big(v)), new Big(0));
  return decimalString(total.toString());
}

export function gte(a: DecimalString | string, b: DecimalString | string): boolean {
  return new Big(a).gte(new Big(b));
}

export function isPositive(value: DecimalString | string): boolean {
  try {
    return new Big(value).gt(0);
  } catch {
    return false;
  }
}
