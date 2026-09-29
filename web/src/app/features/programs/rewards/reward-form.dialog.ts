import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { startWith } from 'rxjs';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
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
  DiscountConfig,
  FreeProductConfig,
  GiftCardConfig,
  PointsBonusConfig,
  Reward,
  RewardAcquisition,
  RewardType,
  RewardTypeConfig,
  TierUpgradeConfig,
} from './reward.model';
import { AccountType } from '../account-types/account-type.model';
import { Tier } from '../tiers/tier.model';

export interface RewardFormData {
  programId: string;
  accountTypes: readonly AccountType[];
  tiers: readonly Tier[];
  existing?: Reward;
}

@Component({
  selector: 'app-reward-form-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, DialogShell, Button, FormErrors, SearchableSelect],
  template: `
    <app-dialog-shell [heading]="isEdit ? 'Edit reward' : 'New reward'" (closed)="closeAnimated()">
      <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-4">
        <app-form-errors [messages]="formErrors()" />
        <div class="grid grid-cols-2 gap-3">
          <div>
            <label for="rewardName" class="field-label">Name (slug)</label>
            <input id="rewardName" formControlName="name" class="field-input font-mono text-xs" autocomplete="off" [attr.aria-invalid]="invalid('name') ? 'true' : null" />
          </div>
          <div>
            <label for="displayName" class="field-label">Display name</label>
            <input id="displayName" formControlName="displayName" class="field-input" autocomplete="off" [attr.aria-invalid]="invalid('displayName') ? 'true' : null" />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <div>
            <label for="rewardType" class="field-label">Reward type</label>
            <select id="rewardType" formControlName="rewardType" class="field-input" [attr.disabled]="isEdit ? '' : null">
              <option value="points_bonus">Points bonus</option>
              <option value="discount">Discount</option>
              <option value="cashback">Cashback</option>
              <option value="free_product">Free product</option>
              <option value="gift_card">Gift card</option>
              <option value="tier_upgrade">Tier upgrade</option>
            </select>
            @if (isEdit) {
              <p class="mt-1 text-xs text-gray-500">What the reward is — cannot be changed after creation.</p>
            }
          </div>
          <div>
            <label for="acquisition" class="field-label">Acquisition</label>
            <select id="acquisition" formControlName="acquisition" class="field-input" [attr.disabled]="isEdit ? '' : null">
              <option value="points_purchase">Purchase with points</option>
              <option value="stamp_completion">Stamp card completion</option>
              <option value="streak_completion">Streak completion</option>
            </select>
            @if (isEdit) {
              <p class="mt-1 text-xs text-gray-500">How it's earned — cannot be changed after creation.</p>
            }
          </div>
        </div>

        @if (form.controls.acquisition.value === 'points_purchase') {
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label for="pointsPrice" class="field-label">Points price</label>
              <input id="pointsPrice" type="number" min="1" formControlName="pointsPrice" class="field-input" [attr.aria-invalid]="invalid('pointsPrice') ? 'true' : null" />
            </div>
            <div>
              <span id="pointsAccountTypeId-label" class="field-label">Points account</span>
              <app-searchable-select formControlName="pointsAccountTypeId" [options]="pointsAccountOptions" placeholder="Select an account…" ariaLabelledby="pointsAccountTypeId-label" [invalid]="invalid('pointsAccountTypeId')" />
            </div>
          </div>
        }

        @if (form.controls.acquisition.value === 'stamp_completion') {
          <div>
            <span id="stampAccountTypeId-label" class="field-label">Stamp account</span>
            <app-searchable-select formControlName="stampAccountTypeId" [options]="stampAccountOptions" placeholder="Select an account…" ariaLabelledby="stampAccountTypeId-label" [invalid]="invalid('stampAccountTypeId')" />
          </div>
        }

        @if (form.controls.acquisition.value === 'streak_completion') {
          <p class="text-xs text-gray-500">
            Earned automatically when a rule's streak config points its reward at this definition.
          </p>
        }

        <div class="border-t border-gray-100 pt-4">
          <p class="section-label mb-3">{{ typeConfigHeading() }}</p>

          @if (form.controls.rewardType.value === 'points_bonus') {
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label for="pbAmount" class="field-label">Bonus amount</label>
                <input id="pbAmount" type="number" min="0.01" step="0.01" formControlName="pbAmount" class="field-input" [attr.aria-invalid]="invalid('pbAmount') ? 'true' : null" />
              </div>
              <div>
                <span id="pbAccount-label" class="field-label">Points account</span>
                <app-searchable-select formControlName="pbAccountTypeId" [options]="pointsAccountOptions" placeholder="Select an account…" ariaLabelledby="pbAccount-label" [invalid]="invalid('pbAccountTypeId')" />
              </div>
            </div>
          }

          @if (form.controls.rewardType.value === 'discount') {
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label for="discountKind" class="field-label">Discount kind</label>
                <select id="discountKind" formControlName="discountKind" class="field-input">
                  <option value="percentage">Percentage</option>
                  <option value="fixed">Fixed amount</option>
                </select>
              </div>
              <div>
                <label for="discountValue" class="field-label">
                  {{ form.controls.discountKind.value === 'percentage' ? 'Percent off' : 'Amount off' }}
                </label>
                <input id="discountValue" type="number" min="0.01" step="0.01" formControlName="discountValue" class="field-input" [attr.aria-invalid]="invalid('discountValue') ? 'true' : null" />
              </div>
              <div>
                <label for="minPurchaseAmount" class="field-label">Min. purchase (optional)</label>
                <input id="minPurchaseAmount" type="number" min="0" step="0.01" formControlName="minPurchaseAmount" class="field-input" placeholder="None" />
              </div>
              <div>
                <label for="maxDiscountAmount" class="field-label">Max. discount (optional)</label>
                <input id="maxDiscountAmount" type="number" min="0" step="0.01" formControlName="maxDiscountAmount" class="field-input" placeholder="None" />
              </div>
            </div>
          }

          @if (form.controls.rewardType.value === 'cashback') {
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label for="cashbackAmount" class="field-label">Amount</label>
                <input id="cashbackAmount" type="number" min="0.01" step="0.01" formControlName="cashbackAmount" class="field-input" [attr.aria-invalid]="invalid('cashbackAmount') ? 'true' : null" />
              </div>
              <div>
                <label for="cashbackCurrency" class="field-label">Currency</label>
                <input id="cashbackCurrency" formControlName="cashbackCurrency" class="field-input" placeholder="SAR" maxlength="3" />
              </div>
            </div>
          }

          @if (form.controls.rewardType.value === 'free_product') {
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label for="productSku" class="field-label">Product SKU</label>
                <input id="productSku" formControlName="productSku" class="field-input font-mono text-xs" placeholder="FREE_DRINK_M" [attr.aria-invalid]="invalid('productSku') ? 'true' : null" />
              </div>
              <div>
                <label for="quantity" class="field-label">Quantity</label>
                <input id="quantity" type="number" min="1" step="1" formControlName="quantity" class="field-input" [attr.aria-invalid]="invalid('quantity') ? 'true' : null" />
              </div>
            </div>
          }

          @if (form.controls.rewardType.value === 'gift_card') {
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label for="giftCardValue" class="field-label">Value</label>
                <input id="giftCardValue" type="number" min="0.01" step="0.01" formControlName="giftCardValue" class="field-input" [attr.aria-invalid]="invalid('giftCardValue') ? 'true' : null" />
              </div>
              <div>
                <label for="giftCardCurrency" class="field-label">Currency</label>
                <input id="giftCardCurrency" formControlName="giftCardCurrency" class="field-input" placeholder="SAR" maxlength="3" />
              </div>
            </div>
          }

          @if (form.controls.rewardType.value === 'tier_upgrade') {
            <div class="grid grid-cols-2 gap-3">
              <div>
                <span id="targetTier-label" class="field-label">Target tier</span>
                <app-searchable-select formControlName="targetTierId" [options]="tierOptions" placeholder="Select a tier…" ariaLabelledby="targetTier-label" [invalid]="invalid('targetTierId')" />
              </div>
              <div>
                <label for="durationDays" class="field-label">Duration in days (optional)</label>
                <input id="durationDays" type="number" min="1" step="1" formControlName="durationDays" class="field-input" placeholder="Permanent" />
              </div>
            </div>
          }
        </div>
      </form>
      <app-button footer variant="secondary" (click)="closeAnimated()">Cancel</app-button>
      <app-button footer type="submit" [pending]="pending()" (click)="submit()">{{ isEdit ? 'Save' : 'Create' }}</app-button>
    </app-dialog-shell>
  `,
})
export class RewardFormDialog {
  readonly ref = inject<DialogRef<Reward, RewardFormDialog>>(DialogRef);
  private readonly data = inject<RewardFormData>(DIALOG_DATA);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly rewards = inject(RewardsService);

  protected readonly isEdit = !!this.data.existing;
  protected readonly pending = signal(false);
  protected readonly formErrors = signal<string[]>([]);
  // Field errors stay hidden until a first submit attempt, then update live — mirrors
  // reward-form's sibling dialogs. Needed because aria-invalid must not show on a pristine form.
  protected readonly submitted = signal(false);
  protected readonly pointsAccountTypes = this.data.accountTypes.filter((a) => a.type === 'POINTS');
  protected readonly stampAccountTypes = this.data.accountTypes.filter((a) => a.type === 'STAMP');
  protected readonly pointsAccountOptions: SelectOption<string | null>[] = [
    { value: null, label: '—' },
    ...this.pointsAccountTypes.map((a) => ({ value: a.id, label: a.name })),
  ];
  protected readonly stampAccountOptions: SelectOption<string | null>[] = [
    { value: null, label: '—' },
    ...this.stampAccountTypes.map((a) => ({ value: a.id, label: a.name })),
  ];
  protected readonly tierOptions: SelectOption<string | null>[] = [
    { value: null, label: '—' },
    ...this.data.tiers.map((t) => ({ value: t.id, label: t.displayName })),
  ];

  protected readonly typeConfigHeading = (): string => {
    const labels: Record<RewardType, string> = {
      points_bonus: 'Points bonus details',
      discount: 'Discount details',
      cashback: 'Cashback details',
      free_product: 'Free product details',
      gift_card: 'Gift card details',
      tier_upgrade: 'Tier upgrade details',
    };
    return labels[this.form.controls.rewardType.value];
  };

  private readonly pbConfig = this.data.existing?.typeConfig as PointsBonusConfig | undefined;
  private readonly discountConfig = this.data.existing?.typeConfig as DiscountConfig | undefined;
  private readonly cashbackConfig = this.data.existing?.typeConfig as CashbackConfig | undefined;
  private readonly freeProductConfig = this.data.existing?.typeConfig as FreeProductConfig | undefined;
  private readonly giftCardConfig = this.data.existing?.typeConfig as GiftCardConfig | undefined;
  private readonly tierUpgradeConfig = this.data.existing?.typeConfig as TierUpgradeConfig | undefined;

  protected readonly form = this.fb.group({
    name: this.fb.control(this.data.existing?.name ?? '', [Validators.required, Validators.maxLength(100)]),
    displayName: this.fb.control(this.data.existing?.displayName ?? '', [Validators.required, Validators.maxLength(255)]),
    acquisition: this.fb.control<RewardAcquisition>(this.data.existing?.acquisition ?? 'points_purchase'),
    rewardType: this.fb.control<RewardType>(this.data.existing?.rewardType ?? 'points_bonus'),
    pointsPrice: this.fb.control(Number(this.data.existing?.pointsPrice ?? 100), [
      requiredWhen((root) => root.get('acquisition')?.value === 'points_purchase'),
      Validators.min(1),
    ]),
    pointsAccountTypeId: this.fb.control<string | null>(this.data.existing?.pointsAccountTypeId ?? null, [
      requiredWhen((root) => root.get('acquisition')?.value === 'points_purchase'),
    ]),
    stampAccountTypeId: this.fb.control<string | null>(this.data.existing?.stampAccountTypeId ?? null, [
      requiredWhen((root) => root.get('acquisition')?.value === 'stamp_completion'),
    ]),

    // points_bonus
    pbAmount: this.fb.control(Number(this.pbConfig?.amount ?? 100), [
      requiredWhen((root) => root.get('rewardType')?.value === 'points_bonus'),
      Validators.min(0.01),
    ]),
    pbAccountTypeId: this.fb.control<string | null>(this.pbConfig?.account_type_id ?? null, [
      requiredWhen((root) => root.get('rewardType')?.value === 'points_bonus'),
    ]),

    // discount
    discountKind: this.fb.control<'percentage' | 'fixed'>(this.discountConfig?.discount_kind ?? 'percentage'),
    discountValue: this.fb.control(Number(this.discountConfig?.value ?? 10), [
      requiredWhen((root) => root.get('rewardType')?.value === 'discount'),
      Validators.min(0.01),
    ]),
    minPurchaseAmount: this.fb.control(numberOrNull(this.discountConfig?.min_purchase_amount)),
    maxDiscountAmount: this.fb.control(numberOrNull(this.discountConfig?.max_discount_amount)),

    // cashback
    cashbackAmount: this.fb.control(Number(this.cashbackConfig?.amount ?? 10), [
      requiredWhen((root) => root.get('rewardType')?.value === 'cashback'),
      Validators.min(0.01),
    ]),
    cashbackCurrency: this.fb.control(this.cashbackConfig?.currency ?? 'SAR', [Validators.maxLength(3)]),

    // free_product
    productSku: this.fb.control(this.freeProductConfig?.product_sku ?? '', [
      requiredWhen((root) => root.get('rewardType')?.value === 'free_product'),
      Validators.maxLength(100),
    ]),
    quantity: this.fb.control(this.freeProductConfig?.quantity ?? 1, [
      requiredWhen((root) => root.get('rewardType')?.value === 'free_product'),
      Validators.min(1),
    ]),

    // gift_card
    giftCardValue: this.fb.control(Number(this.giftCardConfig?.value ?? 50), [
      requiredWhen((root) => root.get('rewardType')?.value === 'gift_card'),
      Validators.min(0.01),
    ]),
    giftCardCurrency: this.fb.control(this.giftCardConfig?.currency ?? 'SAR', [Validators.maxLength(3)]),

    // tier_upgrade
    targetTierId: this.fb.control<string | null>(this.tierUpgradeConfig?.target_tier_id ?? null, [
      requiredWhen((root) => root.get('rewardType')?.value === 'tier_upgrade'),
    ]),
    durationDays: this.fb.control(numberOrNull(this.tierUpgradeConfig?.duration_days)),
  });

  constructor() {
    // requiredWhen validators read a sibling control's value but only Angular's own value
    // changes trigger revalidation — without this, switching acquisition/rewardType away from
    // the option that made a field required leaves it permanently (and invisibly) marked
    // invalid, and the form can never be submitted again.
    this.form.controls.acquisition.valueChanges.subscribe(() => {
      for (const key of ['pointsPrice', 'pointsAccountTypeId', 'stampAccountTypeId'] as const) {
        this.form.controls[key].updateValueAndValidity({ emitEvent: false });
      }
    });
    this.form.controls.rewardType.valueChanges.subscribe(() => {
      for (const key of [
        'pbAmount',
        'pbAccountTypeId',
        'discountValue',
        'cashbackAmount',
        'productSku',
        'quantity',
        'giftCardValue',
        'targetTierId',
      ] as const) {
        this.form.controls[key].updateValueAndValidity({ emitEvent: false });
      }
    });
  }

  // Re-run invalid() on any validity change, not just the checked control's own value — a
  // sibling's requiredWhen predicate can flip this control's validity via updateValueAndValidity.
  private readonly formStatus = toSignal(this.form.statusChanges.pipe(startWith(this.form.status)), {
    initialValue: this.form.status,
  });

  /** True once the user has attempted a submit and this control is still invalid — drives
   * aria-invalid (and the matching `.field-input[aria-invalid='true']` styling). */
  protected invalid(name: keyof typeof this.form.controls): boolean {
    this.formStatus();
    return this.submitted() && !!this.form.controls[name].invalid;
  }

  protected closeAnimated(): void {
    closeDialogAnimated(this.ref);
  }

  private buildTypeConfig(): RewardTypeConfig {
    const v = this.form.getRawValue();
    switch (v.rewardType) {
      case 'points_bonus':
        return { amount: decimalString(v.pbAmount), account_type_id: v.pbAccountTypeId! } satisfies PointsBonusConfig;
      case 'discount': {
        const config: DiscountConfig = { discount_kind: v.discountKind, value: decimalString(v.discountValue) };
        if (v.minPurchaseAmount !== null) config.min_purchase_amount = decimalString(v.minPurchaseAmount);
        if (v.maxDiscountAmount !== null) config.max_discount_amount = decimalString(v.maxDiscountAmount);
        return config;
      }
      case 'cashback':
        return { amount: decimalString(v.cashbackAmount), currency: v.cashbackCurrency.toUpperCase() } satisfies CashbackConfig;
      case 'free_product':
        return { product_sku: v.productSku, quantity: Number(v.quantity) } satisfies FreeProductConfig;
      case 'gift_card':
        return { value: decimalString(v.giftCardValue), currency: v.giftCardCurrency.toUpperCase() } satisfies GiftCardConfig;
      case 'tier_upgrade': {
        const config: TierUpgradeConfig = { target_tier_id: v.targetTierId! };
        if (v.durationDays !== null) config.duration_days = Number(v.durationDays);
        return config;
      }
    }
  }

  protected async submit(): Promise<void> {
    if (this.pending()) return;
    this.submitted.set(true);
    if (this.form.invalid) {
      markAllDirtyAndTouched(this.form);
      this.formErrors.set(['Please fix the highlighted fields.']);
      return;
    }
    this.pending.set(true);
    this.formErrors.set([]);
    try {
      const v = this.form.getRawValue();
      const typeConfig = this.buildTypeConfig();
      const result = this.isEdit
        ? await this.rewards.update(this.data.programId, this.data.existing!.id, {
            name: v.name,
            displayName: v.displayName,
            pointsPrice: v.acquisition === 'points_purchase' ? decimalString(v.pointsPrice) : null,
            pointsAccountTypeId: v.acquisition === 'points_purchase' ? v.pointsAccountTypeId : null,
            stampAccountTypeId: v.acquisition === 'stamp_completion' ? v.stampAccountTypeId : null,
            typeConfig,
          })
        : await this.rewards.create(this.data.programId, {
            name: v.name,
            displayName: v.displayName,
            acquisition: v.acquisition,
            rewardType: v.rewardType,
            pointsPrice: v.acquisition === 'points_purchase' ? decimalString(v.pointsPrice) : null,
            pointsAccountTypeId: v.acquisition === 'points_purchase' ? v.pointsAccountTypeId : null,
            stampAccountTypeId: v.acquisition === 'stamp_completion' ? v.stampAccountTypeId : null,
            typeConfig,
            isActive: true,
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

function numberOrNull(value: string | number | undefined | null): number | null {
  if (value === undefined || value === null || value === '') return null;
  return Number(value);
}
