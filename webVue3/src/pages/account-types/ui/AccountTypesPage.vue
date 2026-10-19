<script setup lang="ts">
import {
  AppButton,
  AppCard,
  AppDialog,
  AppEmptyState,
  AppPageHeader,
  AppPaginator,
  AppSkeletonList,
} from '@/shared/ui';
import { AccountTypeCard } from '@/entities/account-type';
import { AccountTypeForm } from '@/features/account-type-edit';
import { useAccountTypesPage } from '../model/useAccountTypesPage';

const props = defineProps<{ programId: string }>();
const { crumbs, page, pageSize, accountTypes, total, loading, firstLoad, create, edit, form } =
  useAccountTypesPage(props);
const { visible, isEdit, values, errors, set, formErrors, redemptionTargets, pending, submit } = form;
</script>

<template>
  <AppPageHeader :heading="$t('programs.overview.cards.accountTypes.label')" :crumbs="crumbs">
    <template #actions>
      <AppButton @click="create">{{ $t('accountTypes.add') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="account-types-page">
    <AppCard>
      <AppSkeletonList v-if="firstLoad" :count="3" />
      <AppEmptyState
        v-else-if="accountTypes.length === 0"
        :heading="$t('accountTypes.empty')"
        :description="$t('accountTypes.emptyHint')"
        icon="⭐"
      />
      <template v-else>
        <div class="account-types-page__list" :aria-busy="loading || undefined">
          <AccountTypeCard v-for="at in accountTypes" :key="at.id" :account-type="at" @edit="edit" />
        </div>
        <AppPaginator v-model:page="page" :page-size="pageSize" :total="total" />
      </template>
    </AppCard>
  </div>

  <AppDialog
    v-model:visible="visible"
    :header="isEdit ? $t('accountTypes.form.editHeading') : $t('accountTypes.form.newHeading')"
  >
    <AccountTypeForm
      :values="values"
      :errors="errors"
      :form-errors="formErrors"
      :is-edit="isEdit"
      :redemption-targets="redemptionTargets"
      @set="set"
      @submit="submit"
    />
    <template #footer>
      <AppButton variant="secondary" @click="visible = false">{{ $t('action.cancel') }}</AppButton>
      <AppButton :pending="pending" @click="submit">
        {{ isEdit ? $t('action.save') : $t('accountTypes.form.create') }}
      </AppButton>
    </template>
  </AppDialog>
</template>

<style scoped>
.account-types-page {
  max-width: 64rem;
  margin: 0 auto;
  padding: 1.5rem 2rem;
}
.account-types-page__list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
</style>
