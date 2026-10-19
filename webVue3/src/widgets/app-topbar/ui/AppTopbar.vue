<script setup lang="ts">
import { AppButton, AppDialog, AppPopupMenu } from '@/shared/ui';
import { TenantPickerList } from '@/features/tenant-switch';
import { useTopbar } from '../model/useTopbar';

const { canSwitchTenant, tenantLabel, pickerOpen, picker, userId, userInitial, userMenu } = useTopbar();
const { query, page, pageSize, tenants, total, loading, activeTenantId, select } = picker;
</script>

<template>
  <header class="topbar">
    <span class="topbar__title">{{ $t('app.title') }}</span>
    <div class="topbar__spacer" />
    <button
      v-if="canSwitchTenant"
      type="button"
      class="topbar__tenant"
      aria-haspopup="dialog"
      @click="pickerOpen = true"
    >
      <span>{{ tenantLabel }}</span>
      <span aria-hidden="true">▾</span>
    </button>
    <span v-else class="topbar__tenant-label">{{ tenantLabel }}</span>
    <AppPopupMenu :items="userMenu">
      <template #trigger>
        <button type="button" class="topbar__avatar" :aria-label="userId">
          {{ userInitial }}
        </button>
      </template>
    </AppPopupMenu>

    <AppDialog v-model:visible="pickerOpen" :header="$t('tenant.switch')" width="36rem">
      <TenantPickerList
        v-model:query="query"
        v-model:page="page"
        :tenants="tenants"
        :loading="loading"
        :active-tenant-id="activeTenantId"
        :page-size="pageSize"
        :total="total"
        @select="select"
      />
      <template #footer>
        <AppButton variant="secondary" @click="pickerOpen = false">{{ $t('action.close') }}</AppButton>
      </template>
    </AppDialog>
  </header>
</template>

<style scoped>
.topbar {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-shrink: 0;
  height: 3.5rem;
  padding: 0 1.25rem;
  background: white;
  border-bottom: 1px solid var(--app-border);
}
.topbar__title {
  font-weight: 500;
  color: var(--app-text-soft);
}
.topbar__spacer {
  flex: 1;
}
.topbar__tenant {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.375rem 0.5rem;
  border: none;
  border-radius: 0.5rem;
  background: none;
  font: inherit;
  font-weight: 500;
  color: var(--app-text-soft);
  cursor: pointer;
}
.topbar__tenant:hover {
  background: #f3f4f6;
}
.topbar__tenant-label {
  color: var(--app-text-soft);
}
.topbar__avatar {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 2rem;
  border: none;
  border-radius: 50%;
  background: var(--app-navy);
  color: white;
  font-size: 0.75rem;
  font-weight: 700;
  cursor: pointer;
}
</style>
