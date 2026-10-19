<script setup lang="ts">
import { AppButton, AppDataTable, AppIcon, AppStatusPill, type TableColumn } from '@/shared/ui';
import { RuleStatusPill, type ExclusiveRank, type Rule } from '@/entities/rule';

/**
 * One table of rules, shared by every group on the rules page. Inactive rules are dimmed: they
 * don't compete. Exclusive tables pass `ranks`: where each rule stands in the
 * one-winner-per-account race. Presentational.
 */
const props = withDefaults(
  defineProps<{
    rules: readonly Rule[];
    columns: readonly TableColumn[];
    caption: string;
    loading: boolean;
    busyId: string | null;
    editPath: (rule: Rule) => string;
    ranks?: ReadonlyMap<string, ExclusiveRank> | null;
  }>(),
  { ranks: null },
);
const emit = defineEmits<{ toggle: [rule: Rule]; approve: [rule: Rule]; remove: [rule: Rule] }>();
const asRule = (row: unknown) => row as Rule;
</script>

<template>
  <AppDataTable
    :rows="props.rules"
    :columns="props.columns"
    row-key="id"
    :caption="props.caption"
    :loading="props.loading"
    :empty-text="''"
  >
    <template #cell-trigger="{ row }">
      <span class="font-mono text-xs text-muted-foreground">{{ asRule(row).trigger }}</span>
    </template>
    <template #cell-name="{ row }">
      <span :class="{ 'opacity-60': asRule(row).status !== 'active' }">
        <RouterLink :to="props.editPath(asRule(row))" class="font-medium">{{ asRule(row).name }}</RouterLink>
      </span>
      <template v-if="props.ranks">
        <AppStatusPill v-if="props.ranks.get(asRule(row).id) === 'top'" tone="success" class="ml-2">
          {{ $t('rules.groups.rank.top') }}
        </AppStatusPill>
        <AppStatusPill v-else-if="props.ranks.get(asRule(row).id) === 'tie'" tone="warning" class="ml-2">
          {{ $t('rules.groups.rank.tie') }}
        </AppStatusPill>
        <span
          v-else-if="props.ranks.get(asRule(row).id) === 'fallback'"
          class="ml-2 text-xs text-muted-foreground"
        >
          {{ $t('rules.groups.rank.fallback') }}
        </span>
      </template>
    </template>
    <template #cell-type="{ row }">
      <span
        class="text-xs text-muted-foreground"
        :class="{ 'opacity-60': asRule(row).status !== 'active' }"
        >{{ asRule(row).type }}</span
      >
    </template>
    <template #cell-priority="{ row }">
      <span
        class="text-xs text-muted-foreground"
        :class="{ 'opacity-60': asRule(row).status !== 'active' }"
        >{{ asRule(row).priority }}</span
      >
    </template>
    <template #cell-status="{ row }"><RuleStatusPill :status="asRule(row).status" /></template>
    <template #cell-actions="{ row }">
      <AppButton
        v-if="asRule(row).status === 'pending_approval'"
        size="sm"
        variant="secondary"
        :pending="props.busyId === asRule(row).id"
        @click="emit('approve', asRule(row))"
      >
        {{ $t('rules.list.approve') }}
      </AppButton>
      <AppButton
        v-else
        variant="text"
        size="sm"
        :aria-label="asRule(row).status === 'active' ? $t('rules.list.disable') : $t('rules.list.activate')"
        :disabled="props.busyId === asRule(row).id"
        @click="emit('toggle', asRule(row))"
      >
        <AppIcon :name="asRule(row).status === 'active' ? 'pause' : 'play'" />
      </AppButton>
      <AppButton
        variant="text"
        size="sm"
        :aria-label="$t('rules.list.delete.title')"
        :disabled="props.busyId === asRule(row).id"
        @click="emit('remove', asRule(row))"
      >
        <AppIcon name="trash" />
      </AppButton>
    </template>
  </AppDataTable>
</template>
