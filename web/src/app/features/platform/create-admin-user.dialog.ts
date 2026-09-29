import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { closeDialogAnimated } from '../../core/ui/dialog.service';
import { DialogShell } from '../../shared/ui/dialog-shell';
import { Button } from '../../shared/ui/button';
import { FormErrors } from '../../shared/ui/form-errors';
import { ApiError } from '../../core/http/api-error';
import { applyServerErrors } from '../../shared/forms/server-errors';
import { markAllDirtyAndTouched } from '../../shared/forms/form-utils';
import { PlatformService } from './platform.service';
import { AdminUser } from './platform.model';

@Component({
  selector: 'app-create-admin-user-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, DialogShell, Button, FormErrors],
  template: `
    <app-dialog-shell heading="New tenant admin" (closed)="closeAnimated()">
      <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-4">
        <app-form-errors [messages]="formErrors()" />
        <p class="text-sm text-gray-500">Grants sign-in access to <strong>{{ tenantId }}</strong>.</p>
        <div>
          <label for="email" class="field-label">Email</label>
          <input id="email" type="email" formControlName="email" class="field-input" autocomplete="off" />
        </div>
        <div>
          <label for="password" class="field-label">Temporary password</label>
          <input id="password" type="text" formControlName="password" class="field-input" autocomplete="off" />
          <p class="mt-1 text-xs text-gray-500">At least 12 characters. Share it with the tenant admin out of band.</p>
        </div>
      </form>
      <app-button footer variant="secondary" (click)="closeAnimated()">Cancel</app-button>
      <app-button footer type="submit" [pending]="pending()" (click)="submit()">Create admin</app-button>
    </app-dialog-shell>
  `,
})
export class CreateAdminUserDialog {
  readonly ref = inject<DialogRef<AdminUser, CreateAdminUserDialog>>(DialogRef);
  protected readonly tenantId = inject<string>(DIALOG_DATA);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly platform = inject(PlatformService);

  protected readonly pending = signal(false);
  protected readonly formErrors = signal<string[]>([]);

  protected readonly form = this.fb.group({
    email: this.fb.control('', [Validators.required, Validators.email]),
    password: this.fb.control('', [Validators.required, Validators.minLength(12)]),
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
      const { email, password } = this.form.getRawValue();
      const user = await this.platform.createAdminUser(this.tenantId, email, password);
      this.ref.close(user);
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
