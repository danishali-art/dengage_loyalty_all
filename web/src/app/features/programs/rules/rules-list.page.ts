import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { PageHeader } from '../../../shared/ui/page-header';
import { DataTable, Column } from '../../../shared/ui/data-table';
import { StatusPill, StatusTone } from '../../../shared/ui/status-pill';
import { Button } from '../../../shared/ui/button';
import { IconButton } from '../../../shared/ui/icon-button';
import { EmptyState } from '../../../shared/ui/empty-state';
import { ConfirmService } from '../../../core/ui/confirm.service';
import { ToastService } from '../../../core/ui/toast.service';
import { ApiError } from '../../../core/http/api-error';
import { ProgramContextStore } from '../../../core/program/program-context.store';
import { AccountTypesService } from '../account-types/account-types.service';
import { AccountType } from '../account-types/account-type.model';
import { RulesService } from './rules.service';
import { Rule, RuleStatus } from './rule.model';
import { groupRulesByApplication, rankExclusiveRules } from './rule-grouping';

/**
 * Rules grouped the way the engine applies them (1.3.CL item 6 addendum): per trigger event and
 * target account, exclusive rules (highest priority wins) apart from stackable ones (added on
 * top), with Transfer/Reversal — which skip winner selection — listed on their own. Loads the
 * whole program's rules: a group split across pages would misrepresent what competes.
 */
@Component({
  selector: 'app-rules-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    RouterLink,
    TranslatePipe,
    PageHeader,
    DataTable,
    StatusPill,
    Button,
    IconButton,
    EmptyState,
  ],
  template: `
    <app-page-header
      heading="Rules"
      [crumbs]="[
        { label: 'Programs', link: ['/programs'] },
        { label: programName(), link: ['/programs', programId()] },
        { label: 'Rules' },
      ]"
    >
      <div actions>
        <app-button (click)="create()">
          <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true">
            <path
              d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z"
            />
          </svg>
          New rule
        </app-button>
      </div>
    </app-page-header>

    <!-- Shared cells for every group's table. Inactive rules are dimmed: they aren't competing. -->
    <ng-template #rowTpl let-rule let-rank="rank">
      <td
        class="cursor-pointer px-4 py-3.5 font-medium text-gray-800"
        [class.opacity-60]="rule.status !== 'active'"
        [routerLink]="['/programs', programId(), 'rules', rule.id]"
      >
        {{ rule.name }}
        <!-- Exclusive tables only: where this rule stands in the one-winner-per-account race. -->
        @switch (rank) {
          @case ('top') {
            <app-status-pill tone="success" class="ml-2">{{
              'rules.groups.rank.top' | translate
            }}</app-status-pill>
          }
          @case ('tie') {
            <app-status-pill tone="warning" class="ml-2">{{
              'rules.groups.rank.tie' | translate
            }}</app-status-pill>
          }
          @case ('fallback') {
            <span class="ml-2 text-xs font-normal text-gray-500">{{
              'rules.groups.rank.fallback' | translate
            }}</span>
          }
        }
      </td>
      <td class="px-4 py-3.5 text-xs text-gray-500" [class.opacity-60]="rule.status !== 'active'">
        {{ rule.type }}
      </td>
      <td class="px-4 py-3.5 text-xs text-gray-500" [class.opacity-60]="rule.status !== 'active'">
        {{ rule.priority }}
      </td>
      <td class="px-4 py-3.5">
        <app-status-pill [tone]="statusTone(rule.status)" [dot]="true">
          {{ rule.status === 'pending_approval' ? 'pending approval' : rule.status }}
        </app-status-pill>
      </td>
      <td class="px-4 py-3.5 text-right">
        @if (rule.status === 'pending_approval') {
          <app-button size="sm" variant="secondary" (click)="approve(rule)">Approve</app-button>
        } @else {
          <app-icon-button
            [ariaLabel]="rule.status === 'active' ? 'Disable rule' : 'Activate rule'"
            (click)="toggleStatus(rule)"
          >
            {{ rule.status === 'active' ? '⏸' : '▶' }}
          </app-icon-button>
        }
        <app-icon-button ariaLabel="Delete rule" (click)="remove(rule)">🗑</app-icon-button>
      </td>
    </ng-template>

    <div class="mx-auto max-w-7xl space-y-6 px-8 py-6">
      @if (loading() && rules().length === 0) {
        <div class="card !p-0">
          <app-data-table [columns]="columns" [rows]="[]" caption="Rules" [busy]="true" />
        </div>
      } @else if (rules().length === 0) {
        <div class="card">
          <app-empty-state
            heading="No rules yet"
            description="Create one to start earning points."
            icon="⚙"
          />
        </div>
      } @else {
        <p class="text-xs text-gray-500">{{ 'rules.groups.intro' | translate }}</p>

        @for (group of grouped().groups; track group.key; let gi = $index) {
          <section
            class="card !p-0"
            [attr.aria-labelledby]="'rule-group-' + gi"
            [attr.aria-busy]="loading() ? 'true' : null"
          >
            <h2
              [id]="'rule-group-' + gi"
              class="section-heading flex flex-wrap items-center gap-2 px-4 pt-4"
            >
              <span class="font-mono text-sm">{{ group.trigger }}</span>
              <span aria-hidden="true" class="text-gray-400">→</span>
              <span class="sr-only">{{ 'rules.groups.onAccount' | translate }}</span>
              <span>{{ accountLabel(group.targetAccountTypeId) }}</span>
            </h2>

            @if (group.exclusive.length > 0) {
              <div class="flex items-center gap-2 px-4 pt-3">
                <app-status-pill tone="neutral">{{
                  'rules.stackable.no' | translate
                }}</app-status-pill>
                <span class="text-xs text-gray-500">{{
                  'rules.groups.exclusiveHint' | translate
                }}</span>
              </div>
              <app-data-table
                [columns]="columns"
                [rows]="group.exclusive"
                [caption]="
                  groupCaption(group.trigger, group.targetAccountTypeId, 'rules.stackable.no')
                "
                [trackKey]="trackById"
              >
                <ng-template #row let-rule>
                  <ng-container
                    [ngTemplateOutlet]="rowTpl"
                    [ngTemplateOutletContext]="{
                      $implicit: rule,
                      rank: exclusiveRanks().get(rule.id),
                    }"
                  />
                </ng-template>
              </app-data-table>
            }

            @if (group.stackable.length > 0) {
              <div class="flex items-center gap-2 px-4 pt-3">
                <app-status-pill tone="info">{{
                  'rules.stackable.yes' | translate
                }}</app-status-pill>
                <span class="text-xs text-gray-500">{{
                  'rules.groups.stackableHint' | translate
                }}</span>
              </div>
              <app-data-table
                [columns]="columns"
                [rows]="group.stackable"
                [caption]="
                  groupCaption(group.trigger, group.targetAccountTypeId, 'rules.stackable.yes')
                "
                [trackKey]="trackById"
              >
                <ng-template #row let-rule>
                  <ng-container
                    [ngTemplateOutlet]="rowTpl"
                    [ngTemplateOutletContext]="{ $implicit: rule }"
                  />
                </ng-template>
              </app-data-table>
            }
          </section>
        }

        @if (grouped().outsideWinnerSelection.length > 0) {
          <section class="card !p-0" aria-labelledby="rule-group-outside">
            <h2 id="rule-group-outside" class="section-heading px-4 pt-4">
              {{ 'rules.groups.outside' | translate }}
            </h2>
            <p class="px-4 pt-1 text-xs text-gray-500">
              {{ 'rules.groups.outsideHint' | translate }}
            </p>
            <app-data-table
              [columns]="outsideColumns"
              [rows]="grouped().outsideWinnerSelection"
              [caption]="'rules.groups.outside' | translate"
              [trackKey]="trackById"
            >
              <ng-template #row let-rule>
                <td class="px-4 py-3.5 font-mono text-xs text-gray-500">{{ rule.trigger }}</td>
                <ng-container
                  [ngTemplateOutlet]="rowTpl"
                  [ngTemplateOutletContext]="{ $implicit: rule }"
                />
              </ng-template>
            </app-data-table>
          </section>
        }
      }
    </div>
  `,
})
export class RulesListPage implements OnInit {
  readonly programId = input.required<string>();

  private readonly rulesService = inject(RulesService);
  private readonly accountTypesService = inject(AccountTypesService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly programContext = inject(ProgramContextStore);
  private readonly translate = inject(TranslateService);

  protected readonly rules = signal<readonly Rule[]>([]);
  protected readonly accountTypes = signal<readonly AccountType[]>([]);
  protected readonly loading = signal(true);
  protected readonly trackById = (r: Rule): string => r.id;
  protected readonly programName = (): string => this.programContext.program()?.name ?? 'Program';
  protected readonly statusTone = (status: RuleStatus): StatusTone =>
    status === 'active' ? 'success' : status === 'pending_approval' ? 'warning' : 'neutral';

  private readonly accountsById = computed(
    () => new Map(this.accountTypes().map((a) => [a.id, a])),
  );

  // CR 2026-10-05 (D8): the account-type list hides retired STAMP wallets, so a disabled rule
  // that targeted one has no entry here — label it instead of showing a bare id.
  protected readonly accountLabel = (accountTypeId: string): string => {
    const account = this.accountsById().get(accountTypeId);
    return account
      ? `${account.name} (${account.type})`
      : String(this.translate.instant('rules.groups.retiredAccount'));
  };

  /** Screen-reader caption for one sub-table, e.g. "order.created → Beans (POINTS): Exclusive". */
  protected readonly groupCaption = (
    trigger: string,
    accountTypeId: string,
    kindKey: string,
  ): string =>
    `${trigger} → ${this.accountLabel(accountTypeId)}: ${String(this.translate.instant(kindKey))}`;

  protected readonly grouped = computed(() =>
    groupRulesByApplication(this.rules(), (id) => this.accountsById().get(id)?.name ?? id),
  );

  /** Winner / tie / fallback for every exclusive rule, per trigger/account group. */
  protected readonly exclusiveRanks = computed(
    () => new Map(this.grouped().groups.flatMap((g) => [...rankExclusiveRules(g.exclusive)])),
  );

  protected readonly columns: Column<Rule>[] = [
    { key: 'name', header: 'Name' },
    { key: 'type', header: 'Type' },
    { key: 'priority', header: 'Priority' },
    { key: 'status', header: 'Status' },
    { key: 'actions', header: '' },
  ];

  protected readonly outsideColumns: Column<Rule>[] = [
    { key: 'trigger', header: String(this.translate.instant('rules.groups.trigger')) },
    ...this.columns,
  ];

  ngOnInit(): void {
    void this.reload();
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    try {
      const [rules, accountTypes] = await Promise.all([
        this.rulesService.listAll(this.programId()),
        this.accountTypesService.listAll(this.programId()),
      ]);
      this.rules.set(rules);
      this.accountTypes.set(accountTypes);
    } finally {
      this.loading.set(false);
    }
  }

  protected create(): void {
    void this.router.navigate(['/programs', this.programId(), 'rules', 'new']);
  }

  protected async toggleStatus(rule: Rule): Promise<void> {
    const next = rule.status === 'active' ? 'disabled' : 'active';
    await this.rulesService.setStatus(this.programId(), rule.id, next);
    this.toast.success(`Rule ${next === 'active' ? 'activated' : 'disabled'}`);
    await this.reload();
  }

  // CR-04: server enforces creator ≠ approver — a self-approval attempt surfaces as a normal
  // ApiError toast rather than a blocked button, since we don't know who created it without
  // fetching the full rule record.
  protected async approve(rule: Rule): Promise<void> {
    try {
      await this.rulesService.approve(this.programId(), rule.id);
      this.toast.success('Rule approved and activated');
      await this.reload();
    } catch (err) {
      this.toast.error(err instanceof ApiError ? err.message : 'Could not approve this rule.');
    }
  }

  protected async remove(rule: Rule): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Delete rule',
      message: `Delete "${rule.name}"? This cannot be undone.`,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    await this.rulesService.delete(this.programId(), rule.id);
    this.toast.success('Rule deleted');
    await this.reload();
  }
}
