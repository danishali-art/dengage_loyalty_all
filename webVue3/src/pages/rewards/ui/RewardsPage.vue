<script setup lang="ts">
import {
  AppButton,
  AppCard,
  AppDataTable,
  AppDialog,
  AppEmptyState,
  AppPageHeader,
  AppPaginator,
  AppSkeletonList,
} from '@/shared/ui';
import { RewardStatusPill, type Reward } from '@/entities/reward';
import { RewardForm } from '@/features/reward-edit';
import { useRewardsPage } from '../model/useRewardsPage';

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
  ready,
  create,
  edit,
  approve,
  approvingId,
  form,
} = useRewardsPage(props);
const {
  visible,
  isEdit,
  retired,
  pendingApproval,
  currencyLocked,
  nameGrandfathered,
  existingName,
  namePrefix,
  options,
  values,
  errors,
  set,
  formErrors,
  pending,
  submit,
} = form;
const asRow = (r: unknown) => r as { reward: Reward; retired: boolean };
</script>

<template>
  <AppPageHeader :heading="$t('programs.overview.cards.rewards.label')" :crumbs="crumbs">
    <template #actions>
      <AppButton :disabled="!ready" @click="create">{{ $t('rewards.add') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="rewards-page">
    <AppCard flush>
      <div v-if="firstLoad" class="rewards-page__pad"><AppSkeletonList :count="5" /></div>
      <div v-else-if="rows.length === 0" class="rewards-page__pad">
        <AppEmptyState :heading="$t('rewards.empty')" :description="$t('rewards.emptyHint')" icon="🎁" />
      </div>
      <template v-else>
        <AppDataTable
          :rows="rows"
          :columns="columns"
          row-key="id"
          :caption="$t('programs.overview.cards.rewards.label')"
          :loading="loading"
          :empty-text="$t('rewards.empty')"
        >
          <template #cell-name="{ row }">
            {{ asRow(row).reward.displayName }}
            <span class="rewards-page__slug">({{ asRow(row).reward.name }})</span>
          </template>
          <template #cell-type="{ row }">
            {{ $t(`rewards.type.${asRow(row).reward.rewardType}`) }}
            <span v-if="asRow(row).retired" class="rewards-page__muted"
              >({{ $t('rewards.list.retired') }})</span
            >
          </template>
          <template #cell-acquisition="{ row }">
            {{ $t(`rewards.acquisition.${asRow(row).reward.acquisition}`) }}
          </template>
          <template #cell-status="{ row }"><RewardStatusPill :reward="asRow(row).reward" /></template>
          <template #cell-actions="{ row }">
            <AppButton
              v-if="asRow(row).reward.status === 'pending_approval'"
              size="sm"
              variant="secondary"
              :pending="approvingId === asRow(row).reward.id"
              @click="approve(asRow(row).reward)"
            >
              {{ $t('rewards.list.approve') }}
            </AppButton>
            <AppButton variant="text" size="sm" :disabled="!ready" @click="edit(asRow(row).reward)">
              {{ asRow(row).retired ? $t('rewards.list.view') : $t('action.edit') }}
            </AppButton>
          </template>
        </AppDataTable>
        <AppPaginator v-model:page="page" :page-size="pageSize" :total="total" />
      </template>
    </AppCard>
  </div>

  <AppDialog
    v-model:visible="visible"
    :header="isEdit ? $t('rewards.form.editHeading') : $t('rewards.form.newHeading')"
    width="44rem"
  >
    <RewardForm
      :values="values"
      :errors="errors"
      :form-errors="formErrors"
      :is-edit="isEdit"
      :retired="retired"
      :pending-approval="pendingApproval"
      :currency-locked="currencyLocked"
      :name-grandfathered="nameGrandfathered"
      :existing-name="existingName"
      :name-prefix="namePrefix"
      :options="options"
      @set="set"
      @submit="submit"
    />
    <template #footer>
      <AppButton variant="secondary" @click="visible = false">
        {{ retired ? $t('rewards.form.close') : $t('action.cancel') }}
      </AppButton>
      <AppButton v-if="!retired" :pending="pending" @click="submit">
        {{ isEdit ? $t('action.save') : $t('rewards.form.create') }}
      </AppButton>
    </template>
  </AppDialog>
</template>

<style scoped>
.rewards-page {
  max-width: 80rem;
  margin: 0 auto;
  padding: 1.5rem 2rem;
}
.rewards-page__pad {
  padding: 1.25rem;
}
.rewards-page__slug {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.75rem;
  color: var(--app-muted);
}
.rewards-page__muted {
  color: var(--app-muted);
}
</style>
