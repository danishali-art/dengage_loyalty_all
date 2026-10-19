<script setup lang="ts">
import { computed } from 'vue';
import { AppButton, AppCard, AppIcon } from '@/shared/ui';
import {
  emptyGroup,
  emptyLeaf,
  GROUP_JOIN_OPS,
  type ConditionGroup,
  type ConditionLeaf,
  type ConditionTree,
  type GroupJoinOp,
} from '@/shared/lib/dsl';
import { fieldKinds, type KnownField } from '../model/fields';
import ConditionLeafRow from './ConditionLeafRow.vue';

/**
 * CR-05/CR-11 grouped AND/OR condition editor (port of condition-tree-editor.ts), for rules.
 * Presentational: `v-model` the tree.
 *
 * A5 UI invariants: at least one group (the last can't be removed), at least one condition per
 * group (the last can't be removed), a new group seeds one condition, an undeclared field path is
 * flagged but not blocked.
 */
const tree = defineModel<ConditionTree>({ required: true });
const props = withDefaults(defineProps<{ knownFields?: readonly KnownField[] }>(), {
  knownFields: () => [],
});
const kinds = computed(() => fieldKinds(props.knownFields));

function setGroups(groups: ConditionGroup[]): void {
  tree.value = { ...tree.value, groups };
}
function patchGroup(gi: number, group: ConditionGroup): void {
  setGroups(tree.value.groups.map((g, i) => (i === gi ? group : g)));
}
function setLeaf(gi: number, li: number, leaf: ConditionLeaf): void {
  const group = tree.value.groups[gi];
  if (group) patchGroup(gi, { ...group, conditions: group.conditions.map((c, i) => (i === li ? leaf : c)) });
}
function removeLeaf(gi: number, li: number): void {
  const group = tree.value.groups[gi];
  if (!group || group.conditions.length < 2) return;
  patchGroup(gi, { ...group, conditions: group.conditions.filter((_, i) => i !== li) });
}
function addLeaf(gi: number): void {
  const group = tree.value.groups[gi];
  if (group) patchGroup(gi, { ...group, conditions: [...group.conditions, emptyLeaf()] });
}
function removeGroup(gi: number): void {
  if (tree.value.groups.length < 2) return;
  setGroups(tree.value.groups.filter((_, i) => i !== gi));
}
function setGroupOp(gi: number, op: GroupJoinOp): void {
  const group = tree.value.groups[gi];
  if (group) patchGroup(gi, { ...group, op });
}
</script>

<template>
  <AppCard :heading="$t('conditions.tree.heading')">
    <div class="flex flex-col gap-4">
      <div class="flex items-center justify-end gap-2">
        <span id="condition-root-op" class="text-xs text-muted-foreground">{{
          $t('conditions.tree.joinGroups')
        }}</span>
        <span
          class="inline-flex overflow-hidden rounded-md border"
          role="group"
          aria-labelledby="condition-root-op"
        >
          <button
            v-for="op in GROUP_JOIN_OPS"
            :key="op"
            type="button"
            class="px-2.5 py-1 text-xs"
            :class="tree.op === op ? 'bg-primary text-primary-foreground' : 'bg-white text-(--app-text-soft)'"
            :aria-pressed="tree.op === op"
            @click="tree = { ...tree, op }"
          >
            {{ op }}
          </button>
        </span>
      </div>

      <template v-for="(group, gi) in tree.groups" :key="gi">
        <div v-if="gi > 0" class="flex items-center gap-3" aria-hidden="true">
          <i class="h-px flex-1 bg-gray-200" />
          <span class="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-(--app-text-soft)">{{
            tree.op
          }}</span>
          <i class="h-px flex-1 bg-gray-200" />
        </div>
        <div
          class="flex flex-col gap-3 rounded-lg border bg-gray-50 p-3"
          role="group"
          :aria-labelledby="`condition-group-${gi}-title`"
        >
          <div class="flex items-center justify-between">
            <span :id="`condition-group-${gi}-title`" class="text-xs font-medium text-(--app-text-soft)">
              {{ $t('conditions.tree.group', { n: gi + 1 }) }}
            </span>
            <div class="flex items-center gap-2">
              <span :id="`condition-group-${gi}-op`" class="text-xs text-muted-foreground">{{
                $t('conditions.tree.match')
              }}</span>
              <span
                class="inline-flex overflow-hidden rounded-md border"
                role="group"
                :aria-labelledby="`condition-group-${gi}-op`"
              >
                <button
                  v-for="op in GROUP_JOIN_OPS"
                  :key="op"
                  type="button"
                  class="px-2.5 py-1 text-xs"
                  :class="
                    group.op === op ? 'bg-primary text-primary-foreground' : 'bg-white text-(--app-text-soft)'
                  "
                  :aria-pressed="group.op === op"
                  @click="setGroupOp(gi, op)"
                >
                  {{ op }}
                </button>
              </span>
              <AppButton
                variant="text"
                size="sm"
                :aria-label="$t('conditions.tree.removeGroup')"
                :disabled="tree.groups.length < 2"
                @click="removeGroup(gi)"
              >
                <AppIcon name="close" />
              </AppButton>
            </div>
          </div>

          <template v-for="(leaf, li) in group.conditions" :key="li">
            <div
              v-if="li > 0"
              class="ml-1 flex items-center gap-2 text-xs text-muted-foreground"
              aria-hidden="true"
            >
              <span>{{ group.op }}</span>
              <i class="h-px flex-1 bg-gray-200" />
            </div>
            <ConditionLeafRow
              :leaf="leaf"
              :kinds="kinds"
              :removable="group.conditions.length > 1"
              :id-prefix="`condition-${gi}-${li}`"
              @change="setLeaf(gi, li, $event)"
              @remove="removeLeaf(gi, li)"
            />
          </template>
          <div>
            <AppButton size="sm" variant="secondary" @click="addLeaf(gi)">{{
              $t('conditions.add')
            }}</AppButton>
          </div>
        </div>
      </template>
      <div>
        <AppButton size="sm" variant="secondary" @click="setGroups([...tree.groups, emptyGroup()])">
          {{ $t('conditions.tree.addGroup') }}
        </AppButton>
      </div>
      <p class="m-0 text-xs text-muted-foreground">{{ $t('conditions.tree.help') }}</p>
    </div>
  </AppCard>
</template>
