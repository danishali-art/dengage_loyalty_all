import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { TranslatePipe } from '@ngx-translate/core';
import { startWith } from 'rxjs';
import { closeDialogAnimated } from '../../../core/ui/dialog.service';
import { DialogShell } from '../../../shared/ui/dialog-shell';
import { Button } from '../../../shared/ui/button';
import { FieldHint } from '../../../shared/ui/field-hint';
import { FormErrors } from '../../../shared/ui/form-errors';
import { ApiError } from '../../../core/http/api-error';
import { applyServerErrors } from '../../../shared/forms/server-errors';
import { markAllDirtyAndTouched } from '../../../shared/forms/form-utils';
import { requiredWhen } from '../../../shared/forms/conditional-validators';
import { AccountTypesService } from './account-types.service';
import {
  AccountType,
  AccountTypeKind,
  CashConfig,
  DEFAULT_CURRENCY,
  PointsConfig,
  SUPPORTED_CURRENCIES,
} from './account-type.model';

export interface AccountTypeFormData {
  programId: string;
  existing?: AccountType;
  existingAccountTypes?: readonly AccountType[];
}

@Component({
  selector: 'app-account-type-form-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslatePipe, DialogShell, Button, FieldHint, FormErrors],
  template: `
    <app-dialog-shell
      [heading]="isEdit ? 'Edit account type' : 'New account type'"
      (closed)="closeAnimated()"
    >
      <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-4">
        <app-form-errors [messages]="formErrors()" />

        <div>
          <label for="accountTypeName" class="field-label">Account name</label>
          <input
            id="accountTypeName"
            formControlName="name"
            class="field-input"
            autocomplete="off"
          />
        </div>

        <div>
          <label for="type" class="field-label">Account type</label>
          <select id="type" formControlName="type" class="field-input">
            <option value="POINTS">⭐ POINTS</option>
            <option value="CASH">💳 CASH</option>
          </select>
          @if (isEdit) {
            <p class="mt-1 text-xs text-gray-500">Type cannot be changed after creation.</p>
          }
        </div>

        @if (form.controls.type.value === 'POINTS') {
          <div class="grid grid-cols-2 gap-3">
            <div>
              <div class="flex items-center gap-1">
                <label for="decimals" class="field-label">Decimal places</label>
                <!-- 1.3.CL item 2: a focusable button so the explanation reaches keyboard and screen-reader users, not just a hover title. -->
                <button
                  type="button"
                  class="hover:text-brand focus-visible:text-brand mb-1 cursor-help rounded-full text-gray-400"
                  [attr.aria-label]="'accountTypes.decimals.info' | translate"
                  [title]="'accountTypes.decimals.info' | translate"
                >
                  <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true">
                    <path
                      fill-rule="evenodd"
                      d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-7-4a1 1 0 1 1-2 0 1 1 0 0 1 2 0ZM9 9a.75.75 0 0 0 0 1.5h.253a.25.25 0 0 1 .244.304l-.459 2.066A1.75 1.75 0 0 0 10.747 15H11a.75.75 0 0 0 0-1.5h-.253a.25.25 0 0 1-.244-.304l.459-2.066A1.75 1.75 0 0 0 9.253 9H9Z"
                      clip-rule="evenodd"
                    />
                  </svg>
                </button>
              </div>
              <input
                id="decimals"
                type="number"
                min="0"
                max="4"
                step="1"
                formControlName="decimals"
                class="field-input"
              />
            </div>
            <div>
              <label for="expirationDays" class="field-label">Points expiration (days)</label>
              <input
                id="expirationDays"
                type="number"
                min="1"
                formControlName="expirationDays"
                class="field-input"
                placeholder="Never"
              />
            </div>
          </div>
          <div>
            <label for="warningDays" class="field-label">{{
              'accountTypes.warningDays.label' | translate
            }}</label>
            <input
              id="warningDays"
              type="number"
              min="1"
              formControlName="warningDays"
              class="field-input"
              aria-describedby="warningDays-hint"
              [attr.aria-invalid]="!!warningDaysError() || null"
              [placeholder]="'accountTypes.warningDays.placeholder' | translate"
            />
            <app-field-hint
              id="warningDays-hint"
              [error]="warningDaysError() ? (warningDaysError()! | translate) : null"
              [hint]="
                (hasExpiration()
                  ? 'accountTypes.warningDays.hint'
                  : 'accountTypes.warningDays.needsExpiry'
                ) | translate
              "
            />
          </div>
          <div>
            <label class="flex items-center gap-2 text-sm text-gray-700">
              <input
                type="checkbox"
                formControlName="isTierQualifying"
                class="checkbox"
                aria-describedby="tierQualifying-hint"
              />
              {{ 'accountTypes.tierQualifying.label' | translate }}
            </label>
            <app-field-hint
              id="tierQualifying-hint"
              [hint]="'accountTypes.tierQualifying.hint' | translate"
            />
          </div>
          <!-- CR 2026-10-05: defaults only — a new redeem rule for this wallet starts with these
               values; redeem events use the rule, never these. -->
          <label class="flex items-center gap-2 text-sm text-gray-700">
            <input
              type="checkbox"
              formControlName="redemptionEnabled"
              class="checkbox"
              aria-describedby="redemption-hint"
            />
            {{ 'accountTypes.redemption.label' | translate }}
          </label>
          <app-field-hint
            id="redemption-hint"
            [hint]="'accountTypes.redemption.hint' | translate"
          />
          @if (form.controls.redemptionEnabled.value) {
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label for="rate" class="field-label">{{
                  'accountTypes.redemption.cashPerPoint' | translate
                }}</label>
                <input
                  id="rate"
                  type="number"
                  min="0"
                  step="0.0001"
                  formControlName="redemptionRate"
                  class="field-input"
                />
              </div>
              <div>
                <label for="minPoints" class="field-label">{{
                  'accountTypes.redemption.minimum' | translate
                }}</label>
                <input
                  id="minPoints"
                  type="number"
                  min="0"
                  formControlName="redemptionMinPoints"
                  class="field-input"
                />
              </div>
            </div>
            <div>
              <label for="redemptionTarget" class="field-label">{{
                'accountTypes.redemption.redeemInto' | translate
              }}</label>
              @if (redemptionTargets().length > 0) {
                <select
                  id="redemptionTarget"
                  formControlName="redemptionTargetAccountTypeId"
                  class="field-input"
                >
                  <option value="" disabled>
                    {{ 'accountTypes.redemption.selectWallet' | translate }}
                  </option>
                  @for (t of redemptionTargets(); track t.id) {
                    <option [value]="t.id">{{ t.name }}</option>
                  }
                </select>
              } @else {
                <p class="text-xs text-amber-600">
                  {{ 'accountTypes.redemption.noTarget' | translate }}
                </p>
              }
            </div>
          }
          <!-- CR 2026-09-30 addendum A: the daily cap PointsTransferHandler enforces. -->
          <label class="flex items-center gap-2 text-sm text-gray-700">
            <input
              type="checkbox"
              formControlName="transferEnabled"
              class="checkbox"
              aria-describedby="transfer-hint"
            />
            {{ 'accountTypes.transfer.label' | translate }}
          </label>
          <app-field-hint id="transfer-hint" [hint]="'accountTypes.transfer.hint' | translate" />
          @if (form.controls.transferEnabled.value) {
            <div>
              <label for="transferDailyLimit" class="field-label">{{
                'accountTypes.transfer.dailyLimit' | translate
              }}</label>
              <input
                id="transferDailyLimit"
                type="number"
                min="1"
                formControlName="transferDailyLimit"
                class="field-input"
                aria-describedby="transferDailyLimit-hint"
                [attr.aria-invalid]="form.controls.transferDailyLimit.invalid || null"
              />
              <app-field-hint
                id="transferDailyLimit-hint"
                [error]="
                  form.controls.transferDailyLimit.invalid
                    ? ('accountTypes.transfer.dailyLimitError' | translate)
                    : null
                "
                [hint]="'accountTypes.transfer.dailyLimitHint' | translate"
              />
            </div>
          }
        }

        @if (form.controls.type.value === 'CASH') {
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label for="currency" class="field-label">{{
                'accountTypes.currency.label' | translate
              }}</label>
              <!-- 1.3.CL item 4: a fixed list (SupportedCurrencies.cs), locked once created. -->
              <select
                id="currency"
                formControlName="currency"
                class="field-input"
                aria-describedby="currency-hint"
              >
                @for (code of currencies; track code) {
                  <option [value]="code">{{ code }}</option>
                }
              </select>
              @if (isEdit) {
                <app-field-hint
                  id="currency-hint"
                  [hint]="'accountTypes.currency.locked' | translate"
                />
              }
            </div>
            <div>
              <label for="cashDecimals" class="field-label">Decimal places</label>
              <input
                id="cashDecimals"
                type="number"
                min="0"
                max="4"
                step="1"
                formControlName="decimals"
                class="field-input"
              />
            </div>
          </div>
          <!-- 1.3.CL item 3: no Expiration (days) — cash is real credit and never expires. -->
          <p class="text-xs text-gray-500">{{ 'accountTypes.cash.noExpiry' | translate }}</p>
        }
      </form>
      <app-button footer variant="secondary" (click)="closeAnimated()">Cancel</app-button>
      <app-button footer type="submit" [pending]="pending()" (click)="submit()">{{
        isEdit ? 'Save' : 'Create'
      }}</app-button>
    </app-dialog-shell>
  `,
})
export class AccountTypeFormDialog {
  readonly ref = inject<DialogRef<AccountType, AccountTypeFormDialog>>(DialogRef);
  private readonly data = inject<AccountTypeFormData>(DIALOG_DATA);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly accountTypes = inject(AccountTypesService);

  protected readonly isEdit = !!this.data.existing;
  protected readonly pending = signal(false);
  protected readonly formErrors = signal<string[]>([]);
  protected readonly redemptionTargets = (): readonly AccountType[] =>
    (this.data.existingAccountTypes ?? []).filter(
      (t) => t.id !== this.data.existing?.id && t.type !== 'POINTS',
    );

  protected readonly form = this.fb.group({
    name: this.fb.control(this.data.existing?.name ?? '', [
      Validators.required,
      Validators.maxLength(255),
    ]),
    type: this.fb.control<AccountTypeKind>(this.data.existing?.type ?? 'POINTS'),
    decimals: this.fb.control(this.initialDecimals(), [
      Validators.required,
      Validators.min(0),
      Validators.max(4),
    ]),
    expirationDays: this.fb.control(this.initialExpirationDays()),
    warningDays: this.fb.control(this.initialWarningDays()),
    isTierQualifying: this.fb.control(this.data.existing?.isTierQualifying ?? false),
    redemptionEnabled: this.fb.control(this.initialRedemptionEnabled()),
    redemptionRate: this.fb.control(this.initialRedemption()?.rate ?? 0.01, [Validators.min(0)]),
    redemptionMinPoints: this.fb.control(this.initialRedemption()?.min_points ?? 0, [
      Validators.min(0),
    ]),
    redemptionTargetAccountTypeId: this.fb.control(
      this.initialRedemption()?.target_account_type_id ?? '',
    ),
    transferEnabled: this.fb.control(this.initialTransferLimit() !== null),
    transferDailyLimit: this.fb.control<number | null>(this.initialTransferLimit() ?? 1000, [
      requiredWhen((root) => root.get('transferEnabled')?.value === true),
      Validators.min(1),
    ]),
    currency: this.fb.control<string>({ value: this.initialCurrency(), disabled: this.isEdit }),
  });

  protected readonly currencies = SUPPORTED_CURRENCIES;

  constructor() {
    // The limit only counts while transfer is on: disabled, it is left out of the form's
    // validity, so a stale value in a hidden field can never block a save.
    const syncTransferLimit = (enabled: boolean): void => {
      const limit = this.form.controls.transferDailyLimit;
      if (enabled) limit.enable({ emitEvent: false });
      else limit.disable({ emitEvent: false });
    };
    syncTransferLimit(this.form.controls.transferEnabled.value);
    this.form.controls.transferEnabled.valueChanges.subscribe(syncTransferLimit);
  }

  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(startWith(this.form.getRawValue())),
    {
      initialValue: this.form.getRawValue(),
    },
  );

  protected readonly hasExpiration = (): boolean => isFilled(this.formValue().expirationDays);

  /**
   * Mirrors PointsAccountTypeConfigValidator (1.3.CL item 1): warning_days needs expiration_days
   * and must satisfy 0 < warning_days < expiration_days. Returns an i18n key, or null.
   */
  protected readonly warningDaysError = (): string | null => {
    const { warningDays, expirationDays } = this.formValue();
    if (!isFilled(warningDays)) return null;
    if (!isFilled(expirationDays)) return 'accountTypes.warningDays.needsExpiry';
    const warning = Number(warningDays);
    if (!Number.isInteger(warning) || warning <= 0) return 'accountTypes.warningDays.positive';
    if (warning >= Number(expirationDays)) return 'accountTypes.warningDays.beforeExpiry';
    return null;
  };

  protected closeAnimated(): void {
    closeDialogAnimated(this.ref);
  }

  private initialDecimals(): number {
    const c = this.data.existing?.config as PointsConfig | CashConfig | undefined;
    return c && 'decimals' in c ? c.decimals : 0;
  }
  private initialExpirationDays(): number | null {
    const c = this.data.existing?.config as PointsConfig | CashConfig | undefined;
    return c && 'expiration_days' in c ? (c.expiration_days ?? null) : null;
  }
  private initialWarningDays(): number | null {
    const c = this.data.existing?.config as PointsConfig | undefined;
    return c?.warning_days ?? null;
  }
  private initialRedemptionEnabled(): boolean {
    const c = this.data.existing?.config as PointsConfig | undefined;
    return !!c?.redemption;
  }
  private initialRedemption(): {
    rate: number;
    min_points: number;
    target_account_type_id: string;
  } | null {
    const c = this.data.existing?.config as PointsConfig | undefined;
    return c?.redemption ?? null;
  }
  private initialTransferLimit(): number | null {
    const c = this.data.existing?.config as PointsConfig | undefined;
    return c?.transfer?.daily_limit ?? null;
  }
  private initialCurrency(): string {
    const c = this.data.existing?.config as CashConfig | undefined;
    return c?.currency ?? DEFAULT_CURRENCY;
  }

  private buildConfig(): Record<string, unknown> {
    const v = this.form.getRawValue();
    if (v.type === 'POINTS') {
      // Start from the stored config minus the keys this form owns, so a setting the form doesn't
      // show survives a save. Rebuilding from the form alone used to drop `transfer` on every edit.
      const config: Record<string, unknown> = {
        ...withoutKeys(
          this.data.existing?.type === 'POINTS' ? this.data.existing.config : {},
          POINTS_FORM_KEYS,
        ),
        decimals: Number(v.decimals),
      };
      if (isFilled(v.expirationDays)) {
        config['expiration_days'] = Number(v.expirationDays);
        // A warning only means something before expiry — dropped with it (validator agrees).
        if (isFilled(v.warningDays)) config['warning_days'] = Number(v.warningDays);
      }
      if (v.redemptionEnabled) {
        config['redemption'] = {
          rate: Number(v.redemptionRate),
          min_points: Number(v.redemptionMinPoints),
          target_account_type_id: v.redemptionTargetAccountTypeId,
        };
      }
      if (v.transferEnabled) {
        config['transfer'] = { daily_limit: Number(v.transferDailyLimit) };
      }
      return config;
    }
    return { currency: v.currency, decimals: Number(v.decimals) };
  }

  protected async submit(): Promise<void> {
    if (this.pending()) return;
    if (
      this.form.invalid ||
      (this.form.controls.type.value === 'POINTS' && this.warningDaysError())
    ) {
      markAllDirtyAndTouched(this.form);
      return;
    }
    if (
      this.form.controls.type.value === 'POINTS' &&
      this.form.controls.redemptionEnabled.value &&
      !this.form.controls.redemptionTargetAccountTypeId.value
    ) {
      this.formErrors.set(['Select which wallet redeemed points convert into.']);
      return;
    }

    this.pending.set(true);
    this.formErrors.set([]);
    try {
      const { name, type, isTierQualifying: flag } = this.form.getRawValue();
      const config = this.buildConfig();
      // Only POINTS can carry the tier flag (backend rejects it elsewhere) — send it for POINTS only.
      const isTierQualifying = type === 'POINTS' ? flag : undefined;
      const result = this.isEdit
        ? await this.accountTypes.update(this.data.programId, this.data.existing!.id, {
            name,
            config,
            isTierQualifying,
          })
        : await this.accountTypes.create(this.data.programId, {
            name,
            type,
            config,
            isTierQualifying,
          });
      this.ref.close(result);
    } catch (err) {
      if (err instanceof ApiError) this.formErrors.set(applyServerErrors(this.form, err));
      else this.formErrors.set(['Something went wrong. Please try again.']);
    } finally {
      this.pending.set(false);
    }
  }
}

/** A number input that the user left blank comes back as null or ''. */
function isFilled(value: number | string | null | undefined): boolean {
  return value !== null && value !== undefined && `${value}` !== '';
}

/** POINTS config keys the form edits; any other stored key is carried through unchanged. */
const POINTS_FORM_KEYS = [
  'decimals',
  'expiration_days',
  'warning_days',
  'redemption',
  'transfer',
] as const;

function withoutKeys(config: object, keys: readonly string[]): Record<string, unknown> {
  return Object.fromEntries(Object.entries(config).filter(([key]) => !keys.includes(key)));
}
