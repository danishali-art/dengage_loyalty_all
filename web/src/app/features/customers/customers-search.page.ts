import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { PageHeader } from '../../shared/ui/page-header';
import { Button } from '../../shared/ui/button';
import { Paginator } from '../../shared/ui/paginator';
import { EmptyState } from '../../shared/ui/empty-state';
import { StatusPill } from '../../shared/ui/status-pill';
import { CustomersService } from './customers.service';
import { CustomerSummary } from './customer.model';
import { CUSTOMER_PAGE_SIZE, CUSTOMER_PAGE_SIZES } from './customer-paging';
import { CustomerListRow, customerListRow } from './customer-list-row';

const SEARCH_DEBOUNCE_MS = 350;

/**
 * Customers list, Layout A (UI-only, 2026-10-03): one card with a single search box (Enter on an
 * exact contact key opens the profile), the table, and the standard paginator. The account count
 * comes from the list; the program count and first seen from the existing profile endpoint,
 * fetched for the visible rows only. Headline numbers, segments, tier/program filters and sorting need
 * list-API changes (a scope change) and are not part of this version.
 */
@Component({
  selector: 'app-customers-search-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, Button, Paginator, EmptyState, StatusPill, DatePipe, TranslatePipe],
  template: `
    <app-page-header
      [heading]="'nav.customers' | translate"
      [subtitle]="'customers.list.subtitle' | translate"
    />

    <div class="mx-auto max-w-7xl space-y-6 px-8 py-6">
      <section class="card overflow-hidden !p-0">
        <form
          class="flex flex-wrap items-end gap-3 p-5"
          role="search"
          [attr.aria-label]="'customers.list.searchLabel' | translate"
          (submit)="$event.preventDefault(); submitSearch()"
        >
          <label class="flex min-w-[16rem] flex-1 flex-col gap-1 text-xs text-gray-500">
            {{ 'customers.list.searchLabel' | translate }}
            <input
              class="field-input !py-2 font-mono placeholder:font-sans"
              [placeholder]="'customers.list.searchPlaceholder' | translate"
              [value]="search()"
              (input)="onSearchInput($any($event.target).value)"
              autocomplete="off"
            />
          </label>
          <div class="flex shrink-0 gap-2">
            <app-button type="submit" [pending]="loading()">{{
              'customers.filter.search' | translate
            }}</app-button>
            <app-button type="button" variant="secondary" (click)="reset()">{{
              'customers.filter.reset' | translate
            }}</app-button>
          </div>
          <p class="basis-full text-xs text-gray-400">
            {{ 'customers.list.searchHint' | translate }}
          </p>
        </form>

        <div class="border-t border-gray-100">
          @if (!loading() && customers().length === 0) {
            <div class="p-5">
              <app-empty-state
                [heading]="
                  (search() ? 'customers.list.noMatch' : 'customers.list.empty') | translate
                "
                [description]="
                  (search() ? 'customers.list.noMatchBody' : 'customers.list.emptyBody') | translate
                "
                icon="👥"
              />
            </div>
          } @else {
            <div class="overflow-x-auto">
              <table class="w-full text-sm" [attr.aria-busy]="loading() ? 'true' : null">
                <thead>
                  <tr>
                    <th class="section-label px-4 py-3 text-left">
                      {{ 'customers.list.col.contactKey' | translate }}
                    </th>
                    <th class="section-label px-4 py-3 text-right">
                      {{ 'customers.list.col.programs' | translate }}
                    </th>
                    <th class="section-label px-4 py-3 text-right">
                      {{ 'customers.list.col.accounts' | translate }}
                    </th>
                    <th class="section-label px-4 py-3 text-left">
                      {{ 'customers.list.col.firstSeen' | translate }}
                    </th>
                    <th class="section-label px-4 py-3 text-left">
                      {{ 'customers.list.col.lastActivity' | translate }}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  @for (c of customers(); track c.contactKey) {
                    <tr
                      class="cursor-pointer border-t border-gray-100 hover:bg-gray-50"
                      tabindex="0"
                      (click)="open(c.contactKey)"
                      (keydown.enter)="open(c.contactKey)"
                    >
                      <td class="px-4 py-3 font-mono text-sm font-medium text-gray-900">
                        {{ c.contactKey }}
                      </td>
                      @if (details()[c.contactKey]; as d) {
                        <td class="px-4 py-3 text-right">
                          <span
                            [title]="
                              d.programs.join(
                                '
'
                              )
                            "
                          >
                            <app-status-pill tone="neutral">{{
                              d.programs.length
                            }}</app-status-pill>
                          </span>
                        </td>
                      } @else {
                        <td class="px-4 py-3" aria-hidden="true">
                          <div class="ml-auto h-4 w-8 animate-pulse rounded bg-gray-200"></div>
                        </td>
                      }
                      <td class="px-4 py-3 text-right">
                        <app-status-pill tone="neutral">{{ c.accountCount }}</app-status-pill>
                      </td>
                      <td class="px-4 py-3 whitespace-nowrap text-gray-500">
                        @if (details()[c.contactKey]; as d) {
                          {{ d.firstSeenAt ? (d.firstSeenAt | date: 'mediumDate') : '—' }}
                        } @else {
                          <div
                            class="h-4 w-20 animate-pulse rounded bg-gray-200"
                            aria-hidden="true"
                          ></div>
                        }
                      </td>
                      <td class="px-4 py-3 whitespace-nowrap text-gray-500">
                        {{ c.lastActivityAt | date: 'medium' }}
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <div class="px-4">
              <app-paginator
                [page]="page()"
                [pageSize]="pageSize()"
                [total]="total()"
                [pageSizeOptions]="pageSizes"
                [pageSizeLabel]="'customers.paging.pageView' | translate"
                (pageChange)="loadPage($event)"
                (pageSizeChange)="changePageSize($event)"
              />
            </div>
          }
        </div>
      </section>
    </div>
  `,
})
export class CustomersSearchPage implements OnInit {
  private readonly router = inject(Router);
  private readonly customersService = inject(CustomersService);
  private searchDebounce?: ReturnType<typeof setTimeout>;
  /** Ignores responses from a page load that a newer one has replaced. */
  private generation = 0;

  protected readonly pageSizes = CUSTOMER_PAGE_SIZES;
  protected readonly pageSize = signal(CUSTOMER_PAGE_SIZE);
  protected readonly customers = signal<readonly CustomerSummary[]>([]);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly search = signal('');
  /** Per-row facts from the profile and cap-usage endpoints, by contact key. */
  protected readonly details = signal<Readonly<Record<string, CustomerListRow>>>({});

  ngOnInit(): void {
    void this.loadPage(1);
  }

  protected async loadPage(page: number): Promise<void> {
    const generation = ++this.generation;
    this.loading.set(true);
    try {
      const result = await this.customersService.list(page, this.pageSize(), this.search().trim());
      if (generation !== this.generation) return;
      this.customers.set(result.data);
      this.page.set(result.page);
      this.total.set(result.total);
      void this.loadDetails(result.data, generation);
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }

  /** One profile call per visible row; a row that fails just stays without details. */
  private async loadDetails(rows: readonly CustomerSummary[], generation: number): Promise<void> {
    await Promise.all(
      rows
        .filter((c) => !this.details()[c.contactKey])
        .map(async (c) => {
          try {
            const profile = await this.customersService.getProfile(c.contactKey);
            if (generation !== this.generation) return;
            this.details.update((d) => ({ ...d, [c.contactKey]: customerListRow(profile) }));
          } catch {
            // Leave the row with its list columns only.
          }
        }),
    );
  }

  protected onSearchInput(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchDebounce);
    this.searchDebounce = setTimeout(() => void this.loadPage(1), SEARCH_DEBOUNCE_MS);
  }

  /** Enter / Search: an exact contact-key match opens that profile; otherwise it filters. */
  protected async submitSearch(): Promise<void> {
    clearTimeout(this.searchDebounce);
    const key = this.search().trim();
    await this.loadPage(1);
    if (key && this.customers().some((c) => c.contactKey === key)) this.open(key);
  }

  protected reset(): void {
    clearTimeout(this.searchDebounce);
    this.search.set('');
    void this.loadPage(1);
  }

  protected changePageSize(size: number): void {
    this.pageSize.set(size);
    void this.loadPage(1);
  }

  protected open(contactKey: string): void {
    void this.router.navigate(['/customers', contactKey]);
  }
}
