<script setup lang="ts">
import {
  AppButton,
  AppCard,
  AppEmptyState,
  AppIcon,
  AppPageHeader,
  AppSkeletonList,
  AppStatusPill,
} from '@/shared/ui';
import { useRulesPage } from '../model/useRulesPage';
import RuleRowsTable from './RuleRowsTable.vue';

const props = defineProps<{ programId: string }>();
const {
  crumbs,
  columns,
  outsideColumns,
  loading,
  firstLoad,
  empty,
  grouped,
  ranks,
  accountLabel,
  groupCaption,
  create,
  editPath,
  busyId,
  toggleStatus,
  approve,
  deleteRule,
} = useRulesPage(props);
</script>

<template>
  <AppPageHeader :heading="$t('rules.heading')" :crumbs="crumbs">
    <template #actions>
      <AppButton @click="create"><AppIcon name="plus" />{{ $t('rules.list.add') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-8 py-6">
    <AppCard v-if="firstLoad"><AppSkeletonList :count="4" /></AppCard>
    <AppCard v-else-if="empty">
      <AppEmptyState :heading="$t('rules.list.empty')" :description="$t('rules.list.emptyHint')" icon="⚙" />
    </AppCard>
    <template v-else>
      <p class="m-0 text-xs text-muted-foreground">{{ $t('rules.groups.intro') }}</p>

      <section
        v-for="(group, gi) in grouped.groups"
        :key="group.key"
        :aria-labelledby="`rule-group-${gi}`"
        :aria-busy="loading || undefined"
        class="overflow-hidden rounded-(--app-radius) border bg-white shadow-(--app-shadow-card)"
      >
        <h2
          :id="`rule-group-${gi}`"
          class="m-0 flex flex-wrap items-center gap-2 px-4 pt-4 text-[0.9375rem] font-semibold text-(--app-heading)"
        >
          <span class="font-mono text-sm">{{ group.trigger }}</span>
          <span aria-hidden="true" class="text-muted-foreground">→</span>
          <span class="sr-only">{{ $t('rules.groups.onAccount') }}</span>
          <span>{{ accountLabel(group.targetAccountTypeId) }}</span>
        </h2>

        <template v-if="group.exclusive.length > 0">
          <div class="flex items-center gap-2 px-4 pt-3">
            <AppStatusPill tone="neutral">{{ $t('rules.stackable.no') }}</AppStatusPill>
            <span class="text-xs text-muted-foreground">{{ $t('rules.groups.exclusiveHint') }}</span>
          </div>
          <RuleRowsTable
            :rules="group.exclusive"
            :columns="columns"
            :caption="groupCaption(group.trigger, group.targetAccountTypeId, 'rules.stackable.no')"
            :loading="loading"
            :busy-id="busyId"
            :edit-path="editPath"
            :ranks="ranks"
            @toggle="toggleStatus"
            @approve="approve"
            @remove="deleteRule"
          />
        </template>

        <template v-if="group.stackable.length > 0">
          <div class="flex items-center gap-2 px-4 pt-3">
            <AppStatusPill tone="info">{{ $t('rules.stackable.yes') }}</AppStatusPill>
            <span class="text-xs text-muted-foreground">{{ $t('rules.groups.stackableHint') }}</span>
          </div>
          <RuleRowsTable
            :rules="group.stackable"
            :columns="columns"
            :caption="groupCaption(group.trigger, group.targetAccountTypeId, 'rules.stackable.yes')"
            :loading="loading"
            :busy-id="busyId"
            :edit-path="editPath"
            @toggle="toggleStatus"
            @approve="approve"
            @remove="deleteRule"
          />
        </template>
      </section>

      <section
        v-if="grouped.outsideWinnerSelection.length > 0"
        aria-labelledby="rule-group-outside"
        class="overflow-hidden rounded-(--app-radius) border bg-white shadow-(--app-shadow-card)"
      >
        <h2 id="rule-group-outside" class="m-0 px-4 pt-4 text-[0.9375rem] font-semibold text-(--app-heading)">
          {{ $t('rules.groups.outside') }}
        </h2>
        <p class="m-0 px-4 pt-1 text-xs text-muted-foreground">{{ $t('rules.groups.outsideHint') }}</p>
        <RuleRowsTable
          :rules="grouped.outsideWinnerSelection"
          :columns="outsideColumns"
          :caption="$t('rules.groups.outside')"
          :loading="loading"
          :busy-id="busyId"
          :edit-path="editPath"
          @toggle="toggleStatus"
          @approve="approve"
          @remove="deleteRule"
        />
      </section>
    </template>
  </div>
</template>
