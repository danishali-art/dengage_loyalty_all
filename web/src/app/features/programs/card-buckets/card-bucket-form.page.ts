import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { PageHeader } from '../../../shared/ui/page-header';
import { Button } from '../../../shared/ui/button';
import { FormErrors } from '../../../shared/ui/form-errors';
import { SearchableSelect, SelectOption } from '../../../shared/ui/searchable-select';
import { ConditionLeafListEditor } from '../../../shared/ui/condition-leaf-list-editor';
import { ToastService } from '../../../core/ui/toast.service';
import { ApiError } from '../../../core/http/api-error';
import { decimalString } from '../../../shared/money/decimal-string';
import { localInputToUtcIso, utcIsoToLocalInput } from '../../../shared/date/utc';
import { ConditionLeaf } from '../../../shared/forms/condition-tree-dsl';
import { CardBucketsService } from './card-buckets.service';
import { AccountTypesService } from '../account-types/account-types.service';
import { AccountType } from '../account-types/account-type.model';
import { CardBucket, CountryMode, CreateCardBucketRequest } from './card-bucket.model';

const DAYS_OF_WEEK = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'] as const;

function toCsv(values: readonly string[] | null | undefined): string {
  return values?.join(', ') ?? '';
}
function fromCsv(raw: string): string[] | null {
  const values = raw.split(',').map((s) => s.trim()).filter((s) => s);
  return values.length ? values : null;
}

@Component({
  selector: 'app-card-bucket-form-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PageHeader, Button, FormErrors, SearchableSelect, ConditionLeafListEditor],
  template: `
    <app-page-header
      [heading]="isEdit() ? 'Edit card bucket' : 'New card bucket'"
      [crumbs]="[
        { label: 'Programs', link: ['/programs'] },
        { label: 'Card Buckets', link: ['/programs', programId(), 'card-buckets'] },
        { label: isEdit() ? 'Edit' : 'New' },
      ]"
    >
      <div actions>
        <app-button variant="secondary" [routerLink]="['/programs', programId(), 'card-buckets']">Cancel</app-button>
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
              <span id="targetAccountTypeId-label" class="field-label">Reward account</span>
              <app-searchable-select formControlName="targetAccountTypeId" [options]="targetAccountOptions()" placeholder="Select an account…" ariaLabelledby="targetAccountTypeId-label" />
            </div>
            <div>
              <label for="rewardAmount" class="field-label">Reward amount</label>
              <input id="rewardAmount" type="number" min="0" step="0.0001" formControlName="rewardAmount" class="field-input" />
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

      <section class="card space-y-4">
        <div class="section-header">
          <h2 class="section-heading">Match on card transactions</h2>
        </div>
        <form [formGroup]="form" class="space-y-4">
          <div>
            <label for="mccCodes" class="field-label">MCC codes (comma-separated, any match)</label>
            <input id="mccCodes" formControlName="mccCodes" class="field-input" placeholder="5411, 5541" />
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label for="amountMin" class="field-label">Amount min</label>
              <input id="amountMin" type="number" min="0" step="0.01" formControlName="amountMin" class="field-input" />
            </div>
            <div>
              <label for="amountMax" class="field-label">Amount max</label>
              <input id="amountMax" type="number" min="0" step="0.01" formControlName="amountMax" class="field-input" />
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label for="countryMode" class="field-label">Geo</label>
              <select id="countryMode" formControlName="countryMode" class="field-input">
                <option value="in">Only these countries</option>
                <option value="not_in">Exclude these countries</option>
              </select>
            </div>
            <div>
              <label for="countries" class="field-label">Countries (ISO codes, comma-separated)</label>
              <input id="countries" formControlName="countries" class="field-input" placeholder="SA, AE" />
            </div>
          </div>
          <label class="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" formControlName="requireCaptured" class="checkbox" />
            Only captured transactions (excludes pre-auth)
          </label>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label for="hourFrom" class="field-label">Hour from (UTC, 0–23)</label>
              <input id="hourFrom" type="number" min="0" max="23" formControlName="hourFrom" class="field-input" />
            </div>
            <div>
              <label for="hourTo" class="field-label">Hour to (UTC, 0–23)</label>
              <input id="hourTo" type="number" min="0" max="23" formControlName="hourTo" class="field-input" />
            </div>
          </div>
          <div>
            <span class="field-label">Days of week</span>
            <div class="flex flex-wrap gap-3">
              @for (day of daysOfWeek; track day) {
                <label class="flex items-center gap-1.5 text-sm text-gray-700">
                  <input type="checkbox" [checked]="isDaySelected(day)" (change)="toggleDay(day)" class="checkbox" />
                  {{ day }}
                </label>
              }
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label for="perCustomerPerDay" class="field-label">Max reward per customer per day</label>
              <input id="perCustomerPerDay" type="number" min="0" step="0.0001" formControlName="perCustomerPerDay" class="field-input" />
            </div>
            <div>
              <label for="perCustomerTotal" class="field-label">Max reward per customer total</label>
              <input id="perCustomerTotal" type="number" min="0" step="0.0001" formControlName="perCustomerTotal" class="field-input" />
            </div>
          </div>
        </form>
      </section>

      <app-condition-leaf-list-editor [leaves]="additionalConditions()" (leavesChange)="additionalConditions.set($event)" />
    </div>
  `,
})
export class CardBucketFormPage implements OnInit {
  readonly programId = input.required<string>();
  readonly bucketId = input<string | undefined>(undefined);

  private readonly bucketsService = inject(CardBucketsService);
  private readonly accountTypesService = inject(AccountTypesService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly daysOfWeek = DAYS_OF_WEEK;
  protected readonly isEdit = signal(false);
  protected readonly saving = signal(false);
  protected readonly formErrors = signal<string[]>([]);
  protected readonly accountTypes = signal<readonly AccountType[]>([]);
  protected readonly additionalConditions = signal<ConditionLeaf[]>([]);
  protected readonly selectedDays = signal<string[]>([]);

  protected readonly targetAccountOptions = computed<SelectOption<string>[]>(() =>
    this.accountTypes().map((at) => ({ value: at.id, label: `${at.name} (${at.type})` })),
  );

  protected readonly form = this.fb.group({
    name: this.fb.control('', [Validators.required, Validators.maxLength(255)]),
    targetAccountTypeId: this.fb.control(''),
    rewardAmount: this.fb.control(1, [Validators.min(0)]),
    activeFrom: this.fb.control(''),
    activeTo: this.fb.control(''),
    mccCodes: this.fb.control(''),
    amountMin: this.fb.control<number | null>(null),
    amountMax: this.fb.control<number | null>(null),
    countryMode: this.fb.control<CountryMode>('in'),
    countries: this.fb.control(''),
    requireCaptured: this.fb.control(true),
    hourFrom: this.fb.control<number | null>(null),
    hourTo: this.fb.control<number | null>(null),
    perCustomerPerDay: this.fb.control<number | null>(null),
    perCustomerTotal: this.fb.control<number | null>(null),
  });

  ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    this.isEdit.set(!!this.bucketId());
    const accountTypes = await this.accountTypesService.listAll(this.programId());
    this.accountTypes.set(accountTypes);
    const firstAccountType = accountTypes[0];
    if (firstAccountType) this.form.patchValue({ targetAccountTypeId: firstAccountType.id });

    const bucketId = this.bucketId();
    if (bucketId) {
      const bucket = await this.bucketsService.get(this.programId(), bucketId);
      this.applyBucket(bucket);
    }
  }

  private applyBucket(bucket: CardBucket): void {
    this.form.patchValue({
      name: bucket.name,
      targetAccountTypeId: bucket.targetAccountTypeId,
      rewardAmount: Number(bucket.rewardAmount),
      activeFrom: utcIsoToLocalInput(bucket.activeFrom),
      activeTo: utcIsoToLocalInput(bucket.activeTo),
      mccCodes: toCsv(bucket.mccCodes),
      amountMin: bucket.amountMin ? Number(bucket.amountMin) : null,
      amountMax: bucket.amountMax ? Number(bucket.amountMax) : null,
      countryMode: bucket.countryMode ?? 'in',
      countries: toCsv(bucket.countries),
      requireCaptured: bucket.requireCaptured,
      hourFrom: bucket.hourFrom,
      hourTo: bucket.hourTo,
      perCustomerPerDay: bucket.perCustomerPerDay ? Number(bucket.perCustomerPerDay) : null,
      perCustomerTotal: bucket.perCustomerTotal ? Number(bucket.perCustomerTotal) : null,
    });
    this.selectedDays.set(bucket.daysOfWeek ? [...bucket.daysOfWeek] : []);
    this.additionalConditions.set(bucket.additionalConditions ? structuredClone(bucket.additionalConditions) : []);
  }

  protected isDaySelected(day: string): boolean {
    return this.selectedDays().includes(day);
  }

  protected toggleDay(day: string): void {
    const current = this.selectedDays();
    this.selectedDays.set(current.includes(day) ? current.filter((d) => d !== day) : [...current, day]);
  }

  protected async submit(): Promise<void> {
    if (this.saving()) return;
    this.formErrors.set([]);

    if (this.form.invalid) {
      this.formErrors.set(['Please fix the highlighted fields.']);
      return;
    }

    this.saving.set(true);
    try {
      const v = this.form.getRawValue();
      const countries = fromCsv(v.countries);

      const request: CreateCardBucketRequest = {
        name: v.name,
        mccCodes: fromCsv(v.mccCodes),
        amountMin: v.amountMin === null ? null : decimalString(v.amountMin),
        amountMax: v.amountMax === null ? null : decimalString(v.amountMax),
        countryMode: countries ? v.countryMode : null,
        countries,
        requireCaptured: v.requireCaptured,
        hourFrom: v.hourFrom,
        hourTo: v.hourTo,
        daysOfWeek: this.selectedDays().length ? this.selectedDays() : null,
        perCustomerPerDay: v.perCustomerPerDay === null ? null : decimalString(v.perCustomerPerDay),
        perCustomerTotal: v.perCustomerTotal === null ? null : decimalString(v.perCustomerTotal),
        targetAccountTypeId: v.targetAccountTypeId,
        rewardAmount: decimalString(v.rewardAmount),
        priority: 100,
        activeFrom: localInputToUtcIso(v.activeFrom),
        activeTo: localInputToUtcIso(v.activeTo),
        additionalConditions: this.additionalConditions().length ? this.additionalConditions() : null,
      };

      const bucketId = this.bucketId();
      if (bucketId) {
        await this.bucketsService.update(this.programId(), bucketId, request);
        this.toast.success('Bucket saved');
      } else {
        await this.bucketsService.create(this.programId(), request);
        this.toast.success('Bucket created');
      }
      void this.router.navigate(['/programs', this.programId(), 'card-buckets']);
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
