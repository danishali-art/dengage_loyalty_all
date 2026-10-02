import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { startWith } from 'rxjs';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { closeDialogAnimated } from '../../../core/ui/dialog.service';
import { DialogShell } from '../../../shared/ui/dialog-shell';
import { Button } from '../../../shared/ui/button';
import { FormErrors } from '../../../shared/ui/form-errors';
import { SearchableSelect, SelectOption } from '../../../shared/ui/searchable-select';
import { ApiError } from '../../../core/http/api-error';
import { applyServerErrors } from '../../../shared/forms/server-errors';
import { markAllDirtyAndTouched } from '../../../shared/forms/form-utils';
import { requiredWhen } from '../../../shared/forms/conditional-validators';
import { decimalString } from '../../../shared/money/decimal-string';
import { RewardsService } from './rewards.service';
import {
  CashbackConfig,
  CREATABLE_ACQUISITIONS,
  CreatableAcquisition,
  CreatableRewardType,
  isRetired,
  Reward,
  RewardTypeConfig,
  rewardTypesFor,
  TierUpgradeConfig,
} from './reward.model';
import {
  AccountType,
  CashConfig,
  DEFAULT_CURRENCY,
  SUPPORTED_CURRENCIES,
} from '../account-types/account-type.model';
import { Tier } from '../tiers/tier.model';

export interface RewardFormData {
  programId: string;
  /** CR 2026-09-30 (A5): reward names are `{programSlug}_{suffix}`. */
  programSlug: string;
  accountTypes: readonly AccountType[];
  tiers: readonly Tier[];
  existing?: Reward;
}

// Money and points stay strings end to end (DecimalString) — up to 16 integer + 4 fractional
// digits, numeric(20,4) — never a JS number.
const DECIMAL = /^\d{1,16}(\.\d{1,4})?$/;
const positiveDecimal = (control: AbstractControl): ValidationErrors | null => {
  const v = String(control.value ?? '').trim();
  if (v === '') return null; // requiredWhen reports absence
  return DECIMAL.test(v) && Number(v) > 0 ? null : { decimal: true };
};

@Component({
  selector: 'app-reward-form-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslatePipe, DialogShell, Button, FormErrors, SearchableSelect],
  template: `
    <app-dialog-shell
      [heading]="(isEdit ? 'rewards.form.editHeading' : 'rewards.form.newHeading') | translate"
      (closed)="closeAnimated()"
    >
      <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-4">
        <app-form-errors [messages]="formErrors()" />

        @if (retired) {
          <p class="rounded bg-gray-50 p-3 text-xs text-gray-600" role="status">
            {{ 'rewards.form.retiredNotice' | translate }}
          </p>
        } @else if (data.existing?.status === 'pending_approval') {
          <p class="rounded bg-gray-50 p-3 text-xs text-gray-600" role="status">
            {{ 'rewards.form.pendingNotice' | translate }}
          </p>
        }

        <div class="grid grid-cols-2 gap-3">
          <div>
            <label for="rewardName" class="field-label">{{
              'rewards.form.name' | translate
            }}</label>
            @if (nameGrandfathered) {
              <input
                id="rewardName"
                class="field-input font-mono text-xs"
                [value]="data.existing!.name"
                readonly
              />
            } @else {
              <div class="flex items-stretch">
                <span
                  class="field-input w-auto rounded-r-none border-r-0 bg-gray-50 font-mono text-xs text-gray-500"
                  aria-hidden="true"
                  >{{ namePrefix }}</span
                >
                <input
                  id="rewardName"
                  formControlName="nameSuffix"
                  class="field-input rounded-l-none font-mono text-xs"
                  autocomplete="off"
                  [attr.aria-describedby]="'rewardName-hint'"
                  [attr.aria-invalid]="invalid('nameSuffix') ? 'true' : null"
                />
              </div>
              <p id="rewardName-hint" class="mt-1 text-xs text-gray-500">
                {{ 'rewards.form.nameHint' | translate: { prefix: namePrefix } }}
              </p>
            }
          </div>
          <div>
            <label for="displayName" class="field-label">{{
              'rewards.form.displayName' | translate
            }}</label>
            <input
              id="displayName"
              formControlName="displayName"
              class="field-input"
              autocomplete="off"
              [attr.aria-invalid]="invalid('displayName') ? 'true' : null"
            />
          </div>
        </div>

        <!-- CR 2026-09-30 item 1: how it's earned comes first; the reward types follow from it. -->
        <div class="grid grid-cols-2 gap-3">
          <div>
            <label for="acquisition" class="field-label">{{
              'rewards.form.acquisition' | translate
            }}</label>
            <select id="acquisition" formControlName="acquisition" class="field-input">
              @for (a of acquisitionOptions; track a) {
                <option [value]="a">{{ 'rewards.acquisition.' + a | translate }}</option>
              }
            </select>
            @if (isEdit) {
              <p class="mt-1 text-xs text-gray-500">
                {{ 'rewards.form.acquisitionLocked' | translate }}
              </p>
            }
          </div>
          <div>
            <label for="rewardType" class="field-label">{{
              'rewards.form.rewardType' | translate
            }}</label>
            <select id="rewardType" formControlName="rewardType" class="field-input">
              @for (t of rewardTypeOptions(); track t) {
                <option [value]="t">{{ 'rewards.type.' + t | translate }}</option>
              }
            </select>
            @if (isEdit) {
              <p class="mt-1 text-xs text-gray-500">
                {{ 'rewards.form.rewardTypeLocked' | translate }}
              </p>
            } @else if (acquisitionValue() === 'points_purchase') {
              <p class="mt-1 text-xs text-gray-500">
                {{ 'rewards.form.tierNeedsStreak' | translate }}
              </p>
            }
          </div>
        </div>

        @if (acquisitionValue() === 'points_purchase') {
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label for="pointsPrice" class="field-label">{{
                'rewards.form.pointsPrice' | translate
              }}</label>
              <input
                id="pointsPrice"
                inputmode="decimal"
                formControlName="pointsPrice"
                class="field-input"
                [attr.aria-invalid]="invalid('pointsPrice') ? 'true' : null"
              />
            </div>
            <div>
              <span id="pointsAccountTypeId-label" class="field-label">{{
                'rewards.form.pointsAccount' | translate
              }}</span>
              <app-searchable-select
                formControlName="pointsAccountTypeId"
                [options]="pointsAccountOptions"
                [placeholder]="'rewards.form.selectAccount' | translate"
                ariaLabelledby="pointsAccountTypeId-label"
                [invalid]="invalid('pointsAccountTypeId')"
              />
            </div>
          </div>
        } @else {
          <p class="text-xs text-gray-500">{{ 'rewards.form.streakHint' | translate }}</p>
        }

        <div class="border-t border-gray-100 pt-4">
          @if (rewardTypeValue() === 'cashback') {
            <p class="section-label mb-3">{{ 'rewards.form.cashbackHeading' | translate }}</p>
            <div class="grid grid-cols-3 gap-3">
              <div>
                <label for="cashbackAmount" class="field-label">{{
                  'rewards.form.amount' | translate
                }}</label>
                <input
                  id="cashbackAmount"
                  inputmode="decimal"
                  formControlName="cashbackAmount"
                  class="field-input"
                  [attr.aria-invalid]="invalid('cashbackAmount') ? 'true' : null"
                />
              </div>
              <!-- Same fixed list as the CASH account type's currency (SupportedCurrencies.cs). -->
              <div>
                <label for="cashbackCurrency" class="field-label">{{
                  'accountTypes.currency.label' | translate
                }}</label>
                <select
                  id="cashbackCurrency"
                  formControlName="cashbackCurrency"
                  class="field-input"
                  [attr.aria-describedby]="currencyLocked ? 'currency-locked' : null"
                >
                  @for (code of currencies; track code) {
                    <option [value]="code">{{ code }}</option>
                  }
                </select>
                @if (currencyLocked) {
                  <p id="currency-locked" class="mt-1 text-xs text-gray-500">
                    {{ 'rewards.form.currencyLocked' | translate }}
                  </p>
                }
              </div>
              <div>
                <span id="cashWallet-label" class="field-label">{{
                  'rewards.form.cashWallet' | translate
                }}</span>
                <app-searchable-select
                  formControlName="cashAccountTypeId"
                  [options]="cashWalletOptions()"
                  [placeholder]="'rewards.form.selectAccount' | translate"
                  ariaLabelledby="cashWallet-label"
                  [invalid]="invalid('cashAccountTypeId')"
                />
                @if (cashWalletOptions().length === 1) {
                  <p class="text-danger-fg mt-1 text-xs">
                    {{ 'rewards.form.noWalletInCurrency' | translate }}
                  </p>
                }
              </div>
            </div>
            @if (!isEdit) {
              <p class="mt-2 text-xs text-gray-500">
                {{ 'rewards.form.approvalHint' | translate }}
              </p>
            }
          }

          @if (rewardTypeValue() === 'tier_upgrade') {
            <p class="section-label mb-3">{{ 'rewards.form.tierHeading' | translate }}</p>
            <div class="grid grid-cols-2 gap-3">
              <div>
                <span id="targetTier-label" class="field-label">{{
                  'rewards.form.targetTier' | translate
                }}</span>
                <app-searchable-select
                  formControlName="targetTierId"
                  [options]="tierOptions"
                  [placeholder]="'rewards.form.selectTier' | translate"
                  ariaLabelledby="targetTier-label"
                  [invalid]="invalid('targetTierId')"
                />
              </div>
              <div>
                <label for="durationDays" class="field-label">{{
                  'rewards.form.durationDays' | translate
                }}</label>
                <input
                  id="durationDays"
                  type="number"
                  min="1"
                  step="1"
                  formControlName="durationDays"
                  class="field-input"
                  [placeholder]="'rewards.form.untilNextReview' | translate"
                  aria-describedby="durationDays-hint"
                />
                <p id="durationDays-hint" class="mt-1 text-xs text-gray-500">
                  {{ 'rewards.form.durationHint' | translate }}
                </p>
              </div>
            </div>
          }
        </div>
      </form>
      <app-button footer variant="secondary" (click)="closeAnimated()">{{
        (retired ? 'rewards.form.close' : 'action.cancel') | translate
      }}</app-button>
      @if (!retired) {
        <app-button footer type="submit" [pending]="pending()" (click)="submit()">{{
          (isEdit ? 'action.save' : 'rewards.form.create') | translate
        }}</app-button>
      }
    </app-dialog-shell>
  `,
})
export class RewardFormDialog {
  readonly ref = inject<DialogRef<Reward, RewardFormDialog>>(DialogRef);
  protected readonly data = inject<RewardFormData>(DIALOG_DATA);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly rewards = inject(RewardsService);
  private readonly translate = inject(TranslateService);

  protected readonly isEdit = !!this.data.existing;
  /** A1: retired rows stay viewable but can't be changed (the API returns reward_type_retired). */
  protected readonly retired = !!this.data.existing && isRetired(this.data.existing);
  protected readonly namePrefix = `${this.data.programSlug}_`;
  /** A pre-CR name without the prefix: kept as-is until renamed (§3.10 grandfathering). */
  protected readonly nameGrandfathered =
    this.isEdit && !this.data.existing!.name.startsWith(this.namePrefix);
  /** §3.3: an approved cashback's currency is locked (the API returns currency_locked). */
  protected readonly currencyLocked = !!this.data.existing?.approvedBy;
  protected readonly currencies = SUPPORTED_CURRENCIES;
  protected readonly acquisitionOptions = CREATABLE_ACQUISITIONS;

  protected readonly pending = signal(false);
  protected readonly formErrors = signal<string[]>([]);
  // Field errors stay hidden until a first submit attempt, then update live — aria-invalid must
  // not show on a pristine form.
  protected readonly submitted = signal(false);

  private readonly pointsAccounts = this.data.accountTypes.filter((a) => a.type === 'POINTS');
  private readonly cashWallets = this.data.accountTypes.filter((a) => a.type === 'CASH');
  protected readonly pointsAccountOptions: SelectOption<string | null>[] = [
    { value: null, label: '—' },
    ...this.pointsAccounts.map((a) => ({ value: a.id, label: a.name })),
  ];
  protected readonly tierOptions: SelectOption<string | null>[] = [
    { value: null, label: '—' },
    ...this.data.tiers.map((t) => ({ value: t.id, label: t.displayName })),
  ];

  private readonly cashbackConfig = this.data.existing?.typeConfig as
    Partial<CashbackConfig> | undefined;
  private readonly tierUpgradeConfig = this.data.existing?.typeConfig as
    Partial<TierUpgradeConfig> | undefined;

  protected readonly form = this.fb.group({
    nameSuffix: this.fb.control(this.initialSuffix(), [
      Validators.required,
      Validators.pattern(/^[a-z0-9_]+$/),
    ]),
    displayName: this.fb.control(this.data.existing?.displayName ?? '', [
      Validators.required,
      Validators.maxLength(255),
    ]),
    acquisition: this.fb.control<CreatableAcquisition>(
      (this.data.existing?.acquisition as CreatableAcquisition | undefined) ?? 'points_purchase',
    ),
    rewardType: this.fb.control<CreatableRewardType>(
      (this.data.existing?.rewardType as CreatableRewardType | undefined) ?? 'cashback',
    ),
    pointsPrice: this.fb.control(this.data.existing?.pointsPrice ?? '100', [
      requiredWhen((root) => root.get('acquisition')?.value === 'points_purchase'),
      positiveDecimal,
    ]),
    pointsAccountTypeId: this.fb.control<string | null>(
      this.data.existing?.pointsAccountTypeId ?? null,
      [requiredWhen((root) => root.get('acquisition')?.value === 'points_purchase')],
    ),

    // cashback
    cashbackAmount: this.fb.control(this.cashbackConfig?.amount ?? '10.00', [
      requiredWhen((root) => root.get('rewardType')?.value === 'cashback'),
      positiveDecimal,
    ]),
    cashbackCurrency: this.fb.control<string>(this.cashbackConfig?.currency ?? DEFAULT_CURRENCY),
    cashAccountTypeId: this.fb.control<string | null>(
      this.cashbackConfig?.cash_account_type_id ?? null,
      [requiredWhen((root) => root.get('rewardType')?.value === 'cashback')],
    ),

    // tier_upgrade
    targetTierId: this.fb.control<string | null>(this.tierUpgradeConfig?.target_tier_id ?? null, [
      requiredWhen((root) => root.get('rewardType')?.value === 'tier_upgrade'),
    ]),
    durationDays: this.fb.control<number | null>(this.tierUpgradeConfig?.duration_days ?? null, [
      Validators.min(1),
    ]),
  });

  protected readonly acquisitionValue = toSignal(
    this.form.controls.acquisition.valueChanges.pipe(
      startWith(this.form.controls.acquisition.value),
    ),
    { initialValue: this.form.controls.acquisition.value },
  );
  protected readonly rewardTypeValue = toSignal(
    this.form.controls.rewardType.valueChanges.pipe(startWith(this.form.controls.rewardType.value)),
    { initialValue: this.form.controls.rewardType.value },
  );
  private readonly currencyValue = toSignal(
    this.form.controls.cashbackCurrency.valueChanges.pipe(
      startWith(this.form.controls.cashbackCurrency.value),
    ),
    { initialValue: this.form.controls.cashbackCurrency.value },
  );

  /** Mirrors RewardType.IsAllowedWith — a tier upgrade is only offered for streak completion. */
  protected readonly rewardTypeOptions = computed(() => rewardTypesFor(this.acquisitionValue()));

  /** §3.3: only CASH wallets in the chosen currency can receive the cashback. */
  protected readonly cashWalletOptions = computed<SelectOption<string | null>[]>(() => [
    { value: null, label: '—' },
    ...this.cashWallets
      .filter((w) => (w.config as CashConfig).currency === this.currencyValue())
      .map((w) => ({ value: w.id, label: w.name })),
  ]);

  constructor() {
    if (this.isEdit) {
      this.form.controls.acquisition.disable();
      this.form.controls.rewardType.disable();
    }
    if (this.currencyLocked) this.form.controls.cashbackCurrency.disable();
    if (this.retired) this.form.disable();

    // requiredWhen validators read a sibling control's value but only Angular's own value
    // changes trigger revalidation — without this, switching acquisition/rewardType away from
    // the option that made a field required leaves it permanently (and invisibly) invalid.
    this.form.controls.acquisition.valueChanges.subscribe((acquisition) => {
      // Item 3: points_purchase can't carry a tier upgrade — fall back to cashback.
      if (!rewardTypesFor(acquisition).includes(this.form.controls.rewardType.value)) {
        this.form.controls.rewardType.setValue('cashback');
      }
      for (const key of ['pointsPrice', 'pointsAccountTypeId'] as const) {
        this.form.controls[key].updateValueAndValidity({ emitEvent: false });
      }
    });
    this.form.controls.rewardType.valueChanges.subscribe(() => {
      for (const key of ['cashbackAmount', 'cashAccountTypeId', 'targetTierId'] as const) {
        this.form.controls[key].updateValueAndValidity({ emitEvent: false });
      }
    });
    // A wallet in another currency is no longer a valid choice.
    this.form.controls.cashbackCurrency.valueChanges.subscribe((currency) => {
      const wallet = this.cashWallets.find(
        (w) => w.id === this.form.controls.cashAccountTypeId.value,
      );
      if (wallet && (wallet.config as CashConfig).currency !== currency) {
        this.form.controls.cashAccountTypeId.setValue(null);
      }
    });
  }

  // Re-run invalid() on any validity change, not just the checked control's own value — a
  // sibling's requiredWhen predicate can flip this control's validity via updateValueAndValidity.
  private readonly formStatus = toSignal(
    this.form.statusChanges.pipe(startWith(this.form.status)),
    {
      initialValue: this.form.status,
    },
  );

  /** True once the user has attempted a submit and this control is still invalid. */
  protected invalid(name: keyof typeof this.form.controls): boolean {
    this.formStatus();
    return this.submitted() && !!this.form.controls[name].invalid;
  }

  protected closeAnimated(): void {
    closeDialogAnimated(this.ref);
  }

  private initialSuffix(): string {
    const name = this.data.existing?.name;
    if (!name) return '';
    const prefix = `${this.data.programSlug}_`;
    return name.startsWith(prefix) ? name.slice(prefix.length) : name;
  }

  private buildTypeConfig(): RewardTypeConfig {
    const v = this.form.getRawValue();
    if (v.rewardType === 'cashback') {
      return {
        amount: decimalString(v.cashbackAmount),
        currency: v.cashbackCurrency,
        cash_account_type_id: v.cashAccountTypeId!,
      } satisfies CashbackConfig;
    }
    const config: TierUpgradeConfig = { target_tier_id: v.targetTierId! };
    if (v.durationDays !== null && String(v.durationDays) !== '')
      config.duration_days = Number(v.durationDays);
    return config;
  }

  protected async submit(): Promise<void> {
    if (this.pending() || this.retired) return;
    this.submitted.set(true);
    if (this.nameGrandfathered) this.form.controls.nameSuffix.disable();
    if (this.form.invalid) {
      markAllDirtyAndTouched(this.form);
      this.formErrors.set([String(this.translate.instant('rewards.form.fixFields'))]);
      return;
    }
    this.pending.set(true);
    this.formErrors.set([]);
    try {
      const v = this.form.getRawValue();
      const typeConfig = this.buildTypeConfig();
      const purchase = v.acquisition === 'points_purchase';
      const name = `${this.namePrefix}${v.nameSuffix}`;
      const result = this.isEdit
        ? await this.rewards.update(this.data.programId, this.data.existing!.id, {
            ...(this.nameGrandfathered || name === this.data.existing!.name ? {} : { name }),
            displayName: v.displayName,
            pointsPrice: purchase ? decimalString(v.pointsPrice) : null,
            pointsAccountTypeId: purchase ? v.pointsAccountTypeId : null,
            typeConfig,
          })
        : await this.rewards.create(this.data.programId, {
            name,
            displayName: v.displayName,
            acquisition: v.acquisition,
            rewardType: v.rewardType,
            pointsPrice: purchase ? decimalString(v.pointsPrice) : null,
            pointsAccountTypeId: purchase ? v.pointsAccountTypeId : null,
            typeConfig,
            isActive: true,
          });
      this.ref.close(result);
    } catch (err) {
      if (err instanceof ApiError) this.formErrors.set(applyServerErrors(this.form, err));
      else this.formErrors.set([String(this.translate.instant('rewards.form.genericError'))]);
    } finally {
      this.pending.set(false);
    }
  }
}
