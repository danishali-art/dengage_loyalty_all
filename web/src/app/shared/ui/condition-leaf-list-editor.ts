import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from './button';
import { IconButton } from './icon-button';
import {
  ConditionLeaf,
  ConditionLeafOp,
  ConditionValue,
  MONEY_NUMBER_OPS,
  STRING_OPS,
  UNKNOWN_OPS,
  defaultValueForOperator,
  emptyLeaf,
} from '../forms/condition-tree-dsl';
import type { KnownField } from './condition-tree-editor';

export type { KnownField };

/**
 * Flat-list sibling of `ConditionTreeEditor`: a bare `ConditionLeaf[]`, implicitly AND-joined,
 * with no group/AND-OR chrome. Card Buckets' `additionalConditions` is the one caller — the
 * backend (`CardBucketConditionMapper.ToConditions`) always folds it into a single AND group
 * alongside its own structured leaves (MCC, amount, geo, …), so offering OR-grouping here would
 * promise something the API can't represent. Rules use the full `ConditionTreeEditor` instead.
 */
@Component({
  selector: 'app-condition-leaf-list-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Button, IconButton],
  template: `
    <section class="card space-y-3">
      <div class="section-header">
        <h2 class="section-heading">Additional conditions (optional)</h2>
        <p class="mt-1 text-xs text-gray-500">
          Extra leaves matched with AND alongside the structured fields above. An escape hatch for
          anything the form above doesn't cover.
        </p>
      </div>
      @for (leaf of leaves(); track $index; let li = $index) {
        <div class="grid grid-cols-[1.7fr_0.9fr_1.6fr_auto] items-start gap-2">
          <input
            class="field-input font-mono text-xs"
            [class.border-amber-400]="!isDeclared(leaf.field)"
            placeholder="payload field path"
            [value]="leaf.field"
            (input)="updateField(li, $any($event.target).value)"
          />
          <select class="field-input" [value]="leaf.operator" (change)="updateOperator(li, $any($event.target).value)">
            @for (op of opsFor(leaf.field); track op) {
              <option [value]="op">{{ op }}</option>
            }
          </select>
          @if (leaf.operator === 'exists') {
            <select
              class="field-input"
              [value]="leaf.value.data"
              (change)="updateValue(li, { ...leaf.value, data: $any($event.target).value === 'true' })"
            >
              <option [value]="true">present</option>
              <option [value]="false">absent</option>
            </select>
          } @else if (leaf.operator === 'is_null') {
            <div class="field-input flex items-center text-xs text-gray-400">no value needed</div>
          } @else if (leaf.operator === 'in' || leaf.operator === 'not_in') {
            <input
              class="field-input"
              placeholder="comma-separated values"
              [value]="asCsv(leaf.value.data)"
              (input)="
                updateValue(li, {
                  ...leaf.value,
                  type: 'string[]',
                  data: $any($event.target).value.split(',').map(s => s.trim()).filter(s => s),
                })
              "
            />
          } @else if (leaf.operator === 'between') {
            <div class="flex gap-1.5">
              <input
                class="field-input"
                type="number"
                placeholder="min"
                [value]="betweenBound(leaf.value.data, 0)"
                (input)="updateBetween(li, 0, $any($event.target).value)"
              />
              <input
                class="field-input"
                type="number"
                placeholder="max"
                [value]="betweenBound(leaf.value.data, 1)"
                (input)="updateBetween(li, 1, $any($event.target).value)"
              />
            </div>
          } @else {
            <input
              class="field-input"
              [type]="kindFor(leaf.field) === 'string' ? 'text' : 'number'"
              [value]="leaf.value.data"
              (input)="updateValue(li, { ...leaf.value, type: kindFor(leaf.field), data: $any($event.target).value })"
            />
          }
          <app-icon-button ariaLabel="Remove condition" (click)="removeLeaf(li)">✕</app-icon-button>
        </div>
      }
      <app-button size="sm" variant="secondary" (click)="addLeaf()">Add condition</app-button>
    </section>
  `,
})
export class ConditionLeafListEditor {
  readonly leaves = input.required<readonly ConditionLeaf[]>();
  readonly leavesChange = output<ConditionLeaf[]>();
  readonly knownFields = input<readonly KnownField[]>([]);

  private readonly fieldKindMap = computed(
    () => new Map(this.knownFields().map((f) => [f.path, f.kind])),
  );

  protected isDeclared(field: string): boolean {
    return !field || this.fieldKindMap().has(field);
  }

  protected kindFor(field: string): 'money' | 'number' | 'string' {
    return this.fieldKindMap().get(field) ?? 'string';
  }

  protected opsFor(field: string): readonly ConditionLeafOp[] {
    const kind = this.fieldKindMap().get(field);
    if (kind === 'money' || kind === 'number') return MONEY_NUMBER_OPS.concat('exists', 'is_null');
    if (kind === 'string') return STRING_OPS.concat('exists', 'is_null');
    return UNKNOWN_OPS;
  }

  protected asCsv(value: unknown): string {
    return Array.isArray(value) ? value.join(', ') : '';
  }

  protected betweenBound(value: unknown, index: 0 | 1): number {
    return Array.isArray(value) && typeof value[index] === 'number' ? value[index] : 0;
  }

  protected addLeaf(): void {
    this.leavesChange.emit([...this.leaves(), emptyLeaf()]);
  }

  protected removeLeaf(li: number): void {
    this.leavesChange.emit(this.leaves().filter((_, i) => i !== li));
  }

  protected updateField(li: number, field: string): void {
    this.patchLeaf(li, (leaf) => ({ ...leaf, field }));
  }

  protected updateOperator(li: number, operator: ConditionLeafOp): void {
    this.patchLeaf(li, (leaf) => ({ ...leaf, operator, value: defaultValueForOperator(operator) }));
  }

  protected updateValue(li: number, value: ConditionValue): void {
    this.patchLeaf(li, (leaf) => ({ ...leaf, value }));
  }

  protected updateBetween(li: number, index: 0 | 1, raw: string): void {
    const leaf = this.leaves()[li];
    if (!leaf) return;
    const data = leaf.value.data;
    const current: number[] = Array.isArray(data) ? (data as number[]).slice() : [0, 0];
    current[index] = Number(raw);
    this.updateValue(li, { ...leaf.value, type: 'number', data: current });
  }

  private patchLeaf(li: number, fn: (leaf: ConditionLeaf) => ConditionLeaf): void {
    this.leavesChange.emit(this.leaves().map((c, i) => (i === li ? fn(c) : c)));
  }
}
