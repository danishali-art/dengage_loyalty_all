import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { EmptyState } from '../../shared/ui/empty-state';
import { Meter } from '../../shared/ui/meter';
import { StatusPill } from '../../shared/ui/status-pill';
import { CustomerProfile } from './customer.model';
import { isZeroAmount } from './customer-filters';
import { CustomerIcon, walletIcon } from './customer-icon';
import { CustomerProfilePanel } from './customer-profile-panel';

/**
 * CR 2026-10-02 (Customer 360) Overview, redesigned 2026-10-03 (UI only — same API data): a
 * profile panel on the left; on the right one card per program with its wallets (balance,
 * pending, expiring soon) and its progress (tier, streaks). Postings live on the Activity tab.
 */
@Component({
  selector: 'app-customer-overview-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Same vertical rhythm as the page, so every tab spaces its cards alike.
  host: { class: 'block space-y-6' },
  imports: [
    DatePipe,
    TranslatePipe,
    EmptyState,
    Meter,
    StatusPill,
    CustomerIcon,
    CustomerProfilePanel,
  ],
  template: `
    <div class="grid grid-cols-1 gap-6 lg:grid-cols-[17rem_1fr]">
      <app-customer-profile-panel [profile]="profile()" />

      <div class="min-w-0 space-y-6">
        @for (program of programs(); track program.programId) {
          <section class="card">
            <div class="mb-5 flex flex-wrap items-center justify-between gap-3">
              <div class="flex items-center gap-3">
                <span
                  class="bg-brand-light text-brand flex h-10 w-10 items-center justify-center rounded-xl"
                >
                  <app-customer-icon name="star" />
                </span>
                <h2 class="text-heading text-base font-bold">{{ program.programName }}</h2>
              </div>
              @if (program.tier; as tier) {
                <app-status-pill tone="info" [dot]="true">{{
                  tier.tierDisplayName ?? ('customers.overview.noTier' | translate)
                }}</app-status-pill>
              }
            </div>

            <div class="grid grid-cols-1 gap-6 xl:grid-cols-2">
              <!-- Wallets -->
              <div>
                <h3 class="section-label mb-3">{{ 'customers.overview.wallets' | translate }}</h3>
                <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  @for (w of program.wallets; track w.accountTypeId) {
                    <div class="rounded-xl border border-gray-100 bg-gray-50 p-4">
                      <div class="flex items-center gap-2 text-xs text-gray-500">
                        <span
                          class="text-brand flex h-7 w-7 items-center justify-center rounded-lg bg-white shadow-sm"
                        >
                          <app-customer-icon [name]="walletIcon(w.type)" size="sm" />
                        </span>
                        <span class="min-w-0 truncate">{{ w.name }}</span>
                        <span class="ml-auto text-gray-400">{{ w.type }}</span>
                      </div>
                      <p class="mt-3 text-2xl font-semibold text-gray-900">
                        {{ w.balance }}
                        <span class="text-sm font-normal text-gray-500">{{
                          w.currency ?? ''
                        }}</span>
                      </p>
                      @if (!isZero(w.pendingAmount)) {
                        <p class="mt-2 text-xs text-gray-500">
                          {{
                            'customers.overview.pending' | translate: { amount: w.pendingAmount }
                          }}
                        </p>
                      }
                      @if (w.expiringAmount) {
                        <p class="text-warn-fg mt-2 flex items-center gap-1 text-xs">
                          <app-customer-icon name="warning" size="sm" />
                          {{
                            'customers.overview.expiring'
                              | translate
                                : {
                                    amount: w.expiringAmount,
                                    date: (w.expiresOn | date: 'mediumDate'),
                                  }
                          }}
                        </p>
                      }
                    </div>
                  }
                </div>
              </div>

              <!-- Progress -->
              <div>
                <h3 class="section-label mb-3">{{ 'customers.progress.heading' | translate }}</h3>
                <div class="space-y-4">
                  @if (program.tier; as tier) {
                    <div>
                      <p class="mb-1 flex items-center gap-1.5 text-sm text-gray-700">
                        <span class="text-brand"
                          ><app-customer-icon name="trophy" size="sm"
                        /></span>
                        {{ 'customers.overview.tier' | translate }}
                      </p>
                      @if (tier.nextTierMinPoints) {
                        <app-meter
                          [value]="tier.qualifyingPoints"
                          [max]="tier.nextTierMinPoints"
                          [label]="
                            'customers.overview.qualifyingOf'
                              | translate
                                : {
                                    points: tier.qualifyingPoints,
                                    target: tier.nextTierMinPoints,
                                    tier: tier.nextTierDisplayName,
                                  }
                          "
                        />
                      } @else {
                        <p class="text-xs text-gray-500">
                          {{
                            'customers.progress.topTier'
                              | translate: { points: tier.qualifyingPoints }
                          }}
                        </p>
                      }
                      @if (tier.graceEndsAt || tier.lockedUntil) {
                        <p class="mt-1 text-xs text-gray-400">
                          @if (tier.graceEndsAt) {
                            {{ 'customers.tiers.graceEnds' | translate }}:
                            {{ tier.graceEndsAt | date: 'mediumDate' }}
                          }
                          @if (tier.lockedUntil) {
                            · {{ 'customers.tiers.lockedUntil' | translate }}:
                            {{ tier.lockedUntil | date: 'mediumDate' }}
                          }
                        </p>
                      }
                    </div>
                  }
                  @for (s of program.streaks; track s.campaignId) {
                    <div>
                      <p class="mb-1 flex items-center gap-1.5 text-sm text-gray-700">
                        <span class="text-streak-fg"
                          ><app-customer-icon name="fire" size="sm"
                        /></span>
                        {{ s.campaignName }}
                      </p>
                      <app-meter
                        [value]="s.streakCount"
                        [max]="s.targetPeriods"
                        [label]="
                          'customers.streaks.progress'
                            | translate
                              : {
                                  count: s.streakCount,
                                  target: s.targetPeriods,
                                  completions: s.completions,
                                }
                        "
                      />
                    </div>
                  }
                  @if (!program.tier && program.streaks.length === 0) {
                    <p class="text-xs text-gray-500">{{ 'customers.progress.none' | translate }}</p>
                  }
                </div>
              </div>
            </div>
          </section>
        } @empty {
          <app-empty-state [heading]="'customers.overview.noPrograms' | translate" />
        }
      </div>
    </div>
  `,
})
export class CustomerOverviewTab {
  readonly profile = input.required<CustomerProfile>();

  protected readonly isZero = isZeroAmount;
  protected readonly walletIcon = walletIcon;
  protected readonly programs = computed(() => this.profile().programs ?? []);
}
