import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type StatusTone = 'success' | 'neutral' | 'warning' | 'danger' | 'info' | 'streak';

const TONE: Record<StatusTone, string> = {
  success: 'border-success-border text-success-fg bg-success-bg',
  neutral: 'border-gray-300 text-gray-500 bg-gray-50',
  warning: 'border-warn-border text-warn-fg bg-warn-bg',
  danger: 'border-danger-border text-danger-fg bg-danger-bg',
  info: 'border-brand/30 text-brand bg-brand-light',
  streak: 'border-streak-border text-streak-fg bg-streak-bg',
};

/** Small labelled status chip. The text label is always rendered — never colour-only. */
@Component({
  selector: 'app-status-pill',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-medium"
      [class]="toneClass()"
    >
      @if (dot()) {
        <span class="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true"></span>
      }
      <ng-content />
    </span>
  `,
})
export class StatusPill {
  readonly tone = input<StatusTone>('neutral');
  readonly dot = input(false);
  protected readonly toneClass = computed(() => TONE[this.tone()] ?? TONE.neutral);
}
