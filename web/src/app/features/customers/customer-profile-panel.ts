import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { StatusPill } from '../../shared/ui/status-pill';
import { CustomerProfile } from './customer.model';
import { CustomerIcon } from './customer-icon';

/**
 * Overview's left column: who the customer is in loyalty terms — contact key, the tier held in
 * each program, and the engagement facts that used to sit in the chip row above the tabs.
 * No birthday and no complaints (D2/D3).
 */
@Component({
  selector: 'app-customer-profile-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, TranslatePipe, StatusPill, CustomerIcon],
  template: `
    <aside class="card space-y-6">
      <div class="flex flex-col items-center text-center">
        <div
          class="bg-brand-light text-brand flex h-20 w-20 items-center justify-center rounded-2xl"
        >
          <app-customer-icon name="user" size="lg" />
        </div>
        <p class="mt-3 font-mono text-sm font-medium break-all text-gray-900">
          {{ profile().contactKey }}
        </p>
        @if (summary(); as s) {
          <p class="mt-1 text-xs text-gray-500">
            {{ 'customers.panel.memberSince' | translate }}
            {{ s.firstSeenAt ? (s.firstSeenAt | date: 'mediumDate') : '—' }}
          </p>
        }
      </div>

      <section>
        <h3 class="section-label mb-2">{{ 'customers.panel.programs' | translate }}</h3>
        <ul class="space-y-2">
          @for (p of profile().programs ?? []; track p.programId) {
            <li class="flex items-center justify-between gap-2">
              <span class="text-sm text-gray-700">{{ p.programName }}</span>
              @if (p.tier) {
                <app-status-pill tone="info" [dot]="true">{{
                  p.tier.tierDisplayName ?? ('customers.overview.noTier' | translate)
                }}</app-status-pill>
              } @else {
                <span class="text-xs text-gray-400">{{
                  'customers.panel.noTiers' | translate
                }}</span>
              }
            </li>
          } @empty {
            <li class="text-xs text-gray-500">{{ 'customers.overview.noPrograms' | translate }}</li>
          }
        </ul>
      </section>

      @if (summary(); as s) {
        <section>
          <h3 class="section-label mb-2">{{ 'customers.panel.engagement' | translate }}</h3>
          <dl class="space-y-3 text-sm">
            <div>
              <dt class="text-xs text-gray-500">
                {{ 'customers.detail.lastActivity' | translate }}
              </dt>
              <dd class="text-gray-900">{{ s.lastActivityAt | date: 'medium' }}</dd>
            </div>
            <div class="flex items-center justify-between">
              <dt class="text-xs text-gray-500">
                {{ 'customers.panel.streaksInProgress' | translate }}
              </dt>
              <dd>
                <app-status-pill [tone]="s.activeStreakCount > 0 ? 'streak' : 'neutral'">{{
                  s.activeStreakCount
                }}</app-status-pill>
              </dd>
            </div>
            <div class="flex items-center justify-between">
              <dt class="text-xs text-gray-500">
                {{ 'customers.panel.failedEvents' | translate }}
              </dt>
              <dd>
                <app-status-pill
                  [tone]="s.failedEventsLast7Days > 0 ? 'danger' : 'neutral'"
                  [dot]="s.failedEventsLast7Days > 0"
                  >{{ s.failedEventsLast7Days }}</app-status-pill
                >
              </dd>
            </div>
            <div class="flex items-center justify-between">
              <dt class="text-xs text-gray-500">{{ 'customers.panel.wallets' | translate }}</dt>
              <dd>
                <app-status-pill tone="neutral">{{ walletCount() }}</app-status-pill>
              </dd>
            </div>
          </dl>
        </section>
      }
    </aside>
  `,
})
export class CustomerProfilePanel {
  readonly profile = input.required<CustomerProfile>();
  protected readonly summary = computed(() => this.profile().summary);
  protected readonly walletCount = computed(() => this.profile().balances.length);
}
