import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Display-only share of a value in a maximum, as a 0–100 percentage. Inputs stay strings
 * (DecimalString on the wire); the numbers parsed here only size a bar, never a value sent back.
 */
export function meterPercent(
  value: string | number,
  max: string | number | null | undefined,
): number {
  const v = Number(value);
  const m = Number(max);
  if (!Number.isFinite(v) || !Number.isFinite(m) || m <= 0) return 0;
  return Math.min(100, Math.max(0, (v / m) * 100));
}

/**
 * Determinate bar ("120 of 500"). Presentational: the caller supplies the label text, which is
 * always rendered — the bar is never the only way to read the value. Turns to the warning tone
 * when full.
 */
@Component({
  selector: 'app-meter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      role="progressbar"
      aria-valuemin="0"
      aria-valuemax="100"
      [attr.aria-valuenow]="percent()"
      [attr.aria-label]="label()"
    >
      <div class="h-2 w-full overflow-hidden rounded-full bg-gray-100">
        <div
          class="h-full rounded-full"
          [class]="percent() >= 100 ? 'bg-warn-fg' : 'bg-brand'"
          [style.width.%]="percent()"
        ></div>
      </div>
      <p class="mt-1 text-xs text-gray-500">{{ label() }}</p>
    </div>
  `,
})
export class Meter {
  readonly value = input.required<string | number>();
  readonly max = input<string | number | null | undefined>(null);
  readonly label = input.required<string>();
  protected readonly percent = computed(() => meterPercent(this.value(), this.max()));
}
