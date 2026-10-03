import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { EmptyState } from '../../shared/ui/empty-state';
import { Meter } from '../../shared/ui/meter';
import { StatusPill } from '../../shared/ui/status-pill';
import { CustomersService } from './customers.service';
import { CustomerStreak, StreakCompletion } from './customer.model';
import { CUSTOMER_PAGE_SIZE, CUSTOMER_PAGE_SIZES, pageSlice } from './customer-paging';
import { Paginator } from '../../shared/ui/paginator';

/** CR 2026-10-02 (Customer 360) P2 Streaks: progress per campaign and its completion history. */
@Component({
  selector: 'app-customer-streaks-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Same vertical rhythm as the page, so every tab spaces its cards alike.
  host: { class: 'block space-y-6' },
  imports: [DatePipe, TranslatePipe, EmptyState, Meter, StatusPill, Paginator],
  template: `
    @if (error()) {
      <p class="card text-danger-fg text-sm">{{ 'customers.loadError' | translate }}</p>
    } @else if (loading()) {
      <p class="text-sm text-gray-500" aria-busy="true">{{ 'common.loading' | translate }}</p>
    } @else {
      @for (s of streaks(); track s.campaignId) {
        <section class="card">
          <div class="section-header flex items-center justify-between">
            <h2 class="section-heading">{{ s.campaignName }}</h2>
            <span class="flex items-center gap-2 text-xs text-gray-500">
              {{ s.programName }}
              <app-status-pill [tone]="s.status === 'active' ? 'streak' : 'neutral'" [dot]="true">{{
                'customers.streaks.status.' + s.status | translate
              }}</app-status-pill>
            </span>
          </div>
          <app-meter
            [value]="s.streakCount"
            [max]="s.targetPeriods"
            [label]="
              'customers.streaks.progress'
                | translate
                  : { count: s.streakCount, target: s.targetPeriods, completions: s.completions }
            "
          />
          <dl class="mt-3 grid grid-cols-[11rem_1fr] gap-y-1 text-xs">
            <dt class="text-gray-500">{{ 'customers.streaks.rule' | translate }}</dt>
            <dd>
              {{
                'customers.streaks.ruleText'
                  | translate: { period: s.period, metric: s.metric, threshold: s.threshold }
              }}
            </dd>
            <dt class="text-gray-500">{{ 'customers.streaks.lastMet' | translate }}</dt>
            <dd>{{ s.lastMetPeriod ? (s.lastMetPeriod | date: 'mediumDate') : '—' }}</dd>
            @if (s.latestPeriodStart) {
              <dt class="text-gray-500">{{ 'customers.streaks.latestPeriod' | translate }}</dt>
              <dd>
                {{
                  'customers.streaks.latestPeriodText'
                    | translate
                      : {
                          date: (s.latestPeriodStart | date: 'mediumDate'),
                          sum: s.latestAggSum ?? '0',
                          count: s.latestAggCount ?? 0,
                        }
                }}
                ·
                {{
                  (s.latestPeriodMet ? 'customers.streaks.met' : 'customers.streaks.notMet')
                    | translate
                }}
              </dd>
            }
          </dl>
          @if (s.history.length > 0) {
            <table class="mt-4 w-full text-sm">
              <thead>
                <tr>
                  <th class="section-label px-2 py-2 text-left">#</th>
                  <th class="section-label px-2 py-2 text-left">
                    {{ 'customers.streaks.completedPeriod' | translate }}
                  </th>
                  <th class="section-label px-2 py-2 text-left">
                    {{ 'customers.streaks.reward' | translate }}
                  </th>
                  <th class="section-label px-2 py-2 text-left">
                    {{ 'customers.col.date' | translate }}
                  </th>
                </tr>
              </thead>
              <tbody>
                @for (h of historyPage(s); track h.completionNo) {
                  <tr class="border-t border-gray-100">
                    <td class="px-2 py-2">{{ h.completionNo }}</td>
                    <td class="px-2 py-2">{{ h.completedPeriod | date: 'mediumDate' }}</td>
                    <td class="px-2 py-2 text-xs text-gray-600">
                      {{ 'customers.streaks.kind.' + h.rewardKind | translate }}
                    </td>
                    <td class="px-2 py-2 text-gray-500">{{ h.createdAt | date: 'medium' }}</td>
                  </tr>
                }
              </tbody>
            </table>
            <app-paginator
              [page]="historyPageNo(s.campaignId)"
              [pageSize]="pageSize()"
              [total]="s.history.length"
              (pageChange)="setHistoryPage(s.campaignId, $event)"
              [pageSizeOptions]="pageSizes"
              [pageSizeLabel]="'customers.paging.pageView' | translate"
              (pageSizeChange)="changePageSize($event)"
            />
          }
        </section>
      } @empty {
        <app-empty-state [heading]="'customers.streaks.empty' | translate" />
      }
      <p class="text-xs text-gray-400">{{ 'customers.streaks.note' | translate }}</p>
    }
  `,
})
export class CustomerStreaksTab implements OnInit {
  readonly contactKey = input.required<string>();
  private readonly customers = inject(CustomersService);

  protected readonly streaks = signal<readonly CustomerStreak[]>([]);
  protected readonly pageSizes = CUSTOMER_PAGE_SIZES;
  protected readonly pageSize = signal(CUSTOMER_PAGE_SIZE);
  /** The history page shown for each campaign (page 1 when absent). */
  private readonly historyPages = signal<Readonly<Record<string, number>>>({});
  protected readonly loading = signal(true);
  protected readonly error = signal(false);

  protected historyPageNo(campaignId: string): number {
    return this.historyPages()[campaignId] ?? 1;
  }

  protected setHistoryPage(campaignId: string, page: number): void {
    this.historyPages.update((pages) => ({ ...pages, [campaignId]: page }));
  }

  /** One page view for every campaign's history; a new size starts each at page 1. */
  protected changePageSize(size: number): void {
    this.pageSize.set(size);
    this.historyPages.set({});
  }

  protected historyPage(s: CustomerStreak): StreakCompletion[] {
    return pageSlice(s.history, this.historyPageNo(s.campaignId), this.pageSize());
  }

  ngOnInit(): void {
    void this.customers
      .getStreaks(this.contactKey())
      .then((s) => this.streaks.set(s))
      .catch(() => this.error.set(true))
      .finally(() => this.loading.set(false));
  }
}
