import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { Button } from '../../shared/ui/button';
import { EmptyState } from '../../shared/ui/empty-state';
import { Meter } from '../../shared/ui/meter';
import { CustomersService } from './customers.service';
import { CUSTOMER_PAGE_SIZES, CursorPager } from './customer-paging';
import { Paginator } from '../../shared/ui/paginator';
import { CustomerCardBucket, LedgerEntry } from './customer.model';
import { isDebit } from './customer-filters';
import { CustomerEventButton, OpenEventRequest } from './customer-event-button';

/**
 * CR 2026-10-02 (Customer 360) P3 Card buckets: each card bucket in the customer's programs, its
 * per-customer caps against this customer's usage (D7, from the ledger), and — for the chosen
 * bucket — its postings.
 */
@Component({
  selector: 'app-customer-card-buckets-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Same vertical rhythm as the page, so every tab spaces its cards alike.
  host: { class: 'block space-y-6' },
  imports: [DatePipe, TranslatePipe, Button, EmptyState, Meter, CustomerEventButton, Paginator],
  template: `
    @if (error()) {
      <p class="card text-danger-fg text-sm">{{ 'customers.loadError' | translate }}</p>
    } @else if (loading()) {
      <p class="text-sm text-gray-500" aria-busy="true">{{ 'common.loading' | translate }}</p>
    } @else {
      @for (b of buckets(); track b.ruleId) {
        <section class="card">
          <div class="section-header flex items-center justify-between">
            <h2 class="section-heading">{{ b.name }}</h2>
            <span class="text-xs text-gray-500">{{ b.programName }} · {{ b.status }}</span>
          </div>
          <p class="text-xs text-gray-600">
            {{
              'customers.buckets.summary'
                | translate: { reward: b.rewardAmount ?? '—', postings: b.postings }
            }}
            @if (b.lastPostedAt) {
              ·
              {{
                'customers.buckets.last' | translate: { date: (b.lastPostedAt | date: 'medium') }
              }}
            }
          </p>
          <div class="mt-3 grid grid-cols-2 gap-4">
            @if (b.perCustomerPerDay) {
              <app-meter
                [value]="b.usedToday"
                [max]="b.perCustomerPerDay"
                [label]="
                  'customers.rules.today'
                    | translate: { used: b.usedToday, cap: b.perCustomerPerDay }
                "
              />
            }
            @if (b.perCustomerTotal) {
              <app-meter
                [value]="b.usedTotal"
                [max]="b.perCustomerTotal"
                [label]="
                  'customers.rules.total'
                    | translate: { used: b.usedTotal, cap: b.perCustomerTotal }
                "
              />
            }
            @if (!b.perCustomerPerDay && !b.perCustomerTotal) {
              <p class="text-xs text-gray-500">
                {{ 'customers.buckets.noCap' | translate: { used: b.usedTotal } }}
              </p>
            }
          </div>
          @if (b.postings > 0) {
            <div class="mt-3">
              <app-button
                variant="secondary"
                size="sm"
                [pending]="postingsLoading() === b.ruleId"
                (click)="toggle(b.ruleId)"
              >
                {{
                  (selected() === b.ruleId
                    ? 'customers.buckets.hidePostings'
                    : 'customers.buckets.showPostings'
                  ) | translate
                }}
              </app-button>
            </div>
          }
          @if (selected() === b.ruleId) {
            <ul class="mt-3 divide-y divide-gray-100 text-sm">
              @for (entry of postings(); track entry.id) {
                <li class="flex items-center justify-between gap-3 py-2">
                  <span class="text-gray-500">{{ entry.createdAt | date: 'medium' }}</span>
                  <span class="flex-1 text-gray-700">{{
                    'customers.reason.' + entry.reason | translate
                  }}</span>
                  <span
                    class="font-medium"
                    [class]="debit(entry.delta) ? 'text-danger-fg' : 'text-success-fg'"
                    >{{ debit(entry.delta) ? '' : '+' }}{{ entry.delta }}</span
                  >
                  <app-customer-event-button
                    (opened)="openEvent.emit({ eventId: entry.sourceEventId, postingId: entry.id })"
                  />
                </li>
              }
            </ul>
            <app-paginator
              [page]="pager.page()"
              [pageSize]="pager.pageSize()"
              [total]="pager.total()"
              (pageChange)="go(b.ruleId, $event)"
              [pageSizeOptions]="pageSizes"
              [pageSizeLabel]="'customers.paging.pageView' | translate"
              (pageSizeChange)="changePageSize(b.ruleId, $event)"
            />
          }
        </section>
      } @empty {
        <app-empty-state [heading]="'customers.buckets.empty' | translate" />
      }
    }
  `,
})
export class CustomerCardBucketsTab implements OnInit {
  readonly contactKey = input.required<string>();
  readonly openEvent = output<OpenEventRequest>();
  private readonly customers = inject(CustomersService);

  protected readonly debit = isDebit;
  protected readonly buckets = signal<readonly CustomerCardBucket[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);

  protected readonly selected = signal<string | null>(null);
  protected readonly postings = signal<readonly LedgerEntry[]>([]);
  protected readonly pager = new CursorPager();
  protected readonly pageSizes = CUSTOMER_PAGE_SIZES;
  protected readonly postingsLoading = signal<string | null>(null);

  ngOnInit(): void {
    void this.customers
      .getCardBuckets(this.contactKey())
      .then((b) => this.buckets.set(b))
      .catch(() => this.error.set(true))
      .finally(() => this.loading.set(false));
  }

  protected toggle(ruleId: string): void {
    if (this.selected() === ruleId) {
      this.selected.set(null);
      return;
    }
    this.selected.set(ruleId);
    this.postings.set([]);
    this.pager.reset();
    void this.fetch(ruleId, 1);
  }

  protected go(ruleId: string, page: number): void {
    if (!this.postingsLoading()) void this.fetch(ruleId, page);
  }

  // All time: a bucket's postings are few, and the bucket's own totals are all-time too.
  protected changePageSize(ruleId: string, size: number): void {
    this.pager.setPageSize(size);
    void this.fetch(ruleId, 1);
  }

  private async fetch(ruleId: string, page: number): Promise<void> {
    const cursor = this.pager.cursorFor(page);
    if (cursor === undefined) return;
    this.postingsLoading.set(ruleId);
    try {
      const result = await this.customers.getLedger(
        this.contactKey(),
        { ruleId },
        cursor,
        this.pager.pageSize(),
      );
      this.postings.set(result.data);
      this.pager.record(page, result);
    } catch {
      this.error.set(true);
    } finally {
      this.postingsLoading.set(null);
    }
  }
}
