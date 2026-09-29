import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  computed,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export interface SelectOption<T> {
  value: T;
  label: string;
  /** Optional group header rendered above the first consecutive option carrying it — pass
   * options pre-sorted so options sharing a group are adjacent. */
  group?: string;
}

/**
 * A `<select>` replacement that adds a filter box once there are more than a handful of
 * options — built the same lightweight way `layout/tenant-switcher/tenant-switcher.ts` does its
 * own dropdown (`relative` wrapper + `absolute` panel, no CDK Overlay). Implements
 * `ControlValueAccessor` so it drops into an existing `formControlName` binding unchanged.
 */
@Component({
  selector: 'app-searchable-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => SearchableSelect), multi: true },
  ],
  template: `
    <div class="relative">
      <button
        type="button"
        class="field-input flex w-full cursor-pointer items-center justify-between gap-2 text-left transition-colors hover:border-gray-300"
        [class.opacity-60]="disabled()"
        [disabled]="disabled()"
        role="combobox"
        aria-haspopup="listbox"
        [attr.aria-expanded]="open()"
        [attr.aria-controls]="open() ? listboxId : null"
        [attr.aria-labelledby]="ariaLabelledby()"
        [attr.aria-invalid]="invalid() ? 'true' : null"
        (click)="toggle()"
      >
        <span class="truncate" [class.text-gray-400]="!selectedLabel()">{{ selectedLabel() ?? placeholder() }}</span>
        <span aria-hidden="true" class="flex-shrink-0 text-gray-400">▾</span>
      </button>

      @if (open()) {
        <div
          class="absolute z-50 mt-1 w-full overflow-hidden rounded-lg border border-gray-200 bg-white shadow-card"
        >
          @if (showFilter()) {
            <div class="border-b border-gray-100 p-1.5">
              <input
                #filterInput
                type="text"
                class="w-full rounded-md border-0 bg-gray-50 px-2.5 py-1.5 text-sm outline-none focus:ring-2 focus:ring-brand/20"
                placeholder="Search…"
                [value]="filter()"
                (input)="onFilterInput($any($event.target).value)"
                (keydown)="onKeydown($event)"
              />
            </div>
          }
          <ul [id]="listboxId" role="listbox" class="max-h-56 overflow-y-auto py-1">
            @for (option of filteredOptions(); track option.value; let i = $index) {
              @if (groupHeaderFor(i); as header) {
                <li role="presentation" class="px-3 pt-2 pb-1 text-xs font-semibold uppercase tracking-wide text-gray-400">
                  {{ header }}
                </li>
              }
              <li
                role="option"
                tabindex="-1"
                [attr.aria-selected]="option.value === value()"
                class="cursor-pointer px-3 py-2 text-sm transition-colors"
                [class]="
                  i === highlighted()
                    ? 'bg-brand-light text-brand'
                    : option.value === value()
                      ? 'font-medium text-gray-900'
                      : 'text-gray-700 hover:bg-gray-50'
                "
                (mouseenter)="highlighted.set(i)"
                (click)="select(option)"
                (keydown)="onKeydown($event)"
              >
                {{ option.label }}
              </li>
            } @empty {
              <li class="px-3 py-6 text-center text-sm text-gray-400">No matches</li>
            }
          </ul>
        </div>
      }
    </div>
  `,
})
export class SearchableSelect<T> implements ControlValueAccessor {
  private static nextId = 0;
  protected readonly listboxId = `searchable-select-${SearchableSelect.nextId++}`;

  readonly options = input.required<readonly SelectOption<T>[]>();
  readonly placeholder = input('Select…');
  /** Id of an external `<span>`/label element describing this field (it isn't a native input, so
   * a `<label for>` can't reach it — pass the label's id through here instead). */
  readonly ariaLabelledby = input<string | null>(null);
  readonly invalid = input(false);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly value = signal<T | null>(null);
  protected readonly disabled = signal(false);
  protected readonly open = signal(false);
  protected readonly filter = signal('');
  protected readonly highlighted = signal(0);

  protected readonly showFilter = computed(() => this.options().length > 5);
  protected readonly selectedLabel = computed(
    () => this.options().find((o) => o.value === this.value())?.label ?? null,
  );
  protected readonly filteredOptions = computed(() => {
    const term = this.filter().trim().toLowerCase();
    const all = this.options();
    return term ? all.filter((o) => o.label.toLowerCase().includes(term)) : all;
  });

  /** The group header to render above option `index`, or null if it continues the prior
   * option's group (or neither option carries a group). */
  protected groupHeaderFor(index: number): string | null {
    const options = this.filteredOptions();
    const group = options[index]?.group;
    if (!group) return null;
    return options[index - 1]?.group === group ? null : group;
  }

  private onChange: (value: T | null) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  writeValue(value: T | null): void {
    this.value.set(value);
  }
  registerOnChange(fn: (value: T | null) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }

  protected toggle(): void {
    if (this.disabled()) return;
    if (this.open()) this.close();
    else this.openPanel();
  }

  private openPanel(): void {
    this.filter.set('');
    this.highlighted.set(Math.max(0, this.options().findIndex((o) => o.value === this.value())));
    this.open.set(true);
  }

  private close(): void {
    this.open.set(false);
    this.onTouched();
  }

  protected onFilterInput(term: string): void {
    this.filter.set(term);
    this.highlighted.set(0);
  }

  protected select(option: SelectOption<T>): void {
    this.value.set(option.value);
    this.onChange(option.value);
    this.close();
  }

  protected onKeydown(event: KeyboardEvent): void {
    const options = this.filteredOptions();
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.highlighted.set(Math.min(options.length - 1, this.highlighted() + 1));
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.highlighted.set(Math.max(0, this.highlighted() - 1));
        break;
      case 'Enter': {
        event.preventDefault();
        const chosen = options[this.highlighted()];
        if (chosen) this.select(chosen);
        break;
      }
      case 'Escape':
        event.preventDefault();
        this.close();
        break;
    }
  }
}
