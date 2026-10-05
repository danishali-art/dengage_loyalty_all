import { TestBed } from '@angular/core/testing';
import { Paginator } from './paginator';

// The optional "page view" selector: absent unless options are given, and it reports the choice.
describe('Paginator page view', () => {
  function render(inputs: Record<string, unknown>) {
    TestBed.configureTestingModule({ imports: [Paginator] });
    const fixture = TestBed.createComponent(Paginator);
    for (const [k, v] of Object.entries({ page: 1, pageSize: 20, total: 95, ...inputs })) {
      fixture.componentRef.setInput(k, v);
    }
    fixture.detectChanges();
    return fixture;
  }

  it('shows no selector without options, as on every existing page', () => {
    const el = render({}).nativeElement as HTMLElement;
    expect(el.querySelector('select')).toBeNull();
    expect(el.textContent).toContain('1–20 of 95');
  });

  it('offers the page sizes and reports the one picked', () => {
    const fixture = render({ pageSizeOptions: [20, 30, 40], pageSizeLabel: 'Page view' });
    const el = fixture.nativeElement as HTMLElement;
    const select = el.querySelector('select')!;
    expect([...select.options].map((o) => o.value)).toEqual(['20', '30', '40']);
    expect(el.textContent).toContain('Page view');

    const picked: number[] = [];
    fixture.componentInstance.pageSizeChange.subscribe((size) => picked.push(size));
    select.value = '40';
    select.dispatchEvent(new Event('change'));
    expect(picked).toEqual([40]);
  });
});
