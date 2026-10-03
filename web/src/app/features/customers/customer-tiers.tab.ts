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
import { StatusPill } from '../../shared/ui/status-pill';
import { meterPercent } from '../../shared/ui/meter';
import { Paginator } from '../../shared/ui/paginator';
import { CUSTOMER_PAGE_SIZE, CUSTOMER_PAGE_SIZES, pageSlice } from './customer-paging';
import { CustomersService } from './customers.service';
import { CustomerProfile, TierHistoryEntry } from './customer.model';
import { CustomerEventButton, OpenEventRequest } from './customer-event-button';

/**
 * CR 2026-10-02 (Customer 360) P2 Tiers: the tier status in each program and every tier change,
 * with its program and cause (points, a reward, or the nightly downgrade — which has no event).
 * Tier status (2026-10-03): one tile per program in a row that scrolls sideways, like the caps on
 * Rules & caps; the history table below it.
 */
@Component({
  selector: 'app-customer-tiers-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Same vertical rhythm as the page, so every tab spaces its cards alike.
  host: { class: 'block space-y-6' },
  imports: [DatePipe, TranslatePipe, EmptyState, StatusPill, CustomerEventButton, Paginator],
  template: `
    <div class="space-y-6">
      <!-- Same tile row as Rules & caps (2026-10-03): one tile per program that has tiers. -->
      <section class="card">
        <div class="section-header flex items-center justify-between gap-3">
          <h2 class="section-heading">{{ 'customers.tiers.statusHeading' | translate }}</h2>
          @if (tiles().length > 0) {
            <span class="text-xs text-gray-500">{{
              'customers.tiers.summary' | translate: { count: tiles().length }
            }}</span>
          }
        </div>
        @if (tiles().length === 0) {
          <p class="text-sm text-gray-500">{{ 'customers.tiers.noneTiered' | translate }}</p>
        } @else {
          <div
            class="-mx-1 flex snap-x snap-mandatory gap-4 overflow-x-auto px-1 pt-1 pb-3"
            role="list"
            tabindex="0"
            [attr.aria-label]="'customers.tiers.statusHeading' | translate"
          >
            @for (tile of tiles(); track tile.programId) {
              <article
                role="listitem"
                class="border-t-brand w-[23rem] shrink-0 snap-start rounded-xl border border-t-[3px] border-gray-100 bg-white p-4 shadow-sm"
              >
                <header class="flex items-start justify-between gap-2">
                  <h3
                    class="min-w-0 truncate text-sm font-semibold text-gray-900"
                    [title]="tile.programName"
                  >
                    {{ tile.programName }}
                  </h3>
                  <app-status-pill tone="info" [dot]="true">{{
                    tile.tier.tierDisplayName ?? ('customers.overview.noTier' | translate)
                  }}</app-status-pill>
                </header>
                @if (tile.tier; as tier) {
                  <div class="mt-4 flex items-center gap-4">
                    <div
                      class="flex h-16 w-16 shrink-0 items-center justify-center rounded-full"
                      role="img"
                      [attr.aria-label]="
                        tier.nextTierMinPoints
                          ? ('customers.tiers.toNext'
                            | translate: { percent: tile.percent, tier: tier.nextTierDisplayName })
                          : ('customers.tiers.topTierShort' | translate)
                      "
                      [style.background]="ringBackground(tile.percent)"
                    >
                      <span
                        class="flex h-12 w-12 items-center justify-center rounded-full bg-white text-sm font-semibold text-gray-900"
                        >{{ tile.percent }}%</span
                      >
                    </div>
                    <dl class="min-w-0 flex-1 space-y-1.5 text-xs">
                      <div class="flex justify-between gap-2">
                        <dt class="text-gray-500">{{ 'customers.tiers.points' | translate }}</dt>
                        <dd class="font-medium text-gray-900 tabular-nums">
                          {{ tier.qualifyingPoints }}
                          @if (tier.nextTierMinPoints) {
                            / {{ tier.nextTierMinPoints }}
                          }
                        </dd>
                      </div>
                      <div class="flex justify-between gap-2">
                        <dt class="text-gray-500">{{ 'customers.tiers.nextTier' | translate }}</dt>
                        <dd class="truncate font-medium text-gray-900">
                          {{
                            tier.nextTierDisplayName ?? ('customers.tiers.topTierShort' | translate)
                          }}
                        </dd>
                      </div>
                      <div class="flex justify-between gap-2">
                        <dt class="text-gray-500">
                          {{ 'customers.tiers.periodStart' | translate }}
                        </dt>
                        <dd class="text-gray-900">
                          {{ tier.periodStart ? (tier.periodStart | date: 'mediumDate') : '—' }}
                        </dd>
                      </div>
                      <div class="flex justify-between gap-2">
                        <dt class="text-gray-500">{{ 'customers.tiers.review' | translate }}</dt>
                        <dd class="text-gray-900">
                          {{ tier.graceEndsAt ? (tier.graceEndsAt | date: 'mediumDate') : '—' }}
                        </dd>
                      </div>
                      <div class="flex justify-between gap-2">
                        <dt class="text-gray-500">
                          {{ 'customers.tiers.lockedUntil' | translate }}
                        </dt>
                        <dd class="text-gray-900">
                          {{ tier.lockedUntil ? (tier.lockedUntil | date: 'mediumDate') : '—' }}
                        </dd>
                      </div>
                    </dl>
                  </div>
                }
              </article>
            }
          </div>
        }
      </section>

      <section class="card">
        <div class="section-header">
          <h2 class="section-heading">{{ 'customers.overview.tierHistory' | translate }}</h2>
        </div>
        @if (error()) {
          <p class="text-danger-fg text-sm">{{ 'customers.loadError' | translate }}</p>
        } @else if (!loading() && history().length === 0) {
          <app-empty-state [heading]="'customers.overview.noTierHistory' | translate" />
        } @else {
          <div class="overflow-x-auto">
            <table class="w-full text-sm" [attr.aria-busy]="loading() ? 'true' : null">
              <thead>
                <tr>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.col.date' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.filter.program' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.col.from' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.col.to' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-right">
                    {{ 'customers.col.qualifyingPoints' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.tiers.cause' | translate }}
                  </th>
                  <th class="px-4 py-3"></th>
                </tr>
              </thead>
              <tbody>
                @for (entry of historyPage(); track entry.id) {
                  <tr class="border-t border-gray-100">
                    <td class="px-4 py-3 whitespace-nowrap text-gray-500">
                      {{ entry.createdAt | date: 'medium' }}
                    </td>
                    <td class="px-4 py-3 text-gray-700">{{ entry.programName ?? '—' }}</td>
                    <td class="px-4 py-3 text-gray-500">{{ entry.fromTierName ?? '—' }}</td>
                    <td class="px-4 py-3 font-medium text-gray-800">{{ entry.toTierName }}</td>
                    <td class="px-4 py-3 text-right">{{ entry.qualifyingPoints }}</td>
                    <td class="px-4 py-3 text-xs text-gray-600">
                      {{ 'customers.tiers.causes.' + (entry.cause ?? 'points') | translate }}
                    </td>
                    <td class="px-4 py-3 text-right">
                      <!-- Only a points-driven change has an event of its own to open. -->
                      @if (entry.cause === 'points' && entry.sourceEventId) {
                        <app-customer-event-button
                          (opened)="openEvent.emit({ eventId: entry.sourceEventId })"
                        />
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <app-paginator
            [page]="historyPageNo()"
            [pageSize]="pageSize()"
            [total]="history().length"
            (pageChange)="historyPageNo.set($event)"
            [pageSizeOptions]="pageSizes"
            [pageSizeLabel]="'customers.paging.pageView' | translate"
            (pageSizeChange)="changePageSize($event)"
          />
        }
      </section>
    </div>
  `,
})
export class CustomerTiersTab implements OnInit {
  readonly contactKey = input.required<string>();
  readonly profile = input.required<CustomerProfile>();
  readonly openEvent = output<OpenEventRequest>();
  private readonly customers = inject(CustomersService);

  /**
   * One tile per program that has tiers (a program without a tier system is left out); the ring
   * is the progress to the next tier (100% at the top tier).
   */
  protected readonly tiles = computed(() =>
    (this.profile().programs ?? []).flatMap((p) =>
      p.tier
        ? [
            {
              programId: p.programId,
              programName: p.programName,
              tier: p.tier,
              percent: p.tier.nextTierMinPoints
                ? Math.round(meterPercent(p.tier.qualifyingPoints, p.tier.nextTierMinPoints))
                : 100,
            },
          ]
        : [],
    ),
  );

  protected ringBackground(percent: number): string {
    return `conic-gradient(var(--color-brand) ${percent}%, var(--color-gray-100) 0)`;
  }
  protected readonly history = signal<readonly TierHistoryEntry[]>([]);
  protected readonly pageSizes = CUSTOMER_PAGE_SIZES;
  protected readonly pageSize = signal(CUSTOMER_PAGE_SIZE);
  protected readonly historyPageNo = signal(1);
  protected readonly historyPage = computed(() =>
    pageSlice(this.history(), this.historyPageNo(), this.pageSize()),
  );
  protected readonly loading = signal(true);
  protected readonly error = signal(false);

  protected changePageSize(size: number): void {
    this.pageSize.set(size);
    this.historyPageNo.set(1);
  }

  ngOnInit(): void {
    void this.customers
      .getTierHistory(this.contactKey())
      .then((h) => this.history.set(h))
      .catch(() => this.error.set(true))
      .finally(() => this.loading.set(false));
  }
}
