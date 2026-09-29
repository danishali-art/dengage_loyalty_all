import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { PageHeader } from '../../../shared/ui/page-header';
import { Button } from '../../../shared/ui/button';
import { FormErrors } from '../../../shared/ui/form-errors';
import { SearchableSelect, SelectOption } from '../../../shared/ui/searchable-select';
import { ConditionsEditor } from '../../../shared/ui/conditions-editor';
import { ToastService } from '../../../core/ui/toast.service';
import { ApiError } from '../../../core/http/api-error';
import { EventTypesService } from '../../../core/events/event-types.service';
import { decimalString } from '../../../shared/money/decimal-string';
import { localInputToUtcIso, utcIsoToLocalInput } from '../../../shared/date/utc';
import { listIanaTimezones } from '../../../shared/date/timezones';
import { ConditionClause, validateConditions } from '../../../shared/forms/condition-dsl';
import { StreakCampaignsService } from './streak-campaigns.service';
import { AccountTypesService } from '../account-types/account-types.service';
import { AccountType } from '../account-types/account-type.model';
import { RewardsService } from '../rewards/rewards.service';
import { Reward } from '../rewards/reward.model';
import { CreateStreakCampaignRequest, StreakCampaign, StreakConfig } from './streak-campaign.model';
import { validateStreakConfig } from './streak-config';

const EVENT_TRIGGERS = [
  'order.created',
  'order.refunded',
  'cash.added',
  'cash.spent',
  'points.redeem',
  'points.transfer',
  'reward.purchase',
] as const;
const DEFAULT_TRIGGER: (typeof EVENT_TRIGGERS)[number] = 'order.created';

@Component({
  selector: 'app-streak-campaign-form-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PageHeader, Button, FormErrors, SearchableSelect, ConditionsEditor],
  template: `
    <app-page-header
      [heading]="isEdit() ? 'Edit streak campaign' : 'New streak campaign'"
      [crumbs]="[
        { label: 'Programs', link: ['/programs'] },
        { label: 'Streak Campaigns', link: ['/programs', programId(), 'streak-campaigns'] },
        { label: isEdit() ? 'Edit' : 'New' },
      ]"
    >
      <div actions>
        <app-button variant="secondary" [routerLink]="['/programs', programId(), 'streak-campaigns']">Cancel</app-button>
        <app-button [pending]="saving()" (click)="submit()">Save</app-button>
      </div>
    </app-page-header>

    <div class="mx-auto max-w-5xl space-y-6 px-8 py-6">
      <app-form-errors [messages]="formErrors()" />

      <section class="card space-y-4">
        <div class="section-header">
          <h2 class="section-heading">Basics</h2>
        </div>
        <form [formGroup]="form" class="space-y-4">
          <div>
            <label for="name" class="field-label">Name</label>
            <input id="name" formControlName="name" class="field-input" />
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <span id="trigger-label" class="field-label">Trigger event</span>
              <app-searchable-select formControlName="trigger" [options]="triggerOptions()" placeholder="Select a trigger…" ariaLabelledby="trigger-label" />
            </div>
            <div>
              <span id="targetAccountTypeId-label" class="field-label">Target account</span>
              <app-searchable-select formControlName="targetAccountTypeId" [options]="targetAccountOptions()" placeholder="Select an account…" ariaLabelledby="targetAccountTypeId-label" />
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label for="activeFrom" class="field-label">Active from</label>
              <input id="activeFrom" type="datetime-local" formControlName="activeFrom" class="field-input" />
            </div>
            <div>
              <label for="activeTo" class="field-label">Active to</label>
              <input id="activeTo" type="datetime-local" formControlName="activeTo" class="field-input" />
            </div>
          </div>
        </form>
      </section>

      <app-conditions-editor [conditions]="conditions()" (conditionsChange)="conditions.set($event)" />

      <section class="card space-y-4">
        <div class="section-header">
          <h2 class="section-heading">Streak</h2>
        </div>
        <form [formGroup]="streakForm" class="space-y-4">
          <div class="grid grid-cols-3 gap-3">
            <div>
              <label for="period" class="field-label">Period</label>
              <select id="period" formControlName="period" class="field-input">
                <option value="day">day</option>
                <option value="week">week</option>
                <option value="month">month</option>
              </select>
            </div>
            <div>
              <label for="targetPeriods" class="field-label">Target periods</label>
              <input id="targetPeriods" type="number" min="1" formControlName="targetPeriods" class="field-input" />
            </div>
            <div>
              <label for="weekStart" class="field-label">Week starts</label>
              <select id="weekStart" formControlName="weekStart" class="field-input">
                <option value="monday">Monday</option>
                <option value="sunday">Sunday</option>
              </select>
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label for="metric" class="field-label">Aggregate metric</label>
              <select id="metric" formControlName="metric" class="field-input">
                <option value="sum">sum</option>
                <option value="count">count</option>
              </select>
            </div>
            <div>
              <label for="threshold" class="field-label">Threshold per period</label>
              <input id="threshold" type="number" min="0" step="0.0001" formControlName="threshold" class="field-input" />
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label for="timezone" class="field-label">Timezone</label>
              <input id="timezone" formControlName="timezone" list="tz-list" class="field-input" />
              <datalist id="tz-list">
                @for (tz of timezones; track tz) {
                  <option [value]="tz"></option>
                }
              </datalist>
            </div>
            <div>
              <label for="onComplete" class="field-label">On complete</label>
              <select id="onComplete" formControlName="onComplete" class="field-input">
                <option value="restart">restart</option>
                <option value="stop">stop</option>
              </select>
            </div>
          </div>
          <div>
            <label for="rewardKind" class="field-label">Reward</label>
            <select id="rewardKind" formControlName="rewardKind" class="field-input">
              <option value="fixed_bonus">Fixed bonus (on target account)</option>
              <option value="reward_definition">Reward definition</option>
            </select>
          </div>
          @if (streakForm.controls.rewardKind.value === 'fixed_bonus') {
            <div>
              <label for="rewardAmount" class="field-label">Bonus amount</label>
              <input id="rewardAmount" type="number" min="0" step="0.0001" formControlName="rewardAmount" class="field-input" />
            </div>
          } @else {
            <div>
              <span id="rewardDefinitionId-label" class="field-label">Reward</span>
              <app-searchable-select formControlName="rewardDefinitionId" [options]="rewardDefinitionOptions()" placeholder="Select a reward…" ariaLabelledby="rewardDefinitionId-label" />
            </div>
          }
        </form>
      </section>
    </div>
  `,
})
export class StreakCampaignFormPage implements OnInit {
  readonly programId = input.required<string>();
  readonly campaignId = input<string | undefined>(undefined);

  private readonly campaignsService = inject(StreakCampaignsService);
  private readonly accountTypesService = inject(AccountTypesService);
  private readonly rewardsService = inject(RewardsService);
  private readonly eventTypesService = inject(EventTypesService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly eventTriggers = EVENT_TRIGGERS;
  protected readonly timezones = listIanaTimezones();

  // Seeded with the built-ins so the dropdown isn't empty before /events/types resolves;
  // load() replaces this with the deployment's real catalog, generic types included.
  protected readonly triggerOptions = signal<SelectOption<string>[]>(
    EVENT_TRIGGERS.map((t) => ({ value: t, label: t })),
  );

  protected readonly isEdit = signal(false);
  protected readonly saving = signal(false);
  protected readonly formErrors = signal<string[]>([]);
  protected readonly accountTypes = signal<readonly AccountType[]>([]);
  protected readonly rewards = signal<readonly Reward[]>([]);
  protected readonly conditions = signal<ConditionClause[]>([]);

  protected readonly targetAccountOptions = computed<SelectOption<string>[]>(() =>
    this.accountTypes().map((at) => ({ value: at.id, label: `${at.name} (${at.type})` })),
  );
  protected readonly rewardDefinitionOptions = computed<SelectOption<string>[]>(() =>
    this.rewards().map((r) => ({ value: r.id, label: r.displayName })),
  );

  protected readonly form = this.fb.group({
    name: this.fb.control('', [Validators.required, Validators.maxLength(255)]),
    trigger: this.fb.control<string>(DEFAULT_TRIGGER),
    targetAccountTypeId: this.fb.control(''),
    activeFrom: this.fb.control(''),
    activeTo: this.fb.control(''),
  });

  protected readonly streakForm = this.fb.group({
    period: this.fb.control<StreakConfig['period']>('week'),
    weekStart: this.fb.control<StreakConfig['week_start']>('monday'),
    targetPeriods: this.fb.control(4, [Validators.min(1)]),
    metric: this.fb.control<StreakConfig['aggregate']['metric']>('count'),
    threshold: this.fb.control(1),
    timezone: this.fb.control('UTC'),
    onComplete: this.fb.control<StreakConfig['on_complete']>('restart'),
    rewardKind: this.fb.control<StreakConfig['reward']['kind']>('fixed_bonus'),
    rewardAmount: this.fb.control<number | null>(10),
    rewardDefinitionId: this.fb.control(''),
  });

  ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    this.isEdit.set(!!this.campaignId());
    const [accountTypes, rewards, eventTypes] = await Promise.all([
      this.accountTypesService.listAll(this.programId()),
      this.rewardsService.listAll(this.programId()),
      this.eventTypesService.get().catch(() => null),
    ]);
    this.accountTypes.set(accountTypes);
    this.rewards.set(rewards);
    if (eventTypes) {
      this.triggerOptions.set(
        [...eventTypes.builtIn, ...eventTypes.generic].map((t) => ({ value: t, label: t })),
      );
    }
    const firstAccountType = accountTypes[0];
    if (firstAccountType) this.form.patchValue({ targetAccountTypeId: firstAccountType.id });

    const campaignId = this.campaignId();
    if (campaignId) {
      const campaign = await this.campaignsService.get(this.programId(), campaignId);
      this.applyCampaign(campaign);
    }
  }

  private applyCampaign(campaign: StreakCampaign): void {
    this.form.patchValue({
      name: campaign.name,
      trigger: campaign.trigger,
      targetAccountTypeId: campaign.targetAccountTypeId,
      activeFrom: utcIsoToLocalInput(campaign.activeFrom),
      activeTo: utcIsoToLocalInput(campaign.activeTo),
    });
    this.conditions.set(campaign.conditions ? structuredClone(campaign.conditions) : []);
    const c = campaign.config;
    this.streakForm.patchValue({
      period: c.period,
      weekStart: c.week_start,
      targetPeriods: c.target_periods,
      metric: c.aggregate.metric,
      threshold: Number(c.aggregate.threshold),
      timezone: c.timezone,
      onComplete: c.on_complete,
      rewardKind: c.reward.kind,
      rewardAmount: c.reward.amount ? Number(c.reward.amount) : null,
      rewardDefinitionId: c.reward.reward_definition_id ?? '',
    });
  }

  protected async submit(): Promise<void> {
    if (this.saving()) return;
    this.formErrors.set([]);

    const conditionErrors = validateConditions(this.conditions());

    const s = this.streakForm.getRawValue();
    const config: StreakConfig = {
      period: s.period,
      week_start: s.weekStart,
      target_periods: Number(s.targetPeriods),
      aggregate: { metric: s.metric, threshold: decimalString(s.threshold) },
      timezone: s.timezone,
      on_complete: s.onComplete,
      reward:
        s.rewardKind === 'fixed_bonus'
          ? { kind: 'fixed_bonus', amount: decimalString(s.rewardAmount ?? 0) }
          : { kind: 'reward_definition', reward_definition_id: s.rewardDefinitionId },
    };
    conditionErrors.push(...validateStreakConfig(config));

    if (this.form.invalid || conditionErrors.length > 0) {
      this.formErrors.set(conditionErrors.length ? conditionErrors : ['Please fix the highlighted fields.']);
      return;
    }

    this.saving.set(true);
    try {
      const v = this.form.getRawValue();

      const request: CreateStreakCampaignRequest = {
        name: v.name,
        trigger: v.trigger,
        targetAccountTypeId: v.targetAccountTypeId,
        conditions: this.conditions().length ? this.conditions() : null,
        config,
        activeFrom: localInputToUtcIso(v.activeFrom),
        activeTo: localInputToUtcIso(v.activeTo),
      };

      const campaignId = this.campaignId();
      if (campaignId) {
        await this.campaignsService.update(this.programId(), campaignId, request);
        this.toast.success('Campaign saved');
      } else {
        await this.campaignsService.create(this.programId(), request);
        this.toast.success('Campaign created');
      }
      void this.router.navigate(['/programs', this.programId(), 'streak-campaigns']);
    } catch (err) {
      if (err instanceof ApiError) {
        const fieldMessages = Object.values(err.fieldErrors).flat();
        this.formErrors.set(fieldMessages.length ? fieldMessages : [err.message]);
      } else {
        this.formErrors.set(['Something went wrong. Please try again.']);
      }
    } finally {
      this.saving.set(false);
    }
  }
}
