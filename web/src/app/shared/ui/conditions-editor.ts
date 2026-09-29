import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from './button';
import { IconButton } from './icon-button';
import { SearchableSelect, SelectOption } from './searchable-select';
import {
  CONDITION_OPS,
  ConditionClause,
  ConditionOp,
  defaultValueForOp,
} from '../forms/condition-dsl';

/**
 * The condition-clause editor block, shared by `features/programs/rules` and
 * `features/programs/streak-campaigns` — both evaluate the same condition DSL against an event.
 * Presentational only: no HTTP, driven entirely by `conditions`/`conditionsChange`.
 */
@Component({
  selector: 'app-conditions-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Button, IconButton, SearchableSelect],
  template: `
    <section class="card space-y-4">
      <div class="section-header flex items-center justify-between">
        <h2 class="section-heading">Conditions (optional, all must match)</h2>
        <app-button size="sm" variant="secondary" (click)="add()">Add condition</app-button>
      </div>
      @for (clause of conditions(); track $index; let i = $index) {
        <div class="grid grid-cols-[1fr_auto_1fr_auto] items-start gap-2">
          <input
            class="field-input"
            placeholder="field, e.g. order.category"
            [value]="clause.field"
            (input)="update(i, { field: $any($event.target).value })"
          />
          <app-searchable-select
            [ngModel]="clause.op"
            [ngModelOptions]="{ standalone: true }"
            (ngModelChange)="updateOp(i, $event)"
            [options]="conditionOpOptions"
          />
          @if (clause.op === 'exists') {
            <select class="field-input" [value]="clause.value" (change)="update(i, { value: $any($event.target).value === 'true' })">
              <option [value]="true">true</option>
              <option [value]="false">false</option>
            </select>
          } @else if (clause.op === 'in') {
            <input
              class="field-input"
              placeholder="comma-separated values"
              [value]="asCsv(clause.value)"
              (input)="update(i, { value: $any($event.target).value.split(',').map(s => s.trim()).filter(s => s) })"
            />
          } @else if (clause.op === 'occurred_within') {
            <div class="flex gap-2">
              <input class="field-input" placeholder="after event" [value]="occurredAfterEvent(clause)" (input)="updateOccurredWithin(i, 'after_event', $any($event.target).value)" />
              <input class="field-input" type="number" placeholder="hours" [value]="occurredHours(clause)" (input)="updateOccurredWithin(i, 'hours', $any($event.target).value)" />
            </div>
          } @else {
            <input class="field-input" [value]="clause.value" (input)="update(i, { value: $any($event.target).value })" />
          }
          <app-icon-button ariaLabel="Remove condition" (click)="remove(i)">🗑</app-icon-button>
        </div>
      }
    </section>
  `,
})
export class ConditionsEditor {
  readonly conditions = input.required<ConditionClause[]>();
  readonly conditionsChange = output<ConditionClause[]>();

  protected readonly conditionOpOptions: SelectOption<ConditionOp>[] = CONDITION_OPS.map((op) => ({
    value: op,
    label: op,
  }));

  protected add(): void {
    this.conditionsChange.emit([...this.conditions(), { field: '', op: 'eq', value: defaultValueForOp('eq') }]);
  }

  protected remove(index: number): void {
    this.conditionsChange.emit(this.conditions().filter((_, i) => i !== index));
  }

  protected update(index: number, patch: Partial<ConditionClause>): void {
    this.conditionsChange.emit(this.conditions().map((c, i) => (i === index ? { ...c, ...patch } : c)));
  }

  protected updateOp(index: number, op: ConditionOp): void {
    this.update(index, { op, value: defaultValueForOp(op) });
  }

  protected asCsv(value: ConditionClause['value']): string {
    return Array.isArray(value) ? value.join(', ') : '';
  }

  protected occurredAfterEvent(clause: ConditionClause): string {
    return typeof clause.value === 'object' && !Array.isArray(clause.value) && 'after_event' in clause.value
      ? clause.value.after_event
      : '';
  }

  protected occurredHours(clause: ConditionClause): number {
    return typeof clause.value === 'object' && !Array.isArray(clause.value) && 'hours' in clause.value
      ? clause.value.hours
      : 0;
  }

  protected updateOccurredWithin(index: number, key: 'after_event' | 'hours', raw: string): void {
    const current = this.conditions()[index];
    if (!current) return;
    const base =
      typeof current.value === 'object' && !Array.isArray(current.value) && 'after_event' in current.value
        ? current.value
        : { after_event: '', hours: 24 };
    const value = key === 'hours' ? { ...base, hours: Number(raw) } : { ...base, after_event: raw };
    this.update(index, { value });
  }
}
