import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { closeDialogAnimated } from '../../../core/ui/dialog.service';
import { DialogShell } from '../../../shared/ui/dialog-shell';
import { Button } from '../../../shared/ui/button';
import { FormErrors } from '../../../shared/ui/form-errors';
import { FieldHint } from '../../../shared/ui/field-hint';
import { ApiError } from '../../../core/http/api-error';
import { applyServerErrors } from '../../../shared/forms/server-errors';
import { markAllDirtyAndTouched } from '../../../shared/forms/form-utils';
import { requiredWhen } from '../../../shared/forms/conditional-validators';
import { decimalString } from '../../../shared/money/decimal-string';
import { TiersService } from './tiers.service';
import { QualifyingModel, Tier } from './tier.model';

export interface TierFormData {
  programId: string;
  nextSortOrder: number;
  existing?: Tier;
}

@Component({
  selector: 'app-tier-form-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, DialogShell, Button, FormErrors, FieldHint],
  template: `
    <app-dialog-shell [heading]="isEdit ? 'Edit tier' : 'New tier'" (closed)="closeAnimated()">
      <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-4">
        <app-form-errors [messages]="formErrors()" />
        <div class="grid grid-cols-2 gap-3">
          <div>
            <label for="tierName" class="field-label">Name (slug)</label>
            <input id="tierName" formControlName="name" class="field-input font-mono text-xs" placeholder="gold" autocomplete="off" />
          </div>
          <div>
            <label for="displayName" class="field-label">Display name</label>
            <input id="displayName" formControlName="displayName" class="field-input" placeholder="Gold" autocomplete="off" />
          </div>
        </div>

        <div>
          <label for="minPoints" class="field-label">Minimum qualifying points</label>
          <input id="minPoints" type="number" min="0" formControlName="minPoints" class="field-input" />
        </div>

        <div>
          <span id="qualifying-model-label" class="field-label mb-2">Qualifying model</span>
          <div class="grid grid-cols-2 gap-2" role="radiogroup" aria-labelledby="qualifying-model-label">
            <label class="radio-card text-sm">
              <input type="radio" value="periodic" formControlName="qualifyingModel" class="sr-only" />
              <div class="font-medium text-gray-800">Periodic</div>
              <div class="mt-0.5 text-xs text-gray-500">Points reset after a rolling window</div>
            </label>
            <label class="radio-card text-sm">
              <input type="radio" value="lifetime" formControlName="qualifyingModel" class="sr-only" />
              <div class="font-medium text-gray-800">Lifetime</div>
              <div class="mt-0.5 text-xs text-gray-500">Points never reset</div>
            </label>
          </div>
          @if (lockQualifyingModel) {
            <app-field-hint hint="Cannot be changed once customer accounts are assigned to this tier." />
          }
        </div>

        @if (form.controls.qualifyingModel.value === 'periodic') {
          <div>
            <label for="qualifyingPeriodDays" class="field-label">Qualifying period (days)</label>
            <input id="qualifyingPeriodDays" type="number" min="1" formControlName="qualifyingPeriodDays" class="field-input" />
          </div>
        }

        <div>
          <label for="graceDays" class="field-label">Grace period (days)</label>
          <input id="graceDays" type="number" min="0" formControlName="graceDays" class="field-input" />
        </div>
      </form>
      <app-button footer variant="secondary" (click)="closeAnimated()">Cancel</app-button>
      <app-button footer type="submit" [pending]="pending()" (click)="submit()">{{ isEdit ? 'Save' : 'Create' }}</app-button>
    </app-dialog-shell>
  `,
})
export class TierFormDialog {
  readonly ref = inject<DialogRef<Tier, TierFormDialog>>(DialogRef);
  private readonly data = inject<TierFormData>(DIALOG_DATA);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly tiers = inject(TiersService);

  protected readonly isEdit = !!this.data.existing;
  protected readonly pending = signal(false);
  protected readonly formErrors = signal<string[]>([]);
  // Server-authoritative lock (TiersAppService rejects the change once accounts are assigned)
  // — disabled here purely as a UX affordance so the user isn't surprised by a 409 on save.
  protected readonly lockQualifyingModel = !!this.data.existing?.hasAssignedAccounts;

  protected readonly form = this.fb.group({
    name: this.fb.control(this.data.existing?.name ?? '', [Validators.required, Validators.maxLength(100)]),
    displayName: this.fb.control(this.data.existing?.displayName ?? '', [Validators.required, Validators.maxLength(255)]),
    minPoints: this.fb.control(Number(this.data.existing?.minPoints ?? 0), [Validators.required, Validators.min(0)]),
    qualifyingModel: this.fb.control<QualifyingModel>({
      value: this.data.existing?.qualifyingModel ?? 'lifetime',
      disabled: this.lockQualifyingModel,
    }),
    qualifyingPeriodDays: this.fb.control(this.data.existing?.qualifyingPeriodDays ?? 365, [
      requiredWhen((root) => root.get('qualifyingModel')?.value === 'periodic'),
    ]),
    graceDays: this.fb.control(this.data.existing?.graceDays ?? 0, [Validators.required, Validators.min(0)]),
  });

  protected closeAnimated(): void {
    closeDialogAnimated(this.ref);
  }

  protected async submit(): Promise<void> {
    if (this.pending()) return;
    if (this.form.invalid) {
      markAllDirtyAndTouched(this.form);
      return;
    }
    this.pending.set(true);
    this.formErrors.set([]);
    try {
      const v = this.form.getRawValue();
      const periodic = v.qualifyingModel === 'periodic';
      const result = this.isEdit
        ? await this.tiers.update(this.data.programId, this.data.existing!.id, {
            name: v.name,
            displayName: v.displayName,
            minPoints: decimalString(v.minPoints),
            qualifyingModel: v.qualifyingModel,
            qualifyingPeriodDays: periodic ? Number(v.qualifyingPeriodDays) : null,
            graceDays: Number(v.graceDays),
          })
        : await this.tiers.create(this.data.programId, {
            name: v.name,
            displayName: v.displayName,
            minPoints: decimalString(v.minPoints),
            qualifyingModel: v.qualifyingModel,
            qualifyingPeriodDays: periodic ? Number(v.qualifyingPeriodDays) : null,
            graceDays: Number(v.graceDays),
            sortOrder: this.data.nextSortOrder,
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
