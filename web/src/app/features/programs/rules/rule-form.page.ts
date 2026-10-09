import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { startWith } from 'rxjs';
import { Router, RouterLink } from '@angular/router';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { PageHeader } from '../../../shared/ui/page-header';
import { Button } from '../../../shared/ui/button';
import { FormErrors } from '../../../shared/ui/form-errors';
import { FieldHint } from '../../../shared/ui/field-hint';
import { SearchableSelect, SelectOption } from '../../../shared/ui/searchable-select';
import { ConditionTreeEditor, KnownField } from '../../../shared/ui/condition-tree-editor';
import { ToastService } from '../../../core/ui/toast.service';
import { ConfirmService } from '../../../core/ui/confirm.service';
import { ApiError } from '../../../core/http/api-error';
import { EventTypesCatalog, EventTypesService } from '../../../core/events/event-types.service';
import { decimalString, toDecimalStringOrNull } from '../../../shared/money/decimal-string';
import { applyServerErrors } from '../../../shared/forms/server-errors';
import { markAllDirtyAndTouched, scrollToFirstError } from '../../../shared/forms/form-utils';
import { requiredWhen } from '../../../shared/forms/conditional-validators';
import { localInputToUtcIso, utcIsoToLocalInput } from '../../../shared/date/utc';
import {
  ConditionTree,
  conditionsForSave,
  emptyTree,
  hasConditions,
  validateConditionTree,
} from '../../../shared/forms/condition-tree-dsl';
import { RulesService } from './rules.service';
import { BurnRuleDefaults, burnRuleDefaults } from './burn-rule-defaults';
import { fieldVisibility, isConfigVisible, isLimitVisible } from './rule-field-visibility';
import {
  ALL_LIMIT_FIELDS,
  LIMIT_GROUPS,
  LimitControl,
  LimitValues,
  usesOnBreach,
  usesPeriod,
  validateLimits,
} from './rule-limits';
import { AccountTypesService } from '../account-types/account-types.service';
import { ProgramsService } from '../programs.service';
import { RoundingDirection } from '../program.model';
import { AccountType } from '../account-types/account-type.model';
import {
  CreateRuleRequest,
  RULE_TYPES,
  Rule,
  RuleCalculation,
  RuleConfiguration,
  RuleLimits,
  RuleType,
  RulesMetadata,
} from './rule.model';

const DEFAULT_TRIGGER = 'order.created';

// Category display order for the grouped trigger dropdown; matches EventCategory's declaration
// order in EventTypes.cs. Event types the metadata catalog doesn't know about (tenant-approved
// generic types — see EventTypesCatalog) have no category and sort last, under "Other".
const CATEGORY_ORDER = ['Earn', 'Burn', 'Reverse', 'Adjust'] as const;
const OTHER_GROUP = 'Other';

/** Exported for its spec only. */
export function buildTriggerOptions(
  catalog: EventTypesCatalog | null,
  metadata: RulesMetadata | null,
  keep: string | null,
): SelectOption<string>[] {
  if (!catalog) return [{ value: DEFAULT_TRIGGER, label: DEFAULT_TRIGGER, group: 'Earn' }];

  const categoryByType = new Map(metadata?.events.map((e) => [e.eventType, e.category]) ?? []);
  // CR 2026-09-30 item 8: an event no rule type can use (reward.purchase — configured through
  // its reward definition) isn't offered as a trigger. `keep` is an existing rule's own trigger,
  // so a rule saved before that change still opens with its trigger shown.
  const ruleless = new Set(
    metadata?.events.filter((e) => e.compatibleRuleTypes.length === 0).map((e) => e.eventType) ??
      [],
  );
  const groupRank = (group: string): number => {
    const i = CATEGORY_ORDER.indexOf(group as (typeof CATEGORY_ORDER)[number]);
    return i === -1 ? CATEGORY_ORDER.length : i;
  };

  // `generic` is a deployment-configured allow-list (RabbitMq:GenericEventTypes) and isn't
  // guaranteed disjoint from the built-in set — de-dupe so a misconfigured overlap (e.g. a
  // built-in accidentally also listed as generic) doesn't show the same trigger twice.
  const seen = new Set<string>();
  return [...catalog.builtIn, ...catalog.generic]
    .filter((type) => (seen.has(type) ? false : (seen.add(type), true)))
    .filter((type) => !ruleless.has(type) || type === keep)
    .map((type) => ({ value: type, label: type, group: categoryByType.get(type) ?? OTHER_GROUP }))
    .sort((a, b) => groupRank(a.group) - groupRank(b.group) || a.label.localeCompare(b.label));
}

@Component({
  selector: 'app-rule-form-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslatePipe,
    RouterLink,
    PageHeader,
    Button,
    FormErrors,
    FieldHint,
    SearchableSelect,
    ConditionTreeEditor,
  ],
  template: `
    <app-page-header
      [heading]="isEdit() ? 'Edit rule' : 'New rule'"
      [crumbs]="[
        { label: 'Programs', link: ['/programs'] },
        { label: 'Rules', link: ['/programs', programId(), 'rules'] },
        { label: isEdit() ? 'Edit' : 'New' },
      ]"
    >
      <div actions>
        <app-button variant="secondary" [routerLink]="['/programs', programId(), 'rules']"
          >Cancel</app-button
        >
        <app-button [pending]="saving()" [disabled]="noRuleType()" (click)="submit()"
          >Save</app-button
        >
      </div>
    </app-page-header>

    <div class="mx-auto max-w-5xl space-y-6 px-8 py-6">
      <app-form-errors [messages]="formErrors()" />

      <section class="card space-y-4">
        <div class="section-header">
          <h2 class="section-heading">Basics</h2>
          <p class="mt-1 text-xs text-gray-500">
            What fires this rule, and how it ranks against other rules on the same wallet.
          </p>
        </div>
        <form [formGroup]="form" class="space-y-4">
          <div class="grid grid-cols-2 gap-3">
            <div>
              @let nameError =
                fieldError('name', {
                  required: 'Name is required.',
                  maxlength: 'Name must be 255 characters or fewer.',
                });
              <label for="name" class="field-label"
                >Name <span class="text-danger-fg" aria-hidden="true">*</span></label
              >
              <input
                id="name"
                formControlName="name"
                class="field-input"
                [attr.aria-invalid]="nameError ? 'true' : null"
                [attr.aria-describedby]="nameError ? 'name-error' : null"
              />
              <app-field-hint id="name-error" [error]="nameError" />
            </div>
            <div>
              @let priorityError =
                fieldError('priority', {
                  required: 'Priority is required.',
                  min: 'Priority must be 0 or greater.',
                });
              <label for="priority" class="field-label"
                >Priority <span class="text-danger-fg" aria-hidden="true">*</span></label
              >
              <input
                id="priority"
                type="number"
                min="0"
                formControlName="priority"
                class="field-input"
                [attr.aria-invalid]="priorityError ? 'true' : null"
                aria-describedby="priority-desc"
              />
              <app-field-hint
                id="priority-desc"
                [error]="priorityError"
                [hint]="'rules.priority.hint' | translate"
              />
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <span id="trigger-label" class="field-label"
                >Trigger event <span class="text-danger-fg" aria-hidden="true">*</span></span
              >
              <app-searchable-select
                formControlName="trigger"
                [options]="triggerOptions()"
                placeholder="Select a trigger…"
                ariaLabelledby="trigger-label"
              />
              <app-field-hint
                hint="Sets which event fields are available under Conditions below."
              />
            </div>
            <div>
              <label for="type" class="field-label"
                >Rule type <span class="text-danger-fg" aria-hidden="true">*</span></label
              >
              <select
                id="type"
                formControlName="type"
                class="field-input"
                [attr.disabled]="isEdit() ? '' : null"
                [attr.aria-describedby]="isEdit() ? 'type-hint' : null"
              >
                @for (t of typeOptions(); track t) {
                  <option [value]="t">{{ t }}</option>
                }
              </select>
              @if (noRuleType()) {
                <p class="text-danger-fg mt-1 text-xs" role="status">
                  {{ 'rules.type.none' | translate }}
                </p>
              } @else if (selectedRuleTypeMeta(); as meta) {
                <p class="mt-1 text-xs text-gray-500">{{ meta.note }}</p>
              }
              @if (isEdit()) {
                <app-field-hint id="type-hint" hint="Rule type cannot be changed after creation." />
              }
            </div>
          </div>
          @if (noRuleType()) {
            <!-- No rule type → no target to pick; showing the list here is what used to offer
                 POINTS/CASH for reward.purchase. -->
          } @else if (needsTargetAccount()) {
            <div>
              <span id="targetAccountTypeId-label" class="field-label"
                >Target account <span class="text-danger-fg" aria-hidden="true">*</span></span
              >
              <app-searchable-select
                formControlName="targetAccountTypeId"
                [options]="targetAccountOptions()"
                placeholder="Select an account…"
                ariaLabelledby="targetAccountTypeId-label"
              />
            </div>
          } @else {
            <p class="text-xs text-gray-500">
              ReversalRule inherits its target account from the original posting — nothing to
              configure here.
            </p>
          }
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label for="activeFrom" class="field-label">Active from</label>
              <input
                id="activeFrom"
                type="datetime-local"
                formControlName="activeFrom"
                class="field-input"
              />
              <app-field-hint hint="Leave blank to make the rule active immediately." />
            </div>
            <div>
              <label for="activeTo" class="field-label">Active to</label>
              <input
                id="activeTo"
                type="datetime-local"
                formControlName="activeTo"
                class="field-input"
              />
              <app-field-hint hint="Leave blank for no end date." />
            </div>
          </div>

          <!-- 1.3.CL item 5: Exclusivity group and Stack mode are retired — only "stackable" remains. -->
          <div class="border-t border-gray-100 pt-4">
            <label class="flex cursor-pointer items-center gap-2 text-sm text-gray-700">
              <input
                type="checkbox"
                formControlName="stackable"
                class="checkbox"
                aria-describedby="stackable-desc"
              />
              {{ 'rules.stackable.label' | translate }}
            </label>
            <app-field-hint id="stackable-desc" [hint]="'rules.stackable.hint' | translate" />
          </div>
        </form>
      </section>

      <section class="card space-y-4">
        <div class="section-header">
          <!-- CR 2026-10-06 D17: a redeem / transfer rule's calculation holds the customer-facing terms. -->
          @switch (typeValue()) {
            @case ('RedemptionRule') {
              <h2 class="section-heading">{{ 'rules.calc.headingRedeem' | translate }}</h2>
              <p class="mt-1 text-xs text-gray-500">{{ 'rules.calc.introRedeem' | translate }}</p>
            }
            @case ('TransferRule') {
              <h2 class="section-heading">{{ 'rules.calc.headingTransfer' | translate }}</h2>
              <p class="mt-1 text-xs text-gray-500">{{ 'rules.calc.introTransfer' | translate }}</p>
            }
            @default {
              <h2 class="section-heading">{{ 'rules.calc.heading' | translate }}</h2>
              <p class="mt-1 text-xs text-gray-500">{{ 'rules.calc.intro' | translate }}</p>
            }
          }
        </div>
        <form [formGroup]="form" class="grid grid-cols-2 gap-3">
          @switch (typeValue()) {
            @case ('SpendRule') {
              <div>
                @let calcRateError =
                  fieldError('calcRate', {
                    required: 'Rate is required.',
                    min: 'Rate must be 0 or greater.',
                  });
                <label for="calcRate" class="field-label"
                  >Rate (units earned per 1 spent)
                  <span class="text-danger-fg" aria-hidden="true">*</span></label
                >
                <input
                  id="calcRate"
                  type="number"
                  min="0"
                  step="0.0001"
                  formControlName="calcRate"
                  class="field-input"
                  [attr.aria-invalid]="calcRateError ? 'true' : null"
                  [attr.aria-describedby]="calcRateError ? 'calcRate-error' : null"
                />
                <!-- CR 2026-10-06 D14: a tenant-defined event may or may not carry an amount. -->
                <app-field-hint
                  id="calcRate-error"
                  [error]="calcRateError"
                  [hint]="genericTrigger() ? ('rules.calc.rateNeedsAmount' | translate) : null"
                />
              </div>
            }
            @case ('FixedBonusRule') {
              <div>
                @let calcAmountError =
                  fieldError('calcAmount', { required: 'Amount is required.' });
                <label for="calcAmount" class="field-label"
                  >Fixed amount <span class="text-danger-fg" aria-hidden="true">*</span></label
                >
                <input
                  id="calcAmount"
                  type="number"
                  min="0"
                  step="0.0001"
                  formControlName="calcAmount"
                  class="field-input"
                  [attr.aria-invalid]="calcAmountError ? 'true' : null"
                  [attr.aria-describedby]="calcAmountError ? 'calcAmount-error' : null"
                />
                <app-field-hint id="calcAmount-error" [error]="calcAmountError" />
              </div>
            }
            @case ('RedemptionRule') {
              <!-- CR 2026-10-05: the same fields as the wallet's redemption settings; this rule's
                   values are what a redeem uses. -->
              <div>
                @let cashPerPointError =
                  fieldError('calcCashPerPoint', {
                    required: ('rules.calc.redeem.cashPerPointRequired' | translate),
                    min: ('rules.calc.redeem.cashPerPointRequired' | translate),
                  });
                <label for="calcCashPerPoint" class="field-label"
                  >{{ 'rules.calc.redeem.cashPerPoint' | translate }}
                  <span class="text-danger-fg" aria-hidden="true">*</span></label
                >
                <input
                  id="calcCashPerPoint"
                  type="number"
                  min="0"
                  step="0.0001"
                  formControlName="calcCashPerPoint"
                  class="field-input"
                  [attr.aria-invalid]="cashPerPointError ? 'true' : null"
                  [attr.aria-describedby]="cashPerPointError ? 'calcCashPerPoint-error' : null"
                />
                <app-field-hint id="calcCashPerPoint-error" [error]="cashPerPointError" />
              </div>
              <div>
                <label for="calcMinRedeem" class="field-label">{{
                  'rules.calc.redeem.minimum' | translate
                }}</label>
                <input
                  id="calcMinRedeem"
                  type="number"
                  min="0"
                  step="0.0001"
                  formControlName="calcMinRedeem"
                  class="field-input"
                  [placeholder]="'rules.calc.none' | translate"
                />
              </div>
              <div class="col-span-2">
                @let cashWalletError =
                  fieldError('calcCashAccountTypeId', {
                    required: ('rules.calc.redeem.redeemIntoRequired' | translate),
                  });
                <span id="calcCashAccountTypeId-label" class="field-label"
                  >{{ 'rules.calc.redeem.redeemInto' | translate }}
                  <span class="text-danger-fg" aria-hidden="true">*</span></span
                >
                @if (cashWalletOptions().length > 0) {
                  <app-searchable-select
                    formControlName="calcCashAccountTypeId"
                    [options]="cashWalletOptions()"
                    [placeholder]="'rules.calc.redeem.redeemIntoPlaceholder' | translate"
                    ariaLabelledby="calcCashAccountTypeId-label"
                    [invalid]="!!cashWalletError"
                  />
                } @else {
                  <p class="text-xs text-gray-500">
                    {{ 'rules.calc.redeem.noCashWallets' | translate }}
                  </p>
                }
                <app-field-hint
                  [error]="cashWalletError"
                  [hint]="'rules.calc.redeem.approvalHint' | translate"
                />
              </div>
              @if (!isEdit()) {
                <p class="col-span-2 text-xs text-gray-500">
                  {{ 'rules.calc.prefillHint' | translate }}
                </p>
              }
            }
            @case ('TransferRule') {
              <!-- CR 2026-10-05 (R-O2): the daily transfer limit only — ratio and fee wait for a
                   fee-engine CR. -->
              <div>
                @let dailyLimitError =
                  fieldError('calcMaxPerDay', {
                    required: ('rules.calc.transfer.dailyLimitRequired' | translate),
                    min: ('rules.calc.transfer.dailyLimitRequired' | translate),
                  });
                <label for="calcMaxPerDay" class="field-label"
                  >{{ 'rules.calc.transfer.dailyLimit' | translate }}
                  <span class="text-danger-fg" aria-hidden="true">*</span></label
                >
                <input
                  id="calcMaxPerDay"
                  type="number"
                  min="0"
                  step="0.0001"
                  formControlName="calcMaxPerDay"
                  class="field-input"
                  [attr.aria-invalid]="dailyLimitError ? 'true' : null"
                  [attr.aria-describedby]="dailyLimitError ? 'calcMaxPerDay-error' : null"
                />
                <app-field-hint id="calcMaxPerDay-error" [error]="dailyLimitError" />
              </div>
              @if (!isEdit()) {
                <p class="col-span-2 text-xs text-gray-500">
                  {{ 'rules.calc.prefillHint' | translate }}
                </p>
              }
            }
            @case ('ReversalRule') {
              <div>
                <label for="calcMode" class="field-label">Reversal mode</label>
                <select id="calcMode" formControlName="calcMode" class="field-input">
                  <option value="proportional">
                    Proportional — scales with the reversed amount
                  </option>
                  <option value="full">Full — reverses the entire original posting</option>
                </select>
              </div>
              <div>
                <label for="calcAllowNegative" class="field-label"
                  >If balance would go negative</label
                >
                <select
                  id="calcAllowNegative"
                  formControlName="calcAllowNegative"
                  class="field-input"
                >
                  <option value="allow negative">Allow negative</option>
                  <option value="clamp to zero">Clamp to zero</option>
                </select>
              </div>
            }
            @case ('ManualAdjustmentRule') {
              <div>
                <label for="calcReason" class="field-label">Reason code</label>
                <select id="calcReason" formControlName="calcReason" class="field-input">
                  <option value="goodwill">Goodwill</option>
                  <option value="correction">Correction</option>
                  <option value="dispute">Dispute</option>
                  <option value="migration">Migration</option>
                </select>
              </div>
              <div>
                <label for="calcAmount" class="field-label">Fallback amount (optional)</label>
                <input
                  id="calcAmount"
                  type="number"
                  step="0.0001"
                  formControlName="calcAmount"
                  class="field-input"
                  placeholder="Uses operator.amount from the event"
                />
              </div>
            }
          }
        </form>
      </section>

      <!-- Limits: grouped by what they cap; each value validated by its kind (rule-limits.ts).
           CR 2026-10-06 Phase 2: a new rule shows only the limits that apply to it. -->
      @if (visibleLimitGroups().length > 0) {
        <section class="card space-y-5">
          <div class="section-header">
            <h2 class="section-heading">{{ 'rules.limits.heading' | translate }}</h2>
            <p class="mt-1 text-xs text-gray-500">{{ 'rules.limits.intro' | translate }}</p>
          </div>
          <form [formGroup]="form" class="space-y-5">
            @for (group of visibleLimitGroups(); track group.id) {
              <fieldset class="rounded-lg border border-gray-200 p-4">
                <legend class="px-1 text-sm font-medium text-gray-800">
                  {{ group.i18n + '.title' | translate }}
                </legend>
                <p class="mb-3 text-xs text-gray-500">{{ group.i18n + '.hint' | translate }}</p>
                <div class="grid grid-cols-3 gap-3">
                  @for (field of group.fields; track field.control) {
                    @let error = limitError(field.control);
                    <div>
                      <label [for]="field.control" class="field-label">{{
                        field.i18n + '.label' | translate
                      }}</label>
                      <div class="relative">
                        <input
                          [id]="field.control"
                          type="text"
                          [attr.inputmode]="field.kind === 'count' ? 'numeric' : 'decimal'"
                          autocomplete="off"
                          [formControlName]="field.control"
                          class="field-input pr-16"
                          [placeholder]="'rules.limits.none' | translate"
                          [attr.aria-invalid]="error ? 'true' : null"
                          [attr.aria-describedby]="field.control + '-hint'"
                        />
                        <span
                          class="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs text-gray-400"
                          aria-hidden="true"
                          >{{ 'rules.limits.units.' + field.kind | translate }}</span
                        >
                      </div>
                      <app-field-hint
                        [id]="field.control + '-hint'"
                        [error]="error ? (error | translate) : null"
                        [hint]="
                          field.apiKey === 'min_event_amount' && genericTrigger()
                            ? ('rules.limits.minEventAmount.genericHint' | translate)
                            : (field.i18n + '.hint' | translate)
                        "
                      />
                    </div>
                  }
                </div>
              </fieldset>
            }

            <!-- Only shown when a per-period cap is set — otherwise the engine would silently use "Day". -->
            @if (limitsUsePeriod() && showLimit('period')) {
              <fieldset class="rounded-lg border border-gray-200 p-4">
                <legend class="px-1 text-sm font-medium text-gray-800">
                  {{ 'rules.limits.groups.period.title' | translate }}
                </legend>
                <p class="mb-3 text-xs text-gray-500">
                  {{ 'rules.limits.groups.period.hint' | translate }}
                </p>
                <div class="grid grid-cols-3 gap-3">
                  <div>
                    @let periodError = limitError('limitPeriod');
                    <label for="limitPeriod" class="field-label">{{
                      'rules.limits.period.label' | translate
                    }}</label>
                    <select
                      id="limitPeriod"
                      formControlName="limitPeriod"
                      class="field-input"
                      [attr.aria-invalid]="periodError ? 'true' : null"
                      aria-describedby="limitPeriod-hint"
                    >
                      <option value="">{{ 'rules.limits.period.choose' | translate }}</option>
                      <option value="Day">{{ 'rules.limits.period.Day' | translate }}</option>
                      <option value="Week">{{ 'rules.limits.period.Week' | translate }}</option>
                      <option value="Month">{{ 'rules.limits.period.Month' | translate }}</option>
                      <option value="Year">{{ 'rules.limits.period.Year' | translate }}</option>
                    </select>
                    <app-field-hint
                      id="limitPeriod-hint"
                      [error]="periodError ? (periodError | translate) : null"
                    />
                  </div>
                  <div class="col-span-2">
                    <label for="limitResetWindow" class="field-label">{{
                      'rules.limits.resetWindow.label' | translate
                    }}</label>
                    <select
                      id="limitResetWindow"
                      formControlName="limitResetWindow"
                      class="field-input"
                    >
                      <option value="Calendar">
                        {{ 'rules.limits.resetWindow.Calendar' | translate }}
                      </option>
                      <option value="Rolling">
                        {{ 'rules.limits.resetWindow.Rolling' | translate }}
                      </option>
                    </select>
                  </div>
                </div>
              </fieldset>
            }

            <!-- Only shown when a cap that can be partly paid out is set (RuleLimits.cs). -->
            @if (limitsUseOnBreach() && showLimit('on_breach')) {
              <fieldset class="rounded-lg border border-gray-200 p-4">
                <legend class="px-1 text-sm font-medium text-gray-800">
                  {{ 'rules.limits.groups.breach.title' | translate }}
                </legend>
                <p class="mb-3 text-xs text-gray-500">
                  {{ 'rules.limits.groups.breach.hint' | translate }}
                </p>
                <label for="limitOnBreach" class="sr-only">{{
                  'rules.limits.groups.breach.title' | translate
                }}</label>
                <select
                  id="limitOnBreach"
                  formControlName="limitOnBreach"
                  class="field-input max-w-md"
                >
                  <option value="Clamp">{{ 'rules.limits.onBreach.Clamp' | translate }}</option>
                  <option value="Skip">{{ 'rules.limits.onBreach.Skip' | translate }}</option>
                </select>
              </fieldset>
            }
          </form>
        </section>
      }

      @if (anyConfigVisible()) {
        <section class="card space-y-4">
          <div class="section-header">
            <h2 class="section-heading">{{ 'rules.config.heading' | translate }}</h2>
            <p class="mt-1 text-xs text-gray-500">{{ 'rules.config.intro' | translate }}</p>
          </div>
          <form [formGroup]="form" class="grid grid-cols-2 gap-3">
            @if (showConfig('rounding')) {
              <div>
                <label for="cfgRounding" class="field-label">{{
                  'rules.config.rounding.label' | translate
                }}</label>
                <select id="cfgRounding" formControlName="cfgRounding" class="field-input">
                  <option value="">
                    {{
                      programRounding()
                        ? ('rules.config.rounding.inheritWith'
                          | translate
                            : {
                                direction:
                                  ('rules.config.rounding.' + programRounding() | translate),
                              })
                        : ('rules.config.rounding.inherit' | translate)
                    }}
                  </option>
                  <option value="down">{{ 'rules.config.rounding.down' | translate }}</option>
                  <option value="nearest">{{ 'rules.config.rounding.nearest' | translate }}</option>
                  <option value="up">{{ 'rules.config.rounding.up' | translate }}</option>
                </select>
              </div>
            }
            @if (showConfig('posting')) {
              <div>
                <label for="cfgPosting" class="field-label">{{
                  'rules.config.posting.label' | translate
                }}</label>
                <select id="cfgPosting" formControlName="cfgPosting" class="field-input">
                  <option value="Immediate">
                    {{ 'rules.config.posting.Immediate' | translate }}
                  </option>
                  <option value="Delayed">{{ 'rules.config.posting.Delayed' | translate }}</option>
                </select>
              </div>
              @if (postingValue() === 'Delayed') {
                <div>
                  @let cfgHoldDaysError =
                    fieldError('cfgHoldDays', {
                      required: ('rules.config.holdDays.required' | translate),
                      min: ('rules.config.holdDays.min' | translate),
                    });
                  <label for="cfgHoldDays" class="field-label"
                    >{{ 'rules.config.holdDays.label' | translate }}
                    <span class="text-danger-fg" aria-hidden="true">*</span></label
                  >
                  <input
                    id="cfgHoldDays"
                    type="number"
                    min="1"
                    step="1"
                    formControlName="cfgHoldDays"
                    class="field-input"
                    [attr.aria-invalid]="cfgHoldDaysError ? 'true' : null"
                    [attr.aria-describedby]="cfgHoldDaysError ? 'cfgHoldDays-error' : null"
                  />
                  <app-field-hint id="cfgHoldDays-error" [error]="cfgHoldDaysError" />
                </div>
              }
            }
            @if (showConfig('expiryOverrideDays')) {
              <div>
                @let cfgExpiryOverrideDaysError =
                  fieldError('cfgExpiryOverrideDays', {
                    min: ('rules.config.expiryOverride.min' | translate),
                  });
                <label for="cfgExpiryOverrideDays" class="field-label">{{
                  'rules.config.expiryOverride.label' | translate
                }}</label>
                <input
                  id="cfgExpiryOverrideDays"
                  type="number"
                  min="1"
                  step="1"
                  formControlName="cfgExpiryOverrideDays"
                  class="field-input"
                  [placeholder]="
                    walletExpirationDays()
                      ? ('rules.config.expiryOverride.placeholderDays'
                        | translate: { days: walletExpirationDays() })
                      : ('rules.config.expiryOverride.placeholderNever' | translate)
                  "
                  [attr.aria-invalid]="cfgExpiryOverrideDaysError ? 'true' : null"
                  aria-describedby="cfgExpiryOverrideDays-hint"
                />
                <app-field-hint
                  id="cfgExpiryOverrideDays-hint"
                  [hint]="'rules.config.expiryOverride.hint' | translate"
                  [error]="cfgExpiryOverrideDaysError"
                />
              </div>
            }
            @if (showConfig('reversible')) {
              <label class="flex cursor-pointer items-center gap-2 text-sm text-gray-700">
                <input type="checkbox" formControlName="cfgReversible" class="checkbox" />
                {{ 'rules.config.reversible' | translate }}
              </label>
            }
            @if (showConfig('notifyOnAward')) {
              <label class="flex cursor-pointer items-center gap-2 text-sm text-gray-700">
                <input type="checkbox" formControlName="cfgNotifyOnAward" class="checkbox" />
                {{ 'rules.config.notifyOnAward' | translate }}
              </label>
            }
          </form>
        </section>
      }

      <app-condition-tree-editor
        [tree]="conditions()"
        (treeChange)="conditions.set($event)"
        [knownFields]="knownFields()"
      />
    </div>
  `,
})
export class RuleFormPage implements OnInit {
  readonly programId = input.required<string>();
  readonly ruleId = input<string | undefined>(undefined);

  private readonly rulesService = inject(RulesService);

  private readonly programsService = inject(ProgramsService);
  private readonly accountTypesService = inject(AccountTypesService);
  private readonly eventTypesService = inject(EventTypesService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly isEdit = signal(false);
  protected readonly saving = signal(false);
  protected readonly formErrors = signal<string[]>([]);
  protected readonly accountTypes = signal<readonly AccountType[]>([]);
  protected readonly conditions = signal<ConditionTree>(emptyTree());
  protected readonly metadata = signal<RulesMetadata | null>(null);
  /** CR 2026-10-06 Phase 4: the program's default_rounding, shown on "Inherit from program". */
  protected readonly programRounding = signal<RoundingDirection | null>(null);

  // Field errors stay hidden until a first save attempt (rather than per-field blur), then
  // update live as the user fixes things — driven off form.statusChanges since a sibling
  // control's requiredWhen can flip without this control's own value changing.
  protected readonly submitted = signal(false);

  // Seeded (via buildTriggerOptions' null-catalog fallback) with the built-in trigger name so the
  // dropdown isn't empty before /events/types resolves; load() populates eventTypesCatalog with
  // the deployment's real catalog (generic types included) and metadata with each type's
  // category, and triggerOptions recomputes grouped by category (CR-01's EventCategory) once
  // both land — order matters here since app-searchable-select groups by adjacency, not value.
  private readonly eventTypesCatalog = signal<EventTypesCatalog | null>(null);
  /** The loaded rule's trigger, kept in the trigger list even if no rule type fits it any more. */
  private readonly loadedTrigger = signal<string | null>(null);
  protected readonly triggerOptions = computed(() =>
    buildTriggerOptions(this.eventTypesCatalog(), this.metadata(), this.loadedTrigger()),
  );

  private loading = true;

  protected readonly form = this.fb.group(
    {
      name: this.fb.control('', [Validators.required, Validators.maxLength(255)]),
      priority: this.fb.control(0, [Validators.required, Validators.min(0)]),
      trigger: this.fb.control<string>(DEFAULT_TRIGGER),
      type: this.fb.control<RuleType>('SpendRule'),
      targetAccountTypeId: this.fb.control(''),
      activeFrom: this.fb.control(''),
      activeTo: this.fb.control(''),
      stackable: this.fb.control(true),

      calcRate: this.fb.control<number | null>(1, [
        requiredWhen((root) => root.get('type')?.value === 'SpendRule'),
        Validators.min(0),
      ]),
      calcAmount: this.fb.control<number | null>(10, [
        requiredWhen((root) => root.get('type')?.value === 'FixedBonusRule'),
      ]),
      // CR 2026-10-05: a redeem rule's cash per point (saved as `rate`) and Redeem into wallet,
      // and a transfer rule's daily limit — all required by the API, > 0 where numeric.
      calcCashPerPoint: this.fb.control<number | null>(null, [
        requiredWhen((root) => root.get('type')?.value === 'RedemptionRule'),
        Validators.min(0.0001),
      ]),
      calcCashAccountTypeId: this.fb.control('', [
        requiredWhen((root) => root.get('type')?.value === 'RedemptionRule'),
      ]),
      calcMinRedeem: this.fb.control<number | null>(null, [Validators.min(0)]),
      calcMaxPerDay: this.fb.control<number | null>(null, [
        requiredWhen((root) => root.get('type')?.value === 'TransferRule'),
        Validators.min(0.0001),
      ]),
      calcMode: this.fb.control<'proportional' | 'full'>('proportional'),
      calcAllowNegative: this.fb.control<'allow negative' | 'clamp to zero'>('clamp to zero'),
      calcReason: this.fb.control<'goodwill' | 'correction' | 'dispute' | 'migration'>(
        'correction',
      ),

      // Limits are text inputs (DecimalString contract) validated per kind by rule-limits.ts,
      // through the group-level limitsValidator below.
      limitPerCustomerTotal: this.fb.control(''),
      limitPerCustomerPerDay: this.fb.control(''),
      limitMaxPerEvent: this.fb.control(''),
      limitMinEventAmount: this.fb.control(''),
      limitCooldownHours: this.fb.control(''),
      limitMaxCustomers: this.fb.control(''),
      limitRuleBudgetTotal: this.fb.control(''),
      limitRuleBudgetPerPeriod: this.fb.control(''),
      limitPerCustomerPerPeriod: this.fb.control(''),
      limitPeriod: this.fb.control<'' | 'Day' | 'Week' | 'Month' | 'Year'>(''),
      limitResetWindow: this.fb.control<'' | 'Calendar' | 'Rolling'>('Calendar'),
      limitOnBreach: this.fb.control<'Clamp' | 'Skip'>('Clamp'),

      cfgRounding: this.fb.control<'' | 'down' | 'nearest' | 'up'>(''),
      cfgPosting: this.fb.control<'Immediate' | 'Delayed'>('Immediate'),
      cfgHoldDays: this.fb.control<number | null>(null, [
        requiredWhen((root) => root.get('cfgPosting')?.value === 'Delayed'),
        Validators.min(1),
      ]),
      cfgExpiryOverrideDays: this.fb.control<number | null>(null, [Validators.min(1)]),
      cfgReversible: this.fb.control(true),
      cfgNotifyOnAward: this.fb.control(false),
    },
    { validators: [limitsValidator] },
  );

  private readonly triggerValue = toSignal(
    this.form.controls.trigger.valueChanges.pipe(startWith(this.form.controls.trigger.value)),
    { initialValue: this.form.controls.trigger.value },
  );
  protected readonly typeValue = toSignal(
    this.form.controls.type.valueChanges.pipe(startWith(this.form.controls.type.value)),
    { initialValue: this.form.controls.type.value },
  );
  private readonly targetValue = toSignal(
    this.form.controls.targetAccountTypeId.valueChanges.pipe(
      startWith(this.form.controls.targetAccountTypeId.value),
    ),
    { initialValue: this.form.controls.targetAccountTypeId.value },
  );
  // ── Limits (rule-limits.ts) ─────────────────────────────────────────────────────────
  private readonly limitValues = toSignal(
    this.form.valueChanges.pipe(startWith(this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );
  private readonly limitErrors = computed(() => validateLimits(this.limitValues() as LimitValues));
  protected readonly limitsUsePeriod = computed(() =>
    usesPeriod(this.limitValues() as LimitValues),
  );
  protected readonly limitsUseOnBreach = computed(() =>
    usesOnBreach(this.limitValues() as LimitValues),
  );

  /** i18n key of a limit's error — shown once the field was edited or a save was attempted. */
  protected limitError(control: LimitControl | 'limitPeriod'): string | null {
    const error = this.limitErrors()[control];
    if (!error) return null;
    return this.submitted() || this.form.controls[control].dirty ? error : null;
  }

  protected readonly postingValue = toSignal(
    this.form.controls.cfgPosting.valueChanges.pipe(startWith(this.form.controls.cfgPosting.value)),
    { initialValue: this.form.controls.cfgPosting.value },
  );
  private readonly formStatus = toSignal(
    this.form.statusChanges.pipe(startWith(this.form.status)),
    {
      initialValue: this.form.status,
    },
  );

  /** Inline message for a control, or null while hidden (before submit, or once valid). A
   * server-side error (see applyServerErrors) always wins and shows regardless of `submitted`. */
  protected fieldError(
    name: string,
    labels: { required?: string; min?: string; maxlength?: string } = {},
  ): string | null {
    this.formStatus(); // re-run when any control's validity changes, not just this one's value
    const control = this.form.get(name);
    if (!control) return null;
    const server = control.errors?.['server'] as string[] | undefined;
    if (server?.length) return server.join(' ');
    if (!this.submitted() || control.valid) return null;
    if (control.errors?.['required']) return labels.required ?? 'This field is required.';
    if (control.errors?.['min']) return labels.min ?? 'Value is too low.';
    if (control.errors?.['maxlength']) return labels.maxlength ?? 'Value is too long.';
    return 'This value is invalid.';
  }

  private readonly eventMetaForTrigger = computed(
    () => this.metadata()?.events.find((e) => e.eventType === this.triggerValue()) ?? null,
  );

  // ── CR 2026-10-06 Phase 2: fields shown per trigger and rule type (rule-field-visibility.ts) ──
  /** Null = every field: an existing rule (D5) or a tenant-defined trigger (D7). */
  protected readonly fieldVisibility = computed(() =>
    fieldVisibility(this.eventMetaForTrigger(), this.typeValue(), this.isEdit()),
  );

  /** A tenant-defined trigger: nobody declared whether it carries an amount (D14). */
  protected readonly genericTrigger = computed(
    () => this.metadata() !== null && this.eventMetaForTrigger() === null,
  );

  private readonly targetKind = computed(
    () => this.accountTypes().find((a) => a.id === this.targetValue())?.type ?? null,
  );

  /** CR 2026-10-06 Phase 5: the target wallet's own expiry, shown as the override's placeholder. */
  protected readonly walletExpirationDays = computed<number | null>(() => {
    const config = this.accountTypes().find((a) => a.id === this.targetValue())?.config as
      { expiration_days?: number | null } | undefined;
    return config?.expiration_days ?? null;
  });

  protected showConfig(key: string): boolean {
    // An expiry override only means something for points — cash never expires (§3.9).
    if (key === 'expiryOverrideDays' && !this.isEdit() && this.targetKind() === 'CASH')
      return false;
    return isConfigVisible(this.fieldVisibility(), key);
  }

  protected showLimit(key: string): boolean {
    return isLimitVisible(this.fieldVisibility(), key);
  }

  protected readonly visibleLimitGroups = computed(() => {
    const visibility = this.fieldVisibility();
    return LIMIT_GROUPS.map((g) => ({
      ...g,
      fields: g.fields.filter((f) => isLimitVisible(visibility, f.apiKey)),
    })).filter((g) => g.fields.length > 0);
  });

  protected readonly anyConfigVisible = computed(() =>
    ['rounding', 'posting', 'expiryOverrideDays', 'reversible', 'notifyOnAward'].some((k) =>
      this.showConfig(k),
    ),
  );

  // On a new rule, a field that stops applying (another trigger, type or target was picked) is
  // reset, so a value the admin can no longer see is never validated or sent.
  private readonly clearHiddenFields = effect(() => {
    if (this.isEdit()) return;
    const visibility = this.fieldVisibility();
    const cashTarget = this.targetKind() === 'CASH';
    untracked(() => {
      const c = this.form.controls;
      for (const field of ALL_LIMIT_FIELDS) {
        if (!isLimitVisible(visibility, field.apiKey) && c[field.control].value !== '')
          c[field.control].setValue('');
      }
      if (!isLimitVisible(visibility, 'period') && c.limitPeriod.value !== '')
        c.limitPeriod.setValue('');
      if (!isLimitVisible(visibility, 'on_breach') && c.limitOnBreach.value !== 'Clamp')
        c.limitOnBreach.setValue('Clamp');
      if (!isConfigVisible(visibility, 'rounding') && c.cfgRounding.value !== '')
        c.cfgRounding.setValue('');
      if (!isConfigVisible(visibility, 'posting') && c.cfgPosting.value !== 'Immediate') {
        c.cfgPosting.setValue('Immediate');
        c.cfgHoldDays.setValue(null);
      }
      if (
        (!isConfigVisible(visibility, 'expiryOverrideDays') || cashTarget) &&
        c.cfgExpiryOverrideDays.value !== null
      )
        c.cfgExpiryOverrideDays.setValue(null);
      if (!isConfigVisible(visibility, 'reversible') && !c.cfgReversible.value)
        c.cfgReversible.setValue(true);
      if (!isConfigVisible(visibility, 'notifyOnAward') && c.cfgNotifyOnAward.value)
        c.cfgNotifyOnAward.setValue(false);
    });
  });

  // Unknown to the metadata catalog (a tenant-approved generic event type) — treated as
  // compatible with everything, mirroring RuleTypeCatalog.IsCompatible's server-side fallback.
  protected readonly typeOptions = computed<readonly RuleType[]>(() => {
    const evt = this.eventMetaForTrigger();
    if (!evt) return RULE_TYPES;
    return RULE_TYPES.filter((t) => evt.compatibleRuleTypes.includes(t));
  });

  /** No rule type can be used with the chosen trigger (RuleTypeCatalog allows none). */
  protected readonly noRuleType = computed(() => this.typeOptions().length === 0);

  protected readonly selectedRuleTypeMeta = computed(
    () => this.metadata()?.ruleTypes.find((rt) => rt.ruleType === this.typeValue()) ?? null,
  );

  protected readonly needsTargetAccount = computed(() => this.typeValue() !== 'ReversalRule');

  protected readonly targetAccountOptions = computed<SelectOption<string>[]>(() => {
    const kinds = this.selectedRuleTypeMeta()?.validTargetAccountKinds ?? null;
    const pool = kinds
      ? this.accountTypes().filter((a) => kinds.includes(a.type))
      : this.accountTypes();
    return pool.map((at) => ({ value: at.id, label: `${at.name} (${at.type})` }));
  });

  /** Redeem into: the program's CASH wallets. */
  protected readonly cashWalletOptions = computed<SelectOption<string>[]>(() =>
    this.accountTypes()
      .filter((a) => a.type === 'CASH')
      .map((at) => ({ value: at.id, label: at.name })),
  );

  protected readonly knownFields = computed<KnownField[]>(() => {
    const evt = this.eventMetaForTrigger();
    if (!evt) return [];
    return evt.fields.map((f) => ({
      path: f.path,
      kind: f.kind.toLowerCase() as KnownField['kind'],
    }));
  });

  private previousTrigger = this.form.controls.trigger.value;

  constructor() {
    // A9 UI invariant: changing the trigger resets the condition tree (the field vocabulary
    // changes with the event). Guarded by `loading` so patching an existing rule's trigger
    // during load() doesn't wipe the conditions we just loaded. If the user has actually built
    // out conditions already, confirm first and revert the pick on cancel — otherwise the reset
    // silently discards work with no way back.
    this.form.controls.trigger.valueChanges.subscribe((next) => {
      if (this.loading) {
        this.previousTrigger = next;
        return;
      }
      const previous = this.previousTrigger;
      const applyChange = (): void => {
        this.previousTrigger = next;
        this.conditions.set(emptyTree());
        if (!this.isEdit()) {
          const opts = this.typeOptions();
          // No fallback when the trigger allows no rule type: a hidden default type (it used to
          // be RULE_TYPES[0]) drove the target-account list and only failed on Save. The form
          // shows the noRuleType state instead.
          const first = opts[0];
          if (first !== undefined && !opts.includes(this.form.controls.type.value)) {
            this.form.controls.type.setValue(first);
          }
        }
      };
      if (!hasConditions(this.conditions())) {
        applyChange();
        return;
      }
      void this.confirm
        .ask({
          title: 'Change trigger event?',
          message:
            'This rule’s conditions are written for the current trigger. Changing it clears them, since the available fields depend on the event.',
          confirmLabel: 'Change trigger',
          destructive: true,
        })
        .then((ok) => {
          if (ok) applyChange();
          else this.form.controls.trigger.setValue(previous, { emitEvent: false });
        });
    });

    // requiredWhen validators read a sibling control's value but only Angular's own value
    // changes trigger revalidation — without this, e.g. switching off RedemptionRule while
    // a required calc field is empty leaves it permanently (and invisibly, since @switch hides
    // the field) marked invalid, and the form can never be saved again.
    this.form.controls.type.valueChanges.subscribe(() => {
      for (const key of [
        'calcRate',
        'calcAmount',
        'calcCashPerPoint',
        'calcCashAccountTypeId',
        'calcMaxPerDay',
      ] as const) {
        this.form.controls[key].updateValueAndValidity({ emitEvent: false });
      }
      this.applyWalletDefaults();
    });
    this.form.controls.targetAccountTypeId.valueChanges.subscribe(() => this.applyWalletDefaults());
    this.form.controls.cfgPosting.valueChanges.subscribe(() => {
      this.form.controls.cfgHoldDays.updateValueAndValidity({ emitEvent: false });
    });
  }

  ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    this.isEdit.set(!!this.ruleId());
    const [accountTypes, eventTypes, metadata, program] = await Promise.all([
      this.accountTypesService.listAll(this.programId()),
      this.eventTypesService.get().catch(() => null),
      this.rulesService.getMetadata(this.programId()).catch(() => null),
      this.programsService.get(this.programId()).catch(() => null),
    ]);
    this.accountTypes.set(accountTypes);
    if (eventTypes) this.eventTypesCatalog.set(eventTypes);
    if (metadata) this.metadata.set(metadata);
    this.programRounding.set(program?.defaultRounding ?? null);

    const ruleId = this.ruleId();
    if (ruleId) {
      const rule = await this.rulesService.get(this.programId(), ruleId);
      this.applyRule(rule);
    } else {
      const firstAccountType = accountTypes[0];
      if (firstAccountType) this.form.patchValue({ targetAccountTypeId: firstAccountType.id });
    }
    this.loading = false;
    this.applyWalletDefaults();
  }

  /**
   * CR 2026-10-05 (S4): on a new redeem / transfer rule, copy the chosen POINTS wallet's settings
   * into the calculation — once, and never over a field the admin has typed in. Editing a rule
   * never pre-fills: the rule owns its values.
   */
  private applyWalletDefaults(): void {
    if (this.loading || this.isEdit()) return;
    const { type, targetAccountTypeId } = this.form.getRawValue();
    const defaults = burnRuleDefaults(
      type,
      this.accountTypes().find((a) => a.id === targetAccountTypeId),
    );
    const untouched = Object.fromEntries(
      Object.entries(defaults).filter(
        ([key]) => !this.form.controls[key as keyof BurnRuleDefaults].dirty,
      ),
    ) as BurnRuleDefaults;
    this.form.patchValue(untouched, { emitEvent: false });
  }

  private applyRule(rule: Rule): void {
    const calc = rule.calculation;
    const limits = rule.limits;
    const config = rule.configuration;

    this.loadedTrigger.set(rule.trigger);
    this.form.patchValue({
      name: rule.name,
      priority: rule.priority,
      trigger: rule.trigger,
      type: rule.type,
      targetAccountTypeId: rule.targetAccountTypeId ?? '',
      activeFrom: utcIsoToLocalInput(rule.activeFrom),
      activeTo: utcIsoToLocalInput(rule.activeTo),
      stackable: rule.stackable,

      calcRate: numberOrNull(calc?.rate) ?? 1,
      calcAmount: numberOrNull(calc?.amount) ?? 10,
      // A rule saved before CR 2026-10-05 has only the retired `ratio`: the fields stay empty and
      // must be filled in before it can be saved (or switched on) again.
      calcCashPerPoint: rule.type === 'RedemptionRule' ? numberOrNull(calc?.rate) : null,
      calcCashAccountTypeId: calc?.cashAccountTypeId ?? '',
      calcMinRedeem: numberOrNull(calc?.minRedeem),
      calcMaxPerDay: numberOrNull(calc?.maxPerDay),
      calcMode: calc?.mode ?? 'proportional',
      calcAllowNegative: calc?.allowNegative ?? 'clamp to zero',
      calcReason: calc?.reason ?? 'correction',

      limitPerCustomerTotal: toInput(limits?.per_customer_total),
      limitPerCustomerPerDay: toInput(limits?.per_customer_per_day),
      limitMaxPerEvent: toInput(limits?.max_per_event),
      limitMinEventAmount: toInput(limits?.min_event_amount),
      limitCooldownHours: toInput(limits?.cooldown_hours),
      limitMaxCustomers: toInput(limits?.max_customers),
      limitRuleBudgetTotal: toInput(limits?.rule_budget_total),
      limitRuleBudgetPerPeriod: toInput(limits?.rule_budget_per_period),
      limitPerCustomerPerPeriod: toInput(limits?.per_customer_per_period),
      limitPeriod: limits?.period ?? '',
      limitResetWindow: limits?.reset_window ?? 'Calendar',
      limitOnBreach: limits?.on_breach ?? 'Clamp',

      cfgRounding: config?.rounding ?? '',
      cfgPosting: config?.posting ?? 'Immediate',
      cfgHoldDays: config?.holdDays ?? null,
      cfgExpiryOverrideDays: config?.expiryOverrideDays ?? null,
      cfgReversible: config?.reversible ?? true,
      cfgNotifyOnAward: config?.notifyOnAward ?? false,
    });
    this.conditions.set(rule.conditions ? structuredClone(rule.conditions) : emptyTree());
  }

  private buildCalculation(v: ReturnType<typeof this.form.getRawValue>): RuleCalculation | null {
    switch (v.type) {
      case 'SpendRule':
        return { rate: decimalString(v.calcRate ?? 0) };
      case 'FixedBonusRule':
        return { amount: decimalString(v.calcAmount ?? 0) };
      case 'RedemptionRule': {
        const calc: RuleCalculation = {
          rate: decimalString(v.calcCashPerPoint ?? 0),
          cashAccountTypeId: v.calcCashAccountTypeId || null,
        };
        const minRedeem = toDecimalStringOrNull(v.calcMinRedeem);
        if (minRedeem) calc.minRedeem = minRedeem;
        return calc;
      }
      case 'TransferRule':
        return { maxPerDay: decimalString(v.calcMaxPerDay ?? 0) };
      case 'ReversalRule':
        return { mode: v.calcMode, allowNegative: v.calcAllowNegative };
      case 'ManualAdjustmentRule': {
        const calc: RuleCalculation = { reason: v.calcReason };
        const amount = toDecimalStringOrNull(v.calcAmount);
        if (amount) calc.amount = amount;
        return calc;
      }
      // Retired by CR 2026-10-05 — never offered; the API refuses to save them.
      case 'StampRule':
      case 'ExpiryRule':
        return null;
    }
  }

  // Only called on a form that passed limitsValidator, so every filled value is well-formed.
  private buildLimits(v: ReturnType<typeof this.form.getRawValue>): RuleLimits | null {
    const values = v as unknown as LimitValues;
    const limits: RuleLimits = {};
    for (const group of LIMIT_GROUPS) {
      for (const field of group.fields) {
        if (!this.showLimit(field.apiKey)) continue; // Phase 2: not sent when it doesn't apply
        const raw = values[field.control].trim();
        if (raw === '') continue;
        (limits as Record<string, unknown>)[field.apiKey] =
          field.kind === 'count' ? Number(raw) : decimalString(raw);
      }
    }
    if (Object.keys(limits).length === 0) return null;
    // Period / reset window only mean something with a per-period cap; On breach only with a
    // cap that can be partly paid out (rule-limits.ts). Don't send settings that do nothing.
    if (usesPeriod(values) && this.showLimit('period')) {
      limits.period = values.limitPeriod || null;
      limits.reset_window = values.limitResetWindow || 'Calendar';
    }
    if (usesOnBreach(values) && this.showLimit('on_breach')) limits.on_breach = v.limitOnBreach;
    return limits;
  }

  private buildConfiguration(v: ReturnType<typeof this.form.getRawValue>): RuleConfiguration {
    // Phase 2: a field that doesn't apply is sent at its default (the API accepts defaults).
    const posting = this.showConfig('posting') ? v.cfgPosting : 'Immediate';
    const config: RuleConfiguration = {
      posting,
      reversible: this.showConfig('reversible') ? v.cfgReversible : true,
      notifyOnAward: this.showConfig('notifyOnAward') ? v.cfgNotifyOnAward : false,
    };
    if (v.cfgRounding && this.showConfig('rounding')) config.rounding = v.cfgRounding;
    if (v.cfgHoldDays !== null && this.showConfig('posting'))
      config.holdDays = Number(v.cfgHoldDays);
    if (v.cfgExpiryOverrideDays !== null && this.showConfig('expiryOverrideDays'))
      config.expiryOverrideDays = Number(v.cfgExpiryOverrideDays);
    return config;
  }

  protected async submit(): Promise<void> {
    if (this.saving() || this.noRuleType()) return;
    this.formErrors.set([]);
    this.submitted.set(true);

    // CR 2026-10-05 item 4: the form starts with one blank condition, which means "no
    // conditions", not an invalid one.
    const conditions = conditionsForSave(this.conditions());
    const conditionErrors = validateConditionTree(conditions);
    if (this.form.invalid || conditionErrors.length > 0) {
      markAllDirtyAndTouched(this.form);
      this.formErrors.set(
        conditionErrors.length ? conditionErrors : ['Please fix the highlighted fields.'],
      );
      queueMicrotask(() => scrollToFirstError(this.host.nativeElement));
      return;
    }

    this.saving.set(true);
    try {
      const v = this.form.getRawValue();

      const request: CreateRuleRequest = {
        name: v.name,
        trigger: v.trigger,
        targetAccountTypeId: v.type === 'ReversalRule' ? null : v.targetAccountTypeId || null,
        type: v.type,
        calculation: this.buildCalculation(v),
        conditions,
        limits: this.buildLimits(v),
        priority: Number(v.priority),
        stackable: v.stackable,
        configuration: this.buildConfiguration(v),
        activeFrom: localInputToUtcIso(v.activeFrom),
        activeTo: localInputToUtcIso(v.activeTo),
      };

      const ruleId = this.ruleId();
      if (ruleId) {
        await this.rulesService.update(this.programId(), ruleId, request);
        this.toast.success('Rule saved');
      } else {
        await this.rulesService.create(this.programId(), request);
        this.toast.success('Rule created');
      }
      void this.router.navigate(['/programs', this.programId(), 'rules']);
    } catch (err) {
      if (err instanceof ApiError) this.formErrors.set(applyServerErrors(this.form, err));
      else this.formErrors.set(['Something went wrong. Please try again.']);
    } finally {
      this.saving.set(false);
    }
  }
}

/** A stored limit (JSON string or number) as the text the input edits; blank = no limit. */
function toInput(value: string | number | null | undefined): string {
  return value === null || value === undefined ? '' : String(value);
}

function numberOrNull(value: string | null | undefined): number | null {
  if (value === undefined || value === null || value === '') return null;
  return Number(value);
}

/** Group-level: the whole Limits section is invalid while rule-limits.ts reports any error. */
function limitsValidator(group: AbstractControl): ValidationErrors | null {
  const errors = validateLimits(group.getRawValue() as LimitValues);
  return Object.keys(errors).length > 0 ? { limits: errors } : null;
}
