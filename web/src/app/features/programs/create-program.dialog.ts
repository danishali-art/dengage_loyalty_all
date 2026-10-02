import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { DialogRef } from '@angular/cdk/dialog';
import { closeDialogAnimated } from '../../core/ui/dialog.service';
import { DialogShell } from '../../shared/ui/dialog-shell';
import { Button } from '../../shared/ui/button';
import { FormErrors } from '../../shared/ui/form-errors';
import { ApiError } from '../../core/http/api-error';
import { applyServerErrors } from '../../shared/forms/server-errors';
import { markAllDirtyAndTouched } from '../../shared/forms/form-utils';
import { ProgramsService } from './programs.service';
import { PROGRAM_SLUG_PATTERN, Program } from './program.model';

@Component({
  selector: 'app-create-program-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslatePipe, DialogShell, Button, FormErrors],
  template: `
    <app-dialog-shell heading="New program" (closed)="closeAnimated()">
      <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-4">
        <app-form-errors [messages]="formErrors()" />
        <div>
          <label for="name" class="field-label">Program name</label>
          <input id="name" formControlName="name" class="field-input" autocomplete="off" />
        </div>
        <!-- CR 2026-09-30 (A5): prefixes reward names; derived from the name when left empty. -->
        <div>
          <label for="slug" class="field-label">{{ 'programs.slug.label' | translate }}</label>
          <input
            id="slug"
            formControlName="slug"
            class="field-input font-mono text-xs"
            autocomplete="off"
            aria-describedby="slug-hint"
            [attr.aria-invalid]="
              form.controls.slug.invalid && form.controls.slug.touched ? 'true' : null
            "
          />
          <p id="slug-hint" class="mt-1 text-xs text-gray-500">
            {{ 'programs.slug.createHint' | translate }}
          </p>
        </div>
        <div>
          <label for="description" class="field-label">Description</label>
          <textarea
            id="description"
            formControlName="description"
            rows="3"
            class="field-input resize-none"
          ></textarea>
        </div>
      </form>
      <app-button footer variant="secondary" (click)="closeAnimated()">Cancel</app-button>
      <app-button footer type="submit" [pending]="pending()" (click)="submit()"
        >Create program</app-button
      >
    </app-dialog-shell>
  `,
})
export class CreateProgramDialog {
  readonly ref = inject<DialogRef<Program, CreateProgramDialog>>(DialogRef);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly programs = inject(ProgramsService);

  protected readonly pending = signal(false);
  protected readonly formErrors = signal<string[]>([]);

  protected readonly form = this.fb.group({
    name: this.fb.control('', [Validators.required, Validators.maxLength(255)]),
    description: this.fb.control(''),
    slug: this.fb.control('', [
      Validators.minLength(2),
      Validators.maxLength(40),
      Validators.pattern(PROGRAM_SLUG_PATTERN),
    ]),
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
      const { name, description, slug } = this.form.getRawValue();
      // 1.3.CL item 8: a new program is always an inactive draft — publish, then activate.
      const program = await this.programs.create({
        name,
        description: description || null,
        slug: slug || null,
      });
      this.ref.close(program);
    } catch (err) {
      if (err instanceof ApiError) this.formErrors.set(applyServerErrors(this.form, err));
      else this.formErrors.set(['Something went wrong. Please try again.']);
    } finally {
      this.pending.set(false);
    }
  }
}
