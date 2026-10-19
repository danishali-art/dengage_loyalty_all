<script setup lang="ts">
import {
  AppButton,
  AppCard,
  AppDataTable,
  AppDialog,
  AppPageHeader,
  AppPaginator,
  AppStatusPill,
} from '@/shared/ui';
import { formatDate } from '@/shared/lib/date';
import type { Tenant } from '@/entities/tenant';
import { CreateTenantForm } from '@/features/tenant-create';
import { useTenantsPage } from '../model/useTenantsPage';

const { page, pageSize, rows, total, loading, columns, open, createOpen, openCreate, create } =
  useTenantsPage();
const { id, name, errors, formErrors, pending, submit } = create;
</script>

<template>
  <AppPageHeader :heading="$t('platform.heading')" :subtitle="$t('platform.subtitle')">
    <template #actions>
      <AppButton @click="openCreate">{{ $t('platform.tenants.new') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="tenants-page">
    <AppCard flush>
      <AppDataTable
        :rows="rows"
        :columns="columns"
        row-key="id"
        :caption="$t('platform.tenants.caption')"
        :loading="loading"
        :empty-text="$t('platform.tenants.empty')"
        clickable-rows
        @row-click="open"
      >
        <template #cell-name="{ row }">
          <RouterLink :to="`/platform/tenants/${(row as Tenant).id}`" class="tenants-page__name">
            {{ (row as Tenant).name }}
          </RouterLink>
        </template>
        <template #cell-id="{ row }">
          <code class="font-mono text-xs">{{ (row as Tenant).id }}</code>
        </template>
        <template #cell-status="{ row }">
          <AppStatusPill :tone="(row as Tenant).status === 'active' ? 'success' : 'neutral'" dot>
            {{ $t(`platform.status.${(row as Tenant).status}`) }}
          </AppStatusPill>
        </template>
        <template #cell-createdAt="{ row }">{{ formatDate((row as Tenant).createdAt) }}</template>
      </AppDataTable>
      <AppPaginator v-model:page="page" :page-size="pageSize" :total="total" />
    </AppCard>
  </div>

  <AppDialog v-model:visible="createOpen" :header="$t('platform.tenants.form.heading')">
    <CreateTenantForm
      v-model:id="id"
      v-model:name="name"
      :errors="errors"
      :form-errors="formErrors"
      @submit="submit"
    />
    <template #footer>
      <AppButton variant="secondary" @click="createOpen = false">{{ $t('action.cancel') }}</AppButton>
      <AppButton :pending="pending" @click="submit">{{ $t('platform.tenants.form.submit') }}</AppButton>
    </template>
  </AppDialog>
</template>

<style scoped>
.tenants-page {
  max-width: 80rem;
  margin: 0 auto;
  padding: 1.5rem 2rem;
}
.tenants-page__name {
  font-weight: 500;
  color: var(--app-text);
}
</style>
