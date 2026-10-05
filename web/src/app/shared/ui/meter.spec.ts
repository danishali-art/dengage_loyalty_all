import { meterPercent } from './meter';

describe('meterPercent', () => {
  it.each([
    { value: '120', max: '500', percent: 24 },
    { value: '500', max: '500', percent: 100 },
    { value: '700', max: '500', percent: 100 },
    { value: '-5', max: '500', percent: 0 },
    { value: '10', max: '0', percent: 0 },
    { value: '10', max: null, percent: 0 },
    { value: 2, max: 4, percent: 50 },
  ])('$value of $max is $percent%', ({ value, max, percent }) => {
    expect(meterPercent(value, max)).toBe(percent);
  });
});
