import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { closeDialogAnimated } from '../../core/ui/dialog.service';
import { DialogShell } from '../../shared/ui/dialog-shell';
import { Button } from '../../shared/ui/button';
import { CreateApiKeyResult } from './platform.model';

/** Shown once, immediately after creation — the raw key is never retrievable again. */
@Component({
  selector: 'app-api-key-created-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DialogShell, Button],
  template: `
    <app-dialog-shell heading="API key created" (closed)="closeAnimated()">
      <div class="space-y-3">
        <p class="text-sm text-danger-fg" role="alert">
          Copy this key now — it will not be shown again.
        </p>
        <div class="flex items-center gap-2 rounded-lg border border-gray-200 bg-gray-50 p-3">
          <code class="flex-1 break-all font-mono text-xs text-gray-800">{{ data.rawKey }}</code>
          <app-button size="sm" variant="secondary" (click)="copy()">{{ copied() ? 'Copied' : 'Copy' }}</app-button>
        </div>
        <p class="text-xs text-gray-500">Prefix <code class="font-mono">{{ data.prefix }}</code> — use this to identify the key in the list below.</p>
      </div>
      <app-button footer (click)="closeAnimated()">Done</app-button>
    </app-dialog-shell>
  `,
})
export class ApiKeyCreatedDialog {
  readonly ref = inject<DialogRef<void, ApiKeyCreatedDialog>>(DialogRef);
  protected readonly data = inject<CreateApiKeyResult>(DIALOG_DATA);
  protected readonly copied = signal(false);

  protected closeAnimated(): void {
    closeDialogAnimated(this.ref);
  }

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.data.rawKey);
      this.copied.set(true);
    } catch {
      /* clipboard unavailable — the key is still selectable/copyable by hand */
    }
  }
}
