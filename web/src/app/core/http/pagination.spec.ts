import { buildHttpParams, totalPages, emptyPage } from './pagination';

describe('buildHttpParams', () => {
  it('drops null/undefined/empty and keeps primitives', () => {
    const p = buildHttpParams({ page: 1, pageSize: 25, q: '', status: null, active: false, name: 'x' });
    expect(p.get('page')).toBe('1');
    expect(p.get('pageSize')).toBe('25');
    expect(p.get('active')).toBe('false');
    expect(p.get('name')).toBe('x');
    expect(p.has('q')).toBe(false);
    expect(p.has('status')).toBe(false);
  });

  it('expands arrays into repeated params', () => {
    const p = buildHttpParams({ tag: ['a', 'b', ''] });
    expect(p.getAll('tag')).toEqual(['a', 'b']);
  });

  it('ignores object values', () => {
    const p = buildHttpParams({ weird: { a: 1 } });
    expect(p.has('weird')).toBe(false);
  });
});

describe('pagination helpers', () => {
  it('totalPages rounds up and never returns 0', () => {
    expect(totalPages({ ...emptyPage(10), total: 0 })).toBe(1);
    expect(totalPages({ ...emptyPage(10), total: 25 })).toBe(3);
  });
});
