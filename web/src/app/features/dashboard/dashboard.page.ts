import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { PageHeader } from '../../shared/ui/page-header';
import { SearchableSelect, SelectOption } from '../../shared/ui/searchable-select';
import { ProgramsService } from '../programs/programs.service';
import { DashboardService } from './dashboard.service';
import { DashboardSummary } from './dashboard.model';

interface StatTile {
  label: string;
  value: string;
  icon: string;
  link?: string;
}

@Component({
  selector: 'app-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, PageHeader, SearchableSelect],
  template: `
    <app-page-header heading="Dashboard" subtitle="Tenant-wide overview across every program." />

    <div class="mx-auto max-w-6xl px-8 py-6">
      <!-- Filter bar -->
      <div class="card mb-6 flex flex-wrap items-end gap-4">
        <div class="w-56">
          <label id="filter-program-label" class="mb-1.5 block text-xs font-medium text-gray-500">Program</label>
          <app-searchable-select
            [options]="programOptions()"
            [ngModel]="filterProgramId()"
            (ngModelChange)="onProgramChange($event)"
            placeholder="All programs"
            ariaLabelledby="filter-program-label"
          />
        </div>
        <div>
          <label for="filter-from" class="mb-1.5 block text-xs font-medium text-gray-500">From</label>
          <input id="filter-from" type="date" class="field-input" [ngModel]="fromDate()" (ngModelChange)="onFromChange($event)" />
        </div>
        <div>
          <label for="filter-to" class="mb-1.5 block text-xs font-medium text-gray-500">To</label>
          <input id="filter-to" type="date" class="field-input" [ngModel]="toDate()" (ngModelChange)="onToChange($event)" />
        </div>
        @if (filterProgramId() || fromDate() || toDate()) {
          <button type="button" class="text-xs font-medium text-gray-500 hover:text-brand" (click)="clearFilters()">
            Clear filters
          </button>
        }
      </div>

      @if (loading()) {
        <div class="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4" aria-busy="true">
          @for (i of [0, 1, 2, 3, 4, 5, 6, 7]; track i) {
            <div class="card" aria-hidden="true">
              <div class="h-3 w-20 animate-pulse rounded bg-gray-200"></div>
              <div class="mt-3 h-7 w-14 animate-pulse rounded bg-gray-200"></div>
            </div>
          }
        </div>
      } @else if (summary(); as s) {
        <!-- Stat tiles -->
        <div class="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
          @for (tile of tiles(s); track tile.label) {
            @if (tile.link) {
              <a [routerLink]="tile.link" class="card block transition-colors hover:border-brand">
                <div class="flex items-center gap-2 text-xs font-medium text-gray-500">
                  <span aria-hidden="true">{{ tile.icon }}</span>{{ tile.label }}
                </div>
                <div class="mt-2 text-2xl font-bold text-gray-900">{{ tile.value }}</div>
              </a>
            } @else {
              <div class="card">
                <div class="flex items-center gap-2 text-xs font-medium text-gray-500">
                  <span aria-hidden="true">{{ tile.icon }}</span>{{ tile.label }}
                </div>
                <div class="mt-2 text-2xl font-bold text-gray-900">{{ tile.value }}</div>
              </div>
            }
          }
        </div>
      }
    </div>
  `,
})
export class DashboardPage implements OnInit {
  private readonly dashboardService = inject(DashboardService);
  private readonly programsService = inject(ProgramsService);

  protected readonly loading = signal(true);
  protected readonly summary = signal<DashboardSummary | null>(null);
  protected readonly programOptions = signal<readonly SelectOption<string | null>[]>([
    { value: null, label: 'All programs' },
  ]);

  protected readonly filterProgramId = signal<string | null>(null);
  protected readonly fromDate = signal<string | null>(null);
  protected readonly toDate = signal<string | null>(null);

  ngOnInit(): void {
    void this.loadProgramOptions();
    void this.reload();
  }

  private async loadProgramOptions(): Promise<void> {
    const page = await this.programsService.list(1, 100);
    this.programOptions.set([
      { value: null, label: 'All programs' },
      ...page.data.map((p) => ({ value: p.id, label: p.name })),
    ]);
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    try {
      const summary = await this.dashboardService.getSummary({
        programId: this.filterProgramId(),
        fromDate: this.fromDate(),
        toDate: this.toDate(),
      });
      this.summary.set(summary);
    } finally {
      this.loading.set(false);
    }
  }

  protected onProgramChange(value: string | null): void {
    this.filterProgramId.set(value);
    void this.reload();
  }

  protected onFromChange(value: string): void {
    this.fromDate.set(value || null);
    void this.reload();
  }

  protected onToChange(value: string): void {
    this.toDate.set(value || null);
    void this.reload();
  }

  protected clearFilters(): void {
    this.filterProgramId.set(null);
    this.fromDate.set(null);
    this.toDate.set(null);
    void this.reload();
  }

  protected tiles(s: DashboardSummary): StatTile[] {
    return [
      { label: 'Programs', icon: '▦', value: `${s.programCount}`, link: '/programs' },
      { label: 'Tiers', icon: '🏅', value: `${s.tierCount}` },
      { label: 'Customer accounts', icon: '☻', value: `${s.customerAccountCount}`, link: '/customers' },
      { label: 'Total balance', icon: '⭐', value: this.formatMoney(s.totalBalance) },
      { label: 'Redemptions', icon: '🎁', value: `${s.redemptionCount}` },
      { label: 'Active streaks', icon: '🔥', value: `${s.streakActiveCount}` },
      { label: 'Streaks completed', icon: '✓', value: `${s.streakCompletedInRange}` },
    ];
  }

  private formatMoney(value: string): string {
    const n = Number(value);
    return Number.isFinite(n) ? n.toLocaleString(undefined, { maximumFractionDigits: 2 }) : value;
  }
}
