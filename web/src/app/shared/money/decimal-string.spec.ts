import { decimalString, isDecimalString, toDecimalStringOrNull } from './decimal-string';

describe('decimal-string', () => {
  it('accepts numeric(20,4)-shaped values', () => {
    for (const v of ['0', '150.00', '-12.5', '1234567890123456.9999', '0.0001']) {
      expect(isDecimalString(v)).toBe(true);
    }
  });

  it('rejects malformed values', () => {
    for (const v of ['', 'abc', '1.23456', '1e5', '1,000', '  ', '.', '12345678901234567']) {
      expect(isDecimalString(v)).toBe(false);
    }
  });

  it('decimalString() constructs from string or number and throws on garbage', () => {
    expect(decimalString('10.50')).toBe('10.50');
    expect(decimalString(42)).toBe('42');
    expect(() => decimalString('1.99999')).toThrow();
  });

  it('toDecimalStringOrNull() is blank-tolerant', () => {
    expect(toDecimalStringOrNull('')).toBeNull();
    expect(toDecimalStringOrNull(null)).toBeNull();
    expect(toDecimalStringOrNull('  ')).toBeNull();
    expect(toDecimalStringOrNull('7.25')).toBe('7.25');
    expect(toDecimalStringOrNull('nope')).toBeNull();
  });
});
