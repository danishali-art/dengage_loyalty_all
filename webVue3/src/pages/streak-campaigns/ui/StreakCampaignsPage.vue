<script setup lang="ts">
import {
  AppButton,
  AppCard,
  AppDataTable,
  AppEmptyState,
  AppIcon,
  AppPageHeader,
  AppPaginator,
  AppSkeletonList,
  AppStatusPill,
} from '@/shared/ui';
import type { StreakCampaign } from '@/entities/streak-campaign';
import { useStreakCampaignsPage } from '../model/useStreakCampaignsPage';

const props = defineProps<{ programId: string }>();
const {
  crumbs,
  columns,
  page,
  pageSize,
  rows,
  total,
  loading,
  firstLoad,
  create,
  editPath,
  busyId,
  toggleStatus,
  deleteCampaign,
} = useStreakCampaignsPage(props);
const asCampaign = (row: unknown) => row as StreakCampaign;
</script>

<template>
  <AppPageHeader :heading="$t('streaks.heading')" :crumbs="crumbs">
    <template #actions>
      <AppButton @click="create"><AppIcon name="plus" />{{ $t('streaks.add') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="mx-auto max-w-5xl px-8 py-6">
    <AppCard flush>
      <div v-if="firstLoad" class="p-5"><AppSkeletonList :count="4" /></div>
      <div v-else-if="rows.length === 0" class="p-5">
        <AppEmptyState :heading="$t('streaks.empty')" :description="$t('streaks.emptyHint')" icon="🔥" />
      </div>
      <template v-else>
        <AppDataTable
          :rows="rows"
          :columns="columns"
          row-key="id"
          :caption="$t('streaks.heading')"
          :loading="loading"
          :empty-text="$t('streaks.empty')"
        >
          <template #cell-name="{ row }">
            <RouterLink :to="editPath(asCampaign(row))" class="font-medium">{{
              asCampaign(row).name
            }}</RouterLink>
          </template>
          <template #cell-trigger="{ row }">
            <span class="font-mono text-xs text-muted-foreground">{{ asCampaign(row).trigger }}</span>
          </template>
          <template #cell-period="{ row }">
            <span class="text-xs text-muted-foreground">{{
              $t(`streaks.period.${asCampaign(row).config.period}`)
            }}</span>
          </template>
          <template #cell-targetPeriods="{ row }">
            <span class="text-xs text-muted-foreground">{{ asCampaign(row).config.target_periods }}</span>
          </template>
          <template #cell-status="{ row }">
            <AppStatusPill :tone="asCampaign(row).status === 'active' ? 'success' : 'neutral'" dot>
              {{ $t(`common.status.${asCampaign(row).status}`) }}
            </AppStatusPill>
          </template>
          <template #cell-actions="{ row }">
            <AppButton
              variant="text"
              size="sm"
              :aria-label="
                asCampaign(row).status === 'active' ? $t('streaks.disable') : $t('streaks.activate')
              "
              :disabled="busyId === asCampaign(row).id"
              @click="toggleStatus(asCampaign(row))"
            >
              <AppIcon :name="asCampaign(row).status === 'active' ? 'pause' : 'play'" />
            </AppButton>
            <AppButton
              variant="text"
              size="sm"
              :aria-label="$t('streaks.delete.title')"
              :disabled="busyId === asCampaign(row).id"
              @click="deleteCampaign(asCampaign(row))"
            >
              <AppIcon name="trash" />
            </AppButton>
          </template>
        </AppDataTable>
        <AppPaginator v-model:page="page" :page-size="pageSize" :total="total" />
      </template>
    </AppCard>
  </div>
</template>
