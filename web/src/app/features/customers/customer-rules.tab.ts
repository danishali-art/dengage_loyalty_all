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
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { EmptyState } from '../../shared/ui/empty-state';
import { FilterBar } from '../../shared/ui/filter-bar';
import { Paginator } from '../../shared/ui/paginator';
import { meterPercent } from '../../shared/ui/meter';
import { StatusPill } from '../../shared/ui/status-pill';
import { CustomersService } from './customers.service';
import { CUSTOMER_PAGE_SIZES, CursorPager } from './customer-paging';
import { CustomerRuleFire, RuleCapUsage } from './customer.model';
import { defaultRuleFireFilter, toRuleFireQuery } from './customer-filters';
import { CustomerEventButton, OpenEventRequest } from './customer-event-button';

export interface CapWindow {
  key: 'today' | 'period' | 'total';
  labelKey: string;
  used: string;
  cap: string | null | undefined;
}

export interface CapTile {
  cap: RuleCapUsage;
  windows: CapWindow[];
  /** The tightest cap's use, 0–100. */
  percent: number;
  reached: boolean;
}

/** One tile per capped rule: its three windows, and how close the tightest cap is. */
export function capTile(cap: RuleCapUsage): CapTile {
  const windows: CapWindow[] = [
    {
      key: 'total',
      labelKey: 'customers.rules.windowTotal',
      used: cap.usedTotal,
      cap: cap.perCustomerTotal,
    },
    {
      key: 'today',
      labelKey: 'customers.rules.windowToday',
      used: cap.usedToday,
      cap: cap.perCustomerPerDay,
    },
    {
      key: 'period',
      labelKey: 'customers.rules.windowPeriod',
      used: cap.usedThisPeriod ?? '0',
      cap: cap.perCustomerPerPeriod,
    },
  ];
  const percents = windows.filter((w) => w.cap).map((w) => meterPercent(w.used, w.cap));
  const percent = Math.round(Math.max(0, ...percents));
  return { cap, windows, percent, reached: percent >= 100 };
}

/**
 * CR 2026-10-02 (Customer 360) P2 Rules & campaigns: the customer's usage of every per-customer
 * rule cap (D7, from the ledger), and every rule decision for the customer. Clicking a rule name
 * narrows the decisions to that rule.
 */
@Component({
  selector: 'app-customer-rules-tab',
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
    CustomerEventButton,
  ],
  template: `
    <!-- Option B (2026-10-03): one tile per capped rule, in a single row that scrolls sideways. -->
    <section class="card">
      <div class="section-header flex items-center justify-between gap-3">
        <h2 class="section-heading">{{ 'customers.rules.caps' | translate }}</h2>
        @if (caps().length > 0) {
          <span class="text-xs text-gray-500">{{
            'customers.rules.summary' | translate: { count: caps().length, reached: reachedCount() }
          }}</span>
        }
      </div>
      @if (capsError()) {
        <p class="text-danger-fg text-sm">{{ 'customers.loadError' | translate }}</p>
      } @else if (!capsLoading() && caps().length === 0) {
        <p class="text-sm text-gray-500">{{ 'customers.rules.noCaps' | translate }}</p>
      } @else {
        <div
          class="-mx-1 flex snap-x snap-mandatory gap-4 overflow-x-auto px-1 pt-1 pb-3"
          role="list"
          tabindex="0"
          [attr.aria-label]="'customers.rules.caps' | translate"
          [attr.aria-busy]="capsLoading() ? 'true' : null"
        >
          @for (tile of tiles(); track tile.cap.ruleId) {
            <article
              role="listitem"
              class="w-80 shrink-0 snap-start rounded-xl border border-t-[3px] border-gray-100 bg-white p-4 shadow-sm"
              [class.border-t-warn-fg]="tile.reached"
              [class.border-t-brand]="!tile.reached"
            >
              <header class="flex items-start justify-between gap-2">
                <div class="min-w-0">
                  <h3
                    class="truncate text-sm font-semibold text-gray-900"
                    [title]="tile.cap.ruleName"
                  >
                    {{ tile.cap.ruleName }}
                  </h3>
                  <p class="truncate text-xs text-gray-500">{{ tile.cap.programName }}</p>
                </div>
                <app-status-pill [tone]="tile.reached ? 'warning' : 'success'" [dot]="true">{{
                  (tile.reached ? 'customers.rules.reached' : 'customers.rules.earning') | translate
                }}</app-status-pill>
              </header>
              <div class="mt-4 flex items-center gap-4">
                <div
                  class="flex h-16 w-16 shrink-0 items-center justify-center rounded-full"
                  role="img"
                  [attr.aria-label]="
                    'customers.rules.tightest' | translate: { percent: tile.percent }
                  "
                  [style.background]="ringBackground(tile)"
                >
                  <span
                    class="flex h-12 w-12 items-center justify-center rounded-full bg-white text-sm font-semibold text-gray-900"
                    >{{ tile.percent }}%</span
                  >
                </div>
                <dl class="min-w-0 flex-1 space-y-1.5 text-xs">
                  @for (w of tile.windows; track w.key) {
                    <div class="flex justify-between gap-2">
                      <dt class="text-gray-500">
                        {{ w.labelKey | translate: { period: tile.cap.period } }}
                      </dt>
                      @if (w.cap) {
                        <dd class="font-medium text-gray-900 tabular-nums">
                          {{ w.used }} / {{ w.cap }}
                        </dd>
                      } @else {
                        <dd class="text-gray-400">{{ 'customers.rules.noCap' | translate }}</dd>
                      }
                    </div>
                  }
                </dl>
              </div>
            </article>
          }
        </div>
        <p class="mt-1 text-xs text-gray-400">
          {{ 'customers.rules.capsNote' | translate }}
          {{ 'customers.rules.bucketsElsewhere' | translate }}
        </p>
      }
    </section>

    <!-- Option B: filters and results in one card. -->
    <section class="card overflow-hidden !p-0">
      <div class="p-5">
        <app-filter-bar
          [formGroup]="form"
          [label]="'customers.filter.rulesLabel' | translate"
          [searchLabel]="'customers.filter.search' | translate"
          [resetLabel]="'customers.filter.reset' | translate"
          [busy]="firesLoading()"
          (searched)="search()"
          (cleared)="reset()"
        >
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
        <p class="mt-2 text-xs text-gray-400">{{ 'customers.filter.localTime' | translate }}</p>
        @if (ruleFilter(); as rule) {
          <p class="mt-2 text-xs text-gray-600">
            {{ 'customers.rules.onlyRule' | translate: { name: rule.name } }}
            <button
              type="button"
              class="text-brand ml-2 cursor-pointer underline"
              (click)="clearRule()"
            >
              {{ 'customers.rules.allRules' | translate }}
            </button>
          </p>
        }
      </div>
      <div class="border-t border-gray-100">
        @if (firesError()) {
          <p class="text-danger-fg p-5 text-sm">{{ 'customers.loadError' | translate }}</p>
        } @else if (!firesLoading() && fires().length === 0) {
          <div class="p-5">
            <app-empty-state [heading]="'customers.rules.noFires' | translate" />
          </div>
        } @else {
          <div class="overflow-x-auto">
            <table class="w-full text-sm" [attr.aria-busy]="firesLoading() ? 'true' : null">
              <thead>
                <tr>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.col.date' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.col.rule' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-right">
                    {{ 'customers.col.amount' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.col.eventType' | translate }}
                  </th>
                  <th class="section-label px-4 py-3 text-left">
                    {{ 'customers.col.details' | translate }}
                  </th>
                  <th class="px-4 py-3"></th>
                </tr>
              </thead>
              <tbody>
                @for (fire of fires(); track fire.id) {
                  <tr class="border-t border-gray-100 align-top hover:bg-gray-50">
                    <td class="px-4 py-3 whitespace-nowrap text-gray-500">
                      {{ fire.createdAt | date: 'medium' }}
                    </td>
                    <td class="px-4 py-3">
                      <button
                        type="button"
                        class="text-brand cursor-pointer text-left hover:underline"
                        (click)="filterRule(fire.ruleId, fire.ruleName ?? fire.ruleId)"
                      >
                        {{
                          'customers.source.rule'
                            | translate
                              : { name: fire.ruleName ?? fire.ruleId, version: fire.ruleVersion }
                        }}
                      </button>
                    </td>
                    <td class="px-4 py-3 text-right font-medium">{{ fire.resultingDelta }}</td>
                    <td class="px-4 py-3 font-mono text-xs text-gray-500">
                      {{ fire.eventType ?? ('customers.activity.scheduledJob' | translate) }}
                    </td>
                    <td class="px-4 py-3 text-xs">
                      <details>
                        <summary class="cursor-pointer text-gray-500">
                          {{ 'customers.rules.snapshots' | translate }}
                        </summary>
                        @if (fire.conditionsSnapshot) {
                          <p class="mt-2 text-gray-500">
                            {{ 'customers.drawer.conditions' | translate }}
                          </p>
                          <pre class="overflow-auto rounded bg-gray-50 p-2 font-mono">{{
                            fire.conditionsSnapshot
                          }}</pre>
                        }
                        <p class="mt-2 text-gray-500">
                          {{ 'customers.drawer.calculation' | translate }}
                        </p>
                        <pre class="overflow-auto rounded bg-gray-50 p-2 font-mono">{{
                          fire.calculationSnapshot
                        }}</pre>
                        @if (fire.resolutionSnapshot) {
                          <p class="mt-2 text-gray-500">
                            {{ 'customers.drawer.resolution' | translate }}
                          </p>
                          <pre class="overflow-auto rounded bg-gray-50 p-2 font-mono">{{
                            fire.resolutionSnapshot
                          }}</pre>
                        }
                      </details>
                    </td>
                    <td class="px-4 py-3 text-right">
                      <app-customer-event-button
                        (opened)="
                          openEvent.emit({
                            eventId: fire.sourceEventId,
                            postingId: fire.ledgerEntryId ?? undefined,
                          })
                        "
                      />
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
export class CustomerRulesTab implements OnInit {
  readonly contactKey = input.required<string>();
  readonly openEvent = output<OpenEventRequest>();

  private readonly customers = inject(CustomersService);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly form = this.fb.group(defaultRuleFireFilter());
  protected readonly ruleFilter = signal<{ id: string; name: string } | null>(null);

  protected readonly caps = signal<readonly RuleCapUsage[]>([]);
  /** Closest to its limit first, so a reached cap is never off-screen. */
  protected readonly tiles = computed(() =>
    this.caps()
      .map(capTile)
      .sort((a, b) => b.percent - a.percent),
  );
  protected readonly reachedCount = computed(() => this.tiles().filter((t) => t.reached).length);
  protected readonly capsLoading = signal(true);
  protected readonly capsError = signal(false);

  protected readonly fires = signal<readonly CustomerRuleFire[]>([]);
  protected readonly pager = new CursorPager();
  protected readonly pageSizes = CUSTOMER_PAGE_SIZES;
  protected readonly firesLoading = signal(false);
  protected readonly firesError = signal(false);

  ngOnInit(): void {
    void this.customers
      .getCapUsage(this.contactKey())
      .then((caps) => this.caps.set(caps.filter((c) => !c.isCardBucket)))
      .catch(() => this.capsError.set(true))
      .finally(() => this.capsLoading.set(false));
    this.restart();
  }

  protected ringBackground(tile: CapTile): string {
    const color = tile.reached ? 'var(--color-warn-fg)' : 'var(--color-brand)';
    return `conic-gradient(${color} ${tile.percent}%, var(--color-gray-100) 0)`;
  }

  protected search(): void {
    this.restart();
  }

  protected reset(): void {
    this.form.reset(defaultRuleFireFilter());
    this.ruleFilter.set(null);
    this.restart();
  }

  protected filterRule(id: string, name: string): void {
    this.ruleFilter.set({ id, name });
    this.restart();
  }

  protected clearRule(): void {
    this.ruleFilter.set(null);
    this.restart();
  }

  protected go(page: number): void {
    if (!this.firesLoading()) void this.fetch(page);
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
    this.firesLoading.set(true);
    this.firesError.set(false);
    try {
      const query = toRuleFireQuery(this.form.getRawValue(), this.ruleFilter()?.id);
      const result = await this.customers.getRuleFires(
        this.contactKey(),
        query,
        cursor,
        this.pager.pageSize(),
      );
      this.fires.set(result.data);
      this.pager.record(page, result);
    } catch {
      this.firesError.set(true);
    } finally {
      this.firesLoading.set(false);
    }
  }
}
