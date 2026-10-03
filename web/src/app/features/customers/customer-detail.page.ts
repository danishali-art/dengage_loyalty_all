import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { PageHeader } from '../../shared/ui/page-header';
import { StatusPill } from '../../shared/ui/status-pill';
import { EmptyState } from '../../shared/ui/empty-state';
import { FilterBar } from '../../shared/ui/filter-bar';
import { IconButton } from '../../shared/ui/icon-button';
import { Paginator } from '../../shared/ui/paginator';
import { DialogService } from '../../core/ui/dialog.service';
import { EventTypesService } from '../../core/events/event-types.service';
import { CustomersService } from './customers.service';
import { CUSTOMER_PAGE_SIZES, CursorPager } from './customer-paging';
import { CustomerEventButton } from './customer-event-button';
import { CustomerOverviewTab } from './customer-overview.tab';
import { CustomerRulesTab } from './customer-rules.tab';
import { CustomerStreaksTab } from './customer-streaks.tab';
import { CustomerRewardsTab } from './customer-rewards.tab';
import { CustomerTiersTab } from './customer-tiers.tab';
import { CustomerCardBucketsTab } from './customer-card-buckets.tab';
import { CustomerMessagesTab } from './customer-messages.tab';
import {
  CustomerEvent,
  CustomerProfile,
  INBOX_STATUSES,
  LedgerEntry,
  REASON_GROUPS,
} from './customer.model';
import {
  defaultActivityFilter,
  defaultEventFilter,
  isDebit,
  programOptions,
  toEventQuery,
  toLedgerQuery,
  walletOptions,
} from './customer-filters';
import {
  CustomerEventDrawer,
  CustomerEventDrawerData,
  inboxStatusTone,
} from './customer-event.drawer';

type Tab =
  | 'overview'
  | 'activity'
  | 'events'
  | 'rules'
  | 'streaks'
  | 'rewards'
  | 'tiers'
  | 'cardBuckets'
  | 'messages';
const TABS: readonly Tab[] = [
  'overview',
  'activity',
  'events',
  'rules',
  'streaks',
  'rewards',
  'tiers',
  'cardBuckets',
  'messages',
];

/**
 * CR 2026-10-02 (Customer 360): header + tabs. P1: Activity is every posting with where it came
 * from; Events is what the customer sent (received after the CR). P2: Overview per program, Rules
 * & caps, Streaks, Rewards, Tiers; P3: Card buckets, Messages sent (each its own tab component).
 * The eye icon opens the drawer.
 */
@Component({
  selector: 'app-customer-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PageHeader,
    StatusPill,
    EmptyState,
    FilterBar,
    IconButton,
    Paginator,
    DatePipe,
    ReactiveFormsModule,
    TranslatePipe,
    CustomerEventButton,
    CustomerOverviewTab,
    CustomerRulesTab,
    CustomerStreaksTab,
    CustomerRewardsTab,
    CustomerTiersTab,
    CustomerCardBucketsTab,
    CustomerMessagesTab,
  ],
  template: `
    <app-page-header
      [heading]="contactKey()"
      [subtitle]="'customers.detail.subtitle' | translate"
      [crumbs]="[
        { label: 'nav.customers' | translate, link: ['/customers'] },
        { label: contactKey() },
      ]"
    >
      <div actions>
        <app-icon-button
          [ariaLabel]="'customers.detail.copyKey' | translate"
          (click)="copyContactKey()"
        >
          <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true">
            <path
              d="M7 3a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2H7Zm-4 4a1 1 0 0 1 1 1v8a1 1 0 0 0 1 1h6a1 1 0 1 1 0 2H5a3 3 0 0 1-3-3V8a1 1 0 0 1 1-1Z"
            />
          </svg>
        </app-icon-button>
      </div>
    </app-page-header>

    <div class="mx-auto max-w-7xl space-y-6 px-8 py-6">
      @if (loading()) {
        <p class="text-sm text-gray-500" aria-busy="true">{{ 'common.loading' | translate }}</p>
      } @else if (!profile()) {
        <app-empty-state
          [heading]="'customers.detail.notFound' | translate"
          [description]="'customers.detail.notFoundBody' | translate"
          icon="🔍"
        />
      } @else {
        <div class="flex gap-1 border-b border-gray-200" role="tablist">
          @for (t of tabs; track t) {
            <button
              type="button"
              role="tab"
              [attr.aria-selected]="tab() === t"
              class="-mb-px cursor-pointer border-b-2 px-4 py-2 text-sm transition-colors"
              [class]="
                tab() === t
                  ? 'border-brand text-brand font-medium'
                  : 'border-transparent text-gray-500 hover:text-gray-700'
              "
              (click)="selectTab(t)"
            >
              {{ 'customers.tab.' + t | translate }}
            </button>
          }
        </div>

        @switch (tab()) {
          @case ('overview') {
            <app-customer-overview-tab [profile]="profile()!" />
          }

          @case ('activity') {
            <!-- Option B: filters and results in one card. -->
            <section class="card overflow-hidden !p-0">
              <div class="p-5">
                <app-filter-bar
                  [formGroup]="activityForm"
                  [label]="'customers.filter.activityLabel' | translate"
                  [searchLabel]="'customers.filter.search' | translate"
                  [resetLabel]="'customers.filter.reset' | translate"
                  [busy]="ledgerLoading()"
                  (searched)="searchActivity()"
                  (cleared)="resetActivity()"
                >
                  <label
                    class="flex min-w-0 min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-gray-500"
                  >
                    {{ 'customers.filter.program' | translate }}
                    <select class="field-input !py-2" formControlName="programId">
                      <option value="">{{ 'customers.filter.all' | translate }}</option>
                      @for (p of programs(); track p.id) {
                        <option [value]="p.id">{{ p.name }}</option>
                      }
                    </select>
                  </label>
                  <label
                    class="flex min-w-0 min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-gray-500"
                  >
                    {{ 'customers.filter.wallet' | translate }}
                    <select class="field-input !py-2" formControlName="accountTypeId">
                      <option value="">{{ 'customers.filter.all' | translate }}</option>
                      @for (w of wallets(); track w.accountTypeId) {
                        <option [value]="w.accountTypeId">{{ w.accountTypeName }}</option>
                      }
                    </select>
                  </label>
                  <label
                    class="flex min-w-0 min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-gray-500"
                  >
                    {{ 'customers.filter.reasonGroup' | translate }}
                    <select class="field-input !py-2" formControlName="reasonGroup">
                      <option value="">{{ 'customers.filter.all' | translate }}</option>
                      @for (g of reasonGroups; track g) {
                        <option [value]="g">{{ 'customers.reasonGroup.' + g | translate }}</option>
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
                  <label
                    class="flex min-w-0 min-w-[9rem] flex-[1.2] flex-col gap-1 text-xs text-gray-500"
                  >
                    {{ 'customers.filter.eventId' | translate }}
                    <input
                      class="field-input !py-2 font-mono"
                      type="text"
                      formControlName="eventId"
                    />
                  </label>
                </app-filter-bar>
                <p class="mt-2 text-xs text-gray-400">
                  {{ 'customers.filter.localTime' | translate }}
                </p>
              </div>
              <div class="border-t border-gray-100">
                @if (ledgerError()) {
                  <p class="text-danger-fg p-5 text-sm">{{ ledgerError()! | translate }}</p>
                } @else if (!ledgerLoading() && ledger().length === 0) {
                  <div class="p-5">
                    <app-empty-state
                      [heading]="'customers.activity.empty' | translate"
                      [description]="'customers.activity.emptyBody' | translate"
                    />
                  </div>
                } @else {
                  <div class="overflow-x-auto">
                    <table
                      class="w-full text-sm"
                      [attr.aria-busy]="ledgerLoading() ? 'true' : null"
                    >
                      <thead>
                        <tr>
                          <th class="section-label px-4 py-3 text-left">
                            {{ 'customers.col.date' | translate }}
                          </th>
                          <th class="section-label px-4 py-3 text-left">
                            {{ 'customers.col.wallet' | translate }}
                          </th>
                          <th class="section-label px-4 py-3 text-left">
                            {{ 'customers.col.reason' | translate }}
                          </th>
                          <th class="section-label px-4 py-3 text-right">
                            {{ 'customers.col.amount' | translate }}
                          </th>
                          <th class="section-label px-4 py-3 text-left">
                            {{ 'customers.col.source' | translate }}
                          </th>
                          <th class="section-label px-4 py-3 text-left">
                            {{ 'customers.col.eventType' | translate }}
                          </th>
                          <th class="px-4 py-3"></th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (entry of ledger(); track entry.id) {
                          <tr class="border-t border-gray-100 hover:bg-gray-50">
                            <td class="px-4 py-3 whitespace-nowrap text-gray-500">
                              {{ entry.createdAt | date: 'medium' }}
                            </td>
                            <td class="px-4 py-3">
                              {{ entry.accountTypeName }}
                              <div class="text-xs text-gray-400">
                                {{ entry.accountTypeType }} · {{ entry.programName }}
                              </div>
                            </td>
                            <td class="px-4 py-3 text-gray-700">
                              {{ 'customers.reason.' + entry.reason | translate }}
                            </td>
                            <td
                              class="px-4 py-3 text-right font-medium whitespace-nowrap"
                              [class]="debit(entry.delta) ? 'text-danger-fg' : 'text-success-fg'"
                            >
                              {{ debit(entry.delta) ? '' : '+' }}{{ entry.delta }}
                            </td>
                            <td class="px-4 py-3 text-xs text-gray-600">
                              @if (entry.ruleName) {
                                {{
                                  'customers.source.rule'
                                    | translate
                                      : { name: entry.ruleName, version: entry.ruleVersion ?? '—' }
                                }}
                              } @else if (entry.campaignName) {
                                {{
                                  'customers.source.streak'
                                    | translate: { name: entry.campaignName }
                                }}
                              } @else {
                                —
                              }
                            </td>
                            <td class="px-4 py-3 font-mono text-xs text-gray-500">
                              {{
                                entry.eventType ?? ('customers.activity.scheduledJob' | translate)
                              }}
                            </td>
                            <td class="px-4 py-3 text-right">
                              <app-customer-event-button
                                (opened)="openEvent(entry.sourceEventId, entry.id)"
                              />
                            </td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                  <div class="px-4">
                    <app-paginator
                      [page]="ledgerPager.page()"
                      [pageSize]="ledgerPager.pageSize()"
                      [total]="ledgerPager.total()"
                      (pageChange)="goLedger($event)"
                      [pageSizeOptions]="pageSizes"
                      [pageSizeLabel]="'customers.paging.pageView' | translate"
                      (pageSizeChange)="changeLedgerPageSize($event)"
                    />
                  </div>
                }
              </div>
            </section>
          }

          @case ('events') {
            <!-- Option B: filters and results in one card. -->
            <section class="card overflow-hidden !p-0">
              <div class="p-5">
                <app-filter-bar
                  [formGroup]="eventForm"
                  [label]="'customers.filter.eventsLabel' | translate"
                  [searchLabel]="'customers.filter.search' | translate"
                  [resetLabel]="'customers.filter.reset' | translate"
                  [busy]="eventsLoading()"
                  (searched)="searchEvents()"
                  (cleared)="resetEvents()"
                >
                  <label
                    class="flex min-w-0 min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-gray-500"
                  >
                    {{ 'customers.filter.eventType' | translate }}
                    <select class="field-input !py-2" formControlName="eventType">
                      <option value="">{{ 'customers.filter.all' | translate }}</option>
                      @for (t of eventTypes(); track t) {
                        <option [value]="t">{{ t }}</option>
                      }
                    </select>
                  </label>
                  <label
                    class="flex min-w-0 min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-gray-500"
                  >
                    {{ 'customers.filter.status' | translate }}
                    <select class="field-input !py-2" formControlName="status">
                      <option value="">{{ 'customers.filter.all' | translate }}</option>
                      @for (s of statuses; track s) {
                        <option [value]="s">{{ 'customers.status.' + s | translate }}</option>
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
                  {{ 'customers.events.sinceDeploy' | translate }}
                </p>
              </div>
              <div class="border-t border-gray-100">
                @if (eventsError()) {
                  <p class="text-danger-fg p-5 text-sm">{{ eventsError()! | translate }}</p>
                } @else if (!eventsLoading() && events().length === 0) {
                  <div class="p-5">
                    <app-empty-state
                      [heading]="'customers.events.empty' | translate"
                      [description]="'customers.events.emptyBody' | translate"
                    />
                  </div>
                } @else {
                  <div class="overflow-x-auto">
                    <table
                      class="w-full text-sm"
                      [attr.aria-busy]="eventsLoading() ? 'true' : null"
                    >
                      <thead>
                        <tr>
                          <th class="section-label px-4 py-3 text-left">
                            {{ 'customers.col.received' | translate }}
                          </th>
                          <th class="section-label px-4 py-3 text-left">
                            {{ 'customers.col.eventType' | translate }}
                          </th>
                          <th class="section-label px-4 py-3 text-left">
                            {{ 'customers.col.status' | translate }}
                          </th>
                          <th class="section-label px-4 py-3 text-left">
                            {{ 'customers.col.outcome' | translate }}
                          </th>
                          <th class="px-4 py-3"></th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (event of events(); track event.eventId) {
                          <tr class="border-t border-gray-100 hover:bg-gray-50">
                            <td class="px-4 py-3 whitespace-nowrap text-gray-500">
                              {{ event.receivedAt | date: 'medium' }}
                            </td>
                            <td class="px-4 py-3 font-mono text-xs text-gray-700">
                              {{ event.eventType }}
                            </td>
                            <td class="px-4 py-3">
                              <app-status-pill [tone]="statusTone(event.status)" [dot]="true">{{
                                'customers.status.' + event.status | translate
                              }}</app-status-pill>
                              @if (event.error) {
                                <div
                                  class="text-danger-fg mt-1 max-w-xs truncate font-mono text-xs"
                                  [title]="event.error"
                                >
                                  {{ event.error }}
                                </div>
                              }
                            </td>
                            <td class="px-4 py-3 text-xs text-gray-600">
                              @for (o of event.outcome; track o.accountTypeId) {
                                <div>
                                  {{ o.accountTypeName }}:
                                  <span
                                    [class]="
                                      debit(o.netDelta) ? 'text-danger-fg' : 'text-success-fg'
                                    "
                                    >{{ debit(o.netDelta) ? '' : '+' }}{{ o.netDelta }}</span
                                  >
                                </div>
                              } @empty {
                                <span class="text-gray-400">{{
                                  'customers.events.noPostings' | translate
                                }}</span>
                              }
                            </td>
                            <td class="px-4 py-3 text-right">
                              <app-customer-event-button (opened)="openEvent(event.eventId)" />
                            </td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                  <div class="px-4">
                    <app-paginator
                      [page]="eventsPager.page()"
                      [pageSize]="eventsPager.pageSize()"
                      [total]="eventsPager.total()"
                      (pageChange)="goEvents($event)"
                      [pageSizeOptions]="pageSizes"
                      [pageSizeLabel]="'customers.paging.pageView' | translate"
                      (pageSizeChange)="changeEventsPageSize($event)"
                    />
                  </div>
                }
              </div>
            </section>
          }
          @case ('rules') {
            <app-customer-rules-tab
              [contactKey]="contactKey()"
              (openEvent)="openEvent($event.eventId, $event.postingId)"
            />
          }
          @case ('streaks') {
            <app-customer-streaks-tab [contactKey]="contactKey()" />
          }
          @case ('rewards') {
            <app-customer-rewards-tab
              [contactKey]="contactKey()"
              (openEvent)="openEvent($event.eventId, $event.postingId)"
            />
          }
          @case ('tiers') {
            <app-customer-tiers-tab
              [contactKey]="contactKey()"
              [profile]="profile()!"
              (openEvent)="openEvent($event.eventId, $event.postingId)"
            />
          }
          @case ('cardBuckets') {
            <app-customer-card-buckets-tab
              [contactKey]="contactKey()"
              (openEvent)="openEvent($event.eventId, $event.postingId)"
            />
          }
          @case ('messages') {
            <app-customer-messages-tab [contactKey]="contactKey()" />
          }
        }
      }
    </div>
  `,
})
export class CustomerDetailPage implements OnInit {
  readonly contactKey = input.required<string>();

  private readonly customers = inject(CustomersService);
  private readonly eventTypesService = inject(EventTypesService);
  private readonly dialog = inject(DialogService);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly tabs = TABS;
  protected readonly pageSizes = CUSTOMER_PAGE_SIZES;
  protected readonly reasonGroups = REASON_GROUPS;
  protected readonly statuses = INBOX_STATUSES;
  protected readonly debit = isDebit;
  protected readonly statusTone = inboxStatusTone;

  protected readonly tab = signal<Tab>('overview');
  protected readonly loading = signal(true);
  protected readonly profile = signal<CustomerProfile | null>(null);
  protected readonly eventTypes = signal<readonly string[]>([]);

  protected readonly activityForm = this.fb.group(defaultActivityFilter());
  protected readonly ledger = signal<readonly LedgerEntry[]>([]);
  protected readonly ledgerPager = new CursorPager();
  protected readonly ledgerLoading = signal(false);
  protected readonly ledgerError = signal<string | null>(null);
  private ledgerLoaded = false;

  protected readonly eventForm = this.fb.group(defaultEventFilter());
  protected readonly events = signal<readonly CustomerEvent[]>([]);
  protected readonly eventsPager = new CursorPager();
  protected readonly eventsLoading = signal(false);
  protected readonly eventsError = signal<string | null>(null);
  private eventsLoaded = false;

  protected readonly programs = computed(() => programOptions(this.profile()?.balances ?? []));
  private readonly selectedProgram = toSignal(this.activityForm.controls.programId.valueChanges, {
    initialValue: '',
  });
  protected readonly wallets = computed(() =>
    walletOptions(this.profile()?.balances ?? [], this.selectedProgram()),
  );

  ngOnInit(): void {
    void this.load();
    void this.eventTypesService
      .get()
      .then((c) => this.eventTypes.set([...c.builtIn, ...c.generic]))
      .catch(() => this.eventTypes.set([]));
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const key = this.contactKey();
      this.profile.set(await this.customers.getProfile(key).catch(() => null));
    } finally {
      this.loading.set(false);
    }
  }

  protected selectTab(tab: Tab): void {
    this.tab.set(tab);
    if (tab === 'activity' && !this.ledgerLoaded) void this.searchActivity();
    if (tab === 'events' && !this.eventsLoaded) void this.searchEvents();
  }

  // ── Activity ──

  protected async searchActivity(): Promise<void> {
    this.ledgerLoaded = true;
    this.ledgerPager.reset();
    await this.fetchLedger(1);
  }

  protected resetActivity(): void {
    this.activityForm.reset(defaultActivityFilter());
    void this.searchActivity();
  }

  protected goLedger(page: number): void {
    if (!this.ledgerLoading()) void this.fetchLedger(page);
  }

  protected changeLedgerPageSize(size: number): void {
    this.ledgerPager.setPageSize(size);
    void this.fetchLedger(1);
  }

  private async fetchLedger(page: number): Promise<void> {
    const cursor = this.ledgerPager.cursorFor(page);
    if (cursor === undefined) return;
    this.ledgerLoading.set(true);
    this.ledgerError.set(null);
    try {
      const value = this.activityForm.getRawValue();
      const result = await this.customers.getLedger(
        this.contactKey(),
        toLedgerQuery(value),
        cursor,
        this.ledgerPager.pageSize(),
      );
      this.ledger.set(result.data);
      this.ledgerPager.record(page, result);
    } catch {
      this.ledgerError.set('customers.loadError');
    } finally {
      this.ledgerLoading.set(false);
    }
  }

  // ── Events ──

  protected async searchEvents(): Promise<void> {
    this.eventsLoaded = true;
    this.eventsPager.reset();
    await this.fetchEvents(1);
  }

  protected resetEvents(): void {
    this.eventForm.reset(defaultEventFilter());
    void this.searchEvents();
  }

  protected goEvents(page: number): void {
    if (!this.eventsLoading()) void this.fetchEvents(page);
  }

  protected changeEventsPageSize(size: number): void {
    this.eventsPager.setPageSize(size);
    void this.fetchEvents(1);
  }

  private async fetchEvents(page: number): Promise<void> {
    const cursor = this.eventsPager.cursorFor(page);
    if (cursor === undefined) return;
    this.eventsLoading.set(true);
    this.eventsError.set(null);
    try {
      const value = this.eventForm.getRawValue();
      const result = await this.customers.getEvents(
        this.contactKey(),
        toEventQuery(value),
        cursor,
        this.eventsPager.pageSize(),
      );
      this.events.set(result.data);
      this.eventsPager.record(page, result);
    } catch {
      this.eventsError.set('customers.loadError');
    } finally {
      this.eventsLoading.set(false);
    }
  }

  // ── Drawer ──

  protected openEvent(eventId: string, postingId?: string): void {
    this.dialog.openSidePanel<void, CustomerEventDrawerData, CustomerEventDrawer>(
      CustomerEventDrawer,
      {
        data: { contactKey: this.contactKey(), eventId, postingId },
      },
    );
  }

  protected copyContactKey(): void {
    void navigator.clipboard?.writeText(this.contactKey()).catch(() => undefined);
  }
}
