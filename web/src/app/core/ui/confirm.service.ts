import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { DialogService, closeDialogAnimated } from './dialog.service';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  /** Styles the confirm button as destructive and focuses Cancel by default. */
  destructive?: boolean;
}

@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="w-[26rem] max-w-[92vw] rounded-xl bg-white p-5 shadow-card">
      <h2 class="text-sm font-bold text-gray-900">{{ data.title }}</h2>
      <p class="mt-2 text-sm text-gray-600">{{ data.message }}</p>
      <div class="mt-5 flex justify-end gap-2">
        <button
          type="button"
          class="rounded-lg px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-100"
          [attr.autofocus]="data.destructive ? '' : null"
          (click)="closeAnimated(false)"
        >
          {{ data.cancelLabel ?? 'Cancel' }}
        </button>
        <button
          type="button"
          class="rounded-lg px-4 py-2 text-sm font-medium text-white"
          [class]="data.destructive ? 'bg-danger-fg hover:opacity-90' : 'bg-brand hover:bg-brand-dark'"
          [attr.autofocus]="data.destructive ? null : ''"
          (click)="closeAnimated(true)"
        >
          {{ data.confirmLabel ?? 'Confirm' }}
        </button>
      </div>
    </div>
  `,
})
export class ConfirmDialog {
  readonly ref = inject<DialogRef<boolean, ConfirmDialog>>(DialogRef);
  readonly data = inject<ConfirmOptions>(DIALOG_DATA);

  protected closeAnimated(result: boolean): void {
    closeDialogAnimated(this.ref, result);
  }
}

/** `await confirm.ask({ title, message, destructive: true })` -> boolean. */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(DialogService);

  async ask(options: ConfirmOptions): Promise<boolean> {
    const ref = this.dialog.open<boolean, ConfirmOptions, ConfirmDialog>(ConfirmDialog, {
      data: options,
    });
    return (await firstValueFrom(ref.closed)) === true;
  }
}
