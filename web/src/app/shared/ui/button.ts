import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md';

const VARIANT: Record<ButtonVariant, string> = {
  primary: 'bg-brand text-white hover:bg-brand-dark',
  secondary: 'border border-gray-300 text-gray-700 bg-white hover:bg-gray-50',
  ghost: 'text-gray-600 hover:bg-gray-100',
  danger: 'border border-danger-border text-danger-fg hover:bg-danger-bg',
};

@Component({
  selector: 'app-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      [type]="type()"
      [disabled]="disabled() || pending()"
      [attr.aria-busy]="pending() ? 'true' : null"
      [class]="classes()"
    >
      @if (pending()) {
        <span
          class="inline-block h-3.5 w-3.5 animate-spin rounded-full border-2 border-current border-t-transparent"
          aria-hidden="true"
        ></span>
      }
      <ng-content />
    </button>
  `,
})
export class Button {
  readonly variant = input<ButtonVariant>('primary');
  readonly size = input<ButtonSize>('md');
  readonly type = input<'button' | 'submit' | 'reset'>('button');
  readonly disabled = input(false);
  readonly pending = input(false);
  readonly block = input(false);

  protected readonly classes = computed(() => {
    const size = this.size() === 'sm' ? 'px-3 py-1.5 text-xs' : 'px-4 py-2 text-sm';
    const width = this.block() ? 'w-full' : '';
    return [
      'inline-flex items-center justify-center gap-2 rounded-lg font-medium',
      'cursor-pointer transition-all duration-150 active:scale-[0.98]',
      'disabled:cursor-not-allowed disabled:opacity-60 disabled:active:scale-100',
      size,
      width,
      VARIANT[this.variant()] ?? VARIANT.primary,
    ]
      .filter(Boolean)
      .join(' ');
  });
}
