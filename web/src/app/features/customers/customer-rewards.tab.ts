import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { EmptyState } from '../../shared/ui/empty-state';
import { StatusPill, StatusTone } from '../../shared/ui/status-pill';
import { CustomersService } from './customers.service';
import { CUSTOMER_PAGE_SIZE, CUSTOMER_PAGE_SIZES, pageSlice } from './customer-paging';
import { Paginator } from '../../shared/ui/paginator';
import { CustomerReward, RewardOutcome } from './customer.model';
import { CustomerEventButton, OpenEventRequest } from './customer-event-button';

export function rewardOutcomeTone(outcome: RewardOutcome): StatusTone {
  return outcome === 'none' ? 'neutral' : 'success';
}

/**
 * CR 2026-10-02 (Customer 360) P2 Rewards: rewards bought with points, stamp-card rewards and
 * rewards granted by streaks — what each cost and what it paid.
 */
@Component({
  selector: 'app-customer-rewards-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Same vertical rhythm as the page, so every tab spaces its cards alike.
  host: { class: 'block space-y-6' },
  imports: [DatePipe, TranslatePipe, EmptyState, StatusPill, CustomerEventButton, Paginator],
  template: `
    <section class="card !p-0">
      @if (error()) {
        <p class="text-danger-fg p-5 text-sm">{{ 'customers.loadError' | translate }}</p>
      } @else if (!loading() && rewards().length === 0) {
        <div class="p-5"><app-empty-state [heading]="'customers.rewards.empty' | translate" /></div>
      } @else {
        <div class="overflow-x-auto">
          <table class="w-full text-sm" [attr.aria-busy]="loading() ? 'true' : null">
            <thead>
              <tr>
                <th class="section-label px-4 py-3 text-left">
                  {{ 'customers.col.date' | translate }}
                </th>
                <th class="section-label px-4 py-3 text-left">
                  {{ 'customers.rewards.reward' | translate }}
                </th>
                <th class="section-label px-4 py-3 text-left">
                  {{ 'customers.rewards.how' | translate }}
                </th>
                <th class="section-label px-4 py-3 text-right">
                  {{ 'customers.rewards.cost' | translate }}
                </th>
                <th class="section-label px-4 py-3 text-left">
                  {{ 'customers.rewards.payout' | translate }}
                </th>
                <th class="px-4 py-3"></th>
              </tr>
            </thead>
            <tbody>
              @for (r of rewardsPage(); track r.sourceEventId + r.source + r.createdAt) {
                <tr class="border-t border-gray-100 hover:bg-gray-50">
                  <td class="px-4 py-3 whitespace-nowrap text-gray-500">
                    {{ r.createdAt | date: 'medium' }}
                  </td>
                  <td class="px-4 py-3">
                    {{ r.rewardName }}
                    @if (r.rewardType) {
                      <div class="text-xs text-gray-400">
                        {{ 'rewards.type.' + r.rewardType | translate }}
                      </div>
                    }
                  </td>
                  <td class="px-4 py-3 text-xs text-gray-600">
                    {{ 'rewards.acquisition.' + r.source | translate }}
                  </td>
                  <td class="px-4 py-3 text-right whitespace-nowrap">
                    @if (r.cost) {
                      {{ r.cost }} <span class="text-xs text-gray-400">{{ r.costWalletName }}</span>
                    } @else {
                      —
                    }
                  </td>
                  <td class="px-4 py-3">
                    <app-status-pill [tone]="outcomeTone(r.outcome)" [dot]="true">{{
                      'customers.rewards.outcome.' + r.outcome | translate
                    }}</app-status-pill>
                    @if (r.cashAmount) {
                      <div class="mt-1 text-xs text-gray-600">
                        +{{ r.cashAmount }} {{ r.cashWalletName }}
                      </div>
                    }
                    @if (r.tierName) {
                      <div class="mt-1 text-xs text-gray-600">{{ r.tierName }}</div>
                    }
                  </td>
                  <td class="px-4 py-3 text-right">
                    <app-customer-event-button
                      (opened)="openEvent.emit({ eventId: r.sourceEventId })"
                    />
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
            [total]="rewards().length"
            (pageChange)="page.set($event)"
            [pageSizeOptions]="pageSizes"
            [pageSizeLabel]="'customers.paging.pageView' | translate"
            (pageSizeChange)="changePageSize($event)"
          />
        </div>
      }
    </section>
  `,
})
export class CustomerRewardsTab implements OnInit {
  readonly contactKey = input.required<string>();
  readonly openEvent = output<OpenEventRequest>();
  private readonly customers = inject(CustomersService);

  protected readonly rewards = signal<readonly CustomerReward[]>([]);
  protected readonly pageSizes = CUSTOMER_PAGE_SIZES;
  protected readonly pageSize = signal(CUSTOMER_PAGE_SIZE);
  protected readonly page = signal(1);
  protected readonly rewardsPage = computed(() =>
    pageSlice(this.rewards(), this.page(), this.pageSize()),
  );
  protected readonly loading = signal(true);
  protected readonly error = signal(false);
  protected readonly outcomeTone = rewardOutcomeTone;

  protected changePageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
  }

  ngOnInit(): void {
    void this.customers
      .getRewards(this.contactKey())
      .then((r) => this.rewards.set(r))
      .catch(() => this.error.set(true))
      .finally(() => this.loading.set(false));
  }
}
