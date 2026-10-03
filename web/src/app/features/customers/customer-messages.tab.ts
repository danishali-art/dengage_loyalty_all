import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { EmptyState } from '../../shared/ui/empty-state';
import { FilterBar } from '../../shared/ui/filter-bar';
import { Paginator } from '../../shared/ui/paginator';
import { StatusPill, StatusTone } from '../../shared/ui/status-pill';
import { CustomersService } from './customers.service';
import { CUSTOMER_PAGE_SIZES, CursorPager } from './customer-paging';
import { OUTBOX_STATUSES, SentMessage } from './customer.model';
import { defaultMessageFilter, toMessageQuery } from './customer-filters';

export function outboxStatusTone(status: string): StatusTone {
  return status === 'published' ? 'success' : status === 'failed' ? 'danger' : 'warning';
}

/**
 * CR 2026-10-02 (Customer 360) P3 Messages sent: the outbound events for the customer — type,
 * status, attempts, times and dedup key. D5: never the payload.
 */
@Component({
  selector: 'app-customer-messages-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Same vertical rhythm as the page, so every tab spaces its cards alike.
  host: { class: 'block space-y-6' },
  imports: [
    DatePipe,
    ReactiveFormsModule,
    TranslatePipe,
    EmptyState,
    FilterBar,
    Paginator,
    StatusPill,
  ],
  template: `
    <!-- Option B: filters and results in one card. -->
    <section class="card overflow-hidden !p-0">
      <div class="p-5">
        <app-filter-bar
          [formGroup]="form"
          [label]="'customers.filter.messagesLabel' | translate"
          [searchLabel]="'customers.filter.search' | translate"
          [resetLabel]="'customers.filter.reset' | translate"
          [busy]="loading()"
          (searched)="search()"
          (cleared)="reset()"
        >
          <label class="flex min-w-0 min-w-[9rem] flex-[1.2] flex-col gap-1 text-xs text-gray-500">
            {{ 'customers.filter.eventType' | translate }}
            <input
              class="field-input !py-2 font-mono"
              type="text"
              formControlName="eventType"
              placeholder="loyalty.points.earned"
            />
          </label>
          <label class="flex min-w-0 min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-gray-500">
            {{ 'customers.filter.status' | translate }}
            <select class="field-input !py-2" formControlName="status">
              <option value="">{{ 'customers.filter.all' | translate }}</option>
              @for (s of statuses; track s) {
                <option [value]="s">{{ 'customers.messages.status.' + s | translate }}</option>
              }
            </select>
          </label>
          <label
            class="flex min-w-0 min-w-[12.5rem] flex-[1.5] flex-col gap-1 text-xs text-gray-500"
          >
            {{ 'customers.filter.from' | translate }}
            <input class="field-input !py-2" type="datetime-local" formControlName="from" />
          </label>
          <label
            class="flex min-w-0 min-w-[12.5rem] flex-[1.5] flex-col gap-1 text-xs text-gray-500"
          >
            {{ 'customers.filter.to' | translate }}
            <input class="field-input !py-2" type="datetime-local" formControlName="to" />
          </label>
        </app-filter-bar>
        <p class="mt-2 text-xs text-gray-400">
          {{ 'customers.filter.localTime' | translate }}
          {{ 'customers.messages.retention' | translate }}
        </p>
      </div>
      <div class="border-t border-gray-100">
        @if (error()) {
          <p class="text-danger-fg p-5 text-sm">{{ 'customers.loadError' | translate }}</p>
        } @else if (!loading() && messages().length === 0) {
          <div class="p-5">
            <app-empty-state [heading]="'customers.messages.empty' | translate" />
          </div>
        } @else {
          <div class="overflow-x-auto">
            <table class="w-full text-sm" [attr.aria-busy]="loading() ? 'true' : null">
              <thead>
                <tr>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.messages.created' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.col.eventType' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.col.status' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-right">
                    {{ 'customers.messages.attempts' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.messages.published' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.messages.dedupKey' | translate }}
                  </th>
                </tr>
              </thead>
              <tbody>
                @for (m of messages(); track m.eventId) {
                  <tr class="border-t border-gray-100 hover:bg-gray-50">
                    <td class="px-4 py-3 whitespace-nowrap text-gray-500">
                      {{ m.createdAt | date: 'medium' }}
                    </td>
                    <td class="px-4 py-3 font-mono text-xs text-gray-700">{{ m.eventType }}</td>
                    <td class="px-4 py-3">
                      <app-status-pill [tone]="statusTone(m.status)" [dot]="true">{{
                        'customers.messages.status.' + m.status | translate
                      }}</app-status-pill>
                    </td>
                    <td class="px-4 py-3 text-right">{{ m.attempts }}</td>
                    <td class="px-4 py-3 whitespace-nowrap text-gray-500">
                      {{ m.publishedAt ? (m.publishedAt | date: 'medium') : '—' }}
                    </td>
                    <td
                      class="max-w-xs truncate px-4 py-3 font-mono text-xs text-gray-500"
                      [title]="m.dedupKey ?? ''"
                    >
                      {{ m.dedupKey ?? '—' }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="px-4">
            <app-paginator
              [page]="pager.page()"
              [pageSize]="pager.pageSize()"
              [total]="pager.total()"
              (pageChange)="go($event)"
              [pageSizeOptions]="pageSizes"
              [pageSizeLabel]="'customers.paging.pageView' | translate"
              (pageSizeChange)="changePageSize($event)"
            />
          </div>
        }
      </div>
    </section>
  `,
})
export class CustomerMessagesTab implements OnInit {
  readonly contactKey = input.required<string>();
  private readonly customers = inject(CustomersService);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly statuses = OUTBOX_STATUSES;
  protected readonly statusTone = outboxStatusTone;
  protected readonly form = this.fb.group(defaultMessageFilter());
  protected readonly messages = signal<readonly SentMessage[]>([]);
  protected readonly pager = new CursorPager();
  protected readonly pageSizes = CUSTOMER_PAGE_SIZES;
  protected readonly loading = signal(false);
  protected readonly error = signal(false);

  ngOnInit(): void {
    this.restart();
  }

  protected search(): void {
    this.restart();
  }

  protected reset(): void {
    this.form.reset(defaultMessageFilter());
    this.restart();
  }

  protected go(page: number): void {
    if (!this.loading()) void this.fetch(page);
  }

  /** New filters: back to page 1. */
  private restart(): void {
    this.pager.reset();
    void this.fetch(1);
  }

  protected changePageSize(size: number): void {
    this.pager.setPageSize(size);
    void this.fetch(1);
  }

  private async fetch(page: number): Promise<void> {
    const cursor = this.pager.cursorFor(page);
    if (cursor === undefined) return;
    this.loading.set(true);
    this.error.set(false);
    try {
      const result = await this.customers.getMessages(
        this.contactKey(),
        toMessageQuery(this.form.getRawValue()),
        cursor,
        this.pager.pageSize(),
      );
      this.messages.set(result.data);
      this.pager.record(page, result);
    } catch {
      this.error.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
