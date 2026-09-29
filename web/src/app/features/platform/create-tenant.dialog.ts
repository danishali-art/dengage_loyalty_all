import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogRef } from '@angular/cdk/dialog';
import { closeDialogAnimated } from '../../core/ui/dialog.service';
import { DialogShell } from '../../shared/ui/dialog-shell';
import { Button } from '../../shared/ui/button';
import { FormErrors } from '../../shared/ui/form-errors';
import { ApiError } from '../../core/http/api-error';
import { applyServerErrors } from '../../shared/forms/server-errors';
import { markAllDirtyAndTouched } from '../../shared/forms/form-utils';
import { PlatformService } from './platform.service';
import { Tenant } from '../../core/tenant/tenant.model';

const TENANT_ID_PATTERN = /^[a-z0-9_]{1,50}$/;

@Component({
  selector: 'app-create-tenant-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, DialogShell, Button, FormErrors],
  template: `
    <app-dialog-shell heading="New tenant" (closed)="closeAnimated()">
      <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-4">
        <app-form-errors [messages]="formErrors()" />
        <div>
          <label for="id" class="field-label">Tenant id</label>
          <input id="id" formControlName="id" class="field-input" placeholder="novapay" autocomplete="off" />
          <p class="mt-1 text-xs text-gray-500">Lowercase letters, digits, underscores. Cannot be changed later.</p>
          @if (form.controls.id.touched && form.controls.id.errors?.['pattern']) {
            <p class="mt-1 text-xs text-danger-fg">1-50 lowercase letters, digits, or underscores.</p>
          }
        </div>
        <div>
          <label for="name" class="field-label">Display name</label>
          <input id="name" formControlName="name" class="field-input" autocomplete="off" />
        </div>
      </form>
      <app-button footer variant="secondary" (click)="closeAnimated()">Cancel</app-button>
      <app-button footer type="submit" [pending]="pending()" (click)="submit()">Create tenant</app-button>
    </app-dialog-shell>
  `,
})
export class CreateTenantDialog {
  readonly ref = inject<DialogRef<Tenant, CreateTenantDialog>>(DialogRef);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly platform = inject(PlatformService);

  protected readonly pending = signal(false);
  protected readonly formErrors = signal<string[]>([]);

  protected readonly form = this.fb.group({
    id: this.fb.control('', [Validators.required, Validators.pattern(TENANT_ID_PATTERN)]),
    name: this.fb.control('', [Validators.required, Validators.maxLength(255)]),
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
      const tenant = await this.platform.createTenant(this.form.getRawValue());
      this.ref.close(tenant);
    } catch (err) {
      if (err instanceof ApiError) {
        this.formErrors.set(applyServerErrors(this.form, err));
      } else {
        this.formErrors.set(['Something went wrong. Please try again.']);
      }
    } finally {
      this.pending.set(false);
    }
  }
}
