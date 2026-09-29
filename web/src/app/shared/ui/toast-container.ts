import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ToastKind, ToastService } from '../../core/ui/toast.service';
import { IconButton } from './icon-button';

const KIND_CLASS: Record<ToastKind, string> = {
  success: 'border-success-border bg-success-bg text-success-fg',
  error: 'border-danger-border bg-danger-bg text-danger-fg',
  warning: 'border-warn-border bg-warn-bg text-warn-fg',
  info: 'border-brand/30 bg-brand-light text-brand',
};

@Component({
  selector: 'app-toast-container',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconButton],
  template: `
    <div class="pointer-events-none fixed right-4 top-4 z-[60] flex w-80 flex-col gap-2">
      @for (toast of toasts(); track toast.id) {
        <div
          class="pointer-events-auto rounded-lg border p-3 shadow-card"
          [class]="cls(toast.kind)"
          [attr.role]="toast.kind === 'error' ? 'alert' : 'status'"
          [attr.aria-live]="toast.kind === 'error' ? 'assertive' : 'polite'"
        >
          <div class="flex items-start gap-2">
            <div class="min-w-0 flex-1">
              <p class="text-sm font-medium">{{ toast.title }}</p>
              @if (toast.detail) {
                <p class="mt-0.5 break-words font-mono text-xs opacity-80">{{ toast.detail }}</p>
              }
              @if (toast.traceId) {
                <p class="mt-0.5 text-xs opacity-70">trace: {{ toast.traceId }}</p>
              }
            </div>
            <app-icon-button ariaLabel="Dismiss notification" (click)="dismiss(toast.id)">✕</app-icon-button>
          </div>
        </div>
      }
    </div>
  `,
})
export class ToastContainer {
  private readonly service = inject(ToastService);
  protected readonly toasts = this.service.toasts;
  protected readonly cls = (k: ToastKind): string => KIND_CLASS[k] ?? KIND_CLASS.info;
  protected readonly hasToasts = computed(() => this.toasts().length > 0);

  protected dismiss(id: number): void {
    this.service.dismiss(id);
  }
}
