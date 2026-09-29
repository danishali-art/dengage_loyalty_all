import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from './button';
import { IconButton } from './icon-button';
import {
  ConditionGroup,
  ConditionLeaf,
  ConditionLeafOp,
  ConditionTree,
  ConditionValue,
  GROUP_JOIN_OPS,
  GroupJoinOp,
  MONEY_NUMBER_OPS,
  STRING_OPS,
  UNKNOWN_OPS,
  defaultValueForOperator,
  emptyGroup,
  emptyLeaf,
} from '../forms/condition-tree-dsl';

export interface KnownField {
  path: string;
  kind: 'money' | 'number' | 'string';
}

/**
 * CR-05/CR-11 grouped AND/OR condition editor — the Rules-only replacement for the flat
 * `ConditionsEditor` (Streak Campaigns still use that one; see condition-tree-dsl.ts remarks).
 * Presentational only: no HTTP, driven entirely by `conditions`/`conditionsChange`.
 *
 * A5 UI invariants: at least one group (last cannot be deleted), at least one condition per
 * group (last cannot be deleted), a new group seeds one condition, an undeclared field path is
 * flagged but not blocked.
 */
@Component({
  selector: 'app-condition-tree-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Button, IconButton],
  template: `
    <section class="card space-y-4">
      <div class="section-header flex items-center justify-between">
        <h2 class="section-heading">Conditions (optional)</h2>
        <div class="flex items-center gap-2">
          <span class="text-xs text-gray-500">Join groups with</span>
          <span class="inline-flex overflow-hidden rounded-md border border-gray-300" role="group" aria-label="Join groups with">
            @for (opt of joinOps; track opt) {
              <button
                type="button"
                class="px-2.5 py-1 text-xs"
                [class.bg-blue-600]="tree().op === opt"
                [class.text-white]="tree().op === opt"
                [class.text-gray-600]="tree().op !== opt"
                [attr.aria-pressed]="tree().op === opt"
                (click)="setRootOp(opt)"
              >
                {{ opt }}
              </button>
            }
          </span>
        </div>
      </div>

      @for (group of tree().groups; track $index; let gi = $index) {
        @if (gi > 0) {
          <div class="flex items-center gap-3">
            <i class="h-px flex-1 bg-gray-200"></i>
            <span class="pill bg-gray-100 text-gray-600">{{ tree().op }}</span>
            <i class="h-px flex-1 bg-gray-200"></i>
          </div>
        }
        <div class="space-y-3 rounded-lg border border-gray-200 bg-gray-50 p-3">
          <div class="flex items-center justify-between">
            <span class="text-xs font-medium text-gray-600">Group {{ gi + 1 }}</span>
            <div class="flex items-center gap-2">
              <span class="text-xs text-gray-500">match</span>
              <span class="inline-flex overflow-hidden rounded-md border border-gray-300" role="group" aria-label="Match operator for this group">
                @for (opt of joinOps; track opt) {
                  <button
                    type="button"
                    class="px-2.5 py-1 text-xs"
                    [class.bg-blue-600]="group.op === opt"
                    [class.text-white]="group.op === opt"
                    [class.text-gray-600]="group.op !== opt"
                    [attr.aria-pressed]="group.op === opt"
                    (click)="setGroupOp(gi, opt)"
                  >
                    {{ opt }}
                  </button>
                }
              </span>
              <app-icon-button
                ariaLabel="Remove group"
                [disabled]="tree().groups.length < 2"
                (click)="removeGroup(gi)"
              >
                ✕
              </app-icon-button>
            </div>
          </div>

          @for (leaf of group.conditions; track $index; let li = $index) {
            @if (li > 0) {
              <div class="ml-1 flex items-center gap-2 text-xs text-gray-400">
                <span>{{ group.op }}</span>
                <i class="h-px flex-1 bg-gray-200"></i>
              </div>
            }
            <div class="grid grid-cols-[1.7fr_0.9fr_1.6fr_auto] items-start gap-2">
              <input
                class="field-input font-mono text-xs"
                [class.border-amber-400]="!isDeclared(leaf.field)"
                placeholder="payload field path"
                [value]="leaf.field"
                (input)="updateField(gi, li, $any($event.target).value)"
              />
              <select
                class="field-input"
                [value]="leaf.operator"
                (change)="updateOperator(gi, li, $any($event.target).value)"
              >
                @for (op of opsFor(leaf.field); track op) {
                  <option [value]="op">{{ op }}</option>
                }
              </select>
              @if (leaf.operator === 'exists') {
                <select
                  class="field-input"
                  [value]="leaf.value.data"
                  (change)="updateValue(gi, li, { ...leaf.value, data: $any($event.target).value === 'true' })"
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
                    updateValue(gi, li, {
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
                    (input)="updateBetween(gi, li, 0, $any($event.target).value)"
                  />
                  <input
                    class="field-input"
                    type="number"
                    placeholder="max"
                    [value]="betweenBound(leaf.value.data, 1)"
                    (input)="updateBetween(gi, li, 1, $any($event.target).value)"
                  />
                </div>
              } @else {
                <input
                  class="field-input"
                  [type]="kindFor(leaf.field) === 'string' ? 'text' : 'number'"
                  [value]="leaf.value.data"
                  (input)="
                    updateValue(gi, li, { ...leaf.value, type: kindFor(leaf.field), data: $any($event.target).value })
                  "
                />
              }
              <app-icon-button
                ariaLabel="Remove condition"
                [disabled]="group.conditions.length < 2"
                (click)="removeLeaf(gi, li)"
              >
                ✕
              </app-icon-button>
            </div>
          }
          <app-button size="sm" variant="secondary" (click)="addLeaf(gi)">Add condition</app-button>
        </div>
      }
      <app-button size="sm" variant="secondary" (click)="addGroup()">Add group</app-button>
      <p class="text-xs text-gray-500">
        A condition always lives in a group. Groups are joined by the operator above; conditions
        inside a group are joined by that group's own operator.
      </p>
    </section>
  `,
})
export class ConditionTreeEditor {
  readonly tree = input.required<ConditionTree>();
  readonly treeChange = output<ConditionTree>();
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
    return UNKNOWN_OPS; // undeclared field — full set (A5)
  }

  protected asCsv(value: unknown): string {
    return Array.isArray(value) ? value.join(', ') : '';
  }

  protected betweenBound(value: unknown, index: 0 | 1): number {
    return Array.isArray(value) && typeof value[index] === 'number' ? value[index] : 0;
  }

  protected readonly joinOps = GROUP_JOIN_OPS;

  protected setRootOp(op: GroupJoinOp): void {
    this.treeChange.emit({ ...this.tree(), op });
  }

  protected setGroupOp(gi: number, op: GroupJoinOp): void {
    const groups = this.tree().groups.map((g, i) => (i === gi ? { ...g, op } : g));
    this.treeChange.emit({ ...this.tree(), groups });
  }

  protected addGroup(): void {
    this.treeChange.emit({ ...this.tree(), groups: [...this.tree().groups, emptyGroup()] });
  }

  protected removeGroup(gi: number): void {
    if (this.tree().groups.length < 2) return;
    this.treeChange.emit({ ...this.tree(), groups: this.tree().groups.filter((_, i) => i !== gi) });
  }

  protected addLeaf(gi: number): void {
    const groups = this.tree().groups.map((g, i) =>
      i === gi ? { ...g, conditions: [...g.conditions, emptyLeaf()] } : g,
    );
    this.treeChange.emit({ ...this.tree(), groups });
  }

  protected removeLeaf(gi: number, li: number): void {
    const group = this.tree().groups[gi];
    if (!group || group.conditions.length < 2) return;
    this.patchGroup(gi, { ...group, conditions: group.conditions.filter((_, i) => i !== li) });
  }

  protected updateField(gi: number, li: number, field: string): void {
    this.patchLeaf(gi, li, (leaf) => ({ ...leaf, field }));
  }

  protected updateOperator(gi: number, li: number, operator: ConditionLeafOp): void {
    this.patchLeaf(gi, li, (leaf) => ({ ...leaf, operator, value: defaultValueForOperator(operator) }));
  }

  protected updateValue(gi: number, li: number, value: ConditionValue): void {
    this.patchLeaf(gi, li, (leaf) => ({ ...leaf, value }));
  }

  protected updateBetween(gi: number, li: number, index: 0 | 1, raw: string): void {
    const leaf = this.tree().groups[gi]?.conditions[li];
    if (!leaf) return;
    const data = leaf.value.data;
    const current: number[] = Array.isArray(data) ? (data as number[]).slice() : [0, 0];
    current[index] = Number(raw);
    this.updateValue(gi, li, { ...leaf.value, type: 'number', data: current });
  }

  private patchLeaf(gi: number, li: number, fn: (leaf: ConditionLeaf) => ConditionLeaf): void {
    const group = this.tree().groups[gi];
    if (!group) return;
    const conditions = group.conditions.map((c, i) => (i === li ? fn(c) : c));
    this.patchGroup(gi, { ...group, conditions });
  }

  private patchGroup(gi: number, group: ConditionGroup): void {
    const groups = this.tree().groups.map((g, i) => (i === gi ? group : g));
    this.treeChange.emit({ ...this.tree(), groups });
  }
}
