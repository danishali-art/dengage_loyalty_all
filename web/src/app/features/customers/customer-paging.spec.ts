import { CUSTOMER_PAGE_SIZE, CUSTOMER_PAGE_SIZES, CursorPager, pageSlice } from './customer-paging';

// Every customer grid uses the standard paginator; cursor-paged grids reach page n with the cursor
// page n−1 returned, and complete lists are sliced in the browser.
describe('customer paging', () => {
  it('starts every grid at 10 rows, with 10 / 20 / 30 to choose from', () => {
    expect(CUSTOMER_PAGE_SIZE).toBe(10);
    expect(CUSTOMER_PAGE_SIZES).toEqual([10, 20, 30]);
  });

  it('goes back to page 1 when the page size changes', () => {
    const pager = new CursorPager();
    pager.record(1, { nextCursor: 'c2', total: 60 });
    pager.record(2, { nextCursor: 'c3', total: 60 });
    pager.setPageSize(30);
    expect(pager.pageSize()).toBe(30);
    expect(pager.page()).toBe(1);
    expect(pager.cursorFor(2)).toBeUndefined();
  });

  it('remembers the cursor for each page it has seen, forwards and back', () => {
    const pager = new CursorPager();
    expect(pager.cursorFor(1)).toBeNull();
    expect(pager.cursorFor(2)).toBeUndefined();

    pager.record(1, { nextCursor: 'c2', total: 60 });
    expect(pager.page()).toBe(1);
    expect(pager.total()).toBe(60);
    expect(pager.cursorFor(2)).toBe('c2');

    pager.record(2, { nextCursor: 'c3', total: 60 });
    expect(pager.cursorFor(3)).toBe('c3');
    expect(pager.cursorFor(1)).toBeNull();
    expect(pager.cursorFor(2)).toBe('c2');
  });

  it('starts again from page 1 when reset', () => {
    const pager = new CursorPager();
    pager.record(1, { nextCursor: 'c2', total: 60 });
    pager.reset();
    expect(pager.page()).toBe(1);
    expect(pager.total()).toBe(0);
    expect(pager.cursorFor(2)).toBeUndefined();
  });

  it('treats an absent next cursor and total as the last page and zero', () => {
    const pager = new CursorPager();
    pager.record(1, { nextCursor: undefined, total: undefined });
    expect(pager.cursorFor(2)).toBeNull();
    expect(pager.total()).toBe(0);
  });

  it('slices one page out of a complete list', () => {
    const list = Array.from({ length: 30 }, (_, i) => i);
    expect(pageSlice(list, 1)).toHaveLength(10);
    expect(pageSlice(list, 2)).toEqual([10, 11, 12, 13, 14, 15, 16, 17, 18, 19]);
    expect(pageSlice(list, 0)).toHaveLength(10);
    expect(pageSlice(list, 1, 30)).toHaveLength(30);
  });
});
