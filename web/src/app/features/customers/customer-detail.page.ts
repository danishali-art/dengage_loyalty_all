import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { PageHeader } from '../../shared/ui/page-header';
import { Button } from '../../shared/ui/button';
import { StatusPill } from '../../shared/ui/status-pill';
import { EmptyState } from '../../shared/ui/empty-state';
import { CustomersService } from './customers.service';
import { CustomerProfile, LedgerEntry, TierHistoryEntry } from './customer.model';

@Component({
  selector: 'app-customer-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, Button, StatusPill, EmptyState, DatePipe],
  template: `
    <app-page-header
      [heading]="contactKey()"
      subtitle="Customer profile"
      [crumbs]="[{ label: 'Customers', link: ['/customers'] }, { label: contactKey() }]"
    />

    <div class="mx-auto max-w-5xl space-y-6 px-8 py-6">
      @if (loading()) {
        <p class="text-sm text-gray-500">Loading…</p>
      } @else if (!profile()) {
        <app-empty-state heading="Customer not found" description="No profile exists yet for this contact key." icon="🔍" />
      } @else {
        <div class="grid grid-cols-2 gap-6">
          <!-- Tier progress -->
          <div class="card">
            <div class="section-header">
              <h2 class="section-heading">Tier progress</h2>
            </div>
            @if (profile()!.tierProgress; as tp) {
              <div class="flex items-center gap-3">
                <app-status-pill tone="info" [dot]="true">{{ tp.currentTierDisplayName ?? 'No tier' }}</app-status-pill>
                @if (tp.nextTierDisplayName) {
                  <span class="text-xs text-gray-400">→ {{ tp.nextTierDisplayName }}</span>
                }
              </div>
              <div class="mt-3">
                <div class="h-2 w-full overflow-hidden rounded-full bg-gray-100">
                  <div class="h-full rounded-full bg-brand" [style.width.%]="progressPercent()"></div>
                </div>
                <p class="mt-2 text-xs text-gray-500">
                  {{ tp.qualifyingPoints }} qualifying points
                  @if (tp.nextTierMinPoints) {
                    of {{ tp.nextTierMinPoints }} for {{ tp.nextTierDisplayName }}
                  }
                </p>
                @if (tp.expiresAt) {
                  <p class="mt-1 text-xs text-gray-400">Qualifying period ends {{ tp.expiresAt | date: 'mediumDate' }}</p>
                }
              </div>
            } @else {
              <p class="text-sm text-gray-500">This program has no tier system configured.</p>
            }
          </div>

          <!-- Balances -->
          <div class="card">
            <div class="section-header">
              <h2 class="section-heading">Balances</h2>
            </div>
            @if (profile()!.balances.length === 0) {
              <p class="text-sm text-gray-500">No account balances yet.</p>
            } @else {
              <div class="space-y-2">
                @for (balance of profile()!.balances; track balance.accountTypeId) {
                  <div class="flex items-center justify-between border-t border-gray-100 py-2 first:border-t-0 first:pt-0">
                    <span class="text-sm text-gray-700">{{ balance.accountTypeName }}</span>
                    <span class="text-sm font-medium text-gray-900">{{ balance.balance }}</span>
                  </div>
                }
              </div>
            }
          </div>
        </div>

        <!-- Tier history -->
        <div class="card">
          <div class="section-header">
            <h2 class="section-heading">Tier history</h2>
          </div>
          @if (tierHistory().length === 0) {
            <p class="text-sm text-gray-500">No tier changes recorded yet.</p>
          } @else {
            <div class="overflow-x-auto">
              <table class="w-full text-sm">
                <thead>
                  <tr>
                    <th class="section-label px-4 py-3 text-left">From</th>
                    <th class="section-label px-4 py-3 text-left">To</th>
                    <th class="section-label px-4 py-3 text-right">Qualifying points</th>
                    <th class="section-label px-4 py-3 text-left">Date</th>
                  </tr>
                </thead>
                <tbody>
                  @for (entry of tierHistory(); track entry.id) {
                    <tr class="border-t border-gray-100">
                      <td class="px-4 py-3.5 text-gray-500">{{ entry.fromTierName ?? '—' }}</td>
                      <td class="px-4 py-3.5 font-medium text-gray-800">{{ entry.toTierName }}</td>
                      <td class="px-4 py-3.5 text-right">{{ entry.qualifyingPoints }}</td>
                      <td class="px-4 py-3.5 text-gray-500">{{ entry.createdAt | date: 'medium' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>

        <!-- Ledger -->
        <div class="card">
          <div class="section-header">
            <h2 class="section-heading">Recent activity</h2>
          </div>
          @if (ledger().length === 0) {
            <p class="text-sm text-gray-500">No ledger entries yet.</p>
          } @else {
            <div class="overflow-x-auto">
              <table class="w-full text-sm">
                <thead>
                  <tr>
                    <th class="section-label px-4 py-3 text-left">Reason</th>
                    <th class="section-label px-4 py-3 text-right">Delta</th>
                    <th class="section-label px-4 py-3 text-left">Date</th>
                  </tr>
                </thead>
                <tbody>
                  @for (entry of ledger(); track entry.id) {
                    <tr class="border-t border-gray-100">
                      <td class="px-4 py-3.5 text-gray-700">{{ entry.reason }}</td>
                      <td class="px-4 py-3.5 text-right font-medium" [class]="Number(entry.delta) >= 0 ? 'text-success-fg' : 'text-danger-fg'">
                        {{ Number(entry.delta) >= 0 ? '+' : '' }}{{ entry.delta }}
                      </td>
                      <td class="px-4 py-3.5 text-gray-500">{{ entry.createdAt | date: 'medium' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            @if (nextCursor()) {
              <div class="mt-4 text-center">
                <app-button variant="secondary" size="sm" [pending]="ledgerLoading()" (click)="loadMoreLedger()">Load more</app-button>
              </div>
            }
          }
        </div>
      }
    </div>
  `,
})
export class CustomerDetailPage implements OnInit {
  readonly contactKey = input.required<string>();

  private readonly customers = inject(CustomersService);

  protected readonly Number = Number;
  protected readonly loading = signal(true);
  protected readonly profile = signal<CustomerProfile | null>(null);
  protected readonly tierHistory = signal<readonly TierHistoryEntry[]>([]);
  protected readonly ledger = signal<LedgerEntry[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly ledgerLoading = signal(false);

  protected readonly progressPercent = computed(() => {
    const tp = this.profile()?.tierProgress;
    if (!tp?.nextTierMinPoints) return 100;
    const current = Number(tp.qualifyingPoints);
    const target = Number(tp.nextTierMinPoints);
    if (!Number.isFinite(target) || target <= 0) return 0;
    return Math.min(100, Math.max(0, (current / target) * 100));
  });

  ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const key = this.contactKey();
      const [profile, tierHistory, ledgerPage] = await Promise.all([
        this.customers.getProfile(key).catch(() => null),
        this.customers.getTierHistory(key).catch(() => []),
        this.customers.getLedger(key, null),
      ]);
      this.profile.set(profile);
      this.tierHistory.set(tierHistory);
      this.ledger.set([...ledgerPage.data]);
      this.nextCursor.set(ledgerPage.nextCursor);
    } finally {
      this.loading.set(false);
    }
  }

  protected async loadMoreLedger(): Promise<void> {
    const cursor = this.nextCursor();
    if (!cursor || this.ledgerLoading()) return;
    this.ledgerLoading.set(true);
    try {
      const page = await this.customers.getLedger(this.contactKey(), cursor);
      this.ledger.update((list) => [...list, ...page.data]);
      this.nextCursor.set(page.nextCursor);
    } finally {
      this.ledgerLoading.set(false);
    }
  }
}
