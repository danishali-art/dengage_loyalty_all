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
import type { CardBucket } from '@/entities/card-bucket';
import { useCardBucketsPage } from '../model/useCardBucketsPage';

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
  amountRange,
  busyId,
  toggleStatus,
  deleteBucket,
} = useCardBucketsPage(props);
const asBucket = (row: unknown) => row as CardBucket;
</script>

<template>
  <AppPageHeader :heading="$t('cardBuckets.heading')" :crumbs="crumbs">
    <template #actions>
      <AppButton @click="create"><AppIcon name="plus" />{{ $t('cardBuckets.add') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="mx-auto max-w-5xl px-8 py-6">
    <AppCard flush>
      <div v-if="firstLoad" class="p-5"><AppSkeletonList :count="4" /></div>
      <div v-else-if="rows.length === 0" class="p-5">
        <AppEmptyState
          :heading="$t('cardBuckets.empty')"
          :description="$t('cardBuckets.emptyHint')"
          icon="💳"
        />
      </div>
      <template v-else>
        <AppDataTable
          :rows="rows"
          :columns="columns"
          row-key="id"
          :caption="$t('cardBuckets.heading')"
          :loading="loading"
          :empty-text="$t('cardBuckets.empty')"
        >
          <template #cell-name="{ row }">
            <RouterLink :to="editPath(asBucket(row))" class="font-medium">{{
              asBucket(row).name
            }}</RouterLink>
          </template>
          <template #cell-mcc="{ row }">
            <span class="font-mono text-xs text-muted-foreground">{{
              asBucket(row).mccCodes?.join(', ') || '—'
            }}</span>
          </template>
          <template #cell-amount="{ row }">
            <span class="text-xs text-muted-foreground">{{ amountRange(asBucket(row)) }}</span>
          </template>
          <template #cell-status="{ row }">
            <AppStatusPill :tone="asBucket(row).status === 'active' ? 'success' : 'neutral'" dot>
              {{ $t(`common.status.${asBucket(row).status}`) }}
            </AppStatusPill>
          </template>
          <template #cell-actions="{ row }">
            <AppButton
              variant="text"
              size="sm"
              :aria-label="
                asBucket(row).status === 'active' ? $t('cardBuckets.disable') : $t('cardBuckets.activate')
              "
              :disabled="busyId === asBucket(row).id"
              @click="toggleStatus(asBucket(row))"
            >
              <AppIcon :name="asBucket(row).status === 'active' ? 'pause' : 'play'" />
            </AppButton>
            <AppButton
              variant="text"
              size="sm"
              :aria-label="$t('cardBuckets.delete.title')"
              :disabled="busyId === asBucket(row).id"
              @click="deleteBucket(asBucket(row))"
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
