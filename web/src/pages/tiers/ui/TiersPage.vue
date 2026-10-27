<script setup lang="ts">
import {
  AppButton,
  AppCard,
  AppDataTable,
  AppDialog,
  AppEmptyState,
  AppIcon,
  AppPageHeader,
  AppSkeletonList,
} from '@/shared/ui';
import { TierForm } from '@/features/tier-edit';
import { useTiersPage, type TierRow } from '../model/useTiersPage';

const props = defineProps<{ programId: string }>();
const { crumbs, columns, rows, loading, firstLoad, create, edit, form, actions } = useTiersPage(props);
const { visible, isEdit, lockQualifyingModel, values, errors, set, formErrors, pending, submit } = form;
const { move, moving, deleteTier } = actions;
const asRow = (r: unknown): TierRow => r as TierRow;
</script>

<template>
  <AppPageHeader :heading="$t('tiers.heading')" :crumbs="crumbs">
    <template #actions>
      <AppButton @click="create">{{ $t('tiers.add') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="tiers-page">
    <p class="tiers-page__intro">{{ $t('tiers.intro') }}</p>
    <AppCard flush>
      <div v-if="firstLoad" class="tiers-page__pad"><AppSkeletonList :count="4" /></div>
      <div v-else-if="rows.length === 0" class="tiers-page__pad">
        <AppEmptyState :heading="$t('tiers.empty')" :description="$t('tiers.emptyHint')" icon="🏅" />
      </div>
      <AppDataTable
        v-else
        :rows="rows"
        :columns="columns"
        row-key="id"
        :caption="$t('tiers.heading')"
        :loading="loading"
        :empty-text="$t('tiers.empty')"
      >
        <template #cell-order="{ row }">
          <span class="tiers-page__order">
            <span class="tiers-page__position">{{ asRow(row).position }}</span>
            <AppButton
              variant="text"
              size="sm"
              :aria-label="$t('tiers.moveUp')"
              :disabled="!asRow(row).canMoveUp || moving"
              @click="move(asRow(row).index, -1)"
            >
              <AppIcon name="arrow-up" />
            </AppButton>
            <AppButton
              variant="text"
              size="sm"
              :aria-label="$t('tiers.moveDown')"
              :disabled="!asRow(row).canMoveDown || moving"
              @click="move(asRow(row).index, 1)"
            >
              <AppIcon name="arrow-down" />
            </AppButton>
          </span>
        </template>
        <template #cell-name="{ row }">
          {{ asRow(row).tier.displayName }}
          <span class="tiers-page__slug">({{ asRow(row).tier.name }})</span>
        </template>
        <template #cell-minPoints="{ row }">{{ asRow(row).tier.minPoints }}</template>
        <template #cell-qualifying="{ row }">
          {{
            asRow(row).tier.qualifyingModel === 'periodic'
              ? $t('tiers.qualifying.periodic', { days: asRow(row).tier.qualifyingPeriodDays })
              : $t('tiers.qualifying.lifetime')
          }}
        </template>
        <template #cell-grace="{ row }">{{
          $t('tiers.graceDays', { days: asRow(row).tier.graceDays })
        }}</template>
        <template #cell-actions="{ row }">
          <AppButton variant="text" size="sm" @click="edit(asRow(row).tier)">{{
            $t('action.edit')
          }}</AppButton>
          <AppButton
            variant="text"
            size="sm"
            :disabled="!!asRow(row).deleteBlockedKey"
            :title="asRow(row).deleteBlockedKey ? $t(asRow(row).deleteBlockedKey!) : undefined"
            @click="deleteTier(asRow(row).tier)"
          >
            {{ $t('action.delete') }}
          </AppButton>
        </template>
      </AppDataTable>
    </AppCard>
  </div>

  <AppDialog
    v-model:visible="visible"
    :header="isEdit ? $t('tiers.form.editHeading') : $t('tiers.form.newHeading')"
  >
    <TierForm
      :values="values"
      :errors="errors"
      :form-errors="formErrors"
      :lock-qualifying-model="lockQualifyingModel"
      @set="set"
      @submit="submit"
    />
    <template #footer>
      <AppButton variant="secondary" @click="visible = false">{{ $t('action.cancel') }}</AppButton>
      <AppButton :pending="pending" @click="submit">
        {{ isEdit ? $t('action.save') : $t('tiers.form.create') }}
      </AppButton>
    </template>
  </AppDialog>
</template>

<style scoped>
.tiers-page {
  max-width: 64rem;
  margin: 0 auto;
  padding: 1.5rem 2rem;
}
.tiers-page__intro {
  margin: 0 0 1rem;
  color: var(--app-muted);
}
.tiers-page__pad {
  padding: 1.25rem;
}
.tiers-page__order {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
}
.tiers-page__position {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1.5rem;
  height: 1.5rem;
  border-radius: 50%;
  background: #f3f4f6;
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--app-text-soft);
}
.tiers-page__slug {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.75rem;
  color: var(--app-muted);
}
</style>
