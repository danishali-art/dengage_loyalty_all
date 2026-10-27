<script setup lang="ts">
import {
  AppButton,
  AppCard,
  AppDataTable,
  AppDialog,
  AppEmptyState,
  AppPageHeader,
  AppPaginator,
  AppStatusPill,
} from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import type { AdminUser } from '@/entities/admin-user';
import { CreateAdminUserForm } from '@/features/admin-user-create';
import { useAdminUsersPage } from '../model/useAdminUsersPage';

const props = defineProps<{ tenantId: string }>();
const { crumbs, page, pageSize, rows, total, loading, empty, columns, createOpen, openCreate, create } =
  useAdminUsersPage(props);
const { email, password, errors, formErrors, pending, submit } = create;
</script>

<template>
  <AppPageHeader :heading="$t('platform.admins.heading')" :crumbs="crumbs">
    <template #actions>
      <AppButton @click="openCreate">{{ $t('platform.admins.new') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="admin-users-page">
    <AppEmptyState
      v-if="empty"
      :heading="$t('platform.admins.emptyHeading')"
      :description="$t('platform.admins.emptyBody')"
      icon="👤"
    />
    <AppCard v-else flush>
      <AppDataTable
        :rows="rows"
        :columns="columns"
        row-key="id"
        :caption="$t('platform.admins.caption')"
        :loading="loading"
        :empty-text="$t('platform.admins.emptyHeading')"
      >
        <template #cell-status="{ row }">
          <AppStatusPill :tone="(row as AdminUser).status === 'active' ? 'success' : 'neutral'" dot>
            {{ (row as AdminUser).status }}
          </AppStatusPill>
        </template>
        <template #cell-createdAt="{ row }">{{ formatDateTime((row as AdminUser).createdAt) }}</template>
      </AppDataTable>
      <AppPaginator v-model:page="page" :page-size="pageSize" :total="total" />
    </AppCard>
  </div>

  <AppDialog v-model:visible="createOpen" :header="$t('platform.admins.form.heading')">
    <CreateAdminUserForm
      v-model:email="email"
      v-model:password="password"
      :tenant-id="props.tenantId"
      :errors="errors"
      :form-errors="formErrors"
      @submit="submit"
    />
    <template #footer>
      <AppButton variant="secondary" @click="createOpen = false">{{ $t('action.cancel') }}</AppButton>
      <AppButton :pending="pending" @click="submit">{{ $t('platform.admins.form.submit') }}</AppButton>
    </template>
  </AppDialog>
</template>

<style scoped>
.admin-users-page {
  max-width: 64rem;
  margin: 0 auto;
  padding: 1.5rem 2rem;
}
</style>
